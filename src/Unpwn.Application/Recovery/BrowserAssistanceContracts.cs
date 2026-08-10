using Unpwn.Core;

namespace Unpwn.Application.Recovery;

public enum BrowserAssistanceExecutionMode
{
    Production,
    SyntheticTest,
}

public enum BrowserAssistanceState
{
    NotStarted,
    ReadyForAuthorization,
    PausedByUser,
    PausedForMfa,
    PausedForCaptcha,
    PausedForEmailLink,
    ManualGuidanceRequired,
    Submitted,
    Aborted,
}

public enum BrowserAssistanceFailureCode
{
    None,
    InvalidConfiguration,
    BrowserUnavailable,
    NavigationFailed,
    UnexpectedContent,
    AuthorizationRequired,
    CredentialUnavailable,
    Paused,
    Aborted,
    SubmissionFailed,
}

public sealed record BrowserAssistanceLaunchOptions(
    Uri Destination,
    BrowserAssistanceExecutionMode Mode,
    bool Headless,
    bool CaptureArtifacts = false,
    bool UsesSyntheticCredentials = false);

public sealed record SensitiveBrowserSubmissionAuthorization(Guid AuthorizationId, bool Approved);

public sealed record BrowserAssistanceResult(
    bool Succeeded,
    BrowserAssistanceState State,
    BrowserAssistanceFailureCode FailureCode,
    bool RequiresManualGuidance)
{
    public static BrowserAssistanceResult Success(BrowserAssistanceState state) =>
        new(true, state, BrowserAssistanceFailureCode.None, false);

    public static BrowserAssistanceResult Pause(BrowserAssistanceState state) =>
        new(false, state, BrowserAssistanceFailureCode.Paused, false);

    public static BrowserAssistanceResult Failure(
        BrowserAssistanceState state,
        BrowserAssistanceFailureCode failureCode,
        bool requiresManualGuidance = true) =>
        new(false, state, failureCode, requiresManualGuidance);
}

public interface IRecoveryBrowserAssistance : IAsyncDisposable
{
    BrowserAssistanceState State { get; }

    Task<BrowserAssistanceResult> StartAsync(
        BrowserAssistanceLaunchOptions options,
        CancellationToken cancellationToken);

    Task<BrowserAssistanceResult> SubmitPasswordChangeAsync(
        GeneratedCredentialReference credentialReference,
        SensitiveBrowserSubmissionAuthorization authorization,
        CancellationToken cancellationToken);

    Task<BrowserAssistanceResult> PauseAsync(CancellationToken cancellationToken);

    Task<BrowserAssistanceResult> ResumeAsync(CancellationToken cancellationToken);

    Task<BrowserAssistanceResult> AbortAsync(CancellationToken cancellationToken);
}
