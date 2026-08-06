using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountInventoryTests
{
    [Fact]
    public void InferredRolesRemainSuggestedUntilExplicitlyConfirmed()
    {
        var account = CreateAccount(
            "Google Mail",
            AccountInventoryPriority.High,
            login: "person@example.invalid")
            .NormalizeAndInfer(DateTimeOffset.UnixEpoch);

        Assert.Contains(
            account.Roles,
            role => role.Role == AccountInventoryRole.EmailMailbox &&
                    role.Decision == AccountRoleDecision.Suggested);
        Assert.Contains(
            account.Roles,
            role => role.Role == AccountInventoryRole.IdentityProvider &&
                    role.Decision == AccountRoleDecision.Suggested);
        Assert.False(account.HasConfirmedRecoveryRole);

        var confirmed = account with
        {
            Roles =
            [
                .. account.Roles.Select(role =>
                    role.Role == AccountInventoryRole.EmailMailbox
                        ? role with { Decision = AccountRoleDecision.Confirmed }
                        : role),
            ],
        };

        Assert.True(confirmed.HasConfirmedRecoveryRole);
    }

    [Fact]
    public void RecoveryChannelAndDependencyRootAreOrderedBeforeDependentCriticalAccount()
    {
        var mailbox = CreateAccount(
            "Primary mailbox",
            AccountInventoryPriority.High,
            roles:
            [
                new AccountRoleState(
                    AccountInventoryRole.EmailMailbox,
                    AccountRoleDecision.Confirmed),
            ]);
        var critical = CreateAccount(
            "Banking",
            AccountInventoryPriority.Critical,
            dependencies:
            [
                new AccountInventoryDependency(
                    mailbox.Id,
                    AccountDependencyKind.PasswordReset,
                    IsOverride: false,
                    OverrideReason: null),
            ]);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([critical, mailbox], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var plan = state.CreatePlan(IncidentIndicator.CompromisedRecoveryChannel);

        Assert.Equal(mailbox.Id, plan.Recommended?.AccountId);
        Assert.Equal(
            AccountInventoryPlanReasonCode.RecoveryChannelFirst,
            plan.Recommended?.ReasonCode);
        Assert.Equal(
            AccountInventoryPlanStatus.PlannedLater,
            plan.Items.Single(item => item.AccountId == critical.Id).Status);
        Assert.Equal(
            [mailbox.Id],
            plan.Items.Single(item => item.AccountId == critical.Id).WaitingForAccountIds);
    }

    [Fact]
    public void MissingDependenciesAndCyclesRemainBlockingIssues()
    {
        var first = CreateAccount("First", AccountInventoryPriority.Critical);
        var second = CreateAccount("Second", AccountInventoryPriority.High);
        var missingId = Guid.NewGuid();
        first = first with
        {
            Dependencies =
            [
                new AccountInventoryDependency(
                    second.Id,
                    AccountDependencyKind.IdentityProvider,
                    IsOverride: false,
                    OverrideReason: null),
            ],
        };
        second = second with
        {
            Dependencies =
            [
                new AccountInventoryDependency(
                    first.Id,
                    AccountDependencyKind.RecoveryContact,
                    IsOverride: false,
                    OverrideReason: null),
                new AccountInventoryDependency(
                    missingId,
                    AccountDependencyKind.Mfa,
                    IsOverride: false,
                    OverrideReason: null),
            ],
        };
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([first, second], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var plan = state.CreatePlan(IncidentIndicator.None);

        Assert.Contains(plan.Issues, issue =>
            issue.Kind == AccountInventoryIssueKind.MissingDependency &&
            issue.AccountId == second.Id &&
            issue.RelatedAccountId == missingId);
        Assert.Contains(plan.Issues, issue =>
            issue.Kind == AccountInventoryIssueKind.DependencyCycle &&
            issue.AccountId == first.Id);
        Assert.Contains(plan.Items, item =>
            item.AccountId == first.Id &&
            item.Status == AccountInventoryPlanStatus.BlockedCycle);
        Assert.Contains(plan.Items, item =>
            item.AccountId == second.Id &&
            item.Status == AccountInventoryPlanStatus.BlockedMissingDependency);
    }

    [Fact]
    public void OverrideRemovesSchedulingConstraintButKeepsRiskVisible()
    {
        var root = CreateAccount("Root", AccountInventoryPriority.High);
        var dependent = CreateAccount(
            "Dependent",
            AccountInventoryPriority.Critical,
            dependencies:
            [
                new AccountInventoryDependency(
                    root.Id,
                    AccountDependencyKind.PasswordReset,
                    IsOverride: true,
                    OverrideReason: "The provider recovery channel is unavailable."),
            ]);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([dependent, root], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var plan = state.CreatePlan(IncidentIndicator.None);
        var item = plan.Items.Single(candidate => candidate.AccountId == dependent.Id);

        Assert.True(item.HasDependencyOverride);
        Assert.Equal(AccountInventoryPlanReasonCode.UserOverridePresent, item.ReasonCode);
        Assert.Contains(plan.Issues, issue =>
            issue.Kind == AccountInventoryIssueKind.DependencyOverride &&
            issue.AccountId == dependent.Id);
    }

    private static AccountInventoryEntry CreateAccount(
        string provider,
        AccountInventoryPriority priority,
        string? login = null,
        AccountRoleState[]? roles = null,
        AccountInventoryDependency[]? dependencies = null) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            login ?? $"{provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            null,
            priority,
            roles ?? [],
            dependencies ?? [],
            DateTimeOffset.UnixEpoch);
}
