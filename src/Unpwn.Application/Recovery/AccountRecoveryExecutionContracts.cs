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
    PersistenceFailure,
}

public enum AccountRecoveryExecutionTransitionKind
{
    ChangeRecoveryPath,
    SetAccessAvailable,
    SetAccessLost,
    SetWaitingForProviderReview,
    StartAction,
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

public sealed record AccountRecoveryProjectionContext(
    AccountCriticality Criticality,
    int DependencyDepth,
    Guid[] WaitingForAccountIds)
{
    public int InventoryBlockedIssues { get; init; }

    public int InventoryUnresolvedRisks { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(WaitingForAccountIds);
        ArgumentOutOfRangeException.ThrowIfNegative(DependencyDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(InventoryBlockedIssues);
        ArgumentOutOfRangeException.ThrowIfNegative(InventoryUnresolvedRisks);
        if (!Enum.IsDefined(Criticality) || WaitingForAccountIds.Any(id => id == Guid.Empty))
        {
            throw new InvalidOperationException("The account execution projection context is invalid.");
        }
    }
}

public sealed record AccountRecoveryExecutionCreateRequest(
    Guid OperationId,
    Guid AccountId,
    RecoveryWorkflowDefinition Workflow,
    RecoveryPath SelectedPath,
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
    public RecoveryPath? SelectedPath { get; init; }
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
