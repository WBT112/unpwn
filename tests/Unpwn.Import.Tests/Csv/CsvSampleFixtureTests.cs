using System.Text.Json;
using System.Text.RegularExpressions;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Import.Tests.Csv;

public sealed partial class CsvSampleFixtureTests
{
    private const string BitwardenHeader =
        "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp";

    [Fact]
    public void GenericFixtureIsCanonicalParseableSmokeData()
    {
        CsvImportPreview preview = Preview("generic-recovery-sample.csv", GenericMapping);

        Assert.True(preview.CanImport);
        Assert.Equal(16, preview.Candidates.Count);
        Assert.DoesNotContain(preview.Diagnostics, diagnostic =>
            diagnostic.Severity == CsvImportDiagnosticSeverity.Error);
        Assert.Contains(preview.Candidates, candidate => candidate.AccountName == "example-marketplace.test");
        Assert.Contains(preview.Candidates, candidate => candidate.AccountName == "Müller – 测试konto");
        Assert.Contains(preview.Candidates, candidate =>
            candidate.ServiceName is null && candidate.AccountUrl == "https://url-only.example.test/account");
        Assert.Contains(preview.Candidates, candidate =>
            candidate.AccountName is null && candidate.LoginIdentifier == "synthetic-login-only@login-only.example.test");
    }

