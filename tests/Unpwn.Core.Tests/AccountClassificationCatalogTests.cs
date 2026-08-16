using System.Globalization;
using System.Text;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountClassificationCatalogTests
{
    private const string Header =
        "provider_id\tdisplay_name\tcategory\tdomains\tprovider_aliases\tprovenance";

    [Fact]
    public void CatalogMeetsCanonicalProviderMinimumsWithoutCountingAliases()
    {
        var records = RepositoryAccountClassificationCatalog.ProviderRecords;
        var email = records.Count(record => record.Category == AccountRecoveryCategory.Email);
        var critical = records.Count(record => record.Category == AccountRecoveryCategory.Critical);
        var nonCritical = records.Count(record => record.Category == AccountRecoveryCategory.NonCritical);

        Assert.True(email >= 100, $"Expected at least 100 Email providers, found {email}.");
        Assert.True(critical >= 1000, $"Expected at least 1000 Critical providers, found {critical}.");
        Assert.True(nonCritical >= 1000, $"Expected at least 1000 NonCritical providers, found {nonCritical}.");
        Assert.Equal(records.Count, records.Select(record => record.ProviderId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(records, record => record.Category == AccountRecoveryCategory.Unknown);

        var outlook = Assert.Single(records, record => record.ProviderId == "microsoft-outlook");
        Assert.Contains("outlook.com", outlook.Domains);
        Assert.Contains("hotmail.com", outlook.Domains);
        Assert.Contains("live.com", outlook.Domains);
        Assert.True(RepositoryAccountClassificationCatalog.EmailAliasCount > email);
    }

    [Theory]
    [InlineData("gmail", null, AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://mail.proton.me/u/0/inbox", AccountRecoveryCategory.Email)]
    [InlineData("manual", "https://web.de/freemail", AccountRecoveryCategory.Email)]
    [InlineData("microsoft-outlook", null, AccountRecoveryCategory.Email)]
    [InlineData("deutschebank", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://app.n26.com", AccountRecoveryCategory.Critical)]
    [InlineData("paypal", null, AccountRecoveryCategory.Critical)]
    [InlineData("github", null, AccountRecoveryCategory.Critical)]
    [InlineData("bitwarden", null, AccountRecoveryCategory.Critical)]
    [InlineData("manual", "https://www.amazon.de/gp/css/homepage.html", AccountRecoveryCategory.Critical)]
    [InlineData("netflix", null, AccountRecoveryCategory.NonCritical)]
    [InlineData("spotify", null, AccountRecoveryCategory.NonCritical)]
    [InlineData("duolingo", null, AccountRecoveryCategory.NonCritical)]
    [InlineData("allrecipes", null, AccountRecoveryCategory.NonCritical)]
    public void RepresentativeGlobalGermanAndEuropeanProvidersClassifyDeterministically(
        string providerId,
        string? accountUrl,
        AccountRecoveryCategory expected)
    {
        Assert.Equal(
            expected,
            RepositoryAccountClassificationCatalog.Classify(providerId, accountUrl).Category);
    }

    [Fact]
    public void UnknownProviderRemainsUnknown()
    {
        var unique = $"unpwn-unknown-{Guid.NewGuid():N}.example.invalid";

        var result = RepositoryAccountClassificationCatalog.Classify(unique, $"https://{unique}/account");

        Assert.Equal(AccountRecoveryCategory.Unknown, result.Category);
    }

    [Fact]
    public void ClassificationIsIndependentOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = RepositoryAccountClassificationCatalog.Classify(
                "MICROSOFT-OUTLOOK",
                "https://LOGIN.GITHUB.COM/security");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var german = RepositoryAccountClassificationCatalog.Classify(
                "MICROSOFT-OUTLOOK",
                "https://LOGIN.GITHUB.COM/security");

            Assert.Equal(AccountRecoveryCategory.Email, turkish.Category);
            Assert.Equal(turkish, german);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void LoaderNormalizesInternationalizedDomains()
    {
        var catalog = RepositoryAccountClassificationCatalog.Load(new StringReader(Tsv(
            Row("idn-mail", "IDN Mail", "Email", "bücher.de", "idn-mail"))));

        var record = Assert.Single(catalog.Records);
        Assert.Equal("xn--bcher-kva.de", Assert.Single(record.Domains));
    }

    [Fact]
    public void LoaderDeduplicatesEquivalentAliasesWithinOneProvider()
    {
        var catalog = RepositoryAccountClassificationCatalog.Load(new StringReader(Tsv(
            Row("provider-name", "Provider", "Critical", "provider.example", "provider_name|provider-name"))));

        var record = Assert.Single(catalog.Records);
        Assert.Single(record.ProviderIdAliases);
        Assert.Equal("provider_name", record.ProviderIdAliases[0]);
    }

    [Fact]
    public void LoaderKeepsDistinctCanonicalIdsWhenOnlyAliasNormalizationWouldCollide()
    {
        var catalog = RepositoryAccountClassificationCatalog.Load(new StringReader(Tsv(
            Row("provider-name", "Provider One", "Critical", "one.example", "provider-name"),
            Row("providername", "Provider Two", "NonCritical", "two.example", "providername"))));

        Assert.Equal(2, catalog.Records.Length);
        Assert.Contains(catalog.Records, record => record.ProviderId == "provider-name");
        Assert.Contains(catalog.Records, record => record.ProviderId == "providername");
    }

    [Fact]
    public void LoaderRejectsUnknownAsCatalogCategory()
    {
        var input = Tsv(Row("unknown", "Unknown", "Unknown", "unknown.example", "unknown"));

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryAccountClassificationCatalog.Load(new StringReader(input)));
    }

    [Fact]
    public void LoaderRejectsProviderAliasCollisionEvenWithinSameCategory()
    {
        var input = Tsv(
            Row("first", "First", "Critical", "first.example", "shared"),
            Row("second", "Second", "Critical", "second.example", "shared"));

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryAccountClassificationCatalog.Load(new StringReader(input)));
    }

    [Fact]
    public void LoaderRejectsOverlappingDomainAliasesAcrossProviders()
    {
        var input = Tsv(
            Row("parent", "Parent", "Critical", "example.com", "parent"),
            Row("child", "Child", "NonCritical", "login.example.com", "child"));

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryAccountClassificationCatalog.Load(new StringReader(input)));
    }

    [Fact]
    public void LoaderRejectsOverlongLineBeforeUnboundedCatalogGrowth()
    {
        var input = new string('x', AccountClassificationCatalogLoader.MaximumLineCharacters + 1);

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryAccountClassificationCatalog.Load(new StringReader(input)));
    }

    [Fact]
    public void LoaderRejectsMoreThanConfiguredProviderLimit()
    {
        var builder = new StringBuilder(Header).Append('\n');
        for (var index = 0; index <= AccountClassificationCatalogLoader.MaximumProviderRecords; index++)
        {
            builder.Append(Row(
                $"provider-{index}",
                $"Provider {index}",
                "Critical",
                $"provider-{index}.example",
                $"provider-{index}"));
            builder.Append('\n');
        }

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryAccountClassificationCatalog.Load(new StringReader(builder.ToString())));
    }

    private static string Tsv(params string[] rows) =>
        Header + "\n" + string.Join('\n', rows) + "\n";

    private static string Row(
        string providerId,
        string name,
        string category,
        string domains,
        string aliases) =>
        string.Join('\t', providerId, name, category, domains, aliases, "test:fixture");
}
