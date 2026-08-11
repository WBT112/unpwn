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

    [Fact]
    public void EmptyAndMalformedHeadersAreRejected()
    {
        using var empty = new StringReader(string.Empty);
        using var blankColumn = new StringReader("service,,username\n");
        using var unterminated = new StringReader("service,\"username\n");

        Assert.Contains(
            CsvAccountImportService.Analyze(empty).Diagnostics,
            diagnostic => diagnostic.Code == "MissingHeader");
        Assert.Contains(
            CsvAccountImportService.Analyze(blankColumn).Diagnostics,
            diagnostic => diagnostic.Code == "MalformedHeader");
        Assert.Contains(
            CsvAccountImportService.Analyze(unterminated).Diagnostics,
            diagnostic => diagnostic.Code == "MalformedHeader");
    }

    [Fact]
    public void PreviewRejectsRepeatedAndIncompleteMappings()
    {
        using var repeatedSource = new StringReader("service,username\nExample,user\n");
        using var missingServiceSource = new StringReader("service,username\nExample,user\n");
        using var missingAccountSource = new StringReader("service,username\nExample,user\n");

        var repeated = CsvAccountImportService.CreatePreview(
            repeatedSource, new CsvColumnMapping("service", "service", "username", null, []));
        var missingService = CsvAccountImportService.CreatePreview(
            missingServiceSource, new CsvColumnMapping(null, null, "username", null, []));
        var missingAccount = CsvAccountImportService.CreatePreview(
            missingAccountSource, new CsvColumnMapping("service", null, null, null, []));

        Assert.Contains(repeated.Diagnostics, diagnostic => diagnostic.Code == "RepeatedMappedColumn");
        Assert.Contains(missingService.Diagnostics, diagnostic => diagnostic.Code == "MissingServiceMapping");
        Assert.Contains(missingAccount.Diagnostics, diagnostic => diagnostic.Code == "MissingAccountMapping");
    }

    [Fact]
    public void PasswordColumnCannotBeMappedEvenWhenExcluded()
    {
        using var source = new StringReader("service,username,password\nExample,user,discarded\n");
        var mapping = new CsvColumnMapping("password", null, "username", null, ["password"]);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "PasswordColumnMapped");
        Assert.Empty(preview.Candidates);
    }

    [Fact]
    public void MissingRequiredRowValuesRejectOnlyAffectedRows()
    {
        using var source = new StringReader(
            "service,url,account,username\n" +
            ",,Personal,user@example.test\n" +
            "Example,https://example.test,,\n" +
            "Valid,https://valid.example,Personal,user@example.test\n");
        var mapping = new CsvColumnMapping("service", "account", "username", "url", []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Single(preview.Candidates);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "MissingServiceValue");
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "MissingAccountValue");
    }

    [Theory]
    [InlineData("ftp://example.test")]
    [InlineData("/relative/path")]
    [InlineData("https://")]
    [InlineData("https://user:old-password@example.test/account")]
    public void UnsupportedAccountUrlsAreRejected(string url)
    {
        using var source = new StringReader($"url,username\n{url},user@example.test\n");
        var mapping = new CsvColumnMapping(null, null, "username", "url", []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "InvalidAccountUrl");
    }

    [Fact]
    public void ParserFlagsQuotesInUnquotedFieldsAndCharactersAfterClosingQuote()
    {
        using var source = new StringReader(
            "service,username\n" +
            "Ex\"ample,user@example.test\n" +
            "\"Example\"oops,user2@example.test\n" +
            "Valid,user3@example.test\n");
        var mapping = new CsvColumnMapping("service", null, "username", null, []);

        var preview = CsvAccountImportService.CreatePreview(source, mapping);

        Assert.Single(preview.Candidates);
        Assert.Equal(2, preview.Diagnostics.Count(diagnostic => diagnostic.Code == "MalformedRow"));
    }

    [Fact]
    public void RequestedDelimiterAndQuotedDelimiterAreHandledDeterministically()
    {
        using var analysisSource = new StringReader("\"service,name\"|username|pwd\n");
        using var previewSource = new StringReader(
            "service;username;note\nExample;user@example.test;\"trailing whitespace\"   \n");

        Assert.Equal('|', CsvAccountImportService.Analyze(analysisSource).Delimiter);
        var preview = CsvAccountImportService.CreatePreview(
            previewSource,
            new CsvColumnMapping("service", null, "username", null, []),
            delimiter: ';');

        Assert.Single(preview.Candidates);
    }

    [Fact]
    public void DuplicateIdentityFallsBackToAccountNameAndSkipsIncompleteExistingRecords()
    {
        using var source = new StringReader("service,account\nExample,Personal\nexample,PERSONAL\n");
        var mapping = new CsvColumnMapping("service", "account", null, null, []);
        ExistingAccountReference[] existingAccounts =
        [
            new("incomplete", null, null, null, null),
            new("matching", " Example ", " personal ", null, null),
        ];

        var preview = CsvAccountImportService.CreatePreview(source, mapping, existingAccounts);

        Assert.Equal(CsvDuplicateKind.ExistingAccount, preview.Candidates[0].DuplicateKind);
        Assert.Equal(
            CsvDuplicateKind.WithinImport | CsvDuplicateKind.ExistingAccount,
            preview.Candidates[1].DuplicateKind);
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
