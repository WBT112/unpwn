using Unpwn.Core;

namespace Unpwn.Application;

public enum NextUserTaskState
{
    ActionAvailable,
    OptionalWorkMayContinue,
    Blocked,
    TerminalReadOnly,
}

public enum NextUserTaskCode
{
    BeginTrustedDeviceCheck,
    ConfirmTrustedDevice,
    MoveToTrustedDevice,
    CreateOrUnlockVault,
    CreateRecoverySession,
    ImportAccounts,
    ReviewAccountCategories,
    ContinueCategoryReviewOrRecovery,
    ContinueToRecovery,
    StartAccountRecovery,
    HandleCredentialHandoff,
    ReviewCompletion,
    ConfirmCompletionOutcome,
    ResumeSession,
    UnlockVault,
    ReadOnlyReport,
}

public enum NextUserTaskTarget
{
    TrustedDeviceCheck,
    TrustedDeviceGuidance,
    VaultEntry,
    RecoveryOverview,
    CsvImport,
    AccountTriage,
    AccountRecovery,
    CredentialHandoff,
    CompletionReview,
}

public sealed record RecoveryFlowContext(
    int AccountCount,
    int UncategorizedAccountCount,
    bool HasOutstandingAccountWork,
    bool HasPendingCredentialHandoff,
    Guid? RecommendedAccountId = null,
    string? RecommendedActionId = null);

public sealed record NextUserTask(
    RecoveryWizardStepId CurrentStep,
    NextUserTaskState State,
    NextUserTaskCode Code,
    NextUserTaskTarget Target,
    RecoveryWizardStepId? TransitionStep = null,
    Guid? AccountId = null,
    string? ActionId = null)
{
    public bool RequiresTransition => TransitionStep is not null && TransitionStep != CurrentStep;
}

/// <summary>
/// Projects exactly one concrete user task from canonical recovery state. Route
/// selection is an output and is never interpreted as progress.
/// </summary>
public static class RecoveryNextUserTask
{
    public static NextUserTask Project(
        RecoveryWizardState state,
        RecoveryFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        Validate(context);

        if (state.IsTerminal)
        {
            return Task(
                state,
                NextUserTaskState.TerminalReadOnly,
                NextUserTaskCode.ReadOnlyReport,
                NextUserTaskTarget.CompletionReview);
        }

        if (state.Status == RecoveryWizardLifecycleStatus.Locked)
        {
            return Task(
                state,
                NextUserTaskState.Blocked,
                NextUserTaskCode.UnlockVault,
                NextUserTaskTarget.VaultEntry);
        }

        if (state.Status == RecoveryWizardLifecycleStatus.Paused)
        {
            return Task(
                state,
                NextUserTaskState.Blocked,
                NextUserTaskCode.ResumeSession,
                TargetFor(state.ResumeStep));
        }

        if (state.Status != RecoveryWizardLifecycleStatus.Active)
        {
            throw new InvalidOperationException(
                $"The recovery flow cannot project an active task for status '{state.Status}'.");
        }

        return state.CurrentStep.Value switch
        {
            "welcome" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.BeginTrustedDeviceCheck,
                NextUserTaskTarget.TrustedDeviceCheck,
                RecoveryWizardStepId.TrustedDeviceCheck),
            "trusted-device-check" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ConfirmTrustedDevice,
                NextUserTaskTarget.TrustedDeviceCheck),
            "trusted-device-guidance" => Task(
                state,
                NextUserTaskState.Blocked,
                NextUserTaskCode.MoveToTrustedDevice,
                NextUserTaskTarget.TrustedDeviceGuidance),
            "vault-entry" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.CreateOrUnlockVault,
                NextUserTaskTarget.VaultEntry),
            "incident-intake" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.CreateRecoverySession,
                NextUserTaskTarget.RecoveryOverview),
            "account-inventory" when context.AccountCount == 0 => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ImportAccounts,
                NextUserTaskTarget.CsvImport),
            "account-inventory" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ReviewAccountCategories,
                NextUserTaskTarget.AccountTriage,
                RecoveryWizardStepId.AccountTriage),
            "account-triage" when context.UncategorizedAccountCount > 0 => Task(
                state,
                NextUserTaskState.OptionalWorkMayContinue,
                NextUserTaskCode.ContinueCategoryReviewOrRecovery,
                NextUserTaskTarget.RecoveryOverview,
                RecoveryWizardStepId.RecoveryOverview),
            "account-triage" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ContinueToRecovery,
                NextUserTaskTarget.RecoveryOverview,
                RecoveryWizardStepId.RecoveryOverview),
            "recovery-overview" when context.HasOutstandingAccountWork => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.StartAccountRecovery,
                NextUserTaskTarget.AccountRecovery,
                accountId: context.RecommendedAccountId,
                actionId: context.RecommendedActionId),
            "recovery-overview" when context.HasPendingCredentialHandoff => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.HandleCredentialHandoff,
                NextUserTaskTarget.CredentialHandoff,
                RecoveryWizardStepId.CredentialExport),
            "recovery-overview" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ReviewCompletion,
                NextUserTaskTarget.CompletionReview,
                RecoveryWizardStepId.CompletionPreflight),
            "credential-export" => Task(
                state,
                context.HasPendingCredentialHandoff
                    ? NextUserTaskState.OptionalWorkMayContinue
                    : NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ReviewCompletion,
                NextUserTaskTarget.CompletionReview,
                RecoveryWizardStepId.CompletionPreflight),
            "completion-preflight" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ConfirmCompletionOutcome,
                NextUserTaskTarget.CompletionReview,
                RecoveryWizardStepId.FinalReport),
            "final-report" => Task(
                state,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ConfirmCompletionOutcome,
                NextUserTaskTarget.CompletionReview),
            _ => throw new InvalidOperationException(
                $"No next user task exists for recovery step '{state.CurrentStep}'."),
        };
    }

    private static NextUserTask Task(
        RecoveryWizardState state,
        NextUserTaskState taskState,
        NextUserTaskCode code,
        NextUserTaskTarget target,
        RecoveryWizardStepId? transitionStep = null,
        Guid? accountId = null,
        string? actionId = null) =>
        new(state.CurrentStep, taskState, code, target, transitionStep, accountId, actionId);

    private static NextUserTaskTarget TargetFor(RecoveryWizardStepId step) => step.Value switch
    {
        "welcome" or "trusted-device-check" => NextUserTaskTarget.TrustedDeviceCheck,
        "trusted-device-guidance" => NextUserTaskTarget.TrustedDeviceGuidance,
        "vault-entry" => NextUserTaskTarget.VaultEntry,
        "incident-intake" or "recovery-overview" => NextUserTaskTarget.RecoveryOverview,
        "account-inventory" => NextUserTaskTarget.CsvImport,
        "account-triage" => NextUserTaskTarget.AccountTriage,
        "credential-export" => NextUserTaskTarget.CredentialHandoff,
        "completion-preflight" or "final-report" => NextUserTaskTarget.CompletionReview,
        _ => throw new InvalidOperationException($"No workspace target exists for recovery step '{step}'."),
    };

    private static void Validate(RecoveryFlowContext context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(context.AccountCount);
        ArgumentOutOfRangeException.ThrowIfNegative(context.UncategorizedAccountCount);
        if (context.UncategorizedAccountCount > context.AccountCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "Uncategorized accounts cannot exceed all accounts.");
        }
    }
}
