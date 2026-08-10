using System.Text;
using Microsoft.Playwright;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Core;

namespace Unpwn.Automation.Recovery;

public sealed class PlaywrightRecoveryBrowserAssistance(
    IGeneratedCredentialRepository credentialRepository) : IRecoveryBrowserAssistance
{
    private readonly IGeneratedCredentialRepository _credentialRepository =
        credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private SyntheticPasswordChangePage? _passwordChangePage;
    private BrowserAssistanceState _state = BrowserAssistanceState.NotStarted;
    private bool _disposed;

    public BrowserAssistanceState State => _state;

    public async Task<BrowserAssistanceResult> StartAsync(
        BrowserAssistanceLaunchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_state != BrowserAssistanceState.NotStarted || !IsValidConfiguration(options))
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.InvalidConfiguration);
            }

            try
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions { Headless = options.Headless });
                _page = await _browser.NewPageAsync();
                await _page.RouteAsync(
                    "**/*",
                    async route =>
                    {
                        if (Uri.TryCreate(route.Request.Url, UriKind.Absolute, out Uri? requestUri) &&
                            IsSafeLoopbackDestination(requestUri))
                        {
                            await route.ContinueAsync();
                            return;
                        }

                        await route.AbortAsync();
                    });
                _passwordChangePage = new SyntheticPasswordChangePage(_page);
                await _page.GotoAsync(
                    options.Destination.AbsoluteUri,
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                return ApplyInspection(await _passwordChangePage.InspectAsync());
            }
            catch (PlaywrightException)
            {
                bool browserWasCreated = _browser is not null;
                await CloseBrowserAsync();
                return SetManualFallback(
                    browserWasCreated
                        ? BrowserAssistanceFailureCode.NavigationFailed
                        : BrowserAssistanceFailureCode.BrowserUnavailable);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserAssistanceResult> SubmitPasswordChangeAsync(
        GeneratedCredentialReference credentialReference,
        SensitiveBrowserSubmissionAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        ArgumentNullException.ThrowIfNull(authorization);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_state == BrowserAssistanceState.Aborted)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.Aborted,
                    requiresManualGuidance: false);
            }

            if (_state != BrowserAssistanceState.ReadyForAuthorization || _passwordChangePage is null)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.Paused,
                    requiresManualGuidance: _state == BrowserAssistanceState.ManualGuidanceRequired);
            }

            if (!authorization.Approved || authorization.AuthorizationId == Guid.Empty)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.AuthorizationRequired,
                    requiresManualGuidance: false);
            }

            BrowserAssistanceState currentPageState;
            try
            {
                currentPageState = await _passwordChangePage.InspectAsync();
            }
            catch (PlaywrightException)
            {
                return SetManualFallback(BrowserAssistanceFailureCode.NavigationFailed);
            }

            if (currentPageState != BrowserAssistanceState.ReadyForAuthorization)
            {
                return ApplyInspection(currentPageState);
            }

            CredentialSecretLease? lease;
            try
            {
                lease = await _credentialRepository.ReadSecretAsync(
                    credentialReference,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.CredentialUnavailable,
                    requiresManualGuidance: false);
            }

            if (lease is null)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.CredentialUnavailable,
                    requiresManualGuidance: false);
            }

            using (lease)
            {
                string secret = Encoding.UTF8.GetString(lease.SecretUtf8.Span);
                try
                {
                    if (!await _passwordChangePage.SubmitAsync(secret))
                    {
                        return SetManualFallback(BrowserAssistanceFailureCode.SubmissionFailed);
                    }
                }
                catch (PlaywrightException)
                {
                    return SetManualFallback(BrowserAssistanceFailureCode.SubmissionFailed);
                }
                finally
                {
                    secret = string.Empty;
                }
            }

            _state = BrowserAssistanceState.Submitted;
            return BrowserAssistanceResult.Success(_state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserAssistanceResult> PauseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_state is BrowserAssistanceState.Aborted or BrowserAssistanceState.Submitted or
                BrowserAssistanceState.NotStarted)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.InvalidConfiguration,
                    requiresManualGuidance: false);
            }

            _state = BrowserAssistanceState.PausedByUser;
            return BrowserAssistanceResult.Pause(_state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserAssistanceResult> ResumeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_state != BrowserAssistanceState.PausedByUser || _passwordChangePage is null)
            {
                return BrowserAssistanceResult.Failure(
                    _state,
                    BrowserAssistanceFailureCode.InvalidConfiguration,
                    requiresManualGuidance: false);
            }

            try
            {
                return ApplyInspection(await _passwordChangePage.InspectAsync());
            }
            catch (PlaywrightException)
            {
                return SetManualFallback(BrowserAssistanceFailureCode.NavigationFailed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserAssistanceResult> AbortAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await CloseBrowserAsync();
            _state = BrowserAssistanceState.Aborted;
            return BrowserAssistanceResult.Success(_state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            await CloseBrowserAsync();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static bool IsValidConfiguration(BrowserAssistanceLaunchOptions options)
    {
        if (!IsSafeLoopbackDestination(options.Destination))
        {
            return false;
        }

        if (options.Mode == BrowserAssistanceExecutionMode.Production && options.Headless)
        {
            return false;
        }

        if (options.Mode == BrowserAssistanceExecutionMode.SyntheticTest &&
            !options.UsesSyntheticCredentials)
        {
            return false;
        }

        return !options.CaptureArtifacts ||
            options.Mode == BrowserAssistanceExecutionMode.SyntheticTest &&
            options.UsesSyntheticCredentials;
    }

    private static bool IsSafeLoopbackDestination(Uri destination) =>
        destination.IsAbsoluteUri &&
        destination.IsLoopback &&
        string.IsNullOrEmpty(destination.UserInfo) &&
        (destination.Scheme == Uri.UriSchemeHttp || destination.Scheme == Uri.UriSchemeHttps);

    private BrowserAssistanceResult ApplyInspection(BrowserAssistanceState state)
    {
        _state = state;
        return state switch
        {
            BrowserAssistanceState.ReadyForAuthorization => BrowserAssistanceResult.Success(state),
            BrowserAssistanceState.PausedForMfa or
                BrowserAssistanceState.PausedForCaptcha or
                BrowserAssistanceState.PausedForEmailLink => BrowserAssistanceResult.Pause(state),
            _ => BrowserAssistanceResult.Failure(
                BrowserAssistanceState.ManualGuidanceRequired,
                BrowserAssistanceFailureCode.UnexpectedContent),
        };
    }

    private BrowserAssistanceResult SetManualFallback(BrowserAssistanceFailureCode failureCode)
    {
        _state = BrowserAssistanceState.ManualGuidanceRequired;
        return BrowserAssistanceResult.Failure(_state, failureCode);
    }

    private async Task CloseBrowserAsync()
    {
        _passwordChangePage = null;
        _page = null;
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
