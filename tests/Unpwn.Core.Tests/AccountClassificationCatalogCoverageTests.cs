using System.Globalization;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountClassificationCatalogCoverageTests
{
    [Fact]
    public void MultiDomainProviderFamiliesCountOnce()
    {
        var microsoftMail = Assert.Single(
            RepositoryAccountClassificationCatalog.Providers,
            record => record.Id == "email-microsoft");

        Assert.True(microsoftMail.Domains.Count >= 10);
        Assert.Equal(
            1,
            RepositoryAccountClassificationCatalog.Providers.Count(record => record.Id == microsoftMail.Id));
        Assert.True(
            RepositoryAccountClassificationCatalog.EmailAliasCount >
            RepositoryAccountClassificationCatalog.GetProviderCount(AccountRecoveryCategory.Email));
    }

    [Fact]
    public void CatalogContainsOnlyReviewedRecordsWithUniqueUnambiguousClaims()
    {
        var providers = RepositoryAccountClassificationCatalog.Providers;

        Assert.NotEmpty(providers);
        Assert.All(providers, record =>
        {
            Assert.StartsWith("unpwn-curated", record.ProvenanceId, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(record.ReviewBasis));
            Assert.DoesNotContain("ut1-", record.Id, StringComparison.Ordinal);
        });
        Assert.Equal(
            providers.Count,
            providers.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count());

        var domains = providers.SelectMany(record => record.Domains).ToArray();
        Assert.Equal(domains.Length, domains.Distinct(StringComparer.Ordinal).Count());
        for (var i = 0; i < domains.Length; i++)
        {
            for (var j = i + 1; j < domains.Length; j++)
            {
                Assert.False(
                    domains[i].EndsWith('.' + domains[j], StringComparison.Ordinal) ||
                    domains[j].EndsWith('.' + domains[i], StringComparison.Ordinal),
                    $"Ambiguous domain ownership between {domains[i]} and {domains[j]}.");
            }
        }
    }

    [Theory]
    [InlineData("manual", "https://mail.proton.me/u/0/inbox", AccountRecoveryCategory.Email)]
    [InlineData("GMX", null, AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://www.deutsche-bank.de", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.commerzbank.de", AccountRecoveryCategory.Critical)]
    [InlineData("N26", null, AccountRecoveryCategory.Critical)]
    [InlineData("PayPal", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.netflix.com", AccountRecoveryCategory.NonCritical)]
    [InlineData("Spotify", null, AccountRecoveryCategory.NonCritical)]
    public void RepresentativeReviewedServicesClassifyCorrectly(
        string providerId,
        string? url,
        AccountRecoveryCategory expected)
    {
        Assert.Equal(
            expected,
            RepositoryAccountClassificationCatalog.Classify(providerId, url).Category);
    }

    [Theory]
    [InlineData("Banking", null)]
    [InlineData("Streaming", null)]
    [InlineData("News", null)]
    [InlineData("manual", "https://www.apobank.de")]
    [InlineData("manual", "https://www.bild.de")]
    [InlineData("definitely-unlisted-provider", "https://definitely-unlisted-provider.example.test/account")]
    public void UnreviewedOrGenericCategoryHintsRemainUnknown(string providerId, string? url)
    {
        Assert.Equal(
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.Classify(providerId, url).Category);
    }

    [Fact]
    public void ClassificationIsCultureIndependent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            Assert.Equal(
                AccountRecoveryCategory.Critical,
                RepositoryAccountClassificationCatalog.Classify("PAYPAL", null).Category);
            Assert.Equal(
                AccountRecoveryCategory.Email,
                RepositoryAccountClassificationCatalog.Classify("GMAIL", null).Category);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ProvenanceContainsOnlyRepositoryReviewedMetadata()
    {
        var provenance = Assert.Single(RepositoryAccountClassificationCatalog.Provenance);

        Assert.StartsWith("unpwn-curated", provenance.Id, StringComparison.Ordinal);
        Assert.Equal("AGPL-3.0-or-later", provenance.LicenseId);
        Assert.Equal("curated-manual", provenance.SourceCategory);
    }

    [Fact]
    public void ExplicitUserOverrideStillWinsOverReviewedCatalogSuggestion()
    {
        var account = new AccountInventoryEntry(
            Guid.NewGuid(),
            "n26",
            "N26",
            "synthetic@example.invalid",
            "https://www.n26.com",
            AccountRecoveryCategory.Critical,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            AccountRecoveryCategory.NonCritical,
            1,
            DateTimeOffset.UnixEpoch);

        account.Validate();

        Assert.Equal(AccountRecoveryCategory.Critical, account.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.NonCritical, account.EffectiveCategory);
    }
}
