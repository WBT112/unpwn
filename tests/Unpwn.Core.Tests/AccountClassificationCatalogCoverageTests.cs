using System.Globalization;
using Unpwn.Core;
using Unpwn.Providers.Workflows;
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
    [InlineData("manual", "https://www.apobank.de", AccountRecoveryCategory.Critical)]
    [InlineData("ING-DiBa", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://banking.dkb.de", AccountRecoveryCategory.Critical)]
    [InlineData("N26", null, AccountRecoveryCategory.Critical)]
    [InlineData("PayPal", null, AccountRecoveryCategory.Critical)]
    [InlineData("Dashlane", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://gitlab.com/users/sign_in", AccountRecoveryCategory.Critical)]
    [InlineData("BUND ID", null, AccountRecoveryCategory.Critical)]
    [InlineData("WhatsApp", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://posteo.de/login", AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://pm.me", AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://www.netflix.com", AccountRecoveryCategory.NonCritical)]
    [InlineData("Spotify", null, AccountRecoveryCategory.NonCritical)]
    [InlineData("EA", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.spiegel.de", AccountRecoveryCategory.NonCritical)]
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
    [InlineData("manual", "https://www.bankrate.com")]
    [InlineData("manual", "https://www.banquealimentaire.org")]
    [InlineData("manual", "https://10minutemail.com")]
    [InlineData("manual", "https://www.orkut.com")]
    [InlineData("FranceConnect", "https://franceconnect.gouv.fr")]
    [InlineData("manual", "https://www.bild.de")]
    [InlineData("definitely-unlisted-provider", "https://definitely-unlisted-provider.example.test/account")]
    public void UnreviewedOrGenericCategoryHintsRemainUnknown(string providerId, string? url)
    {
        Assert.Equal(
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.Classify(providerId, url).Category);
    }

    [Theory]
    [InlineData("https://mail.ymail.com", AccountRecoveryCategory.Email)]
    [InlineData("https://app.tuta.io", AccountRecoveryCategory.Email)]
    [InlineData("https://e.mail.ru", AccountRecoveryCategory.Email)]
    [InlineData("https://inbox.bellsouth.net", AccountRecoveryCategory.Email)]
    [InlineData("https://mail.ntlworld.com", AccountRecoveryCategory.Email)]
    [InlineData("https://twitter.com/settings", AccountRecoveryCategory.Critical)]
    [InlineData("https://www.ing-diba.de", AccountRecoveryCategory.Critical)]
    public void RegionalAndLegacyDomainsResolveToTheirCanonicalFamily(
        string url,
        AccountRecoveryCategory expected)
    {
        Assert.Equal(
            expected,
            RepositoryAccountClassificationCatalog.Classify("manual", url).Category);
    }

    [Theory]
    [InlineData("Steam", "https://steamcommunity.com/market")]
    [InlineData("Roblox", "https://www.roblox.com/trades")]
    [InlineData("Battle.net", "https://battle.net/account")]
    [InlineData("PlayStation", "https://www.playstation.com/account")]
    [InlineData("EA", "https://www.ea.com/account")]
    public void GamingAccountsWithAssetsPurchasesOrTradingAreCritical(string providerId, string url)
    {
        Assert.Equal(
            AccountRecoveryCategory.Critical,
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
            Assert.Equal(
                AccountRecoveryCategory.Critical,
                RepositoryAccountClassificationCatalog.Classify("CITI", null).Category);
            Assert.Equal(
                AccountRecoveryCategory.Email,
                RepositoryAccountClassificationCatalog.Classify("POSTEO", null).Category);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ClassificationDoesNotRequireAReviewedWorkflowOrAutomationCapability()
    {
        Assert.DoesNotContain(
            RepositoryWorkflowCatalog.Workflows,
            workflow => workflow.ProviderId == "dashlane.com");
        Assert.Equal(
            AccountRecoveryCategory.Critical,
            RepositoryAccountClassificationCatalog.Classify("Dashlane", null).Category);

        var githubWorkflow = Assert.Single(
            RepositoryWorkflowCatalog.Workflows,
            workflow => workflow.ProviderId == "github.com");
        Assert.Contains(
            githubWorkflow.Actions,
            action => action.AutomationSupport != AutomationSupport.None);
        Assert.Equal(
            AccountRecoveryCategory.Critical,
            RepositoryAccountClassificationCatalog.Classify("GitHub", null).Category);
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
