using Unpwn.Core;

namespace Unpwn.Application.Recovery;

public enum RecoveryLocationSelectionPolicy
{
    WellKnownFirst,
    ProviderDefinedFirst,
    ProviderDefinedOnly,
}

public enum RecoveryLocationResolutionSource
{
    WellKnownChangePassword,
    ProviderDefined,
    ProviderFallback,
}

public enum RecoveryLocationDiscoveryFailureCode
{
    None,
    InvalidRequest,
    ProviderLocationNotFound,
    ProviderLocationInvalid,
    InsecureAccountOrigin,
    NetworkFailure,
    UnsupportedResponse,
    MissingRedirectLocation,
    InvalidRedirectLocation,
    InsecureRedirect,
    UnexpectedRedirectOrigin,
    RedirectLimitExceeded,
}

public enum RecoveryLocationFallbackReason
{
    None,
    InsecureAccountOrigin,
    NetworkFailure,
    UnsupportedResponse,
    MissingRedirectLocation,
    InvalidRedirectLocation,
    InsecureRedirect,
    UnexpectedRedirectOrigin,
    RedirectLimitExceeded,
}

public sealed record RecoveryLocationDiscoveryRequest(
    RecoveryWorkflowDefinition Workflow,
    string? ProviderLocationId,
    Uri? AccountUri,
    RecoveryLocationSelectionPolicy SelectionPolicy = RecoveryLocationSelectionPolicy.WellKnownFirst);

public sealed record RecoveryNavigationHandoff(
    Uri Destination,
    string ExpectedOrigin,
    IReadOnlyList<string> ExpectedOrigins,
    RecoveryLocationResolutionSource Source,
    bool RequiresVisibleConfirmation);

public sealed record RecoveryLocationDiscoveryResult(
    bool Succeeded,
    RecoveryNavigationHandoff? Handoff,
    RecoveryLocationDiscoveryFailureCode FailureCode,
    RecoveryLocationFallbackReason FallbackReason,
    IReadOnlyList<string> RedirectOrigins)
{
    public int RedirectCount => Math.Max(0, RedirectOrigins.Count - 1);

    public static RecoveryLocationDiscoveryResult Success(
        RecoveryNavigationHandoff handoff,
        IReadOnlyList<string>? redirectOrigins = null,
        RecoveryLocationFallbackReason fallbackReason = RecoveryLocationFallbackReason.None)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        return new(
            true,
            handoff,
            RecoveryLocationDiscoveryFailureCode.None,
            fallbackReason,
            redirectOrigins ?? []);
    }

    public static RecoveryLocationDiscoveryResult Failure(
        RecoveryLocationDiscoveryFailureCode failureCode,
        IReadOnlyList<string>? redirectOrigins = null) =>
        new(false, null, failureCode, RecoveryLocationFallbackReason.None, redirectOrigins ?? []);
}

public interface IRecoveryLocationDiscoveryService
{
    Task<RecoveryLocationDiscoveryResult> DiscoverAsync(
        RecoveryLocationDiscoveryRequest request,
        CancellationToken cancellationToken);
}
