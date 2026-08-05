using System.Text;

namespace Unpwn.Import.Csv;

internal static class CsvStreamParser
{
    internal static IEnumerable<CsvRecord> Parse(
        TextReader reader,
        char delimiter,
        ISet<int>? excludedColumns = null,
        int firstRowNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var fields = new List<string>();
        var field = new StringBuilder();
        var rowNumber = firstRowNumber;
        var recordRowNumber = rowNumber;
        var fieldIndex = 0;
        var inQuotes = false;
        var afterClosingQuote = false;
        var atFieldStart = true;
        var hasRecordContent = false;
        var malformed = false;

        while (reader.Read() is var read && read >= 0)
        {
            var character = (char)read;
            var isNewLine = character is '\r' or '\n';

            if (character == '\r' && reader.Peek() == '\n')
            {
                _ = reader.Read();
            }

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        _ = reader.Read();
                        AppendIfIncluded(field, '"', fieldIndex, excludedColumns);
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
                    AppendIfIncluded(field, isNewLine ? '\n' : character, fieldIndex, excludedColumns);
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
                    FinishField(fields, field);
                    fieldIndex++;
                    atFieldStart = true;
                    afterClosingQuote = false;
                    hasRecordContent = true;
                    continue;
                }

                if (isNewLine)
                {
                    FinishField(fields, field);
                    yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
                    ResetRecord(
                        fields,
                        field,
                        ref fieldIndex,
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
                FinishField(fields, field);
                yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
                ResetRecord(
                    fields,
                    field,
                    ref fieldIndex,
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
                FinishField(fields, field);
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

            AppendIfIncluded(field, character, fieldIndex, excludedColumns);
            atFieldStart = false;
            hasRecordContent = true;
        }

        if (inQuotes)
        {
            malformed = true;
        }

        if (hasRecordContent || fields.Count > 0 || field.Length > 0)
        {
            FinishField(fields, field);
            yield return new CsvRecord(recordRowNumber, [.. fields], malformed);
        }
    }

    private static void AppendIfIncluded(
        StringBuilder field,
        char character,
        int fieldIndex,
        ISet<int>? excludedColumns)
    {
        if (excludedColumns?.Contains(fieldIndex) != true)
        {
            _ = field.Append(character);
        }
    }

    private static void FinishField(List<string> fields, StringBuilder field)
    {
        fields.Add(field.ToString());
        _ = field.Clear();
    }

    private static void ResetRecord(
        List<string> fields,
        StringBuilder field,
        ref int fieldIndex,
        ref bool atFieldStart,
        ref bool afterClosingQuote,
        ref bool hasRecordContent,
        ref bool malformed)
    {
        fields.Clear();
        _ = field.Clear();
        fieldIndex = 0;
        atFieldStart = true;
        afterClosingQuote = false;
        hasRecordContent = false;
        malformed = false;
    }
}

internal sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Fields, bool IsMalformed);
