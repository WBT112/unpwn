using System.Globalization;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class GuidedRecoveryWizardTests
{
    [Fact]
    public void HappyPathSelectsInventoryIdentityPlanRecoveryCredentialsAndCompletion()
    {
        var state = AtAccountInventory();

        var identity = GuidedRecoveryWizard.GetNext(state, Context(accountCount: 2));
        state = Move(state, identity);
        Assert.Equal(RecoveryWizardStepId.IdentityReview, state.CurrentStep);

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
    public void RequiredInventoryAndRoleConfirmationCannotBeSkipped()
    {
        var inventory = AtAccountInventory();

        var empty = GuidedRecoveryWizard.GetNext(inventory, Context());

        Assert.False(empty.CanMove);
        Assert.Equal(GuidedRecoveryBlockCode.AccountsRequired, empty.BlockCode);

        var identity = Move(
            inventory,
            GuidedRecoveryWizard.GetNext(inventory, Context(accountCount: 1)));
        var suggested = GuidedRecoveryWizard.GetNext(
            identity,
            Context(accountCount: 1, suggestedRoles: 1));

        Assert.False(suggested.CanMove);
        Assert.Equal(GuidedRecoveryBlockCode.RoleConfirmationRequired, suggested.BlockCode);
    }

    [Fact]
    public void RecalculationUsesCurrentMaterializedStateAndPreservesVisibleRisks()
    {
        var plan = Move(
            Move(
                AtAccountInventory(),
                GuidedRecoveryWizard.GetNext(AtAccountInventory(), Context(accountCount: 1))),
            new GuidedRecoveryDecision(
                RecoveryWizardStepId.IdentityReview,
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
        var identity = Move(
            AtAccountInventory(),
            GuidedRecoveryWizard.GetNext(AtAccountInventory(), Context(accountCount: 1)));
        Assert.Equal(
            RecoveryWizardStepId.AccountInventory,
            GuidedRecoveryWizard.GetPrevious(identity).TargetStep);

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
        int suggestedRoles = 0,
        bool outstanding = false,
        bool credentials = false,
        Guid? accountId = null,
        string? actionId = null) =>
        new(
            accountCount,
            suggestedRoles,
            outstanding,
            credentials,
            accountId,
            actionId);
}
