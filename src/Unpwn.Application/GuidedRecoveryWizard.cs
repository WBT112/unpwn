using Unpwn.Core;

namespace Unpwn.Application;

public enum GuidedRecoveryBlockCode
{
    None,
    AccountsRequired,
    Paused,
    Terminal,
    UnsupportedStep,
}

public enum GuidedRecoveryAdvisoryCode
{
    None,
    RemainingCategoryReviewOptional,
    ContinueWithoutEmailCategory,
}

public sealed record GuidedRecoveryContext(
    int AccountCount,
    int UncategorizedAccountCount,
    bool HasConfirmedEmailCategory,
    bool HasOutstandingAccountWork,
    bool HasPendingCredentialHandoff,
    Guid? RecommendedAccountId = null,
    string? RecommendedActionId = null);

public sealed record GuidedRecoveryDecision(
    RecoveryWizardStepId CurrentStep,
    RecoveryWizardStepId? TargetStep,
    GuidedRecoveryBlockCode BlockCode,
    Guid? AccountId = null,
    string? ActionId = null,
    GuidedRecoveryAdvisoryCode AdvisoryCode = GuidedRecoveryAdvisoryCode.None)
{
    public bool CanMove => TargetStep is not null && BlockCode == GuidedRecoveryBlockCode.None;
}

/// <summary>
/// Calculates the next guided step exclusively from language-neutral recovery state.
/// Persisting the returned step remains the responsibility of the application shell.
/// </summary>
public static class GuidedRecoveryWizard
{
    public static GuidedRecoveryDecision GetNext(
        RecoveryWizardState state,
        GuidedRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        Validate(context);

        if (state.IsTerminal)
        {
            return Blocked(state, GuidedRecoveryBlockCode.Terminal);
        }

        if (state.Status != RecoveryWizardLifecycleStatus.Active)
        {
            return Blocked(state, GuidedRecoveryBlockCode.Paused);
        }

        return state.CurrentStep.Value switch
        {
            "account-inventory" when context.AccountCount == 0 =>
                Blocked(state, GuidedRecoveryBlockCode.AccountsRequired),
            "account-inventory" => Move(state, RecoveryWizardStepId.AccountTriage),
            "account-triage" when context.HasConfirmedEmailCategory && context.UncategorizedAccountCount > 0 =>
                Move(
                    state,
                    RecoveryWizardStepId.RecoveryPlan,
                    advisoryCode: GuidedRecoveryAdvisoryCode.RemainingCategoryReviewOptional),
            "account-triage" when !context.HasConfirmedEmailCategory && context.UncategorizedAccountCount > 0 =>
                Move(
                    state,
                    RecoveryWizardStepId.RecoveryPlan,
                    advisoryCode: GuidedRecoveryAdvisoryCode.ContinueWithoutEmailCategory),
            "account-triage" => Move(state, RecoveryWizardStepId.RecoveryPlan),
            "recovery-plan" when context.HasOutstandingAccountWork =>
                Move(
                    state,
                    RecoveryWizardStepId.AccountRecovery,
                    context.RecommendedAccountId,
                    context.RecommendedActionId),
            "recovery-plan" when context.HasPendingCredentialHandoff =>
                Move(state, RecoveryWizardStepId.CredentialExport),
            "recovery-plan" => Move(state, RecoveryWizardStepId.CompletionPreflight),
            "account-recovery" => Move(state, RecoveryWizardStepId.RecoveryPlan),
            "credential-export" => Move(state, RecoveryWizardStepId.CompletionPreflight),
            "completion-preflight" => Move(state, RecoveryWizardStepId.FinalReport),
            _ => Blocked(state, GuidedRecoveryBlockCode.UnsupportedStep),
        };
    }

    public static GuidedRecoveryDecision GetPrevious(RecoveryWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsTerminal)
        {
            return Blocked(state, GuidedRecoveryBlockCode.Terminal);
        }

        if (state.Status != RecoveryWizardLifecycleStatus.Active)
        {
            return Blocked(state, GuidedRecoveryBlockCode.Paused);
        }

        var previous = state.CurrentStep.Value switch
        {
            "account-triage" => RecoveryWizardStepId.AccountInventory,
            "recovery-plan" => RecoveryWizardStepId.AccountTriage,
            "account-recovery" => RecoveryWizardStepId.RecoveryPlan,
            "credential-export" => RecoveryWizardStepId.RecoveryPlan,
            "completion-preflight" => RecoveryWizardStepId.RecoveryPlan,
            "final-report" => RecoveryWizardStepId.CompletionPreflight,
            _ => null,
        };
        return previous is null
            ? Blocked(state, GuidedRecoveryBlockCode.UnsupportedStep)
            : Move(state, previous);
    }

    private static GuidedRecoveryDecision Move(
        RecoveryWizardState state,
        RecoveryWizardStepId target,
        Guid? accountId = null,
        string? actionId = null,
        GuidedRecoveryAdvisoryCode advisoryCode = GuidedRecoveryAdvisoryCode.None) =>
        new(state.CurrentStep, target, GuidedRecoveryBlockCode.None, accountId, actionId, advisoryCode);

    private static GuidedRecoveryDecision Blocked(
        RecoveryWizardState state,
        GuidedRecoveryBlockCode code) =>
        new(state.CurrentStep, null, code);

    private static void Validate(GuidedRecoveryContext context)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(context.AccountCount);
        ArgumentOutOfRangeException.ThrowIfNegative(context.UncategorizedAccountCount);
        if (context.UncategorizedAccountCount > context.AccountCount)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Uncategorized accounts cannot exceed all accounts.");
        }
    }
}
