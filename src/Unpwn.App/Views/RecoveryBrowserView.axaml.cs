using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unpwn.App.Services;

namespace Unpwn.App.Views;

public partial class RecoveryBrowserView : UserControl, IDisposable, IRecoveryBrowserSessionResources
{
    private static readonly TimeSpan PlatformActivationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PlatformActivationPollInterval = TimeSpan.FromMilliseconds(25);
    private readonly Func<string, IRecoveryBrowserPlatformAdapter> _platformAdapterFactory;
    private readonly IRecoveryBrowserSessionLifecycle _sessionLifecycle;
    private readonly bool _ownsSessionLifecycle;
    private AvaloniaRecoveryBrowserHost? _host;
    private RecoveryBrowserSession? _session;

    public RecoveryBrowserView()
        : this(
            CreateDefaultSessionLifecycle(),
            RecoveryBrowserPlatformAdapter.Create,
            ownsSessionLifecycle: true)
    {
    }

    internal RecoveryBrowserView(
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory)
        : this(
            CreateDefaultSessionLifecycle(),
            platformAdapterFactory,
            ownsSessionLifecycle: true)
    {
    }

    internal RecoveryBrowserView(
        IRecoveryBrowserSessionLifecycle sessionLifecycle,
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory,
        bool ownsSessionLifecycle = false)
    {
        _sessionLifecycle = sessionLifecycle ??
            throw new ArgumentNullException(nameof(sessionLifecycle));
        _platformAdapterFactory = platformAdapterFactory ??
            throw new ArgumentNullException(nameof(platformAdapterFactory));
        _ownsSessionLifecycle = ownsSessionLifecycle;
        InitializeComponent();
        _sessionLifecycle.StateChanged += SessionLifecycle_OnStateChanged;
        UpdateSnapshot(null);
        UpdateSessionSnapshot(_sessionLifecycle.Current);
    }

    public RecoveryBrowserHostSnapshot? Snapshot => _host?.Snapshot;

    public event EventHandler? SessionClosed;

    public RecoveryBrowserSessionLifecycleSnapshot SessionSnapshot =>
        _sessionLifecycle.Current;

    public async Task<bool> StartAsync(
        RecoveryBrowserSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_host is not null)
        {
            if (_session?.AccountId != request.AccountId)
            {
                UpdateSessionSnapshot(_sessionLifecycle.Current with
                {
                    FailureCode = RecoveryBrowserSessionFailureCode.AccountSwitchRequiresCleanup,
                });
                return false;
            }

            return _host.Navigate(request.Handoff, request.ContentMode);
        }

        var started = _sessionLifecycle.Start(request.AccountId);
        if (!started.Succeeded)
        {
            UpdateSessionSnapshot(_sessionLifecycle.Current with
            {
                FailureCode = started.FailureCode,
            });
            return false;
        }

        _session = started.Session;
        var hostRequest = new RecoveryBrowserHostRequest(
            request.Handoff,
            request.ContentMode,
            _session!.ProfileDataPath);
        var hostAccepted = Start(hostRequest);
        if (hostAccepted &&
            (TopLevel.GetTopLevel(this) is null || await WaitForPlatformActivationAsync(cancellationToken)))
        {
            return true;
        }