    [Fact]
    public void BitwardenFixtureMatchesDocumentedHeaderAndExcludesPasswords()
    {
        string path = FixturePath("bitwarden-recovery-sample.csv");
        Assert.Equal(BitwardenHeader, File.ReadLines(path).First());

        CsvImportAnalysis analysis;
        using (var source = File.OpenText(path))
        {
            analysis = CsvAccountImportService.Analyze(source);
        }

        Assert.Equal(["login_password"], analysis.DetectedPasswordColumns);
        Assert.Equal(BitwardenMapping.ServiceNameColumn, analysis.SuggestedMapping.ServiceNameColumn);
        Assert.Equal(BitwardenMapping.AccountNameColumn, analysis.SuggestedMapping.AccountNameColumn);
        Assert.Equal(BitwardenMapping.LoginIdentifierColumn, analysis.SuggestedMapping.LoginIdentifierColumn);
        Assert.Equal(BitwardenMapping.AccountUrlColumn, analysis.SuggestedMapping.AccountUrlColumn);
        Assert.Equal(
            BitwardenMapping.ExcludedPasswordColumns,
            analysis.SuggestedMapping.ExcludedPasswordColumns);
        Assert.True(analysis.MappingAssessment.IsComplete);

        CsvImportPreview preview = Preview("bitwarden-recovery-sample.csv", BitwardenMapping);
        string serialized = JsonSerializer.Serialize(preview);

        Assert.True(preview.CanImport);
        Assert.Equal(5, preview.Candidates.Count);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_", serialized, StringComparison.Ordinal);
        Assert.All(preview.Candidates, candidate => Assert.DoesNotContain("password", candidate.ServiceName!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EdgeFixtureKeepsValidRowsAndReportsStableDiagnostics()
    {
        CsvImportPreview preview = Preview("import-edge-cases.csv", EdgeMapping);

        Assert.True(preview.CanImport);
        Assert.Equal(6, preview.Candidates.Count);
        Assert.Collection(
            preview.Diagnostics.Where(diagnostic => diagnostic.Severity == CsvImportDiagnosticSeverity.Error),
            diagnostic =>
            {
                Assert.Equal("InvalidAccountUrl", diagnostic.Code);
                Assert.Equal(5, diagnostic.RowNumber);
            },
            diagnostic =>
            {
                Assert.Equal("MissingAccountValue", diagnostic.Code);
                Assert.Equal(6, diagnostic.RowNumber);
            },
            diagnostic =>
            {
                Assert.Equal("MalformedRow", diagnostic.Code);
                Assert.Equal(7, diagnostic.RowNumber);
            },
            diagnostic =>
            {
                Assert.Equal("MalformedRow", diagnostic.Code);
                Assert.Equal(8, diagnostic.RowNumber);
            });

        ImportAccountCandidate first = preview.Candidates.Single(candidate => candidate.RowNumber == 2);
        ImportAccountCandidate duplicate = preview.Candidates.Single(candidate => candidate.RowNumber == 3);
        Assert.Equal(CsvDuplicateKind.None, first.DuplicateKind);
        Assert.Equal(CsvDuplicateKind.WithinImport, duplicate.DuplicateKind);
        Assert.Equal([2], duplicate.DuplicateImportRowNumbers);
        Assert.Contains(preview.Candidates, candidate => candidate.RowNumber == 11);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_", JsonSerializer.Serialize(preview), StringComparison.Ordinal);
    }

    [Fact]
    public void ReimportMarksEveryValidFixtureCandidateAsExisting()
    {
        CsvImportPreview first = Preview("generic-recovery-sample.csv", GenericMapping);
        ExistingAccountReference[] existing =
        [
            .. first.Candidates
            .Select((candidate, index) => new ExistingAccountReference(
                $"synthetic-existing-{index}",
                candidate.ServiceName,
                candidate.AccountName,
                candidate.LoginIdentifier,
                candidate.AccountUrl)),
        ];

        CsvImportPreview repeated = Preview("generic-recovery-sample.csv", GenericMapping, existing);

        Assert.Equal(first.Candidates.Count, repeated.Candidates.Count);
        Assert.All(repeated.Candidates, candidate =>
            Assert.True(candidate.DuplicateKind.HasFlag(CsvDuplicateKind.ExistingAccount)));
    }

    [Fact]
    public void GenericFixtureCoversEveryShippedProviderAndRecoveryPath()
    {
        CsvImportPreview preview = Preview("generic-recovery-sample.csv", GenericMapping);

        foreach (RecoveryWorkflowDefinition workflow in RepositoryWorkflowCatalog.Workflows)
        {
            foreach (RecoveryPath path in Enum.GetValues<RecoveryPath>())
            {
                int pathIndex = path switch
                {
                    RecoveryPath.AuthenticatedChange => 1,
                    RecoveryPath.PasswordReset => 2,
                    RecoveryPath.ManualRecovery => 3,
                    _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
                };

                Assert.Contains(preview.Candidates, candidate =>
                    candidate.ServiceName == workflow.ProviderId &&
                    candidate.AccountName == $"{workflow.ProviderName} {pathIndex}");
            }
        }
    }

    [Theory]
    [InlineData("generic-recovery-sample.csv", false)]
    [InlineData("bitwarden-recovery-sample.csv", true)]
    [InlineData("import-edge-cases.csv", true)]
    public void FixtureIdentityAndUrlValuesStayInsideSyntheticNamespaces(
        string fileName,
        bool expectsSecretMarkers)
    {
        string content = File.ReadAllText(FixturePath(fileName));
        Match[] identities = [.. EmailLikeValue().Matches(content).Cast<Match>()];
        Match[] urls = [.. HttpUrl().Matches(content).Cast<Match>()];

        Assert.NotEmpty(identities);
        Assert.All(identities, match => Assert.EndsWith(".example.test", match.Value, StringComparison.OrdinalIgnoreCase));
        Assert.All(urls, match =>
            Assert.EndsWith(".example.test", new Uri(match.Value).Host, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectsSecretMarkers, content.Contains("UNPWN_TEST_SECRET_", StringComparison.Ordinal));
    }

    private static CsvImportPreview Preview(
        string fileName,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existing = null)
    {
        using var source = File.OpenText(FixturePath(fileName));
        return CsvAccountImportService.CreatePreview(source, mapping, existing);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(RepositoryRoot, "samples", "import", fileName);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static CsvColumnMapping GenericMapping { get; } =
        new("service", "account", "username", "url", []);

    private static CsvColumnMapping BitwardenMapping { get; } =
        new("folder", "name", "login_username", "login_uri", ["login_password"]);

    private static CsvColumnMapping EdgeMapping { get; } =
        new("service", "account", "username", "url", ["password"]);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "unpwn.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailLikeValue();

    [GeneratedRegex(@"https?://[^,\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrl();
}
