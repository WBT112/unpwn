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

    [Theory]
    [InlineData("recovery-plan")]
    [InlineData("account-recovery")]
    public void RemovedDevelopmentStepIdentifiersFailClosed(string obsoleteStep)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryWizardStepId.Parse(obsoleteStep));
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

    [Fact]
    public void UntrustedOrUncertainDeviceStopsBeforeVaultAccess()
    {
        var state = StartAtTrustedDeviceCheck();
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.NotTrusted,
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
            RecoveryWizardStepId.AccountTriage,
            StartTime.AddMinutes(5));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.RecoveryOverview,
            StartTime.AddMinutes(6));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.CredentialExport,
            StartTime.AddMinutes(7));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.CompletionPreflight,
            StartTime.AddMinutes(8));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.FinalReport,
            StartTime.AddMinutes(9));
        state = RecoveryWizardOrchestrator.Finish(
            state,
            RecoveryWizardTerminalOutcome.Completed,
            StartTime.AddMinutes(10));

        Assert.True(state.IsTerminal);
        Assert.True(state.HasVaultContext);
        Assert.Equal(RecoveryWizardLifecycleStatus.Completed, state.Status);
        Assert.Equal(RecoveryWizardStepId.FinalReport, state.CurrentStep);
        Assert.Equal(10, state.Revision);
    }

    [Fact]
    public void PausingDuringRecoveryResumesAtTheRecoveryOverview()
    {
        var state = StartAtRecoveryOverview();

        state = RecoveryWizardOrchestrator.Pause(state, StartTime.AddMinutes(7));

        Assert.Equal(RecoveryWizardLifecycleStatus.Paused, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryOverview, state.ResumeStep);

        state = RecoveryWizardOrchestrator.Resume(state, StartTime.AddMinutes(8));

        Assert.Equal(RecoveryWizardLifecycleStatus.Active, state.Status);
        Assert.Equal(RecoveryWizardStepId.RecoveryOverview, state.CurrentStep);
    }

    [Fact]
    public void LockingDuringFinalReportRequiresCompletionPreflightAgain()
    {
        var state = StartAtRecoveryOverview();
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

    [Theory]
    [InlineData(RecoveryWizardTerminalOutcome.Completed, RecoveryWizardLifecycleStatus.Completed)]
    [InlineData(RecoveryWizardTerminalOutcome.Archived, RecoveryWizardLifecycleStatus.Archived)]
    [InlineData(RecoveryWizardTerminalOutcome.FollowUpRequired, RecoveryWizardLifecycleStatus.FollowUpRequired)]
    public void EveryTerminalOutcomeMapsToItsLifecycleStatus(
        RecoveryWizardTerminalOutcome outcome,
        RecoveryWizardLifecycleStatus expected)
    {
        var state = StartAtRecoveryOverview();
        state = RecoveryWizardOrchestrator.BeginCompletionReview(state, StartTime.AddMinutes(7));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.FinalReport,
            StartTime.AddMinutes(8));

        state = RecoveryWizardOrchestrator.Finish(state, outcome, StartTime.AddMinutes(9));

        Assert.Equal(expected, state.Status);
    }

    [Fact]
    public void InvalidWizardInputsFailClosed()
    {
        var initial = RecoveryWizardOrchestrator.Start(Guid.NewGuid(), StartTime);
        var trustedCheck = RecoveryWizardOrchestrator.Continue(
            initial,
            RecoveryWizardStepId.TrustedDeviceCheck,
            StartTime.AddMinutes(1));
        var withVault = StartWithVaultContext();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
                trustedCheck,
                TrustedDeviceDecision.NotAnswered,
                StartTime.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Continue(
                trustedCheck,
                RecoveryWizardStepId.VaultEntry,
                StartTime.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Continue(
                withVault with { CurrentStep = RecoveryWizardStepId.VaultEntry },
                RecoveryWizardStepId.IncidentIntake,
                StartTime.AddMinutes(4)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryWizardOrchestrator.Finish(
                withVault with { CurrentStep = RecoveryWizardStepId.FinalReport },
                (RecoveryWizardTerminalOutcome)int.MaxValue,
                StartTime.AddMinutes(4)));
        Assert.Throws<ArgumentException>(() =>
            RecoveryWizardOrchestrator.Start(Guid.Empty, StartTime));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryWizardStepId.Parse("unknown-step"));
    }

    [Fact]
    public void PauseLockResumeCancelAndArchiveCoverEverySupportedLifecycle()
    {
        var active = StartWithVaultContext();
        var paused = RecoveryWizardOrchestrator.Pause(active, StartTime.AddMinutes(4));
        var lockedFromPause = RecoveryWizardOrchestrator.Lock(paused, StartTime.AddMinutes(5));
        var resumed = RecoveryWizardOrchestrator.Resume(lockedFromPause, StartTime.AddMinutes(6));
        var archivedActive = RecoveryWizardOrchestrator.Archive(resumed, StartTime.AddMinutes(7));
        var archivedPaused = RecoveryWizardOrchestrator.Archive(paused, StartTime.AddMinutes(5));
        var cancelledPaused = RecoveryWizardOrchestrator.Cancel(paused, StartTime.AddMinutes(5));
        var cancelledLocked = RecoveryWizardOrchestrator.Cancel(lockedFromPause, StartTime.AddMinutes(6));

        Assert.Equal(RecoveryWizardLifecycleStatus.Archived, archivedActive.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Archived, archivedPaused.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Cancelled, cancelledPaused.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Cancelled, cancelledLocked.Status);
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Resume(active, StartTime.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Archive(lockedFromPause, StartTime.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryWizardOrchestrator.Cancel(archivedActive, StartTime.AddMinutes(8)));
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

    private static RecoveryWizardState StartAtRecoveryOverview()
    {
        var state = StartWithVaultContext();
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountInventory,
            StartTime.AddMinutes(4));
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountTriage,
            StartTime.AddMinutes(5));
        return RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.RecoveryOverview,
            StartTime.AddMinutes(6));
    }
}
