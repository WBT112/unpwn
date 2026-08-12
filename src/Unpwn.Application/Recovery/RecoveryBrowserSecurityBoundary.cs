namespace Unpwn.Application.Recovery;

public enum RecoveryBrowserContentMode
{
    Recovery,
    SyntheticTest,
}

public enum RecoveryBrowserBoundaryDecisionCode
{
    Allowed,
    UnsupportedScheme,
    UnexpectedOrigin,
    PopupDenied,
    DownloadDenied,
    PermissionDenied,
    ExternalProtocolDenied,
}

public sealed record RecoveryBrowserBoundaryDecision(
    bool IsAllowed,
    RecoveryBrowserBoundaryDecisionCode Code,
    string? VisibleOrigin)
{
    public static RecoveryBrowserBoundaryDecision Allow(string visibleOrigin) =>
        new(true, RecoveryBrowserBoundaryDecisionCode.Allowed, visibleOrigin);

    public static RecoveryBrowserBoundaryDecision Deny(
        RecoveryBrowserBoundaryDecisionCode code) =>
        new(false, code, null);
}

public sealed class RecoveryBrowserSecurityBoundary
{
    private readonly HashSet<string> _expectedOrigins;
    private readonly RecoveryBrowserContentMode _mode;

    private RecoveryBrowserSecurityBoundary(
        HashSet<string> expectedOrigins,
        RecoveryBrowserContentMode mode)
    {
        _expectedOrigins = expectedOrigins;
        _mode = mode;
    }

    public static bool TryCreate(
        RecoveryNavigationHandoff handoff,
        RecoveryBrowserContentMode mode,
        out RecoveryBrowserSecurityBoundary? boundary)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        boundary = null;
        if (!handoff.RequiresVisibleConfirmation ||
            !TryGetAllowedOrigin(handoff.Destination, mode, out var destinationOrigin) ||
            handoff.Destination.UserInfo.Length != 0 ||
            handoff.ExpectedOrigins.Count == 0)
        {
            return false;
        }

        var expectedOrigins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in handoff.ExpectedOrigins)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var originUri) ||
                !TryGetAllowedOrigin(originUri, mode, out var normalizedOrigin) ||
                !IsOriginOnly(originUri))
            {
                return false;
            }

            expectedOrigins.Add(normalizedOrigin);
        }

        if (!expectedOrigins.Contains(destinationOrigin) ||
            !StringComparer.Ordinal.Equals(handoff.ExpectedOrigin, destinationOrigin))
        {
            return false;
        }

        boundary = new RecoveryBrowserSecurityBoundary(expectedOrigins, mode);
        return true;
    }

    public RecoveryBrowserBoundaryDecision EvaluateTopLevelNavigation(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!TryGetAllowedOrigin(destination, _mode, out var origin) ||
            destination.UserInfo.Length != 0)
        {
            return RecoveryBrowserBoundaryDecision.Deny(
                RecoveryBrowserBoundaryDecisionCode.UnsupportedScheme);
        }

        return _expectedOrigins.Contains(origin)
            ? RecoveryBrowserBoundaryDecision.Allow(origin)
            : RecoveryBrowserBoundaryDecision.Deny(
                RecoveryBrowserBoundaryDecisionCode.UnexpectedOrigin);
    }

    public static RecoveryBrowserBoundaryDecision DenyPopup() =>
        RecoveryBrowserBoundaryDecision.Deny(RecoveryBrowserBoundaryDecisionCode.PopupDenied);

    public static RecoveryBrowserBoundaryDecision DenyDownload() =>
        RecoveryBrowserBoundaryDecision.Deny(RecoveryBrowserBoundaryDecisionCode.DownloadDenied);

    public static RecoveryBrowserBoundaryDecision DenyPermission() =>
        RecoveryBrowserBoundaryDecision.Deny(RecoveryBrowserBoundaryDecisionCode.PermissionDenied);

    public static RecoveryBrowserBoundaryDecision DenyExternalProtocol() =>
        RecoveryBrowserBoundaryDecision.Deny(RecoveryBrowserBoundaryDecisionCode.ExternalProtocolDenied);

    private static bool TryGetAllowedOrigin(
        Uri uri,
        RecoveryBrowserContentMode mode,
        out string origin)
    {
        origin = string.Empty;
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             (mode != RecoveryBrowserContentMode.SyntheticTest ||
              uri.Scheme != Uri.UriSchemeHttp ||
              !uri.IsLoopback)))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool IsOriginOnly(Uri uri) =>
        uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
