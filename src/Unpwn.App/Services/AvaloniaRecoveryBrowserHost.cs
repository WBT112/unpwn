using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Unpwn.Application.Recovery;

namespace Unpwn.App.Services;

public sealed class AvaloniaRecoveryBrowserHost : IRecoveryBrowserHost, IDisposable
{
    private readonly NativeWebView _webView;
    private readonly Func<string, IRecoveryBrowserPlatformAdapter> _platformAdapterFactory;
    private RecoveryBrowserSecurityBoundary? _boundary;
    private IRecoveryBrowserPlatformAdapter? _platformAdapter;
    private TaskCompletionSource _platformReleased = CompletedRelease();
    private RecoveryBrowserHostSnapshot _snapshot = ClosedSnapshot;
    private RecoveryBrowserContentMode? _contentMode;

    public AvaloniaRecoveryBrowserHost(NativeWebView webView)
        : this(webView, RecoveryBrowserPlatformAdapter.Create)
    {
    }

    internal AvaloniaRecoveryBrowserHost(
        NativeWebView webView,
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _platformAdapterFactory = platformAdapterFactory ??
            throw new ArgumentNullException(nameof(platformAdapterFactory));
        _webView.EnvironmentRequested += WebView_OnEnvironmentRequested;
        _webView.AdapterCreated += WebView_OnAdapterCreated;
        _webView.AdapterDestroyed += WebView_OnAdapterDestroyed;
        _webView.NavigationStarted += WebView_OnNavigationStarted;
        _webView.NavigationCompleted += WebView_OnNavigationCompleted;
        _webView.NewWindowRequested += WebView_OnNewWindowRequested;
    }

    public event EventHandler<RecoveryBrowserHostSnapshot>? SnapshotChanged;

    public RecoveryBrowserHostSnapshot Snapshot => _snapshot;

    public bool Start(RecoveryBrowserHostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_boundary is not null ||
            _webView.TryGetPlatformHandle() is not null ||
            !RecoveryBrowserSecurityBoundary.TryCreate(
                request.Handoff,
                request.ContentMode,
                out var boundary))
        {
            return false;
        }

        RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
            request.ProfileDataPath,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        _platformAdapter = _platformAdapterFactory(request.ProfileDataPath);
        _platformAdapter.SecurityEvent += PlatformAdapter_OnSecurityEvent;
        _webView.IsVisible = true;
        _boundary = boundary;
        _contentMode = request.ContentMode;
        Publish(_snapshot with
        {
            State = RecoveryBrowserHostState.Starting,
            LastSecurityEvent = RecoveryBrowserSecurityEventCode.None,
        });
        return Navigate(request.Handoff.Destination);
    }

    public bool Navigate(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_boundary is null)
        {
            return false;
        }

        var decision = _boundary.EvaluateTopLevelNavigation(destination);
        if (!decision.IsAllowed)
        {
            PublishSecurityEvent(MapNavigationDecision(decision.Code));
            return false;
        }

        _webView.Navigate(destination);
        return true;
    }

    public bool Navigate(
        RecoveryNavigationHandoff handoff,
        RecoveryBrowserContentMode contentMode)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        if (_boundary is null ||
            !RecoveryBrowserSecurityBoundary.TryCreate(handoff, contentMode, out var boundary))
        {
            return false;
        }

        var decision = boundary!.EvaluateTopLevelNavigation(handoff.Destination);
        if (!decision.IsAllowed)
        {
            return false;
        }

        _boundary = boundary;
        _contentMode = contentMode;
        _webView.Navigate(handoff.Destination);
        return true;
    }

    public bool GoBack() => _boundary is not null && _webView.GoBack();

    public bool GoForward() => _boundary is not null && _webView.GoForward();

    public bool Reload() => _boundary is not null && _webView.Refresh();

    public bool StopLoading() => _boundary is not null && _webView.Stop();

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Browser script exceptions can contain the submitted script. Fail closed without surfacing or retaining exception text so credential material cannot enter diagnostics.")]
    public async Task<RecoveryBrowserCredentialAssistanceResult> InspectCredentialInsertionAsync(
        RecoveryBrowserCredentialInsertionContract contract,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        var boundaryResult = ValidateCredentialContract(contract);
        if (boundaryResult is not null)
        {
            return boundaryResult;
        }

        try
        {
            var result = await _webView.InvokeScript(
                RecoveryBrowserCredentialScript.BuildInspection(contract)).WaitAsync(cancellationToken);
            return RecoveryBrowserCredentialScript.Parse(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.InvocationFailed);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Browser script exceptions can contain the submitted script. Fail closed without surfacing or retaining exception text so credential material cannot enter diagnostics.")]
    public async Task<RecoveryBrowserCredentialAssistanceResult> InsertCredentialAsync(
        RecoveryBrowserCredentialInsertionContract contract,
        ReadOnlyMemory<byte> secretUtf8,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        if (secretUtf8.IsEmpty)
        {
            return RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.UnexpectedContent);
        }

        var boundaryResult = ValidateCredentialContract(contract);
        if (boundaryResult is not null)
        {
            return boundaryResult;
        }

        try
        {
            // The script rechecks every repository-controlled selector immediately before insertion.
            // It never submits the form and returns only a non-secret state token.
            var result = await _webView.InvokeScript(
                RecoveryBrowserCredentialScript.BuildInsertion(contract, secretUtf8)).WaitAsync(cancellationToken);
            return RecoveryBrowserCredentialScript.Parse(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.InvocationFailed);
        }
    }

    public Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
        _platformAdapter?.ClearBrowsingDataAsync(cancellationToken) ?? Task.CompletedTask;

    internal Task WaitForPlatformReleaseAsync(CancellationToken cancellationToken) =>
        _platformReleased.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

    public void Close()
    {
        _webView.Stop();
        if (_platformAdapter is not null)
        {
            _platformAdapter.SecurityEvent -= PlatformAdapter_OnSecurityEvent;
            _platformAdapter.Dispose();
        }
        _platformAdapter = null;
        _boundary = null;
        _contentMode = null;
        _webView.IsVisible = false;
        Publish(ClosedSnapshot);
    }

    public void Dispose()
    {
        Close();
        _webView.EnvironmentRequested -= WebView_OnEnvironmentRequested;
        _webView.AdapterCreated -= WebView_OnAdapterCreated;
        _webView.AdapterDestroyed -= WebView_OnAdapterDestroyed;
        _webView.NavigationStarted -= WebView_OnNavigationStarted;
        _webView.NavigationCompleted -= WebView_OnNavigationCompleted;
        _webView.NewWindowRequested -= WebView_OnNewWindowRequested;
    }

    private RecoveryBrowserCredentialAssistanceResult? ValidateCredentialContract(
        RecoveryBrowserCredentialInsertionContract contract)
    {
        try
        {
            contract.Validate();
        }
        catch (InvalidOperationException)
        {
            return RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.UnexpectedContent);
        }

        if (_boundary is null ||
            _contentMode != contract.ContentMode ||
            _snapshot.State != RecoveryBrowserHostState.Ready ||
            string.IsNullOrWhiteSpace(_snapshot.VisibleOrigin))
        {
            return RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.Unavailable,
                RecoveryBrowserCredentialAssistanceFailureCode.BrowserUnavailable);
        }

        var currentOrigin = _snapshot.VisibleOrigin;
        var originAllowed = contract.ExpectedOrigins.Any(origin =>
            TryNormalizeOrigin(origin, out var normalized) &&
            string.Equals(normalized, currentOrigin, StringComparison.OrdinalIgnoreCase));
        return originAllowed
            ? null
            : RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.WrongOrigin);
    }

    private static bool TryNormalizeOrigin(string value, out string? origin)
    {
        origin = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length != 0)
        {
            return false;
        }

        origin = parsed.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private void WebView_OnEnvironmentRequested(
        object? sender,
        WebViewEnvironmentRequestedEventArgs args) =>
        _platformAdapter?.ConfigureEnvironment(args);

    private void WebView_OnAdapterCreated(object? sender, WebViewAdapterEventArgs args)
    {
        _platformReleased = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _platformAdapter?.Attach(args.TryGetPlatformHandle());
        if (_platformAdapter is { IsConfigured: false })
        {
            _webView.Stop();
            _webView.IsVisible = false;
            Publish(_snapshot with
            {
                State = RecoveryBrowserHostState.Unavailable,
                LastSecurityEvent = RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable,
            });
        }
    }

    private void WebView_OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args)
    {
        _platformAdapter?.Attach(null);
        _platformReleased.TrySetResult();
    }

    private void WebView_OnNavigationStarted(
        object? sender,
        WebViewNavigationStartingEventArgs args)
    {
        if (_boundary is null || args.Request is null)
        {
            args.Cancel = true;
            return;
        }

        var decision = _boundary.EvaluateTopLevelNavigation(args.Request);
        if (!decision.IsAllowed)
        {
            args.Cancel = true;
            PublishSecurityEvent(MapNavigationDecision(decision.Code));
            return;
        }

        Publish(_snapshot with
        {
            Source = args.Request,
            VisibleOrigin = decision.VisibleOrigin,
            CanGoBack = _webView.CanGoBack,
            CanGoForward = _webView.CanGoForward,
        });
    }

    private void WebView_OnNavigationCompleted(
        object? sender,
        WebViewNavigationCompletedEventArgs args)
    {
        if (_boundary is null || args.Request is null)
        {
            return;
        }

        var decision = _boundary.EvaluateTopLevelNavigation(args.Request);
        if (!decision.IsAllowed)
        {
            _webView.Stop();
            _webView.IsVisible = false;
            PublishSecurityEvent(MapNavigationDecision(decision.Code));
            return;
        }

        Publish(_snapshot with
        {
            State = args.IsSuccess
                ? RecoveryBrowserHostState.Ready
                : RecoveryBrowserHostState.NavigationFailed,
            Source = args.Request,
            VisibleOrigin = decision.VisibleOrigin,
            CanGoBack = _webView.CanGoBack,
            CanGoForward = _webView.CanGoForward,
        });
    }

    private void WebView_OnNewWindowRequested(
        object? sender,
        WebViewNewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.PopupBlocked);
    }

    private void PlatformAdapter_OnSecurityEvent(
        object? sender,
        RecoveryBrowserSecurityEventCode code)
    {
        if (code is RecoveryBrowserSecurityEventCode.TlsErrorBlocked or
            RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable)
        {
            _webView.Stop();
            _webView.IsVisible = false;
        }

        PublishSecurityEvent(code);
    }

    private void PublishSecurityEvent(RecoveryBrowserSecurityEventCode code) =>
        Publish(_snapshot with { LastSecurityEvent = code });

    private void Publish(RecoveryBrowserHostSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static RecoveryBrowserSecurityEventCode MapNavigationDecision(
        RecoveryBrowserBoundaryDecisionCode code) => code switch
        {
            RecoveryBrowserBoundaryDecisionCode.UnexpectedOrigin =>
                RecoveryBrowserSecurityEventCode.UnexpectedOriginBlocked,
            RecoveryBrowserBoundaryDecisionCode.ExternalProtocolDenied =>
                RecoveryBrowserSecurityEventCode.ExternalProtocolBlocked,
            _ => RecoveryBrowserSecurityEventCode.UnsafeNavigationBlocked,
        };

    private static RecoveryBrowserHostSnapshot ClosedSnapshot { get; } = new(
        RecoveryBrowserHostState.Closed,
        null,
        null,
        CanGoBack: false,
        CanGoForward: false,
        RecoveryBrowserSecurityEventCode.None);

    private static TaskCompletionSource CompletedRelease()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
