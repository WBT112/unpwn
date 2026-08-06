using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class RecoveryWizardOrchestratorTests
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UnixEpoch;

    [Fact]
    public void StepIdentifiersAreStableUniqueAndRoundTrip()
    {
        var values = RecoveryWizardStepId.All.Select(step => step.Value).ToArray();

        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            RecoveryWizardStepId.All,
            step => Assert.Equal(step, RecoveryWizardStepId.Parse(step.Value)));
    }

    [Fact]
    public void SensitiveStepsRequireAnUnlockedVaultContext()
    {
        Assert.All(
            RecoveryWizardStepCatalog.All.Where(contract => contract.MayCollectSensitiveData),
            contract => Assert.True(contract.RequiresUnlockedVault));
    }

    [Fact]
    public void TrustedDeviceGateCannotBeSkipped()
    {
        var state = RecoveryWizardOrchestrator.Start(Guid.NewGuid(), StartTime);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Continue(
                state,
                RecoveryWizardStepId.VaultEntry,
                StartTime.AddMinutes(1)));

        Assert.Contains("welcome", exception.Message, StringComparison.Ordinal);
        Assert.False(state.HasVaultContext);
    }

    [Theory]
    [InlineData(TrustedDeviceDecision.NotTrusted)]
    [InlineData(TrustedDeviceDecision.Unsure)]
    public void UntrustedOrUnsureDeviceStopsBeforeVaultAccess(TrustedDeviceDecision decision)
    {
        var state = StartAtTrustedDeviceCheck();
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            decision,
            StartTime.AddMinutes(2));

        Assert.Equal(RecoveryWizardStepId.TrustedDeviceGuidance, state.CurrentStep);
        Assert.False(state.HasVaultContext);
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.ConfirmVaultReady(state, StartTime.AddMinutes(3)));

        state = RecoveryWizardOrchestrator.StopAfterTrustedDeviceGuidance(
            state,
            StartTime.AddMinutes(4));

        Assert.True(state.IsTerminal);
        Assert.Equal(RecoveryWizardLifecycleStatus.StoppedForDeviceSafety, state.Status);
        Assert.False(state.HasVaultContext);
        Assert.Equal(
            RecoveryWizardRecommendationCode.NoFurtherAction,
            RecoveryWizardOrchestrator.GetRecommendation(state).ReasonCode);
    }

    [Fact]
    public void GoingBackToTrustedDeviceGateClearsThePreviousDecision()
    {
        var state = StartAtTrustedDeviceCheck();
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            StartTime.AddMinutes(2));

        state = RecoveryWizardOrchestrator.GoBack(
            state,
            RecoveryWizardStepId.TrustedDeviceCheck,
            StartTime.AddMinutes(3));

        Assert.Equal(TrustedDeviceDecision.NotAnswered, state.TrustedDeviceDecision);
        Assert.Equal(RecoveryWizardStepId.TrustedDeviceCheck, state.CurrentStep);
        Assert.False(state.HasVaultContext);
    }

    [Fact]
    public void HappyPathReachesACompletedTerminalState()
    {
        var state = StartWithVaultContext();
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountInventory,
            StartTime.AddMinutes(4));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.IdentityReview,
            StartTime.AddMinutes(5));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.RecoveryPlan,
            StartTime.AddMinutes(6));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountRecovery,
            StartTime.AddMinutes(7));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.RecoveryPlan,
            StartTime.AddMinutes(8));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.CredentialExport,
            StartTime.AddMinutes(9));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.CompletionPreflight,
            StartTime.AddMinutes(10));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.FinalReport,
            StartTime.AddMinutes(11));
        state = RecoveryWizardOrchestrator.Finish(
            state,
            RecoveryWizardTerminalOutcome.Completed,
            StartTime.AddMinutes(12));

        Assert.True(state.IsTerminal);
        Assert.True(state.HasVaultContext);
        Assert.Equal(RecoveryWizardLifecycleStatus.Completed, state.Status);
        Assert.Equal(RecoveryWizardStepId.FinalReport, state.CurrentStep);
        Assert.Equal(12, state.Revision);
    }

    [Fact]
    public void PausingDuringExternalAccountRecoveryResumesAtTheRecoveryPlan()
    {
        var state = StartAtRecoveryPlan();
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountRecovery,
            StartTime.AddMinutes(7));

        state = RecoveryWizardOrchestrator.Pause(state, StartTime.AddMinutes(8));

        Assert.Equal(RecoveryWizardLifecycleStatus.Paused, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, state.ResumeStep);
        Assert.Equal(
            RecoveryWizardRecommendationCode.ResumeWizard,
            RecoveryWizardOrchestrator.GetRecommendation(state).ReasonCode);

        state = RecoveryWizardOrchestrator.Resume(state, StartTime.AddMinutes(9));

        Assert.Equal(RecoveryWizardLifecycleStatus.Active, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, state.CurrentStep);
    }

    [Fact]
    public void LockingDuringFinalReportRequiresCompletionPreflightAgain()
    {
        var state = StartAtRecoveryPlan();
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.CompletionPreflight,
            StartTime.AddMinutes(7));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.FinalReport,
            StartTime.AddMinutes(8));

        state = RecoveryWizardOrchestrator.Lock(state, StartTime.AddMinutes(9));

        Assert.Equal(RecoveryWizardLifecycleStatus.Locked, state.Status);
        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, state.ResumeStep);
        Assert.Equal(
            RecoveryWizardRecommendationCode.UnlockVault,
            RecoveryWizardOrchestrator.GetRecommendation(state).ReasonCode);

        state = RecoveryWizardOrchestrator.Resume(state, StartTime.AddMinutes(10));

        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, state.CurrentStep);
    }

    [Fact]
    public void PreVaultWizardCannotBePausedOrLocked()
    {
        var state = StartAtTrustedDeviceCheck();

        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Pause(state, StartTime.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Lock(state, StartTime.AddMinutes(2)));
    }

    [Fact]
    public void TransitionsCannotMoveBackwardsInTime()
    {
        var state = StartAtTrustedDeviceCheck();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
                state,
                TrustedDeviceDecision.Trusted,
                StartTime.AddSeconds(-1)));
    }

    [Fact]
    public void RecommendationsExposeStableCodesInsteadOfDisplayText()
    {
        var state = StartWithVaultContext();
        var recommendation = RecoveryWizardOrchestrator.GetRecommendation(state);

        Assert.Equal(RecoveryWizardStepId.IncidentIntake, recommendation.StepId);
        Assert.Equal(RecoveryWizardRecommendationCode.CaptureIncidentContext, recommendation.ReasonCode);
    }

    private static RecoveryWizardState StartAtTrustedDeviceCheck()
    {
        var state = RecoveryWizardOrchestrator.Start(Guid.NewGuid(), StartTime);
        return RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.TrustedDeviceCheck,
            StartTime.AddMinutes(1));
    }

    private static RecoveryWizardState StartWithVaultContext()
    {
        var state = StartAtTrustedDeviceCheck();
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            StartTime.AddMinutes(2));
        return RecoveryWizardOrchestrator.ConfirmVaultReady(state, StartTime.AddMinutes(3));
    }

    private static RecoveryWizardState StartAtRecoveryPlan()
    {
        var state = StartWithVaultContext();
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountInventory,
            StartTime.AddMinutes(4));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.IdentityReview,
            StartTime.AddMinutes(5));
        return RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.RecoveryPlan,
            StartTime.AddMinutes(6));
    }
}
