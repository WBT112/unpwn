using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.Import.Tests.Csv;

public sealed class CsvSecurityRegressionTests
{
    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void OversizedExcludedSecretFieldIsRejectedWithoutCandidateRetention()
    {
        const string secret = "synthetic-password-value-that-exceeds-the-field-limit";
        var csv = $"service,username,password\nMail,user@example.invalid,{secret}\n";
        var limits = Limits(maximumFieldCharacters: 24);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        Assert.False(preview.CanImport);
        Assert.Empty(preview.Candidates);
        Assert.Equal(
            CsvImportFailureCodes.InputTooComplex,
            Assert.Single(preview.Diagnostics).Code);
        Assert.DoesNotContain(preview.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void FixedSeedOversizedFieldsAlwaysFailClosedWithinSmallBounds()
    {
        var random = new Random(0x134);
        var limits = Limits(maximumFieldCharacters: 32);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var length = random.Next(33, 129);
            var field = new string((char)('A' + (attempt % 26)), length);
            var csv = $"service,username\n{field},user@example.invalid\n";
            var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

            var preview = CsvAccountImportService.CreatePreview(
                new StringReader(csv),
                analysis.SuggestedMapping,
                null,
                analysis.Delimiter,
                limits);

            Assert.False(preview.CanImport);
            Assert.Empty(preview.Candidates);
            Assert.Equal(
                CsvImportFailureCodes.InputTooComplex,
                Assert.Single(preview.Diagnostics).Code);
        }
    }

    private static CsvImportLimits Limits(int maximumFieldCharacters) => new(
        MaximumInputBytes: 4096,
        MaximumInputCharacters: 4096,
        MaximumHeaderCharacters: 128,
        MaximumRecordCharacters: 256,
        MaximumFieldCharacters: maximumFieldCharacters,
        MaximumColumns: 16,
        MaximumRows: 16,
        MaximumPreviewCandidates: 16,
        MaximumDiagnostics: 16,
        MaximumDiagnosticMessageCharacters: 256);
}