        if (!started.WasReused)
        {
            await _sessionLifecycle.EndAsync(
                _session.SessionId,
                this,
                cancellationToken);
        }
        _session = null;
        return false;
    }

    internal bool Start(RecoveryBrowserHostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_host is not null)
        {
            return false;
        }

        var webView = new NativeWebView();
        var host = new AvaloniaRecoveryBrowserHost(webView, _platformAdapterFactory);
        host.SnapshotChanged += Host_OnSnapshotChanged;
        try
        {
            if (!host.Start(request))
            {
                host.SnapshotChanged -= Host_OnSnapshotChanged;
                host.Dispose();
                return false;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                InvalidOperationException)
        {
            host.SnapshotChanged -= Host_OnSnapshotChanged;
            host.Dispose();
            return false;
        }

        _host = host;
        BrowserContent.Content = webView;
        UpdateSnapshot(host.Snapshot);
        return true;
    }

    public Task<RecoveryBrowserCredentialAssistanceResult> InspectCredentialInsertionAsync(
        RecoveryBrowserCredentialInsertionContract contract,
        CancellationToken cancellationToken = default) =>
        _host?.InspectCredentialInsertionAsync(contract, cancellationToken) ?? Task.FromResult(
            RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.Unavailable,
                RecoveryBrowserCredentialAssistanceFailureCode.BrowserUnavailable));

    public Task<RecoveryBrowserCredentialAssistanceResult> InsertCredentialAsync(
        RecoveryBrowserCredentialInsertionContract contract,
        ReadOnlyMemory<byte> secretUtf8,
        CancellationToken cancellationToken = default) =>
        _host?.InsertCredentialAsync(contract, secretUtf8, cancellationToken) ?? Task.FromResult(
            RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.Unavailable,
                RecoveryBrowserCredentialAssistanceFailureCode.BrowserUnavailable));

    public void Dispose()
    {
        _sessionLifecycle.StateChanged -= SessionLifecycle_OnStateChanged;
        if (_host is not null)
        {
            _host.SnapshotChanged -= Host_OnSnapshotChanged;
            _host.Dispose();
            _host = null;
        }

        BrowserContent.Content = null;
        UpdateSnapshot(null);
        if (_ownsSessionLifecycle && _sessionLifecycle is IDisposable disposableLifecycle)
        {
            disposableLifecycle.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    public Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
        _host?.ClearBrowsingDataAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task StopAndReleaseAsync(CancellationToken cancellationToken)
    {
        var host = _host;
        if (host is null)
        {
            return;
        }

        host.StopLoading();
        BrowserContent.Content = null;
        await host.WaitForPlatformReleaseAsync(cancellationToken);
        host.SnapshotChanged -= Host_OnSnapshotChanged;
        host.Dispose();
        _host = null;
        UpdateSnapshot(null);
    }

    private async Task<bool> WaitForPlatformActivationAsync(CancellationToken cancellationToken)
    {
        var host = _host;
        if (host is null || BrowserContent.Content is not NativeWebView webView)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PlatformActivationTimeout);
        try
        {
            while (true)
            {
                var snapshot = host.Snapshot;
                if (snapshot.State == RecoveryBrowserHostState.Unavailable ||
                    snapshot.LastSecurityEvent == RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable)
                {
                    return false;
                }

                if (webView.TryGetPlatformHandle() is not null)
                {
                    // AdapterCreated and the platform hardening callback run synchronously before
                    // the handle is usable. Yield once so the snapshot projection can settle.
                    await Task.Yield();
                    snapshot = host.Snapshot;
                    return snapshot.State != RecoveryBrowserHostState.Unavailable &&
                        snapshot.LastSecurityEvent != RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable;
                }

                await Task.Delay(PlatformActivationPollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void Host_OnSnapshotChanged(
        object? sender,
        RecoveryBrowserHostSnapshot snapshot) => UpdateSnapshot(snapshot);

    private void SessionLifecycle_OnStateChanged(
        object? sender,
        RecoveryBrowserSessionLifecycleSnapshot snapshot) =>
        UpdateSessionSnapshot(snapshot);

    private void UpdateSnapshot(RecoveryBrowserHostSnapshot? snapshot)
    {
        BackButton.IsEnabled = snapshot?.CanGoBack == true;
        ForwardButton.IsEnabled = snapshot?.CanGoForward == true;
        if (snapshot?.VisibleOrigin is { } origin)
        {
            OriginText.Text = origin;
        }
        else
        {
            BindDynamicResource(OriginText, "RecoveryBrowser.OriginUnavailable");
        }

        var securityResourceKey = snapshot?.LastSecurityEvent switch
        {
            RecoveryBrowserSecurityEventCode.None or null => null,
            _ => $"RecoveryBrowser.Security.{snapshot.LastSecurityEvent}",
        };
        if (securityResourceKey is null)
        {
            SecurityStatusText.Text = string.Empty;
        }
        else
        {
            BindDynamicResource(SecurityStatusText, securityResourceKey);
        }
    }

    private static void BindDynamicResource(TextBlock textBlock, string key) =>
        textBlock.Bind(
            TextBlock.TextProperty,
            textBlock.GetResourceObservable(key));

    private void UpdateSessionSnapshot(RecoveryBrowserSessionLifecycleSnapshot snapshot)
    {
        var key = snapshot.FailureCode switch
        {
            RecoveryBrowserSessionFailureCode.AccountSwitchRequiresCleanup =>
                "RecoveryBrowser.Session.AccountSwitchBlocked",
            RecoveryBrowserSessionFailureCode.OrphanedDataRequiresCleanup =>
                "RecoveryBrowser.Session.Orphaned",
            RecoveryBrowserSessionFailureCode.StorageUnavailable =>
                "RecoveryBrowser.Session.CleanupFailed",
            _ => snapshot.State switch
            {
                RecoveryBrowserSessionLifecycleState.Cleaning =>
                    "RecoveryBrowser.Session.Cleaning",
                RecoveryBrowserSessionLifecycleState.CleanupFailed =>
                    "RecoveryBrowser.Session.CleanupFailed",
                RecoveryBrowserSessionLifecycleState.OrphanedDataDetected =>
                    "RecoveryBrowser.Session.Orphaned",
                _ => null,
            },
        };
        if (key is null)
        {
            SessionStatusText.Text = string.Empty;
        }
        else
        {
            BindDynamicResource(SessionStatusText, key);
        }

        var closeResourceKey = snapshot.CanRetryCleanup
            ? "RecoveryBrowser.Session.RetryCleanup"
            : "RecoveryBrowser.Close";
        CloseButton.Bind(
            ContentControl.ContentProperty,
            CloseButton.GetResourceObservable(closeResourceKey));
    }

    private void Back_OnClick(object? sender, RoutedEventArgs args) => _host?.GoBack();

    private void Forward_OnClick(object? sender, RoutedEventArgs args) => _host?.GoForward();

    private void Reload_OnClick(object? sender, RoutedEventArgs args) => _host?.Reload();

    private void Stop_OnClick(object? sender, RoutedEventArgs args) => _host?.StopLoading();

    private async void Close_OnClick(object? sender, RoutedEventArgs args)
    {
        await CloseSessionAsync(CancellationToken.None);
    }

    public async Task<bool> CloseSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            Dispose();
            return true;
        }

        RecoveryBrowserSessionCleanupResult result;
        if (_sessionLifecycle.Current.OrphanedSessions.Any(
                orphan => orphan.SessionId == _session.SessionId))
        {
            result = await _sessionLifecycle.RetryOrphanCleanupAsync(
                _session.SessionId,
                cancellationToken);
        }
        else
        {
            result = await _sessionLifecycle.EndAsync(
                _session.SessionId,
                this,
                cancellationToken);
        }

        if (result.Succeeded)
        {
            _session = null;
            SessionClosed?.Invoke(this, EventArgs.Empty);
            Dispose();
        }

        return result.Succeeded;
    }

    private static RecoveryBrowserSessionLifecycle CreateDefaultSessionLifecycle() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
