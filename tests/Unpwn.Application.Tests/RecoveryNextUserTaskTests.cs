using System.Globalization;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class RecoveryNextUserTaskTests
{
    [Theory]
    [InlineData("welcome", NextUserTaskCode.BeginTrustedDeviceCheck, NextUserTaskTarget.TrustedDeviceCheck)]
    [InlineData("trusted-device-check", NextUserTaskCode.ConfirmTrustedDevice, NextUserTaskTarget.TrustedDeviceCheck)]
    [InlineData("trusted-device-guidance", NextUserTaskCode.MoveToTrustedDevice, NextUserTaskTarget.TrustedDeviceGuidance)]
    [InlineData("vault-entry", NextUserTaskCode.CreateOrUnlockVault, NextUserTaskTarget.VaultEntry)]
    [InlineData("incident-intake", NextUserTaskCode.CreateRecoverySession, NextUserTaskTarget.RecoveryOverview)]
    [InlineData("account-inventory", NextUserTaskCode.ImportAccounts, NextUserTaskTarget.CsvImport)]
    [InlineData("account-triage", NextUserTaskCode.ContinueToRecovery, NextUserTaskTarget.RecoveryOverview)]
    [InlineData("recovery-overview", NextUserTaskCode.ReviewCompletion, NextUserTaskTarget.CompletionReview)]
    [InlineData("credential-export", NextUserTaskCode.ReviewCompletion, NextUserTaskTarget.CompletionReview)]
    [InlineData("completion-preflight", NextUserTaskCode.ConfirmCompletionOutcome, NextUserTaskTarget.CompletionReview)]
    [InlineData("final-report", NextUserTaskCode.ConfirmCompletionOutcome, NextUserTaskTarget.CompletionReview)]
    public void EveryActiveStepProjectsExactlyOneConcreteTask(
        string stepValue,
        NextUserTaskCode expectedCode,
        NextUserTaskTarget expectedTarget)
    {
        var task = RecoveryNextUserTask.Project(
            ActiveAt(RecoveryWizardStepId.Parse(stepValue)),
            Context());

        Assert.Equal(expectedCode, task.Code);
        Assert.Equal(expectedTarget, task.Target);
        Assert.Equal(RecoveryWizardStepId.Parse(stepValue), task.CurrentStep);
    }

    [Fact]
    public void ImportedAccountsLeadToCategoryReview()
    {
        var task = RecoveryNextUserTask.Project(
            ActiveAt(RecoveryWizardStepId.AccountInventory),
            Context(accountCount: 2, uncategorized: 2));

        Assert.Equal(NextUserTaskCode.ReviewAccountCategories, task.Code);
        Assert.Equal(NextUserTaskTarget.AccountTriage, task.Target);
        Assert.Equal(RecoveryWizardStepId.AccountTriage, task.TransitionStep);
    }

    [Fact]
    public void CategoryReviewSupportsEarlyAndCompleteContinuation()
    {
        var state = ActiveAt(RecoveryWizardStepId.AccountTriage);

        var early = RecoveryNextUserTask.Project(
            state,
            Context(accountCount: 3, uncategorized: 2));
        var complete = RecoveryNextUserTask.Project(
            state,
            Context(accountCount: 3));

        Assert.Equal(NextUserTaskState.OptionalWorkMayContinue, early.State);
        Assert.Equal(NextUserTaskCode.ContinueCategoryReviewOrRecovery, early.Code);
        Assert.Equal(RecoveryWizardStepId.RecoveryOverview, early.TransitionStep);
        Assert.Equal(NextUserTaskState.ActionAvailable, complete.State);
        Assert.Equal(NextUserTaskCode.ContinueToRecovery, complete.Code);
        Assert.Equal(RecoveryWizardStepId.RecoveryOverview, complete.TransitionStep);
    }

    [Fact]
    public void RecoveryOverviewUsesCurrentCanonicalWorkAndCredentialState()
    {
        var state = ActiveAt(RecoveryWizardStepId.RecoveryOverview);
        var accountId = Guid.NewGuid();

        var recovery = RecoveryNextUserTask.Project(
            state,
            Context(
                accountCount: 1,
                outstanding: true,
                accountId: accountId,
                actionId: "change-password"));
        var credentials = RecoveryNextUserTask.Project(
            state,
            Context(accountCount: 1, credentials: true));
        var completion = RecoveryNextUserTask.Project(
            state,
            Context(accountCount: 1));

        Assert.Equal(NextUserTaskTarget.AccountRecovery, recovery.Target);
        Assert.Equal(accountId, recovery.AccountId);
        Assert.Equal("change-password", recovery.ActionId);
        Assert.False(recovery.RequiresTransition);
        Assert.Equal(RecoveryWizardStepId.CredentialExport, credentials.TransitionStep);
        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, completion.TransitionStep);
    }

    [Fact]
    public void ResumeBlockedAndTerminalStatesRemainExplicit()
    {
        var active = ActiveAt(RecoveryWizardStepId.AccountTriage);
        var paused = active with { Status = RecoveryWizardLifecycleStatus.Paused };
        var locked = active with { Status = RecoveryWizardLifecycleStatus.Locked };
        var terminal = active with { Status = RecoveryWizardLifecycleStatus.FollowUpRequired };

        var resumeTask = RecoveryNextUserTask.Project(paused, Context(accountCount: 1));
        var unlockTask = RecoveryNextUserTask.Project(locked, Context(accountCount: 1));
        var reportTask = RecoveryNextUserTask.Project(terminal, Context(accountCount: 1));

        Assert.Equal((NextUserTaskState.Blocked, NextUserTaskCode.ResumeSession, NextUserTaskTarget.AccountTriage),
            (resumeTask.State, resumeTask.Code, resumeTask.Target));
        Assert.Equal((NextUserTaskState.Blocked, NextUserTaskCode.UnlockVault, NextUserTaskTarget.VaultEntry),
            (unlockTask.State, unlockTask.Code, unlockTask.Target));
        Assert.Equal((NextUserTaskState.TerminalReadOnly, NextUserTaskCode.ReadOnlyReport, NextUserTaskTarget.CompletionReview),
            (reportTask.State, reportTask.Code, reportTask.Target));
    }

    [Theory]
    [InlineData("welcome", NextUserTaskTarget.TrustedDeviceCheck)]
    [InlineData("trusted-device-check", NextUserTaskTarget.TrustedDeviceCheck)]
    [InlineData("trusted-device-guidance", NextUserTaskTarget.TrustedDeviceGuidance)]
    [InlineData("vault-entry", NextUserTaskTarget.VaultEntry)]
    [InlineData("incident-intake", NextUserTaskTarget.RecoveryOverview)]
    [InlineData("account-inventory", NextUserTaskTarget.CsvImport)]
    [InlineData("account-triage", NextUserTaskTarget.AccountTriage)]
    [InlineData("recovery-overview", NextUserTaskTarget.RecoveryOverview)]
    [InlineData("credential-export", NextUserTaskTarget.CredentialHandoff)]
    [InlineData("completion-preflight", NextUserTaskTarget.CompletionReview)]
    [InlineData("final-report", NextUserTaskTarget.CompletionReview)]
    public void PausedStateProjectsTheConcreteSafeResumeWorkspace(
        string resumeStepValue,
        NextUserTaskTarget expectedTarget)
    {
        var resumeStep = RecoveryWizardStepId.Parse(resumeStepValue);
        var state = ActiveAt(resumeStep) with
        {
            Status = RecoveryWizardLifecycleStatus.Paused,
            ResumeStep = resumeStep,
        };

        var task = RecoveryNextUserTask.Project(state, Context(accountCount: 1));

        Assert.Equal(NextUserTaskCode.ResumeSession, task.Code);
        Assert.Equal(expectedTarget, task.Target);
    }

    [Fact]
    public void TransitionToTheCurrentStepDoesNotClaimProgress()
    {
        var task = new NextUserTask(
            RecoveryWizardStepId.RecoveryOverview,
            NextUserTaskState.ActionAvailable,
            NextUserTaskCode.StartAccountRecovery,
            NextUserTaskTarget.AccountRecovery,
            RecoveryWizardStepId.RecoveryOverview);

        Assert.False(task.RequiresTransition);
    }

    [Fact]
    public void ProjectionDoesNotDependOnUiCulture()
    {
        var state = ActiveAt(RecoveryWizardStepId.AccountInventory);
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var english = RecoveryNextUserTask.Project(state, Context(accountCount: 1));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            var german = RecoveryNextUserTask.Project(state, Context(accountCount: 1));

            Assert.Equal(english, german);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void InvalidOrUnknownStateFailsClosed()
    {
        var active = ActiveAt(RecoveryWizardStepId.AccountInventory);

        Assert.Throws<ArgumentNullException>(() => RecoveryNextUserTask.Project(null!, Context()));
        Assert.Throws<ArgumentNullException>(() => RecoveryNextUserTask.Project(active, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryNextUserTask.Project(active, Context(accountCount: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecoveryNextUserTask.Project(active, Context(accountCount: 1, uncategorized: 2)));
        Assert.Throws<InvalidOperationException>(() =>
            RecoveryNextUserTask.Project(
                active with { Status = (RecoveryWizardLifecycleStatus)int.MaxValue },
                Context()));
    }

    private static RecoveryWizardState ActiveAt(RecoveryWizardStepId step) =>
        RecoveryWizardOrchestrator.Start(Guid.NewGuid(), DateTimeOffset.UnixEpoch) with
        {
            CurrentStep = step,
            ResumeStep = step,
            Status = RecoveryWizardLifecycleStatus.Active,
            HasVaultContext = step != RecoveryWizardStepId.Welcome &&
                step != RecoveryWizardStepId.TrustedDeviceCheck &&
                step != RecoveryWizardStepId.TrustedDeviceGuidance &&
                step != RecoveryWizardStepId.VaultEntry,
        };

    private static RecoveryFlowContext Context(
        int accountCount = 0,
        int uncategorized = 0,
        bool outstanding = false,
        bool credentials = false,
        Guid? accountId = null,
        string? actionId = null) =>
        new(accountCount, uncategorized, outstanding, credentials, accountId, actionId);
}
