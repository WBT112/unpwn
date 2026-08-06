using Unpwn.Core;

namespace Unpwn.Application;

public enum RecoveryWizardRecommendationCode
{
    WelcomeUser,
    ConfirmTrustedDevice,
    MoveToTrustedDevice,
    CreateOrUnlockVault,
    CaptureIncidentContext,
    ReviewAccountInventory,
    ConfirmIdentityAndRecoveryRelationships,
    ReviewRecoveryPlan,
    RecoverRecommendedAccount,
    ExportGeneratedCredentials,
    RunCompletionPreflight,
    ReviewFinalReport,
    ResumeWizard,
    UnlockVault,
    NoFurtherAction,
}

public sealed record RecoveryWizardRecommendation(
    RecoveryWizardStepId StepId,
    RecoveryWizardRecommendationCode ReasonCode);

public sealed class RecoveryWizardOrchestrator
{
    public RecoveryWizardState Start(Guid wizardId, DateTimeOffset createdAt) =>
        RecoveryWizardState.Create(wizardId, createdAt);

    public RecoveryWizardState Continue(
        RecoveryWizardState state,
        RecoveryWizardStepId nextStep,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Advance(state, nextStep, occurredAt);

    public RecoveryWizardState GoBack(
        RecoveryWizardState state,
        RecoveryWizardStepId previousStep,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.GoBack(state, previousStep, occurredAt);

    public RecoveryWizardState RecordTrustedDeviceDecision(
        RecoveryWizardState state,
        TrustedDeviceDecision decision,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.RecordTrustedDeviceDecision(state, decision, occurredAt);

    public RecoveryWizardState StopAfterTrustedDeviceGuidance(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.AcknowledgeTrustedDeviceGuidance(state, occurredAt);

    public RecoveryWizardState ConfirmVaultReady(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.ConfirmVaultReady(state, occurredAt);

    public RecoveryWizardState Pause(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Pause(state, occurredAt);

    public RecoveryWizardState Lock(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Lock(state, occurredAt);

    public RecoveryWizardState Resume(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Resume(state, occurredAt);

    public RecoveryWizardState Cancel(
        RecoveryWizardState state,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Cancel(state, occurredAt);

    public RecoveryWizardState Finish(
        RecoveryWizardState state,
        RecoveryWizardTerminalOutcome outcome,
        DateTimeOffset occurredAt) =>
        RecoveryWizardStateMachine.Finish(state, outcome, occurredAt);

    public RecoveryWizardRecommendation GetRecommendation(RecoveryWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status == RecoveryWizardLifecycleStatus.Paused)
        {
            return new RecoveryWizardRecommendation(
                state.ResumeStep,
                RecoveryWizardRecommendationCode.ResumeWizard);
        }

        if (state.Status == RecoveryWizardLifecycleStatus.Locked)
        {
            return new RecoveryWizardRecommendation(
                state.ResumeStep,
                RecoveryWizardRecommendationCode.UnlockVault);
        }

        if (state.IsTerminal)
        {
            return new RecoveryWizardRecommendation(
                state.CurrentStep,
                RecoveryWizardRecommendationCode.NoFurtherAction);
        }

        var reasonCode = state.CurrentStep.Value switch
        {
            "welcome" => RecoveryWizardRecommendationCode.WelcomeUser,
            "trusted-device-check" => RecoveryWizardRecommendationCode.ConfirmTrustedDevice,
            "trusted-device-guidance" => RecoveryWizardRecommendationCode.MoveToTrustedDevice,
            "vault-entry" => RecoveryWizardRecommendationCode.CreateOrUnlockVault,
            "incident-intake" => RecoveryWizardRecommendationCode.CaptureIncidentContext,
            "account-inventory" => RecoveryWizardRecommendationCode.ReviewAccountInventory,
            "identity-review" => RecoveryWizardRecommendationCode.ConfirmIdentityAndRecoveryRelationships,
            "recovery-plan" => RecoveryWizardRecommendationCode.ReviewRecoveryPlan,
            "account-recovery" => RecoveryWizardRecommendationCode.RecoverRecommendedAccount,
            "credential-export" => RecoveryWizardRecommendationCode.ExportGeneratedCredentials,
            "completion-preflight" => RecoveryWizardRecommendationCode.RunCompletionPreflight,
            "final-report" => RecoveryWizardRecommendationCode.ReviewFinalReport,
            _ => throw new InvalidOperationException($"No recommendation exists for recovery wizard step '{state.CurrentStep}'."),
        };

        return new RecoveryWizardRecommendation(state.CurrentStep, reasonCode);
    }
}
