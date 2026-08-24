using Unpwn.Application.Recovery;

namespace Unpwn.App.Services;

public enum RecoveryBrowserHostState
{
    Closed,
    Starting,
    Ready,
    NavigationFailed,
    Unavailable,
}

public enum RecoveryBrowserSecurityEventCode
{
    None,
    UnsafeNavigationBlocked,
    UnexpectedOriginBlocked,
    PopupBlocked,
    DownloadBlocked,
    PermissionBlocked,
    ExternalProtocolBlocked,
    TlsErrorBlocked,
    ClientCertificateBlocked,
    PlatformHardeningUnavailable,
}

public sealed record RecoveryBrowserHostSnapshot(
    RecoveryBrowserHostState State,
    Uri? Source,
    string? VisibleOrigin,
    bool CanGoBack,
    bool CanGoForward,
    RecoveryBrowserSecurityEventCode LastSecurityEvent);

public sealed record RecoveryBrowserHostRequest(
    RecoveryNavigationHandoff Handoff,
    RecoveryBrowserContentMode ContentMode,
    string ProfileDataPath);

public sealed record RecoveryBrowserSessionStartRequest(
    Guid AccountId,
    RecoveryNavigationHandoff Handoff,
    RecoveryBrowserContentMode ContentMode);

internal interface IRecoveryBrowserPlatformAdapter : IDisposable
{
    event EventHandler<RecoveryBrowserSecurityEventCode>? SecurityEvent;

    bool IsConfigured { get; }

    void ConfigureEnvironment(Avalonia.Controls.WebViewEnvironmentRequestedEventArgs args);

    void Attach(Avalonia.Platform.IPlatformHandle? platformHandle);

    Task ClearBrowsingDataAsync(CancellationToken cancellationToken);

    Task WaitForProfileReleaseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
