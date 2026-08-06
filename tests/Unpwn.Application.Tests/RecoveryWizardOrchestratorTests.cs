using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class RecoveryWizardOrchestratorTests
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UnixEpoch;
    private readonly RecoveryWizardOrchestrator _orchestrator = new();

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
        var state = _orchestrator.Start(Guid.NewGuid(), StartTime);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _orchestrator.Continue(state, RecoveryWizardStepId.VaultEntry, StartTime.AddMinutes(1)));

        Assert.Contains("welcome", exception.Message, StringComparison.Ordinal);
        Assert.False(state.HasVaultContext);
    }

    [Theory]
    [InlineData(TrustedDeviceDecision.NotTrusted)]
    [InlineData(TrustedDeviceDecision.Unsure)]
    public void UntrustedOrUnsureDeviceStopsBeforeVaultAccess(TrustedDeviceDecision decision)
    {
        var state = StartAtTrustedDeviceCheck();
        state = _orchestrator.RecordTrustedDeviceDecision(state, decision, StartTime.AddMinutes(2));

        Assert.Equal(RecoveryWizardStepId.TrustedDeviceGuidance, state.CurrentStep);
        Assert.False(state.HasVaultContext);
        Assert.Throws<InvalidOperationException>(() =>
            _orchestrator.ConfirmVaultReady(state, StartTime.AddMinutes(3)));

        state = _orchestrator.StopAfterTrustedDeviceGuidance(state, StartTime.AddMinutes(4));

        Assert.True(state.IsTerminal);
        Assert.Equal(RecoveryWizardLifecycleStatus.StoppedForDeviceSafety, state.Status);
        Assert.False(state.HasVaultContext);
        Assert.Equal(
            RecoveryWizardRecommendationCode.NoFurtherAction,
            _orchestrator.GetRecommendation(state).ReasonCode);
    }

    [Fact]
    public void GoingBackToTrustedDeviceGateClearsThePreviousDecision()
    {
        var state = StartAtTrustedDeviceCheck();
        state = _orchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            StartTime.AddMinutes(2));

        state = _orchestrator.GoBack(
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
        state = _orchestrator.Continue(state, RecoveryWizardStepId.AccountInventory, StartTime.AddMinutes(4));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.IdentityReview, StartTime.AddMinutes(5));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.RecoveryPlan, StartTime.AddMinutes(6));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.AccountRecovery, StartTime.AddMinutes(7));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.RecoveryPlan, StartTime.AddMinutes(8));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.CredentialExport, StartTime.AddMinutes(9));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.CompletionPreflight, StartTime.AddMinutes(10));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.FinalReport, StartTime.AddMinutes(11));
        state = _orchestrator.Finish(
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
        state = _orchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountRecovery,
            StartTime.AddMinutes(7));

        state = _orchestrator.Pause(state, StartTime.AddMinutes(8));

        Assert.Equal(RecoveryWizardLifecycleStatus.Paused, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, state.ResumeStep);
        Assert.Equal(
            RecoveryWizardRecommendationCode.ResumeWizard,
            _orchestrator.GetRecommendation(state).ReasonCode);

        state = _orchestrator.Resume(state, StartTime.AddMinutes(9));

        Assert.Equal(RecoveryWizardLifecycleStatus.Active, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, state.CurrentStep);
    }

    [Fact]
    public void LockingDuringFinalReportRequiresCompletionPreflightAgain()
    {
        var state = StartAtRecoveryPlan();
        state = _orchestrator.Continue(state, RecoveryWizardStepId.CompletionPreflight, StartTime.AddMinutes(7));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.FinalReport, StartTime.AddMinutes(8));

        state = _orchestrator.Lock(state, StartTime.AddMinutes(9));

        Assert.Equal(RecoveryWizardLifecycleStatus.Locked, state.Status);
        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, state.ResumeStep);
        Assert.Equal(
            RecoveryWizardRecommendationCode.UnlockVault,
            _orchestrator.GetRecommendation(state).ReasonCode);

        state = _orchestrator.Resume(state, StartTime.AddMinutes(10));

        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, state.CurrentStep);
    }

    [Fact]
    public void PreVaultWizardCannotBePausedOrLocked()
    {
        var state = StartAtTrustedDeviceCheck();

        Assert.Throws<InvalidOperationException>(() =>
            _orchestrator.Pause(state, StartTime.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            _orchestrator.Lock(state, StartTime.AddMinutes(2)));
    }

    [Fact]
    public void TransitionsCannotMoveBackwardsInTime()
    {
        var state = StartAtTrustedDeviceCheck();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _orchestrator.RecordTrustedDeviceDecision(
                state,
                TrustedDeviceDecision.Trusted,
                StartTime.AddSeconds(-1)));
    }

    [Fact]
    public void RecommendationsExposeStableCodesInsteadOfDisplayText()
    {
        var state = StartWithVaultContext();
        var recommendation = _orchestrator.GetRecommendation(state);

        Assert.Equal(RecoveryWizardStepId.IncidentIntake, recommendation.StepId);
        Assert.Equal(RecoveryWizardRecommendationCode.CaptureIncidentContext, recommendation.ReasonCode);
    }

    private RecoveryWizardState StartAtTrustedDeviceCheck()
    {
        var state = _orchestrator.Start(Guid.NewGuid(), StartTime);
        return _orchestrator.Continue(
            state,
            RecoveryWizardStepId.TrustedDeviceCheck,
            StartTime.AddMinutes(1));
    }

    private RecoveryWizardState StartWithVaultContext()
    {
        var state = StartAtTrustedDeviceCheck();
        state = _orchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            StartTime.AddMinutes(2));
        return _orchestrator.ConfirmVaultReady(state, StartTime.AddMinutes(3));
    }

    private RecoveryWizardState StartAtRecoveryPlan()
    {
        var state = StartWithVaultContext();
        state = _orchestrator.Continue(state, RecoveryWizardStepId.AccountInventory, StartTime.AddMinutes(4));
        state = _orchestrator.Continue(state, RecoveryWizardStepId.IdentityReview, StartTime.AddMinutes(5));
        return _orchestrator.Continue(state, RecoveryWizardStepId.RecoveryPlan, StartTime.AddMinutes(6));
    }
}
