using System.Collections;

namespace Unpwn.Import.Csv;

public sealed record CsvImportLimits(
    int MaximumInputBytes,
    int MaximumInputCharacters,
    int MaximumHeaderCharacters,
    int MaximumRecordCharacters,
    int MaximumFieldCharacters,
    int MaximumColumns,
    int MaximumRows,
    int MaximumPreviewCandidates,
    int MaximumDiagnostics,
    int MaximumDiagnosticMessageCharacters)
{
    public static CsvImportLimits Default { get; } = new(
        MaximumInputBytes: 32 * 1024 * 1024,
        MaximumInputCharacters: 32 * 1024 * 1024,
        MaximumHeaderCharacters: 64 * 1024,
        MaximumRecordCharacters: 512 * 1024,
        MaximumFieldCharacters: 256 * 1024,
        MaximumColumns: 256,
        MaximumRows: 25_000,
        MaximumPreviewCandidates: 25_000,
        MaximumDiagnostics: 512,
        MaximumDiagnosticMessageCharacters: 512);

    public void Validate()
    {
        if (MaximumInputBytes <= 0 ||
            MaximumInputCharacters <= 0 ||
            MaximumHeaderCharacters <= 0 ||
            MaximumRecordCharacters <= 0 ||
            MaximumFieldCharacters <= 0 ||
            MaximumColumns <= 0 ||
            MaximumRows <= 0 ||
            MaximumPreviewCandidates <= 0 ||
            MaximumDiagnostics <= 0 ||
            MaximumDiagnosticMessageCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CsvImportLimits));
        }

        if (MaximumHeaderCharacters > MaximumInputCharacters ||
            MaximumRecordCharacters > MaximumInputCharacters ||
            MaximumFieldCharacters > MaximumRecordCharacters ||
            MaximumPreviewCandidates > MaximumRows)
        {
            throw new ArgumentException("CSV import resource limits are inconsistent.", nameof(CsvImportLimits));
        }
    }
}

public static class CsvImportFailureCodes
{
    public const string InputTooLarge = "InputTooLarge";
    public const string InputTooComplex = "InputTooComplex";
}

internal sealed class CsvImportLimitException : InvalidOperationException
{
    internal CsvImportLimitException(string code)
        : base("The CSV input exceeded a supported resource limit.")
    {
        Code = code;
    }

    internal string Code { get; }
}

internal sealed class CsvCharacterReadBudget(
    int maximumCharacters,
    CancellationToken cancellationToken)
{
    private int _charactersRead;

    internal int Read(TextReader reader)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = reader.Read();
        if (value < 0)
        {
            return value;
        }

        if (_charactersRead >= maximumCharacters)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooLarge);
        }

        _charactersRead++;
        return value;
    }
}

internal sealed class CsvDiagnosticCollector : IEnumerable<CsvImportDiagnostic>
{
    private readonly CsvImportLimits _limits;
    private readonly List<CsvImportDiagnostic> _diagnostics = [];

    internal CsvDiagnosticCollector(
        CsvImportLimits limits,
        IEnumerable<CsvImportDiagnostic>? initialDiagnostics = null)
    {
        _limits = limits;
        if (initialDiagnostics is null)
        {
            return;
        }

        foreach (var diagnostic in initialDiagnostics)
        {
            Add(diagnostic);
        }
    }

    internal IReadOnlyList<CsvImportDiagnostic> Snapshot => [.. _diagnostics];

    internal void Add(CsvImportDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (_diagnostics.Count >= _limits.MaximumDiagnostics ||
            diagnostic.Message.Length > _limits.MaximumDiagnosticMessageCharacters)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
        }

        _diagnostics.Add(diagnostic);
    }

    public IEnumerator<CsvImportDiagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class CsvImportResourceGuard
{
    internal static string? ReadHeaderLine(
        TextReader source,
        CsvImportLimits limits,
        CsvCharacterReadBudget budget)
    {
        var builder = new System.Text.StringBuilder();
        var hasContent = false;

        while (true)
        {
            var read = budget.Read(source);
            if (read < 0)
            {
                return hasContent ? builder.ToString() : null;
            }

            hasContent = true;
            var character = (char)read;
            if (character == '\r')
            {
                if (source.Peek() == '\n')
                {
                    _ = budget.Read(source);
                }

                return builder.ToString();
            }

            if (character == '\n')
            {
                return builder.ToString();
            }

            if (builder.Length >= limits.MaximumHeaderCharacters)
            {
                throw new CsvImportLimitException(CsvImportFailureCodes.InputTooComplex);
            }

            _ = builder.Append(character);
        }
    }
}

internal sealed class CsvBoundedReadStream(
    Stream inner,
    int maximumBytes,
    CancellationToken cancellationToken) : Stream
{
    private long _bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = maximumBytes - _bytesRead;
        var permitted = (int)Math.Min(buffer.Length, Math.Max(0, remaining) + 1);
        if (permitted == 0)
        {
            return 0;
        }

        var read = inner.Read(buffer[..permitted]);
        if (read <= 0)
        {
            return read;
        }

        if (read > remaining)
        {
            throw new CsvImportLimitException(CsvImportFailureCodes.InputTooLarge);
        }

        _bytesRead += read;
        return read;
    }

    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return Read(value) == 0 ? -1 : value[0];
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
