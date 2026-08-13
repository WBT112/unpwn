using System.Globalization;
using System.Text.Json;
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
            state.CreateRecoveryOrder().Items.Select(item => item.ProviderId));
    }

    [Fact]
    public void CategoryQueueIsStableAcrossCulturesRestartAndIncompleteTriage()
    {
        var state = AccountInventoryState.Empty(Guid.Parse("10000000-0000-0000-0000-000000000000"), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
                [
                    CreateAccount("Streaming") with { Id = Guid.Parse("40000000-0000-0000-0000-000000000000") },
                    CreateAccount("ı-service") with { Id = Guid.Parse("30000000-0000-0000-0000-000000000000") },
                    CreateAccount("Banking") with { Id = Guid.Parse("20000000-0000-0000-0000-000000000000") },
                    CreateAccount("Gmail") with { Id = Guid.Parse("10000000-0000-0000-0000-000000000001") },
                ],
                DateTimeOffset.UnixEpoch.AddSeconds(1));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkishOrder = state.CreateRecoveryOrder().Items.Select(item => item.AccountId).ToArray();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var reloaded = JsonSerializer.Deserialize<AccountInventoryState>(JsonSerializer.Serialize(state))!;
            var restartedOrder = reloaded.CreateRecoveryOrder().Items.Select(item => item.AccountId).ToArray();

            Assert.Equal(turkishOrder, restartedOrder);
            Assert.Equal(
                [AccountRecoveryCategory.Email, AccountRecoveryCategory.Critical,
                    AccountRecoveryCategory.Unknown, AccountRecoveryCategory.NonCritical],
                reloaded.CreateRecoveryOrder().Items.Select(item => item.Category));
            Assert.Contains(reloaded.Accounts, account => !account.IsCategorized);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
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
