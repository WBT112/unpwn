using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountInventoryTests
{
    [Fact]
    public void AccountUrlWithEmbeddedCredentialsIsRejected()
    {
        var account = CreateAccount("Mail") with
        {
            AccountUrl = "https://user:old-password@example.test/account",
        };

        Assert.Throws<InvalidOperationException>(account.Validate);
    }

    [Theory]
    [InlineData("Gmail", null)]
    [InlineData("gmail.com", null)]
    [InlineData("manual", "https://accounts.googlemail.com/security")]
    [InlineData("manual", "https://mail.proton.me/u/0/inbox")]
    [InlineData("manual", "https://login.yahoo.co.jp/account")]
    public void KnownEmailAliasesAreSuggestedAsEmail(string providerId, string? url)
    {
        var suggestion = RepositoryAccountClassificationCatalog.Classify(providerId, url);

        Assert.Equal(AccountRecoveryCategory.Email, suggestion.Category);
        Assert.Equal(RepositoryAccountClassificationCatalog.CurrentVersion, suggestion.CatalogVersion);
    }

    [Fact]
    public void CatalogContainsAtLeastOneHundredEmailAliases()
    {
        Assert.True(RepositoryAccountClassificationCatalog.EmailAliasCount >= 100);
    }

    [Theory]
    [InlineData("Banking", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://vault.bitwarden.com", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.reddit.com/settings", AccountRecoveryCategory.Critical)]
    [InlineData("Streaming", null, AccountRecoveryCategory.NonCritical)]
    [InlineData("synthetic-provider", "https://provider.example.test", AccountRecoveryCategory.Unknown)]
    public void CatalogClassifiesKnownAndUnknownProviders(
        string providerId,
        string? url,
        AccountRecoveryCategory expected)
    {
        Assert.Equal(
            expected,
            RepositoryAccountClassificationCatalog.Classify(providerId, url).Category);
    }

    [Fact]
    public void ExplicitCategoryOverridesSuggestion()
    {
        var account = CreateAccount("Gmail", confirmed: AccountRecoveryCategory.NonCritical)
            .NormalizeAndClassify(DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(AccountRecoveryCategory.Email, account.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.NonCritical, account.EffectiveCategory);
        Assert.True(account.IsCategorized);
    }

    [Fact]
    public void PlanUsesCategoryOrderAndExplicitOverrides()
    {
        var nonCritical = CreateAccount("Streaming", AccountRecoveryCategory.NonCritical);
        var unknown = CreateAccount("Unknown", AccountRecoveryCategory.Unknown);
        var critical = CreateAccount("Banking", AccountRecoveryCategory.Critical);
        var email = CreateAccount("Gmail", AccountRecoveryCategory.Email);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([nonCritical, unknown, critical, email], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var queue = state.CreateRecoveryOrder();

        Assert.Equal(
            [email.Id, critical.Id, unknown.Id, nonCritical.Id],
            queue.Items.Select(item => item.AccountId));
        Assert.Equal(AccountRecoveryOrderReasonCode.EmailCategory, queue.Recommended?.ReasonCode);
    }

    [Fact]
    public void IncidentInputCannotChangeTheCanonicalCategoryOrder()
    {
        var critical = CreateAccount("Banking", AccountRecoveryCategory.Critical);
        var email = CreateAccount("Gmail", AccountRecoveryCategory.Email);
        var state = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([email, critical], DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(email.Id, state.CreateRecoveryOrder().Recommended?.AccountId);
    }

    [Fact]
    public void ExplicitCategoryRequiresConfirmationRevision()
    {
        var account = CreateAccount("Gmail") with
        {
            ConfirmedCategory = AccountRecoveryCategory.Email,
            CategoryConfirmedRevision = null,
        };

        Assert.Throws<InvalidOperationException>(account.Validate);
    }

    [Fact]
    public void CategoryConfirmationCannotReferenceFutureInventoryRevision()
    {
        var account = CreateAccount("Gmail", AccountRecoveryCategory.Email) with
        {
            CategoryConfirmedRevision = 2,
        };
        var state = new AccountInventoryState(
            Guid.NewGuid(),
            1,
            DateTimeOffset.UnixEpoch,
            [account]);

        Assert.Throws<InvalidOperationException>(state.Validate);
    }

    [Fact]
    public void UnknownSerializedCategoryIsRejected()
    {
        var account = CreateAccount("Gmail") with
        {
            SuggestedCategory = (AccountRecoveryCategory)99,
        };

        Assert.Throws<InvalidOperationException>(account.Validate);
    }

    private static AccountInventoryEntry CreateAccount(
        string provider,
        AccountRecoveryCategory? confirmed = null) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            $"{provider.ToLowerInvariant()}@example.invalid",
            null,
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            confirmed,
            confirmed.HasValue ? 1 : null,
            DateTimeOffset.UnixEpoch);
}
