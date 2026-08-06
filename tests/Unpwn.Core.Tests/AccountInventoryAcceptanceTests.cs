using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountInventoryAcceptanceTests
{
    [Fact]
    public void RejectedInferenceRemainsRejectedAndNeverAffectsRecoveryPriority()
    {
        var account = CreateAccount(
            "Google Mail",
            AccountInventoryPriority.High,
            roles:
            [
                new AccountRoleState(
                    AccountInventoryRole.EmailMailbox,
                    AccountRoleDecision.Rejected),
            ]).NormalizeAndInfer(DateTimeOffset.UnixEpoch);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var plan = state.CreatePlan(IncidentIndicator.CompromisedRecoveryChannel);
        var persistedRole = state.Accounts.Single().Roles.Single(role =>
            role.Role == AccountInventoryRole.EmailMailbox);

        Assert.Equal(AccountRoleDecision.Rejected, persistedRole.Decision);
        Assert.False(state.Accounts.Single().HasConfirmedRecoveryRole);
        Assert.NotEqual(
            AccountInventoryPlanReasonCode.RecoveryChannelFirst,
            plan.Recommended?.ReasonCode);
    }

    [Fact]
    public void PriorityChangeImmediatelyRecalculatesDeterministicRecoveryOrder()
    {
        var first = CreateAccount("First", AccountInventoryPriority.Normal);
        var second = CreateAccount("Second", AccountInventoryPriority.High);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([first, second], DateTimeOffset.UnixEpoch.AddSeconds(1));
        var initialPlan = state.CreatePlan(IncidentIndicator.None);

        var updatedFirst = first with { Priority = AccountInventoryPriority.Critical };
        var updatedState = state.ReplaceAccounts(
            [updatedFirst, second],
            DateTimeOffset.UnixEpoch.AddSeconds(2));
        var updatedPlan = updatedState.CreatePlan(IncidentIndicator.None);

        Assert.Equal(second.Id, initialPlan.Recommended?.AccountId);
        Assert.Equal(first.Id, updatedPlan.Recommended?.AccountId);
        Assert.Equal(
            AccountInventoryPlanReasonCode.CriticalPriority,
            updatedPlan.Recommended?.ReasonCode);
        Assert.Equal(
            updatedPlan.Items.Select(item => item.AccountId),
            updatedState.CreatePlan(IncidentIndicator.None).Items.Select(item => item.AccountId));
    }

    private static AccountInventoryEntry CreateAccount(
        string provider,
        AccountInventoryPriority priority,
        AccountRoleState[]? roles = null) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            $"{provider.ToLowerInvariant()}@example.invalid",
            null,
            priority,
            roles ?? [],
            [],
            DateTimeOffset.UnixEpoch);
}
