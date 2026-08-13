using System.Text;

namespace Unpwn.Import.Csv;

internal static class CsvStreamParser
{
    internal static IEnumerable<CsvRecord> Parse(
        TextReader reader,
        char delimiter,
        ISet<int>? excludedColumns = null,
        int firstRowNumber = 1,
        CsvImportLimits? limits = null,
        CsvCharacterReadBudget? readBudget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        limits ??= CsvImportLimits.Default;
        limits.Validate();
        var budget = readBudget ?? new CsvCharacterReadBudget(
            limits.MaximumInputCharacters,
            cancellationToken);

        var fields = new List<string>();
        var field = new StringBuilder();
        var rowNumber = firstRowNumber;
        var recordRowNumber = rowNumber;
        var fieldIndex = 0;
        var fieldCharacters = 0;
        var recordCharacters = 0;
        var records = 0;
        var inQuotes = false;
        var afterClosingQuote = false;
        var atFieldStart = true;
        var hasRecordContent = false;
        var malformed = false;

        while (budget.Read(reader) is var read && read >= 0)
        {
            var character = (char)read;
            IncrementRecordCharacters(ref recordCharacters, limits);
            var isNewLine = character is '\r' or '\n';

            if (character == '\r' && reader.Peek() == '\n')
            {
                _ = budget.Read(reader);
                IncrementRecordCharacters(ref recordCharacters, limits);
            }

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        _ = budget.Read(reader);
                        IncrementRecordCharacters(ref recordCharacters, limits);
                        AppendFieldCharacter(
                            field,
                            '"',
                            fieldIndex,
                            excludedColumns,
                            ref fieldCharacters,
                            limits);
                        hasRecordContent = true;
                    }
                    else
                    {
                        inQuotes = false;
                        afterClosingQuote = true;
                    }
                }
                else
                {
                    AppendFieldCharacter(
                        field,
                        isNewLine ? '\n' : character,
                        fieldIndex,
                        excludedColumns,
                        ref fieldCharacters,
                        limits);
                    hasRecordContent = true;
                    if (isNewLine)
                    {
                        rowNumber++;
                    }
                }

                continue;
            }

            if (afterClosingQuote)
            {
                if (character == delimiter)
                {
                    FinishField(fields, field, ref fieldCharacters, limits);
                    fieldIndex++;
                    atFieldStart = true;
                    afterClosingQuote = false;
                    hasRecordContent = true;
                    continue;
                }

                if (isNewLine)
                {
                    FinishField(fields, field, ref fieldCharacters, limits);
                    IncrementRecordCount(ref records, limits);
                    yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
                    ResetRecord(
                        fields,
                        field,
                        ref fieldIndex,
                        ref fieldCharacters,
                        ref recordCharacters,
                        ref atFieldStart,
                        ref afterClosingQuote,
                        ref hasRecordContent,
                        ref malformed);
                    rowNumber++;
                    recordRowNumber = rowNumber;
                    continue;
                }

                if (character is ' ' or '\t' && delimiter != '\t')
                {
                    continue;
                }

                malformed = true;
                hasRecordContent = true;
                continue;
            }

            if (isNewLine)
            {
                FinishField(fields, field, ref fieldCharacters, limits);
                IncrementRecordCount(ref records, limits);
                yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
                ResetRecord(
                    fields,
                    field,
                    ref fieldIndex,
                    ref fieldCharacters,
                    ref recordCharacters,
                    ref atFieldStart,
                    ref afterClosingQuote,
                    ref hasRecordContent,
                    ref malformed);
                rowNumber++;
                recordRowNumber = rowNumber;
                continue;
            }

            if (character == delimiter)
            {
                FinishField(fields, field, ref fieldCharacters, limits);
                fieldIndex++;
                atFieldStart = true;
                hasRecordContent = true;
                continue;
            }

            if (character == '"')
            {
                if (atFieldStart)
                {
                    inQuotes = true;
                }
                else
                {
                    malformed = true;
                }

                hasRecordContent = true;
                continue;
            }

            AppendFieldCharacter(
                field,
                character,
                fieldIndex,
                excludedColumns,
                ref fieldCharacters,
                limits);
            atFieldStart = false;
            hasRecordContent = true;
        }

        if (inQuotes)
        {
            malformed = true;
        }

        if (hasRecordContent || fields.Count > 0 || field.Length > 0)
        {
            FinishField(fields, field, ref fieldCharacters, limits);
            IncrementRecordCount(ref records, limits);
            yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
        }
    }

    private static void IncrementRecordCharacters(
        ref int recordCharacters,
        CsvImportLimits limits)
    {
        if (recordCharacters >= limits.MaximumRecordCharacters)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
        }

        recordCharacters++;
    }

    private static void IncrementRecordCount(ref int records, CsvImportLimits limits)
    {
        if (records >= limits.MaximumRows)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
        }

        records++;
    }

    private static void AppendFieldCharacter(
        StringBuilder field,
        char character,
        int fieldIndex,
        ISet<int>? excludedColumns,
        ref int fieldCharacters,
        CsvImportLimits limits)
    {
        if (fieldCharacters >= limits.MaximumFieldCharacters)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
        }

        fieldCharacters++;
        if (excludedColumns?.Contains(fieldIndex) != true)
        {
            _ = field.Append(character);
        }
    }

    private static void FinishField(
        List<string> fields,
        StringBuilder field,
        ref int fieldCharacters,
        CsvImportLimits limits)
    {
        if (fields.Count >= limits.MaximumColumns)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
        }

        fields.Add(field.ToString());
        _ = field.Clear();
        fieldCharacters = 0;
    }

    private static void ResetRecord(
        List<string> fields,
        StringBuilder field,
        ref int fieldIndex,
        ref int fieldCharacters,
        ref int recordCharacters,
        ref bool atFieldStart,
        ref bool afterClosingQuote,
        ref bool hasRecordContent,
        ref bool malformed)
    {
        fields.Clear();
        _ = field.Clear();
        fieldIndex = 0;
        fieldCharacters = 0;
        recordCharacters = 0;
        atFieldStart = true;
        afterClosingQuote = false;
        hasRecordContent = false;
        malformed = false;
    }
}

internal sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Fields, bool IsMalformed);
