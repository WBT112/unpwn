using System.Text;

namespace Unpwn.Import.Csv;

public sealed class CsvAccountImportService
{
    private static readonly char[] SupportedDelimiters = [',', ';', '\t', '|'];

    public static CsvImportAnalysis Analyze(TextReader source, char? delimiter = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var headerLine = source.ReadLine();
        return AnalyzeHeader(headerLine, delimiter);
    }

    public static CsvImportPreview CreatePreview(
        TextReader source,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existingAccounts = null,
        char? delimiter = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);

        var headerLine = source.ReadLine();
        var analysis = AnalyzeHeader(headerLine, delimiter);
        var diagnostics = new List<CsvImportDiagnostic>(analysis.Diagnostics);

        if (headerLine is null || HasDocumentErrors(diagnostics))
        {
            return new CsvImportPreview(analysis, mapping, [], diagnostics);
        }

        var headerIndexes = CreateHeaderIndexes(analysis.Headers);
        ValidateMapping(mapping, analysis, headerIndexes, diagnostics);
        if (HasDocumentErrors(diagnostics))
        {
            return new CsvImportPreview(analysis, mapping, [], diagnostics);
        }

        var excludedIndexes = mapping.ExcludedPasswordColumns
            .Select(column => headerIndexes[column])
            .ToHashSet();
        var candidates = new List<ImportAccountCandidate>();

        foreach (var record in CsvStreamParser.Parse(source, analysis.Delimiter, excludedIndexes, 2))
        {
            if (record.IsMalformed || record.Fields.Count != analysis.Headers.Count)
            {
                diagnostics.Add(new CsvImportDiagnostic(
                    CsvImportDiagnosticSeverity.Error,
                    "MalformedRow",
                    "The row is malformed or has an unexpected number of columns.",
                    record.RowNumber));
                continue;
            }

            var candidate = CreateCandidate(record, mapping, headerIndexes, diagnostics);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        MarkDuplicates(candidates, existingAccounts ?? []);
        return new CsvImportPreview(analysis, mapping, candidates, diagnostics);
    }

