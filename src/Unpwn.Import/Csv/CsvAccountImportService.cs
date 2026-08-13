using System.Text;

namespace Unpwn.Import.Csv;

public sealed class CsvAccountImportService
{
    private static readonly char[] SupportedDelimiters = [',', ';', '\t', '|'];
    private static readonly string[] ServiceNameAliases = ["service", "servicename", "site", "provider", "folder"];
    private static readonly string[] AccountNameAliases = ["account", "accountname", "title", "label", "name"];
    private static readonly string[] LoginIdentifierAliases =
        ["username", "user", "login", "loginusername", "email", "emailaddress"];
    private static readonly string[] AccountUrlAliases = ["url", "uri", "loginuri", "website", "origin", "hostname"];

    public static CsvImportAnalysis Analyze(TextReader source, char? delimiter = null) =>
        Analyze(source, delimiter, CsvImportLimits.Default, CancellationToken.None);

    public static CsvImportAnalysis Analyze(
        TextReader source,
        char? delimiter,
        CsvImportLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        try
        {
            var budget = new CsvCharacterReadBudget(
                limits.MaximumInputCharacters,
                cancellationToken);
            var headerLine = CsvImportResourceGuard.ReadHeaderLine(source, limits, budget);
            return AnalyzeHeader(headerLine, delimiter, limits, cancellationToken);
        }
        catch (CsvImportLimitException exception)
        {
            return CreateLimitAnalysis(delimiter ?? ',', exception.Code);
        }
    }

    public static CsvImportAnalysis Analyze(
        Stream source,
        char? delimiter = null,
        CsvImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= CsvImportLimits.Default;
        limits.Validate();

        using var boundedStream = new CsvBoundedReadStream(
            source,
            limits.MaximumInputBytes,
            cancellationToken);
        using var reader = new StreamReader(
            boundedStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        return Analyze((TextReader)reader, delimiter, limits, cancellationToken);
    }

    public static CsvImportAnalysis Analyze(StreamReader source, char? delimiter = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.BaseStream.CanSeek && source.BaseStream.Position == 0
            ? Analyze(source.BaseStream, delimiter)
            : Analyze((TextReader)source, delimiter);
    }

    public static CsvImportPreview CreatePreview(
        TextReader source,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existingAccounts = null,
        char? delimiter = null) =>
        CreatePreview(
            source,
            mapping,
            existingAccounts,
            delimiter,
            CsvImportLimits.Default,
            CancellationToken.None);

    public static CsvImportPreview CreatePreview(
        TextReader source,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existingAccounts,
        char? delimiter,
        CsvImportLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var effectiveDelimiter = delimiter ?? ',';
        try
        {
            var budget = new CsvCharacterReadBudget(
                limits.MaximumInputCharacters,
                cancellationToken);
            var headerLine = CsvImportResourceGuard.ReadHeaderLine(source, limits, budget);
            var analysis = AnalyzeHeader(headerLine, delimiter, limits, cancellationToken);
            effectiveDelimiter = analysis.Delimiter;
            var diagnostics = new CsvDiagnosticCollector(limits, analysis.Diagnostics);

            if (headerLine is null || HasDocumentErrors(diagnostics))
            {
                return new CsvImportPreview(analysis, mapping, [], diagnostics.Snapshot);
            }

            var headerIndexes = CreateHeaderIndexes(analysis.Headers);
            ValidateMapping(mapping, analysis, headerIndexes, diagnostics);
            if (HasDocumentErrors(diagnostics))
            {
                return new CsvImportPreview(analysis, mapping, [], diagnostics.Snapshot);
            }

            var excludedIndexes = mapping.ExcludedPasswordColumns
                .Select(column => headerIndexes[column])
                .ToHashSet();
            var candidates = new List<ImportAccountCandidate>();

            foreach (var record in CsvStreamParser.Parse(
                         source,
                         analysis.Delimiter,
                         excludedIndexes,
                         2,
                         limits,
                         cancellationToken,
                         budget))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    if (candidates.Count >= limits.MaximumPreviewCandidates)
                    {
                        throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
                    }

                    candidates.Add(candidate);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            MarkDuplicates(candidates, existingAccounts ?? []);
            return new CsvImportPreview(analysis, mapping, candidates, diagnostics.Snapshot);
        }
        catch (CsvImportLimitException exception)
        {
            return CreateLimitPreview(mapping, effectiveDelimiter, exception.Code);
        }
    }

    public static CsvImportPreview CreatePreview(
        Stream source,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existingAccounts = null,
        char? delimiter = null,
        CsvImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);
        limits ??= CsvImportLimits.Default;
        limits.Validate();

        using var boundedStream = new CsvBoundedReadStream(
            source,
            limits.MaximumInputBytes,
            cancellationToken);
        using var reader = new StreamReader(
            boundedStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        return CreatePreview(
            (TextReader)reader,
            mapping,
            existingAccounts,
            delimiter,
            limits,
            cancellationToken);
    }

    public static CsvImportPreview CreatePreview(
        StreamReader source,
        CsvColumnMapping mapping,
        IEnumerable<ExistingAccountReference>? existingAccounts = null,
        char? delimiter = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.BaseStream.CanSeek && source.BaseStream.Position == 0
            ? CreatePreview(source.BaseStream, mapping, existingAccounts, delimiter)
            : CreatePreview((TextReader)source, mapping, existingAccounts, delimiter);
    }

    public static CsvMappingAssessment AssessMapping(
        CsvImportAnalysis analysis,
        CsvColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(mapping);
        return AssessMapping(
            analysis.Headers,
            analysis.DetectedPasswordColumns,
            mapping);
    }

    private static CsvImportAnalysis AnalyzeHeader(
        string? headerLine,
        char? requestedDelimiter,
        CsvImportLimits limits,
        CancellationToken cancellationToken)
    {
        var diagnostics = new CsvDiagnosticCollector(limits);
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
                new CsvMappingAssessment(
                    CsvMappingQuality.Incomplete,
                    [
                        CsvMappingIssue.MissingServiceIdentity,
                        CsvMappingIssue.MissingAccountIdentity,
                    ]),
                diagnostics.Snapshot);
        }

