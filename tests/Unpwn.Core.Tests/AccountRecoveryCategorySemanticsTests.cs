using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountRecoveryCategorySemanticsTests
{
    [Fact]
    public void ClassifierProducedUnknownRemainsAValidUnresolvedSystemState()
    {
        var account = CreateAccount() with
        {
            SuggestedCategory = AccountRecoveryCategory.Unknown,
            ConfirmedCategory = null,
            CategoryConfirmedRevision = null,
        };

        account.Validate();

        Assert.Equal(AccountRecoveryCategory.Unknown, account.EffectiveCategory);
        Assert.True(account.RequiresCategoryReview);
        Assert.False(account.IsCategorized);
    }

    [Fact]
    public void ConfirmedUnknownIsRejectedByTheDomainModel()
    {
        var account = CreateAccount() with
        {
            SuggestedCategory = AccountRecoveryCategory.Unknown,
            ConfirmedCategory = AccountRecoveryCategory.Unknown,
            CategoryConfirmedRevision = 1,
        };

        Assert.Throws<InvalidOperationException>(account.Validate);
    }

    [Theory]
    [InlineData(AccountRecoveryCategory.Email)]
    [InlineData(AccountRecoveryCategory.Critical)]
    [InlineData(AccountRecoveryCategory.NonCritical)]
    public void RealRecoveryCategoriesRemainUserSelectable(AccountRecoveryCategory category)
    {
        Assert.True(AccountRecoveryCategoryRules.IsUserSelectable(category));

        var account = CreateAccount() with
        {
            ConfirmedCategory = category,
            CategoryConfirmedRevision = 1,
        };
        account.Validate();
        Assert.Equal(category, account.EffectiveCategory);
    }

    [Fact]
    public void UnknownAndUndefinedValuesAreNotUserSelectable()
    {
        Assert.False(AccountRecoveryCategoryRules.IsUserSelectable(AccountRecoveryCategory.Unknown));
        Assert.False(AccountRecoveryCategoryRules.IsUserSelectable((AccountRecoveryCategory)99));
    }

    private static AccountInventoryEntry CreateAccount() => new(
        Guid.NewGuid(),
        "unclassified.example",
        "Unclassified account",
        "user@example.invalid",
        null,
        AccountRecoveryCategory.Unknown,
        RepositoryAccountClassificationCatalog.CurrentVersion,
        ConfirmedCategory: null,
        CategoryConfirmedRevision: null,
        DateTimeOffset.UnixEpoch);
}
