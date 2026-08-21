using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Platform;
using Unpwn.Application.Recovery;

namespace Unpwn.App.Services;

public sealed class AvaloniaRecoveryBrowserHost : IRecoveryBrowserHost, IDisposable
{
    private readonly IRecoveryBrowserControl _webView;
    private readonly Func<string, IRecoveryBrowserPlatformAdapter> _platformAdapterFactory;
    private readonly string _applicationDataRoot;
    private RecoveryBrowserSecurityBoundary? _boundary;
    private IRecoveryBrowserPlatformAdapter? _platformAdapter;
    private TaskCompletionSource _platformReleased = CompletedRelease();
    private RecoveryBrowserHostSnapshot _snapshot = ClosedSnapshot;
    private RecoveryBrowserContentMode? _contentMode;
    private bool _isClosingSurface;
    private bool _surfaceClosingRaised;

    public AvaloniaRecoveryBrowserHost(NativeWebView webView)
        : this(
            new EmbeddedRecoveryBrowserControl(webView),
            RecoveryBrowserPlatformAdapter.Create,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal AvaloniaRecoveryBrowserHost(
        NativeWebView webView,
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory,
        string? applicationDataRoot = null)
        : this(
            new EmbeddedRecoveryBrowserControl(webView),
            platformAdapterFactory,
            applicationDataRoot ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal AvaloniaRecoveryBrowserHost(
        NativeWebDialog dialog,
        TopLevel owner,
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory,
        string? applicationDataRoot = null)
        : this(
            new DialogRecoveryBrowserControl(dialog, owner),
            platformAdapterFactory,
            applicationDataRoot ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    private AvaloniaRecoveryBrowserHost(
        IRecoveryBrowserControl webView,
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory,
        string applicationDataRoot)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _platformAdapterFactory = platformAdapterFactory ??
            throw new ArgumentNullException(nameof(platformAdapterFactory));
        _applicationDataRoot = Path.GetFullPath(applicationDataRoot);
        _webView.EnvironmentRequested += WebView_OnEnvironmentRequested;
        _webView.AdapterCreated += WebView_OnAdapterCreated;
        _webView.AdapterDestroyed += WebView_OnAdapterDestroyed;
        _webView.NavigationStarted += WebView_OnNavigationStarted;
        _webView.NavigationCompleted += WebView_OnNavigationCompleted;
        _webView.NewWindowRequested += WebView_OnNewWindowRequested;
        _webView.Closing += WebView_OnClosing;
    }

    public event EventHandler<RecoveryBrowserHostSnapshot>? SnapshotChanged;

    internal event EventHandler? SurfaceClosing;

    public RecoveryBrowserHostSnapshot Snapshot => _snapshot;

    internal bool IsEmbedded => _webView.IsEmbedded;

    internal bool IsNativeBackendReady =>
        _webView.TryGetPlatformHandle() is not null && _platformAdapter?.IsConfigured == true;

    internal string NativeBackendStatus => _platformAdapter switch
    {
        WindowsRecoveryBrowserPlatformAdapter => "WebView2",
        LinuxRecoveryBrowserPlatformAdapter linux => $"WebKit-{linux.Backend}",
        null => "not-created",
        _ => "unsupported",
    };

    internal Control? EmbeddedControl => _webView.EmbeddedControl;

    internal IPlatformHandle? TryGetPlatformHandle() => _webView.TryGetPlatformHandle();

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
            _applicationDataRoot);

        _platformAdapter = _platformAdapterFactory(request.ProfileDataPath);
        _platformAdapter.SecurityEvent += PlatformAdapter_OnSecurityEvent;
        _boundary = boundary;
        _contentMode = request.ContentMode;
        _surfaceClosingRaised = false;
        Publish(_snapshot with
        {
            State = RecoveryBrowserHostState.Starting,
            LastSecurityEvent = RecoveryBrowserSecurityEventCode.None,
        });
        _webView.Show();
        if (_snapshot.State == RecoveryBrowserHostState.Unavailable)
        {
            return false;
        }

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

    internal async Task WaitForPlatformReleaseAsync(CancellationToken cancellationToken)
    {
        await _platformReleased.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        if (_platformAdapter is not null)
        {
            await _platformAdapter.WaitForProfileReleaseAsync(cancellationToken);
        }
    }

    internal IAsyncDisposable? BeginEmbeddedRelease() => _webView.BeginReparenting();

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
        HideSurface();
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
        _webView.Closing -= WebView_OnClosing;
        _webView.Dispose();
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
            HideSurface();
            Publish(_snapshot with
            {
                State = RecoveryBrowserHostState.Unavailable,
                LastSecurityEvent = RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable,
            });
        }
    }

    private void WebView_OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args)
    {
        var closeWasRequested = _isClosingSurface;
        _isClosingSurface = false;
        _platformAdapter?.Attach(null);
        _platformReleased.TrySetResult();
        if (!_webView.IsEmbedded && !closeWasRequested)
        {
            NotifySurfaceClosing();
        }
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

        // WebKitGTK raises NavigationStarted through a synchronous dispatch from its GLib thread.
        // Querying the adapter from this callback would synchronously dispatch back to that same
        // thread and deadlock both dispatchers. Preserve the last completed history capabilities;
        // NavigationCompleted refreshes them after the GLib callback has returned.
        Publish(CreateNavigationStartedSnapshot(
            _snapshot,
            args.Request,
            decision.VisibleOrigin));
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
            HideSurface();
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
            HideSurface();
        }

        PublishSecurityEvent(code);
    }

    private void WebView_OnClosing(object? sender, EventArgs args)
    {
        if (!_isClosingSurface)
        {
            NotifySurfaceClosing();
        }
    }

    private void NotifySurfaceClosing()
    {
        if (_surfaceClosingRaised)
        {
            return;
        }

        _surfaceClosingRaised = true;
        SurfaceClosing?.Invoke(this, EventArgs.Empty);
    }

    private void HideSurface()
    {
        _isClosingSurface = true;
        _webView.Hide();
        if (_webView.IsEmbedded)
        {
            _isClosingSurface = false;
        }
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

    internal static RecoveryBrowserHostSnapshot CreateNavigationStartedSnapshot(
        RecoveryBrowserHostSnapshot snapshot,
        Uri source,
        string? visibleOrigin) => snapshot with
        {
            Source = source,
            VisibleOrigin = visibleOrigin,
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