        var delimiter = requestedDelimiter ?? DetectDelimiter(headerLine);
        var headerRecord = CsvStreamParser.Parse(
                new StringReader(headerLine),
                delimiter,
                limits: limits,
                cancellationToken: cancellationToken)
            .Single();
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

        var mapping = new CsvColumnMapping(
            FindUnambiguousHeader(headers, ServiceNameAliases),
            FindUnambiguousHeader(headers, AccountNameAliases),
            FindUnambiguousHeader(headers, LoginIdentifierAliases),
            FindUnambiguousHeader(headers, AccountUrlAliases),
            passwordColumns);
        var assessment = AssessMapping(headers, passwordColumns, mapping);

        return new CsvImportAnalysis(
            delimiter,
            headers,
            mapping,
            passwordColumns,
            assessment,
            diagnostics.Snapshot);
    }

    private static CsvMappingAssessment AssessMapping(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> passwordColumns,
        CsvColumnMapping mapping)
    {
        var issues = new List<CsvMappingIssue>();
        var mappedColumns = new[]
        {
            mapping.ServiceNameColumn,
            mapping.AccountNameColumn,
            mapping.LoginIdentifierColumn,
            mapping.AccountUrlColumn,
        }.Where(column => column is not null).Cast<string>().ToArray();
        var referencedColumns = mappedColumns.Concat(mapping.ExcludedPasswordColumns).ToArray();

        if (referencedColumns.Any(column =>
                !headers.Contains(column, StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(CsvMappingIssue.MissingMappedColumn);
        }

        if (mappedColumns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != mappedColumns.Length)
        {
            issues.Add(CsvMappingIssue.RepeatedMappedColumn);
        }

        if (mappedColumns.Any(mappedColumn =>
                passwordColumns.Contains(mappedColumn, StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(CsvMappingIssue.PasswordColumnMapped);
        }

        if (passwordColumns.Any(passwordColumn =>
                !mapping.ExcludedPasswordColumns.Contains(passwordColumn, StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(CsvMappingIssue.PasswordColumnNotExcluded);
        }

        if (mapping.ServiceNameColumn is null && mapping.AccountUrlColumn is null)
        {
            AddUnresolvedIdentityIssues(
                issues,
                headers,
                ServiceNameAliases,
                CsvMappingIssue.AmbiguousServiceName,
                AccountUrlAliases,
                CsvMappingIssue.AmbiguousAccountUrl,
                CsvMappingIssue.MissingServiceIdentity);
        }

        if (mapping.LoginIdentifierColumn is null && mapping.AccountNameColumn is null)
        {
            AddUnresolvedIdentityIssues(
                issues,
                headers,
                LoginIdentifierAliases,
                CsvMappingIssue.AmbiguousLoginIdentifier,
                AccountNameAliases,
                CsvMappingIssue.AmbiguousAccountName,
                CsvMappingIssue.MissingAccountIdentity);
        }

        var quality = issues.Count == 0
            ? CsvMappingQuality.Complete
            : issues.Contains(CsvMappingIssue.MissingServiceIdentity) ||
              issues.Contains(CsvMappingIssue.MissingAccountIdentity)
                ? CsvMappingQuality.Incomplete
                : CsvMappingQuality.NeedsReview;
        return new CsvMappingAssessment(quality, issues);
    }

    private static void AddUnresolvedIdentityIssues(
        List<CsvMappingIssue> issues,
        IEnumerable<string> headers,
        IReadOnlyCollection<string> primaryAliases,
        CsvMappingIssue primaryAmbiguity,
        IReadOnlyCollection<string> alternativeAliases,
        CsvMappingIssue alternativeAmbiguity,
        CsvMappingIssue missingIssue)
    {
        var primaryMatches = FindHeaders(headers, primaryAliases).Length;
        var alternativeMatches = FindHeaders(headers, alternativeAliases).Length;
        if (primaryMatches > 1)
        {
            issues.Add(primaryAmbiguity);
        }

        if (alternativeMatches > 1)
        {
            issues.Add(alternativeAmbiguity);
        }

        if (primaryMatches <= 1 && alternativeMatches <= 1)
        {
            issues.Add(missingIssue);
        }
    }

    private static void ValidateMapping(
        CsvColumnMapping mapping,
        CsvImportAnalysis analysis,
        Dictionary<string, int> headerIndexes,
        CsvDiagnosticCollector diagnostics)
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
                "Every detected password column must be excluded before previewing the import."));
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
        CsvDiagnosticCollector diagnostics)
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
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static Dictionary<string, int> CreateHeaderIndexes(IReadOnlyList<string> headers) =>
        headers
            .Select((header, index) => (header, index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

    private static string? FindUnambiguousHeader(
        IEnumerable<string> headers,
        IReadOnlyCollection<string> aliases)
    {
        var matches = FindHeaders(headers, aliases);
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string[] FindHeaders(
        IEnumerable<string> headers,
        IReadOnlyCollection<string> aliases) =>
        [.. headers.Where(header =>
            aliases.Contains(NormalizeHeader(header), StringComparer.Ordinal))];

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

    private static CsvImportAnalysis CreateLimitAnalysis(char delimiter, string code)
    {
        var diagnostic = CreateLimitDiagnostic(code);
        return new CsvImportAnalysis(
            delimiter,
            [],
            CsvColumnMapping.Empty,
            [],
            new CsvMappingAssessment(
                CsvMappingQuality.Incomplete,
                [
                    CsvMappingIssue.MissingServiceIdentity,
                    CsvMappingIssue.MissingAccountIdentity,
                ]),
            [diagnostic]);
    }

    private static CsvImportPreview CreateLimitPreview(
        CsvColumnMapping mapping,
        char delimiter,
        string code)
    {
        var analysis = CreateLimitAnalysis(delimiter, code);
        return new CsvImportPreview(analysis, mapping, [], analysis.Diagnostics);
    }

    private static CsvImportDiagnostic CreateLimitDiagnostic(string code) => code switch
    {
        CsvImportFailureCodes.InputTooLarge => new CsvImportDiagnostic(
            CsvImportDiagnosticSeverity.Error,
            CsvImportFailureCodes.InputTooLarge,
            "The CSV input exceeds the supported size limit."),
        CsvImportFailureCodes.InputTooComplex => new CsvImportDiagnostic(
            CsvImportDiagnosticSeverity.Error,
            CsvImportFailureCodes.InputTooComplex,
            "The CSV input exceeds the supported structural complexity limit."),
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static bool HasDocumentErrors(IEnumerable<CsvImportDiagnostic> diagnostics) => diagnostics.Any(
        diagnostic => diagnostic.Severity == CsvImportDiagnosticSeverity.Error && diagnostic.RowNumber is null);
}
