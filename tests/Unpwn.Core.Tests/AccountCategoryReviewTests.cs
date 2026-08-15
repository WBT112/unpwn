using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountCategoryReviewTests
{
    [Theory]
    [InlineData(AccountRecoveryCategory.Email, null, false)]
    [InlineData(AccountRecoveryCategory.Critical, null, false)]
    [InlineData(AccountRecoveryCategory.NonCritical, null, false)]
    [InlineData(AccountRecoveryCategory.Unknown, null, true)]
    [InlineData(AccountRecoveryCategory.Unknown, AccountRecoveryCategory.Unknown, false)]
    [InlineData(AccountRecoveryCategory.Unknown, AccountRecoveryCategory.Critical, false)]
    public void RequiredReviewDependsOnUnknownSuggestionWithoutExplicitChoice(
        AccountRecoveryCategory suggested,
        AccountRecoveryCategory? confirmed,
        bool expected)
    {
        var account = new AccountInventoryEntry(
            Guid.NewGuid(),
            "synthetic",
            "Synthetic account",
            "account@example.invalid",
            null,
            suggested,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            confirmed,
            confirmed.HasValue ? 1 : null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, account.RequiresCategoryReview);
        Assert.Equal(confirmed ?? suggested, account.EffectiveCategory);
    }
}
