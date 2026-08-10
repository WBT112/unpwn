using System.Text;
using System.Text.Json;
using Unpwn.Application.Diagnostics;

namespace Unpwn.App.Services;

public enum DiagnosticExportFailureCode
{
    None,
    InvalidInput,
    PreviewRequired,
    AccessDenied,
    IoFailure,
}

public sealed record DiagnosticReportPreview(
    Guid Token,
    string Content,
    int EventCount,
    DateTimeOffset GeneratedAt);

public sealed record DiagnosticExportResult(
    bool Succeeded,
    DiagnosticExportFailureCode FailureCode)
{
    public static DiagnosticExportResult Success { get; } =
        new(true, DiagnosticExportFailureCode.None);

    public static DiagnosticExportResult Failure(DiagnosticExportFailureCode code) =>
        new(false, code);
}

public interface IDiagnosticExportService
{
    DiagnosticReportPreview CreatePreview();

    Task<DiagnosticExportResult> ExportAsync(
        DiagnosticReportPreview preview,
        string destinationPath,
        bool previewApproved,
        CancellationToken cancellationToken);
}

public interface IDiagnosticFileWriter
{
    Task WriteAtomicallyAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

public sealed class DiagnosticExportService(
    ISecretSafeDiagnosticSource source,
    SecretSafeDiagnostics diagnostics,
    IDiagnosticFileWriter? writer = null,
    Func<DateTimeOffset>? clock = null,
    string? applicationVersion = null) : IDiagnosticExportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ISecretSafeDiagnosticSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly SecretSafeDiagnostics _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly IDiagnosticFileWriter _writer = writer ?? new FileDiagnosticFileWriter();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly string _applicationVersion = string.IsNullOrWhiteSpace(applicationVersion)
            ? "development"
            : applicationVersion;
    private Guid _currentPreviewToken;

    public DiagnosticReportPreview CreatePreview()
    {
        var generatedAt = _clock();
        var events = _source.Snapshot();
        _currentPreviewToken = Guid.NewGuid();
        var report = new DiagnosticReport(
            FormatVersion: 1,
            ApplicationVersion: _applicationVersion,
            RuntimeVersion: Environment.Version.ToString(),
            OperatingSystem: GetOperatingSystemFamily(),
            GeneratedAt: generatedAt,
            Events:
            [
                .. events.Select(CreateSanitizedReportEvent),
            ]);
        return new DiagnosticReportPreview(
            _currentPreviewToken,
            JsonSerializer.Serialize(report, SerializerOptions),
            events.Count,
            generatedAt);
    }

    public async Task<DiagnosticExportResult> ExportAsync(
        DiagnosticReportPreview preview,
        string destinationPath,
        bool previewApproved,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!previewApproved || preview.Token == Guid.Empty ||
            preview.Token != _currentPreviewToken)
        {
            return DiagnosticExportResult.Failure(DiagnosticExportFailureCode.PreviewRequired);
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return DiagnosticExportResult.Failure(DiagnosticExportFailureCode.InvalidInput);
        }

        try
        {
            var content = Encoding.UTF8.GetBytes(preview.Content);
            await _writer.WriteAtomicallyAsync(destinationPath, content, cancellationToken);
            _currentPreviewToken = Guid.Empty;
            return DiagnosticExportResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.DiagnosticExport, exception);
            return DiagnosticExportResult.Failure(DiagnosticExportFailureCode.AccessDenied);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.DiagnosticExport, exception);
            return DiagnosticExportResult.Failure(DiagnosticExportFailureCode.IoFailure);
        }
    }

    private static string GetOperatingSystemFamily() =>
        OperatingSystem.IsWindows()
            ? "Windows"
            : OperatingSystem.IsLinux()
                ? "Linux"
                : OperatingSystem.IsMacOS()
                    ? "macOS"
                    : "Other";

    private static DiagnosticReportEvent CreateSanitizedReportEvent(DiagnosticEvent item)
    {
        var (EventId, Message) = SecretSafeDiagnostics.GetDescriptor(item.Operation);
        var exceptionType = item.ExceptionType.Length <= 160 &&
                            item.ExceptionType.All(character =>
                                char.IsAsciiLetterOrDigit(character) ||
                                character is '.' or '_' or '`')
            ? item.ExceptionType
            : "Exception";
        return new DiagnosticReportEvent(
            item.Severity.ToString(),
            item.Operation.ToString(),
            EventId,
            Message,
            exceptionType);
    }

    private sealed record DiagnosticReport(
        int FormatVersion,
        string ApplicationVersion,
        string RuntimeVersion,
        string OperatingSystem,
        DateTimeOffset GeneratedAt,
        DiagnosticReportEvent[] Events);

    private sealed record DiagnosticReportEvent(
        string Severity,
        string Operation,
        string EventId,
        string Message,
        string ExceptionType);
}

public sealed class FileDiagnosticFileWriter : IDiagnosticFileWriter
{
    public async Task WriteAtomicallyAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The diagnostic destination is unavailable.");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content.ToArray(), cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
