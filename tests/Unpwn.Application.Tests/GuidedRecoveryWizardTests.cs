using System.Globalization;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class GuidedRecoveryWizardTests
{
    [Fact]
    public void HappyPathSelectsInventoryTriagePlanRecoveryCredentialsAndCompletion()
    {
        var state = AtAccountInventory();

        var triage = GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2));
        state = Move(state, triage);
        Assert.Equal(RecoveryWizardStepId.AccountTriage, state.CurrentStep);

        var plan = GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2));
        state = Move(state, plan);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, state.CurrentStep);

        var accountId = Guid.NewGuid();
        var recovery = GuidedRecoveryWizard.GetNext(
            state,
            Context(
                accountCount: 2,
                outstanding: true,
                accountId: accountId,
                actionId: "change-password"));
        Assert.Equal(RecoveryWizardStepId.AccountRecovery, recovery.TargetStep);
        Assert.Equal(accountId, recovery.AccountId);
        Assert.Equal("change-password", recovery.ActionId);

        state = Move(state, recovery);
        state = Move(state, GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2)));
        var credentials = GuidedRecoveryWizard.GetNext(
            state,
            Context(accountCount: 2, credentials: true));
        Assert.Equal(RecoveryWizardStepId.CredentialExport, credentials.TargetStep);

        state = Move(state, credentials);
        state = Move(state, GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2)));
        Assert.Equal(RecoveryWizardStepId.CompletionPreflight, state.CurrentStep);
        state = Move(state, GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2)));
        Assert.Equal(RecoveryWizardStepId.FinalReport, state.CurrentStep);
    }

    [Fact]
    public void InventoryIsRequiredAndTriageCanBeEndedDeliberately()
    {
        var inventory = AtAccountInventory();

        var empty = GuidedRecoveryWizard.GetNext(inventory, Context());

        Assert.False(empty.CanMove);
        Assert.Equal(GuidedRecoveryBlockCode.AccountsRequired, empty.BlockCode);

        var triage = Move(
            inventory,
            GuidedRecoveryWizard.GetNext(inventory, Context(accountCount: 1)));
        var withoutEmail = GuidedRecoveryWizard.GetNext(
            triage,
            Context(accountCount: 1, uncategorized: 1));
        var afterEmail = GuidedRecoveryWizard.GetNext(
            triage,
            Context(accountCount: 1, uncategorized: 1, hasConfirmedEmail: true));

        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, withoutEmail.TargetStep);
        Assert.Equal(GuidedRecoveryAdvisoryCode.ContinueWithoutEmailCategory, withoutEmail.AdvisoryCode);
        Assert.Equal(RecoveryWizardStepId.RecoveryPlan, afterEmail.TargetStep);
        Assert.Equal(GuidedRecoveryAdvisoryCode.RemainingCategoryReviewOptional, afterEmail.AdvisoryCode);
    }

    [Fact]
    public void RecalculationUsesCurrentMaterializedStateAndPreservesVisibleRisks()
    {
        var plan = Move(
            Move(
                AtAccountInventory(),
                GuidedRecoveryWizard.GetNext(AtAccountInventory(), Context(accountCount: 1))),
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.AccountTriage,
                RecoveryWizardStepId.RecoveryPlan,
                GuidedRecoveryBlockCode.None));

        Assert.Equal(
            RecoveryWizardStepId.AccountRecovery,
            GuidedRecoveryWizard.GetNext(
                plan,
                Context(accountCount: 1, outstanding: true)).TargetStep);
        Assert.Equal(
            RecoveryWizardStepId.CompletionPreflight,
            GuidedRecoveryWizard.GetNext(plan, Context(accountCount: 1)).TargetStep);
    }

    [Fact]
    public void DecisionsDoNotDependOnCurrentUiCulture()
    {
        var state = AtAccountInventory();
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var english = GuidedRecoveryWizard.GetNext(state, Context(accountCount: 1));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            var german = GuidedRecoveryWizard.GetNext(state, Context(accountCount: 1));

            Assert.Equal(english, german);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void BackNavigationIsDeterministicAndTerminalStateCannotMove()
    {
        var triage = Move(
            AtAccountInventory(),
            GuidedRecoveryWizard.GetNext(AtAccountInventory(), Context(accountCount: 1)));
        Assert.Equal(
            RecoveryWizardStepId.AccountInventory,
            GuidedRecoveryWizard.GetPrevious(triage).TargetStep);

        var terminal = RecoveryWizardOrchestrator.StopAfterTrustedDeviceGuidance(
            RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
                RecoveryWizardOrchestrator.Continue(
                    RecoveryWizardOrchestrator.Start(Guid.NewGuid(), DateTimeOffset.UnixEpoch),
                    RecoveryWizardStepId.TrustedDeviceCheck,
                    DateTimeOffset.UnixEpoch),
                TrustedDeviceDecision.Unsure,
                DateTimeOffset.UnixEpoch),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(
            GuidedRecoveryBlockCode.Terminal,
            GuidedRecoveryWizard.GetNext(terminal, Context()).BlockCode);
    }

    [Theory]
    [InlineData("account-triage", "account-inventory")]
    [InlineData("recovery-plan", "account-triage")]
    [InlineData("account-recovery", "recovery-plan")]
    [InlineData("credential-export", "recovery-plan")]
    [InlineData("completion-preflight", "recovery-plan")]
    [InlineData("final-report", "completion-preflight")]
    public void EverySupportedBackRouteIsDeterministic(string current, string expected)
    {
        var state = AtAccountInventory() with
        {
            CurrentStep = RecoveryWizardStepId.Parse(current),
            ResumeStep = RecoveryWizardStepId.Parse(current),
        };

        var previous = GuidedRecoveryWizard.GetPrevious(state);

        Assert.True(previous.CanMove);
        Assert.Equal(RecoveryWizardStepId.Parse(expected), previous.TargetStep);
    }

    [Fact]
    public void UnsupportedPausedAndInvalidContextsFailClosed()
    {
        var active = AtAccountInventory();
        var paused = RecoveryWizardOrchestrator.Pause(active, active.UpdatedAt);
        var unsupported = active with { CurrentStep = RecoveryWizardStepId.IncidentIntake };

        Assert.Equal(
            GuidedRecoveryBlockCode.Paused,
            GuidedRecoveryWizard.GetNext(paused, Context()).BlockCode);
        Assert.Equal(
            GuidedRecoveryBlockCode.Paused,
            GuidedRecoveryWizard.GetPrevious(paused).BlockCode);
        Assert.Equal(
            GuidedRecoveryBlockCode.UnsupportedStep,
            GuidedRecoveryWizard.GetNext(unsupported, Context()).BlockCode);
        Assert.Equal(
            GuidedRecoveryBlockCode.UnsupportedStep,
            GuidedRecoveryWizard.GetPrevious(unsupported).BlockCode);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedRecoveryWizard.GetNext(active, Context(accountCount: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedRecoveryWizard.GetNext(active, Context(uncategorized: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedRecoveryWizard.GetNext(active, Context(accountCount: 1, uncategorized: 2)));
        Assert.Throws<ArgumentNullException>(() =>
            GuidedRecoveryWizard.GetNext(null!, Context()));
        Assert.Throws<ArgumentNullException>(() =>
            GuidedRecoveryWizard.GetNext(active, null!));
    }

    private static RecoveryWizardState AtAccountInventory()
    {
        var time = DateTimeOffset.UnixEpoch;
        var state = RecoveryWizardOrchestrator.Start(Guid.NewGuid(), time);
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.TrustedDeviceCheck,
            time);
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            time);
        state = RecoveryWizardOrchestrator.ConfirmVaultReady(state, time);
        return RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.AccountInventory,
            time);
    }

    private static RecoveryWizardState Move(
        RecoveryWizardState state,
        GuidedRecoveryDecision decision) =>
        RecoveryWizardOrchestrator.Continue(
            state,
            Assert.IsType<RecoveryWizardStepId>(decision.TargetStep),
            state.UpdatedAt);

    private static GuidedRecoveryContext Context(
        int accountCount = 0,
        int uncategorized = 0,
        bool hasConfirmedEmail = false,
        bool outstanding = false,
        bool credentials = false,
        Guid? accountId = null,
        string? actionId = null) =>
        new(
            accountCount,
            uncategorized,
            hasConfirmedEmail,
            outstanding,
            credentials,
            accountId,
            actionId);
}
