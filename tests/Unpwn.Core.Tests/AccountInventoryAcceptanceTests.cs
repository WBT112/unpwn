using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountInventoryAcceptanceTests
{
    [Fact]
    public void SuggestionsRemainUnconfirmedUntilUserCategorizesAccount()
    {
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([CreateAccount("Gmail")], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var account = Assert.Single(state.Accounts);
        Assert.Equal(AccountRecoveryCategory.Email, account.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.Email, account.EffectiveCategory);
        Assert.False(account.IsCategorized);
        Assert.Null(account.CategoryConfirmedRevision);
    }

    [Fact]
    public void ReplacingAccountsRefreshesSuggestionButPreservesExplicitDecision()
    {
        var account = CreateAccount("Gmail") with
        {
            ConfirmedCategory = AccountRecoveryCategory.Critical,
            CategoryConfirmedRevision = 1,
        };
        var initial = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch.AddSeconds(1));
        var updated = initial.ReplaceAccounts(
            [initial.Accounts[0] with { ProviderId = "Streaming" }],
            DateTimeOffset.UnixEpoch.AddSeconds(2));

        var persisted = Assert.Single(updated.Accounts);
        Assert.Equal(AccountRecoveryCategory.NonCritical, persisted.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.Critical, persisted.EffectiveCategory);
        Assert.Equal(1, persisted.CategoryConfirmedRevision);
    }

    [Fact]
    public void UnknownSuggestionRemainsExplicitAndDeterministic()
    {
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
                [CreateAccount("unknown-b"), CreateAccount("unknown-a")],
                DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.All(state.Accounts, account =>
            Assert.Equal(AccountRecoveryCategory.Unknown, account.SuggestedCategory));
        Assert.Equal(
            ["unknown-a", "unknown-b"],
            state.CreatePlan().Items.Select(item => item.ProviderId));
    }

    private static AccountInventoryEntry CreateAccount(string provider) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            $"{provider}@example.invalid",
            null,
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            ConfirmedCategory: null,
            CategoryConfirmedRevision: null,
            DateTimeOffset.UnixEpoch);
}
