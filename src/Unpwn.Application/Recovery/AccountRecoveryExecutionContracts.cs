using Unpwn.Core;

namespace Unpwn.Application.Recovery;

public enum AccountRecoveryExecutionLoadState
{
    Locked,
    NotFound,
    Loaded,
    Corrupted,
    LoadFailed,
}

public enum AccountRecoveryExecutionFailureCode
{
    None,
    Locked,
    InvalidInput,
    NotFound,
    Conflict,
    Corrupted,
    NoSafeRecoveryPath,
    PersistenceFailure,
}

public enum AccountRecoveryExecutionTransitionKind
{
    SetAccessAvailable,
    SetAccessLost,
    SetWaitingForProviderReview,
    StartAction,
    SetCompletionCriteriaAcknowledgements,
    CompleteAction,
    RequireUserAction,
    BlockAction,
    FailAction,
    MarkTrulyNotApplicable,
    AcceptNotApplicableRisk,
    AcceptUnresolvedRisk,
    SetUserNotes,
    AttachCredentialReference,
}

public sealed record AccountRecoveryProjectionContext(AccountRecoveryCategory Category)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Category))
        {
            throw new InvalidOperationException("The account execution projection context is invalid.");
        }
    }
}

public sealed record AccountRecoveryExecutionCreateRequest(
    Guid OperationId,
    Guid AccountId,
    RecoveryWorkflowDefinition Workflow,
    AccountRecoveryProjectionContext ProjectionContext);

public sealed record AccountRecoveryExecutionTransitionRequest(
    Guid OperationId,
    Guid AccountId,
    long ExpectedRevision,
    RecoveryWorkflowDefinition Workflow,
    AccountRecoveryExecutionTransitionKind Transition,
    string? ActionDefinitionId,
    string? UserReason,
    string? UserNotes,
    bool CompletionCriteriaAcknowledged,
    GeneratedCredentialReference? CredentialReference,
    AccountRecoveryProjectionContext ProjectionContext)
{
    public string[]? AcknowledgedCompletionCriteria { get; init; }
}

public sealed record AccountRecoveryExecutionResult(
    bool Succeeded,
    AccountRecoveryExecutionFailureCode FailureCode,
    AccountRecoveryExecutionState? State)
{
    public static AccountRecoveryExecutionResult Success(AccountRecoveryExecutionState state) =>
        new(true, AccountRecoveryExecutionFailureCode.None, state);

    public static AccountRecoveryExecutionResult Failure(AccountRecoveryExecutionFailureCode failureCode) =>
        new(false, failureCode, null);
}

public interface IAccountRecoveryExecutionService
{
    Task<AccountRecoveryExecutionResult> LoadAsync(
        Guid accountId,
        RecoveryWorkflowDefinition workflow,
        CancellationToken cancellationToken);

    Task<AccountRecoveryExecutionResult> CreateAsync(
        AccountRecoveryExecutionCreateRequest request,
        CancellationToken cancellationToken);

    Task<AccountRecoveryExecutionResult> ApplyAsync(
        AccountRecoveryExecutionTransitionRequest request,
        CancellationToken cancellationToken);

    void ClearForLock();
}
