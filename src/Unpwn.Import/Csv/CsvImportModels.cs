namespace Unpwn.Import.Csv;

public enum CsvImportDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record CsvImportDiagnostic(
    CsvImportDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? RowNumber = null);

public sealed record CsvColumnMapping(
    string? ServiceNameColumn,
    string? AccountNameColumn,
    string? LoginIdentifierColumn,
    string? AccountUrlColumn,
    IReadOnlyList<string> ExcludedPasswordColumns)
{
    public static CsvColumnMapping Empty { get; } = new(null, null, null, null, []);
}

public enum CsvMappingQuality
{
    Complete,
    NeedsReview,
    Incomplete,
}

public enum CsvMappingIssue
{
    MissingServiceIdentity,
    MissingAccountIdentity,
    AmbiguousServiceName,
    AmbiguousAccountName,
    AmbiguousLoginIdentifier,
    AmbiguousAccountUrl,
    MissingMappedColumn,
    RepeatedMappedColumn,
    PasswordColumnMapped,
    PasswordColumnNotExcluded,
}

public sealed record CsvMappingAssessment(
    CsvMappingQuality Quality,
    IReadOnlyList<CsvMappingIssue> Issues)
{
    public bool IsComplete => Quality == CsvMappingQuality.Complete;
}

public sealed record CsvImportAnalysis(
    char Delimiter,
    IReadOnlyList<string> Headers,
    CsvColumnMapping SuggestedMapping,
    IReadOnlyList<string> DetectedPasswordColumns,
    CsvMappingAssessment MappingAssessment,
    IReadOnlyList<CsvImportDiagnostic> Diagnostics)
{
    public bool ContainsPasswordColumns => DetectedPasswordColumns.Count > 0;
}

[Flags]
public enum CsvDuplicateKind
{
    None = 0,
    WithinImport = 1,
    ExistingAccount = 2,
}

public sealed class ImportAccountCandidate
{
    internal ImportAccountCandidate(
        int rowNumber,
        string? serviceName,
        string? accountName,
        string? loginIdentifier,
        string? accountUrl)
    {
        RowNumber = rowNumber;
        ServiceName = serviceName;
        AccountName = accountName;
        LoginIdentifier = loginIdentifier;
        AccountUrl = accountUrl;
    }

    public int RowNumber { get; }

    public string? ServiceName { get; }

    public string? AccountName { get; }

    public string? LoginIdentifier { get; }

    public string? AccountUrl { get; }

    public CsvDuplicateKind DuplicateKind { get; internal set; }

    public IReadOnlyList<int> DuplicateImportRowNumbers { get; internal set; } = [];

    public IReadOnlyList<string> DuplicateExistingAccountIds { get; internal set; } = [];
}

public sealed record ExistingAccountReference(
    string Id,
    string? ServiceName,
    string? AccountName,
    string? LoginIdentifier,
    string? AccountUrl);

public sealed record CsvImportPreview(
    CsvImportAnalysis Analysis,
    CsvColumnMapping AppliedMapping,
    IReadOnlyList<ImportAccountCandidate> Candidates,
    IReadOnlyList<CsvImportDiagnostic> Diagnostics)
{
    public bool CanImport =>
        Candidates.Count > 0 &&
        !Diagnostics.Any(diagnostic =>
            diagnostic.Severity == CsvImportDiagnosticSeverity.Error && diagnostic.RowNumber is null);
}
