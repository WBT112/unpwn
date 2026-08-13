using System.Text;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.Import.Tests.Csv;

public sealed class CsvImportResourceLimitTests
{
    [Fact]
    public void PreviewRejectsLongUnquotedFieldWithoutCandidates()
    {
        const string csv = "service,username\nABCDEFGHI,person@example.invalid\n";
        var limits = Limits(maximumFieldCharacters: 8);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void PreviewRejectsLongQuotedMultilineField()
    {
        const string csv = "service,username\n\"1234\n5678\",person@example.invalid\n";
        var limits = Limits(maximumFieldCharacters: 8);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void PreviewRejectsUnterminatedQuotedFieldAtConfiguredBoundary()
    {
        const string csv = "service,username\n\"123456789,person@example.invalid";
        var limits = Limits(maximumFieldCharacters: 8);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void AnalyzeRejectsHeaderBeyondConfiguredLength()
    {
        const string csv = "service,username\nMail,person@example.invalid\n";
        var limits = Limits(maximumHeaderCharacters: 15);

        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        Assert.Equal(CsvImportFailureCodes.InputTooComplex, Assert.Single(analysis.Diagnostics).Code);
        Assert.Empty(analysis.Headers);
    }

    [Fact]
    public void PreviewRejectsRecordBeyondConfiguredLength()
    {
        const string csv = "service,username\nABCDEFGHIJ,KLMNOPQRST\n";
        var limits = Limits(maximumRecordCharacters: 20, maximumFieldCharacters: 20);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void AnalyzeRejectsExcessiveColumnCount()
    {
        const string csv = "service,username,extra,more\nMail,person@example.invalid,x,y\n";
        var limits = Limits(maximumColumns: 3);

        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        Assert.Equal(CsvImportFailureCodes.InputTooComplex, Assert.Single(analysis.Diagnostics).Code);
        Assert.Empty(analysis.Headers);
    }

    [Fact]
    public void PreviewRejectsExcessiveRowCount()
    {
        const string csv = "service,username\nMail,a@example.invalid\nMail,b@example.invalid\nMail,c@example.invalid\n";
        var limits = Limits(maximumRows: 2, maximumPreviewCandidates: 2);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void PreviewRejectsExcessiveCandidateCount()
    {
        const string csv = "service,username\nMail,a@example.invalid\nMail,b@example.invalid\n";
        var limits = Limits(maximumRows: 4, maximumPreviewCandidates: 1);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void PreviewRejectsDiagnosticFlood()
    {
        const string csv = "service,username\n,\n,\n,\n";
        var limits = Limits(maximumRows: 4, maximumPreviewCandidates: 4, maximumDiagnostics: 2);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
    }

    [Fact]
    public void StreamPreviewRejectsInputByteLimit()
    {
        const string csv = "service,username\nMail,person@example.invalid\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var limits = Limits(maximumInputBytes: 20);
        var mapping = new CsvColumnMapping("service", null, "username", null, []);
        using var stream = new MemoryStream(bytes, writable: false);

        var preview = CsvAccountImportService.CreatePreview(
            stream,
            mapping,
            limits: limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooLarge);
    }

    [Fact]
    public void TextPreviewRejectsDecodedCharacterLimit()
    {
        const string csv = "service,username\nMail,person@example.invalid\n";
        var limits = Limits(
            maximumInputCharacters: 24,
            maximumHeaderCharacters: 16,
            maximumRecordCharacters: 20,
            maximumFieldCharacters: 20);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooLarge);
    }

    [Fact]
    public void CancellationInterruptsParsingLoop()
    {
        const string csv = "service,username\nMail,a@example.invalid\nMail,b@example.invalid\nMail,c@example.invalid\n";
        var limits = Limits();
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);
        using var cancellation = new CancellationTokenSource();
        using var reader = new CancellingTextReader(csv, cancellation, cancelAfterReads: 28);

        Assert.Throws<OperationCanceledException>(() =>
            CsvAccountImportService.CreatePreview(
                reader,
                analysis.SuggestedMapping,
                null,
                analysis.Delimiter,
                limits,
                cancellation.Token));
    }

    [Fact]
    public void ExcludedPasswordFieldStillCountsTowardLimitWithoutLeakingValue()
    {
        const string secret = "UNPWN_TEST_SECRET_password_value_that_is_too_long";
        var csv = $"service,username,password\nMail,person@example.invalid,{secret}\n";
        var limits = Limits(maximumFieldCharacters: 32);
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv), null, limits);

        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping,
            null,
            analysis.Delimiter,
            limits);

        AssertLimit(preview, CsvImportFailureCodes.InputTooComplex);
        Assert.DoesNotContain(preview.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(secret, StringComparison.Ordinal));
    }

    private static void AssertLimit(CsvImportPreview preview, string expectedCode)
    {
        Assert.False(preview.CanImport);
        Assert.Empty(preview.Candidates);
        var diagnostic = Assert.Single(preview.Diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Null(diagnostic.RowNumber);
    }

    private static CsvImportLimits Limits(
        int maximumInputBytes = 4096,
        int maximumInputCharacters = 4096,
        int maximumHeaderCharacters = 128,
        int maximumRecordCharacters = 128,
        int maximumFieldCharacters = 64,
        int maximumColumns = 16,
        int maximumRows = 16,
        int maximumPreviewCandidates = 16,
        int maximumDiagnostics = 16) => new(
            maximumInputBytes,
            maximumInputCharacters,
            maximumHeaderCharacters,
            maximumRecordCharacters,
            maximumFieldCharacters,
            maximumColumns,
            maximumRows,
            maximumPreviewCandidates,
            maximumDiagnostics,
            MaximumDiagnosticMessageCharacters: 256);

    private sealed class CancellingTextReader : TextReader
    {
        private readonly StringReader _inner;
        private readonly CancellationTokenSource _cancellation;
        private readonly int _cancelAfterReads;
        private int _reads;

        internal CancellingTextReader(
            string value,
            CancellationTokenSource cancellation,
            int cancelAfterReads)
        {
            _inner = new StringReader(value);
            _cancellation = cancellation;
            _cancelAfterReads = cancelAfterReads;
        }

        public override int Peek() => _inner.Peek();

        public override int Read()
        {
            if (_reads++ == _cancelAfterReads)
            {
                _cancellation.Cancel();
            }

            return _inner.Read();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
