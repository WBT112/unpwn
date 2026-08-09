using System.Text.Json;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.Import.Tests.Csv;

public sealed class CsvAccountImportServiceTests
{
    [Fact]
    public void AnalyzeDetectsPasswordColumnsAndSuggestsSafeMapping()
    {
        using var source = new StringReader("name,url,username,password,note\n");

        var analysis = CsvAccountImportService.Analyze(source);

        Assert.True(analysis.ContainsPasswordColumns);
        Assert.Equal(["password"], analysis.DetectedPasswordColumns);
        Assert.Equal(["password"], analysis.SuggestedMapping.ExcludedPasswordColumns);
        Assert.Equal("name", analysis.SuggestedMapping.ServiceNameColumn);
        Assert.Equal("url", analysis.SuggestedMapping.AccountUrlColumn);
        Assert.Equal("username", analysis.SuggestedMapping.LoginIdentifierColumn);
        Assert.Contains(
            analysis.Diagnostics,
            diagnostic => diagnostic.Message == CsvImportAnalysis.PasswordWarning);
    }

    [Fact]
    public void PreviewDiscardsPasswordValuesAndContainsOnlyAllowedAccountFields()
    {
        const string oldPassword = "UNPWN_TEST_SECRET_imported-old-password";
        using var source = new StringReader(
            $"name,url,username,password\nExample,https://example.test/login,user@example.test,{oldPassword}\n");
        var mapping = new CsvColumnMapping("name", null, "username", "url", ["password"]);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        var candidate = Assert.Single(preview.Candidates);
        Assert.True(preview.CanImport);
        Assert.Equal("Example", candidate.ServiceName);
        Assert.Equal("user@example.test", candidate.LoginIdentifier);
        Assert.DoesNotContain(oldPassword, JsonSerializer.Serialize(preview), StringComparison.Ordinal);
        Assert.DoesNotContain(oldPassword, JsonSerializer.Serialize(mapping), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(',', "service,username,password\nExample,user@example.test,discarded\n")]
    [InlineData(';', "service;username;password\nExample;user@example.test;discarded\n")]
    [InlineData('\t', "service\tusername\tpassword\nExample\tuser@example.test\tdiscarded\n")]
    public void PreviewDetectsCommonDelimiters(char delimiter, string csv)
    {
        using var source = new StringReader(csv);
        var mapping = new CsvColumnMapping("service", null, "username", null, ["password"]);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Equal(delimiter, preview.Analysis.Delimiter);
        Assert.Single(preview.Candidates);
    }

    [Fact]
    public void PreviewSupportsQuotedValuesEscapedQuotesMultilineFieldsAndUnicode()
    {
        using var source = new StringReader(
            "service,account,username,password\n" +
            "\"Example, GmbH\",\"Privat\nKonto\",\"müller \"\"Admin\"\"\",discarded\n");
        var mapping = new CsvColumnMapping("service", "account", "username", null, ["password"]);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("Example, GmbH", candidate.ServiceName);
        Assert.Equal("Privat\nKonto", candidate.AccountName);
        Assert.Equal("müller \"Admin\"", candidate.LoginIdentifier);
    }

    [Fact]
    public void MissingMappedColumnProducesDocumentError()
    {
        using var source = new StringReader("service,username\nExample,user@example.test\n");
        var mapping = new CsvColumnMapping("missing", null, "username", null, []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.False(preview.CanImport);
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "MissingMappedColumn");
    }

    [Fact]
    public void MalformedRowDoesNotAbortRemainingImport()
    {
        using var source = new StringReader(
            "service,username,password\n" +
            "Broken,user@example.test\n" +
            "Valid,valid@example.test,discarded\n");
        var mapping = new CsvColumnMapping("service", null, "username", null, ["password"]);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(3, candidate.RowNumber);
        Assert.Equal("Valid", candidate.ServiceName);
        Assert.Contains(
            preview.Diagnostics,
            diagnostic => diagnostic.Code == "MalformedRow" && diagnostic.RowNumber == 2);
    }

    [Fact]
    public void PreviewKeepsFirstOccurrenceAndMarksOnlyLaterImportDuplicates()
    {
        using var source = new StringReader(
            "service,username\n" +
            "Example,user@example.test\n" +
            "example,USER@example.test\n" +
            "EXAMPLE,user@example.test\n");
        var mapping = new CsvColumnMapping("service", null, "username", null, []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Equal(3, preview.Candidates.Count);
        Assert.Equal(CsvDuplicateKind.None, preview.Candidates[0].DuplicateKind);
        Assert.Empty(preview.Candidates[0].DuplicateImportRowNumbers);
        Assert.All(preview.Candidates.Skip(1), candidate =>
        {
            Assert.Equal(CsvDuplicateKind.WithinImport, candidate.DuplicateKind);
            Assert.Equal([2], candidate.DuplicateImportRowNumbers);
        });
    }

    [Fact]
    public void PreviewMarksExistingAccountOnFirstOccurrenceAndWithinImportOnLaterOccurrence()
    {
        using var source = new StringReader(
            "service,url,username,password\n" +
            "Example,https://example.test/login,user@example.test,discarded\n" +
            "Different label,https://EXAMPLE.test/other,USER@example.test,discarded\n");
        var mapping = new CsvColumnMapping("service", null, "username", "url", ["password"]);
        ExistingAccountReference[] existingAccounts =
        [
            new("account-42", "Example", null, "user@example.test", "https://example.test"),
        ];

        var preview = CsvAccountImportService.CreatePreview(source, mapping, existingAccounts);

        Assert.Equal(2, preview.Candidates.Count);
        Assert.Equal(CsvDuplicateKind.ExistingAccount, preview.Candidates[0].DuplicateKind);
        Assert.Empty(preview.Candidates[0].DuplicateImportRowNumbers);
        Assert.Equal(
            CsvDuplicateKind.WithinImport | CsvDuplicateKind.ExistingAccount,
            preview.Candidates[1].DuplicateKind);
        Assert.Equal([2], preview.Candidates[1].DuplicateImportRowNumbers);
        Assert.All(preview.Candidates, candidate =>
            Assert.Equal(["account-42"], candidate.DuplicateExistingAccountIds));
    }

    [Fact]
    public void PreviewRequiresExplicitExclusionOfEveryPasswordColumn()
    {
        const string oldPassword = "UNPWN_TEST_SECRET_must-not-be-read";
        using var source = new TrackingReader(
            $"service,username,password\nExample,user@example.test,{oldPassword}\n");
        var mapping = new CsvColumnMapping("service", null, "username", null, []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.False(preview.CanImport);
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "PasswordColumnNotExcluded");
        Assert.DoesNotContain(oldPassword, source.ObservedText, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateHeadersAreRejectedCaseInsensitively()
    {
        using var source = new StringReader("service,Username,username\nExample,user,other\n");

        var analysis = CsvAccountImportService.Analyze(source);

        Assert.Contains(analysis.Diagnostics, diagnostic => diagnostic.Code == "DuplicateHeader");
    }

    [Fact]
    public void InvalidUrlRejectsOnlyItsRow()
    {
        using var source = new StringReader(
            "service,url,username\n" +
            "Bad,javascript:alert(1),bad@example.test\n" +
            "Good,https://example.test,good@example.test\n");
        var mapping = new CsvColumnMapping("service", null, "username", "url", []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Single(preview.Candidates);
        Assert.Contains(
            preview.Diagnostics,
            diagnostic => diagnostic.Code == "InvalidAccountUrl" && diagnostic.RowNumber == 2);
    }

    private sealed class TrackingReader(string text) : StringReader(text)
    {
        private readonly List<char> _observedCharacters = [];

        public string ObservedText => new([.. _observedCharacters]);

        public override int Read()
        {
            var value = base.Read();
            if (value >= 0)
            {
                _observedCharacters.Add((char)value);
            }

            return value;
        }

        public override string? ReadLine()
        {
            var value = base.ReadLine();
            if (value is not null)
            {
                _observedCharacters.AddRange(value);
            }

            return value;
        }
    }
}