    private static CsvImportAnalysis AnalyzeHeader(string? headerLine, char? requestedDelimiter)
    {
        var diagnostics = new List<CsvImportDiagnostic>();
        if (headerLine is null)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingHeader",
                "The CSV source does not contain a header row."));
            return new CsvImportAnalysis(
                requestedDelimiter ?? ',',
                [],
                CsvColumnMapping.Empty,
                [],
                diagnostics);
        }

        var delimiter = requestedDelimiter ?? DetectDelimiter(headerLine);
        var headerRecord = CsvStreamParser.Parse(new StringReader(headerLine), delimiter).Single();
        var headers = headerRecord.Fields.Select(header => header.Trim()).ToArray();

        if (headerRecord.IsMalformed || headers.Any(string.IsNullOrWhiteSpace))
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MalformedHeader",
                "The CSV header is malformed or contains an empty column name."));
        }

        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "DuplicateHeader",
                "The CSV header contains duplicate column names."));
        }

        var passwordColumns = headers.Where(IsPasswordColumn).ToArray();
        if (passwordColumns.Length > 0)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Warning,
                "PasswordColumnsDetected",
                CsvImportAnalysis.PasswordWarning));
        }

        var mapping = new CsvColumnMapping(
            FindHeader(headers, "service", "servicename", "site", "provider", "name"),
            FindHeader(headers, "account", "accountname", "title", "label"),
            FindHeader(headers, "username", "user", "login", "loginusername", "email", "emailaddress"),
            FindHeader(headers, "url", "uri", "loginuri", "website", "origin", "hostname"),
            passwordColumns);

        return new CsvImportAnalysis(delimiter, headers, mapping, passwordColumns, diagnostics);
    }

    private static void ValidateMapping(
        CsvColumnMapping mapping,
        CsvImportAnalysis analysis,
        Dictionary<string, int> headerIndexes,
        List<CsvImportDiagnostic> diagnostics)
    {
        var mappedColumns = new[]
        {
            mapping.ServiceNameColumn,
            mapping.AccountNameColumn,
            mapping.LoginIdentifierColumn,
            mapping.AccountUrlColumn,
        }.Where(column => column is not null).Cast<string>().ToArray();

        var referencedColumns = mappedColumns.Concat(mapping.ExcludedPasswordColumns).ToArray();
        if (referencedColumns.Any(column => !headerIndexes.ContainsKey(column)))
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingMappedColumn",
                "The mapping references a column that is not present in the CSV header."));
        }

        if (mappedColumns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != mappedColumns.Length)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "RepeatedMappedColumn",
                "A source column cannot be mapped to more than one account field."));
        }

        if (analysis.DetectedPasswordColumns.Any(passwordColumn =>
                !mapping.ExcludedPasswordColumns.Contains(passwordColumn, StringComparer.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "PasswordColumnNotExcluded",
                "Every detected password column must be explicitly excluded before previewing the import."));
        }

        if (mappedColumns.Any(mappedColumn =>
                analysis.DetectedPasswordColumns.Contains(mappedColumn, StringComparer.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "PasswordColumnMapped",
                "A detected password column cannot be mapped to an account field."));
        }

        if (mapping.ServiceNameColumn is null && mapping.AccountUrlColumn is null)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingServiceMapping",
                "Map either a service name or an account URL column."));
        }

        if (mapping.LoginIdentifierColumn is null && mapping.AccountNameColumn is null)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingAccountMapping",
                "Map either a login identifier or an account name column."));
        }
    }

    private static ImportAccountCandidate? CreateCandidate(
        CsvRecord record,
        CsvColumnMapping mapping,
        Dictionary<string, int> headerIndexes,
        List<CsvImportDiagnostic> diagnostics)
    {
        var serviceName = ReadMappedValue(record, mapping.ServiceNameColumn, headerIndexes);
        var accountName = ReadMappedValue(record, mapping.AccountNameColumn, headerIndexes);
        var loginIdentifier = ReadMappedValue(record, mapping.LoginIdentifierColumn, headerIndexes);
        var accountUrl = ReadMappedValue(record, mapping.AccountUrlColumn, headerIndexes);

        if (serviceName is null && accountUrl is null)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingServiceValue",
                "The row does not contain a service name or account URL.",
                record.RowNumber));
            return null;
        }

        if (loginIdentifier is null && accountName is null)
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "MissingAccountValue",
                "The row does not contain a login identifier or account name.",
                record.RowNumber));
            return null;
        }

        if (accountUrl is not null && !IsSupportedAccountUrl(accountUrl))
        {
            diagnostics.Add(new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "InvalidAccountUrl",
                "The row contains an invalid or unsupported account URL.",
                record.RowNumber));
            return null;
        }

        return new ImportAccountCandidate(
            record.RowNumber,
            serviceName,
            accountName,
            loginIdentifier,
            accountUrl);
    }

    private static string? ReadMappedValue(
        CsvRecord record,
        string? column,
        Dictionary<string, int> headerIndexes)
    {
        if (column is null)
        {
            return null;
        }

        var value = record.Fields[headerIndexes[column]].Trim();
        return value.Length == 0 ? null : value;
    }

    private static void MarkDuplicates(
        IReadOnlyList<ImportAccountCandidate> candidates,
        IEnumerable<ExistingAccountReference> existingAccounts)
    {
        var candidateGroups = candidates
            .Select(candidate => (Candidate: candidate, Key: CreateIdentityKey(
                candidate.ServiceName,
                candidate.AccountName,
                candidate.LoginIdentifier,
                candidate.AccountUrl)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal);

        foreach (var group in candidateGroups.Where(group => group.Count() > 1))
        {
            var groupCandidates = group
                .Select(item => item.Candidate)
                .OrderBy(candidate => candidate.RowNumber)
                .ToArray();
            var firstCandidate = groupCandidates[0];
            foreach (var candidate in groupCandidates.Skip(1))
            {
                candidate.DuplicateKind |= CsvDuplicateKind.WithinImport;
                candidate.DuplicateImportRowNumbers = [firstCandidate.RowNumber];
            }
        }

        var existingByKey = existingAccounts
            .Select(account => (Account: account, Key: CreateIdentityKey(
                account.ServiceName,
                account.AccountName,
                account.LoginIdentifier,
                account.AccountUrl)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Account.Id).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var key = CreateIdentityKey(
                candidate.ServiceName,
                candidate.AccountName,
                candidate.LoginIdentifier,
                candidate.AccountUrl);
            if (key is null || !existingByKey.TryGetValue(key, out var existingIds))
            {
                continue;
            }

            candidate.DuplicateKind |= CsvDuplicateKind.ExistingAccount;
            candidate.DuplicateExistingAccountIds = existingIds;
        }
    }

    private static string? CreateIdentityKey(
        string? serviceName,
        string? accountName,
        string? loginIdentifier,
        string? accountUrl)
    {
        var service = accountUrl is not null && Uri.TryCreate(accountUrl, UriKind.Absolute, out var uri)
            ? uri.IdnHost
            : serviceName;
        var account = loginIdentifier ?? accountName;
        if (service is null || account is null)
        {
            return null;
        }

        return $"{NormalizeIdentityPart(service)}\u001f{NormalizeIdentityPart(account)}";
    }

    private static string NormalizeIdentityPart(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static bool IsSupportedAccountUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static Dictionary<string, int> CreateHeaderIndexes(IReadOnlyList<string> headers) =>
        headers
            .Select((header, index) => (header, index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

    private static string? FindHeader(IEnumerable<string> headers, params string[] aliases) =>
        headers.FirstOrDefault(header => aliases.Contains(NormalizeHeader(header), StringComparer.Ordinal));

    private static bool IsPasswordColumn(string header)
    {
        var normalized = NormalizeHeader(header);
        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized is "pass" or "passwd" or "pwd";
    }

    private static string NormalizeHeader(string header) => string.Concat(
        header.Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));

    private static char DetectDelimiter(string headerLine)
    {
        var counts = SupportedDelimiters.ToDictionary(delimiter => delimiter, _ => 0);
        var inQuotes = false;

        for (var index = 0; index < headerLine.Length; index++)
        {
            var character = headerLine[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < headerLine.Length && headerLine[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && counts.TryGetValue(character, out var count))
            {
                counts[character] = count + 1;
            }
        }

        return SupportedDelimiters
            .OrderByDescending(delimiter => counts[delimiter])
            .First();
    }

    private static bool HasDocumentErrors(IEnumerable<CsvImportDiagnostic> diagnostics) => diagnostics.Any(
        diagnostic => diagnostic.Severity == CsvImportDiagnosticSeverity.Error && diagnostic.RowNumber is null);
}
