using System.Globalization;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountClassificationCatalogCoverageTests
{
    [Fact]
    public void CatalogMeetsCanonicalProviderRecordMinimums()
    {
        Assert.True(RepositoryAccountClassificationCatalog.GetProviderCount(AccountRecoveryCategory.Email) >= 100);
        Assert.True(RepositoryAccountClassificationCatalog.GetProviderCount(AccountRecoveryCategory.Critical) >= 1_000);
        Assert.True(RepositoryAccountClassificationCatalog.GetProviderCount(AccountRecoveryCategory.NonCritical) >= 1_000);
    }

    [Fact]
    public void ProviderCountsAreRecordsNotAliasCounts()
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
    public void CanonicalIdsAndDomainClaimsAreUniqueAcrossCategories()
    {
        var providers = RepositoryAccountClassificationCatalog.Providers;

        Assert.Equal(
            providers.Count,
            providers.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count());
        var domainClaims = providers
            .SelectMany(record => record.Domains.Select(domain => (Domain: domain, record.Category)))
            .ToArray();
        Assert.Equal(
            domainClaims.Length,
            domainClaims.Select(claim => claim.Domain).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("manual", "https://mail.proton.me/u/0/inbox", AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://cock.li", AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://www.apobank.de", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.deutsche-bank.de", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.commerzbank.de", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.n26.com", AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.bild.de", AccountRecoveryCategory.NonCritical)]
    [InlineData("manual", "https://www.netflix.com", AccountRecoveryCategory.NonCritical)]
    public void RepresentativeGlobalGermanAndEuropeanServicesClassifyCorrectly(
        string providerId,
        string url,
        AccountRecoveryCategory expected)
    {
        Assert.Equal(
            expected,
            RepositoryAccountClassificationCatalog.Classify(providerId, url).Category);
    }

    [Fact]
    public void UnknownProviderRemainsUnknown()
    {
        var suggestion = RepositoryAccountClassificationCatalog.Classify(
            "definitely-unlisted-provider",
            "https://definitely-unlisted-provider.example.test/account");

        Assert.Equal(AccountRecoveryCategory.Unknown, suggestion.Category);
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
                RepositoryAccountClassificationCatalog.Classify("BANKING", null).Category);
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
    public void ProvenanceIsPinnedAndSeparatesCuratedAndThirdPartyData()
    {
        var provenance = RepositoryAccountClassificationCatalog.Provenance;

        Assert.Contains(provenance, entry =>
            entry.Id.StartsWith("unpwn-curated", StringComparison.Ordinal) &&
            entry.LicenseId == "AGPL-3.0-or-later");
        Assert.Equal(
            3,
            provenance.Count(entry =>
                entry.SourceRevision == RepositoryAccountClassificationCatalog.Ut1SourceRevision &&
                entry.LicenseId == "CC-BY-SA-4.0"));
    }

    [Fact]
    public void ExplicitUserOverrideStillWinsOverBroadCatalogSuggestion()
    {
        var account = new AccountInventoryEntry(
            Guid.NewGuid(),
            "apobank.de",
            "APO Bank",
            "synthetic@example.invalid",
            "https://www.apobank.de",
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
