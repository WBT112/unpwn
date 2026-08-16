using System.Reflection;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountClassificationCatalogInvariantTests
{
    private static readonly string[] ExampleDomain = ["example.test"];
    private static readonly string[] InvalidDomain = ["not a domain"];
    private static readonly string[] DuplicateExampleDomains = ["example.test", "EXAMPLE.TEST"];
    private static readonly string[] InvalidAlias = ["---"];
    private static readonly string[] DuplicateAliases = ["same-alias", "same alias"];
    private static readonly string[] OtherDomain = ["other.example"];
    private static readonly string[] ExistingAlias = ["existing-alias"];
    private static readonly string[] ValidAlias = ["valid-alias"];

    [Fact]
    public void InvalidProviderRecordShapesAreRejected()
    {
        AccountClassificationProviderRecord[] invalidRecords =
        [
            ValidRecord() with { Id = "" },
            ValidRecord() with { Name = "" },
            ValidRecord() with { Category = AccountRecoveryCategory.Unknown },
            ValidRecord() with { Category = (AccountRecoveryCategory)999 },
            ValidRecord() with { ProvenanceId = "" },
            ValidRecord() with { Domains = [] },
            ValidRecord() with { Domains = InvalidDomain },
            ValidRecord() with { Domains = DuplicateExampleDomains },
            ValidRecord() with { ProviderIdAliases = InvalidAlias },
            ValidRecord() with { ProviderIdAliases = DuplicateAliases },
        ];

        foreach (var record in invalidRecords)
        {
            AssertAddRecordThrows(record);
        }
    }

    [Fact]
    public void ClaimedSourceDomainIsSkippedWithoutCreatingAnotherCanonicalRecord()
    {
        var existing = ValidRecord();
        List<AccountClassificationProviderRecord> records = [existing];
        var ids = new HashSet<string>(StringComparer.Ordinal) { existing.Id };
        var domains = new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal)
        {
            ["example.test"] = existing,
        };
        var aliases = new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal);
        var source = ValidRecord() with
        {
            Id = "source:example.test",
            Name = "Source example",
            ProvenanceId = "source",
        };

        InvokeAddRecord(source, records, ids, domains, aliases, allowClaimedDomainSkip: true);

        Assert.Single(records);
        Assert.Single(domains);
        Assert.DoesNotContain("source:example.test", ids);
    }

    [Fact]
    public void DuplicateCanonicalIdDomainAndProviderAliasAreRejected()
    {
        var existing = ValidRecord();

        AssertAddRecordThrows(
            ValidRecord() with { Domains = OtherDomain },
            ids: new HashSet<string>(StringComparer.Ordinal) { existing.Id });

        AssertAddRecordThrows(
            ValidRecord() with { Id = "other-id" },
            domains: new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal)
            {
                ["example.test"] = existing,
            });

        AssertAddRecordThrows(
            ValidRecord() with
            {
                Id = "other-id",
                Domains = OtherDomain,
                ProviderIdAliases = ExistingAlias,
            },
            aliases: new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal)
            {
                ["existingalias"] = existing,
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an absolute url")]
    [InlineData("ftp://cock.li/account")]
    public void NonHttpAccountLocationsNeverContributeAClassification(string? accountUrl)
    {
        var suggestion = RepositoryAccountClassificationCatalog.Classify(
            "definitely-unlisted-provider",
            accountUrl);

        Assert.Equal(AccountRecoveryCategory.Unknown, suggestion.Category);
    }

    private static AccountClassificationProviderRecord ValidRecord() => new(
        "valid-id",
        "Valid provider",
        AccountRecoveryCategory.Critical,
        ExampleDomain,
        ValidAlias,
        "test-provenance");

    private static void AssertAddRecordThrows(
        AccountClassificationProviderRecord record,
        HashSet<string>? ids = null,
        Dictionary<string, AccountClassificationProviderRecord>? domains = null,
        Dictionary<string, AccountClassificationProviderRecord>? aliases = null)
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeAddRecord(
                record,
                [],
                ids ?? new HashSet<string>(StringComparer.Ordinal),
                domains ?? new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal),
                aliases ?? new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal),
                allowClaimedDomainSkip: false));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static void InvokeAddRecord(
        AccountClassificationProviderRecord record,
        List<AccountClassificationProviderRecord> records,
        HashSet<string> ids,
        Dictionary<string, AccountClassificationProviderRecord> domains,
        Dictionary<string, AccountClassificationProviderRecord> aliases,
        bool allowClaimedDomainSkip)
    {
        var method = typeof(RepositoryAccountClassificationCatalog).GetMethod(
            "AddRecord",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(
            null,
            [record, records, ids, domains, aliases, allowClaimedDomainSkip]);
    }
}
