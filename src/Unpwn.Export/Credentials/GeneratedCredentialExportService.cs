using System.Security.Cryptography;
using System.Text;
using Unpwn.Application.Credentials;
using Unpwn.Core;

namespace Unpwn.Export.Credentials;

public sealed class GeneratedCredentialExportService(
    IGeneratedCredentialRepository repository) : IGeneratedCredentialExportService
{
    private readonly IGeneratedCredentialRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<CredentialExportResult> ExportAsync(
        CredentialExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validationFailure = ValidateRequest(request, out var destinationPath);
        if (validationFailure != CredentialExportFailureCode.None)
        {
            return CredentialExportResult.Failure(validationFailure);
        }

        var materials = new List<CredentialExportMaterial>(request.Selections.Count);
        try
        {
            foreach (var selection in request.Selections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = await _repository.GetMetadataAsync(selection.Reference, cancellationToken);
                if (metadata is null || metadata.IsDeleted)
                {
                    return CredentialExportResult.Failure(
                        CredentialExportFailureCode.CredentialUnavailable);
                }

                if (metadata.HasOperation(
                        request.OperationId,
                        GeneratedCredentialAuditEventType.Exported))
                {
                    return CredentialExportResult.Failure(
                        CredentialExportFailureCode.AlreadyCompleted);
                }

                var lease = await _repository.ReadSecretAsync(selection.Reference, cancellationToken);
                if (lease is null)
                {
                    return CredentialExportResult.Failure(
                        CredentialExportFailureCode.CredentialUnavailable);
                }

                materials.Add(new CredentialExportMaterial(selection, lease));
            }

            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(destinationPath)!,
                $".unpwn-export-{Guid.NewGuid():N}.tmp");
            try
            {
                await WriteExportFileAsync(
                    request.Format,
                    temporaryPath,
                    materials,
                    cancellationToken);
                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(temporaryPath);
                throw;
            }
            catch (IOException)
            {
                TryDelete(temporaryPath);
                return CredentialExportResult.Failure(
                    File.Exists(destinationPath)
                        ? CredentialExportFailureCode.DestinationExists
                        : CredentialExportFailureCode.WriteFailure);
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(temporaryPath);
                return CredentialExportResult.Failure(
                    CredentialExportFailureCode.DestinationUnavailable);
            }

            var marked = await _repository.MarkExportedAsync(
                request.Selections.Select(selection => selection.Reference).ToArray(),
                request.OperationId,
                cancellationToken);
            return marked.Succeeded
                ? CredentialExportResult.Success(destinationPath, marked.Credentials.Count)
                : CredentialExportResult.Failure(
                    CredentialExportFailureCode.StateUpdateFailedAfterFileCreation,
                    fileCreated: true,
                    destinationPath);
        }
        finally
        {
            foreach (var material in materials)
            {
                material.Dispose();
            }
        }
    }

    private static CredentialExportFailureCode ValidateRequest(
        CredentialExportRequest request,
        out string destinationPath)
    {
        destinationPath = string.Empty;
        if (request.OperationId == Guid.Empty ||
            !Enum.IsDefined(request.Format) ||
            request.Selections is null ||
            request.Selections.Count == 0 ||
            request.Selections.Select(selection => selection.Reference.CredentialId).Distinct().Count() !=
            request.Selections.Count)
        {
            return CredentialExportFailureCode.InvalidInput;
        }

        if (!request.PlaintextRiskAcknowledged)
        {
            return CredentialExportFailureCode.RiskAcknowledgementRequired;
        }

        try
        {
            foreach (var selection in request.Selections)
            {
                selection.Validate();
            }

            destinationPath = Path.GetFullPath(request.DestinationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            return CredentialExportFailureCode.InvalidInput;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return CredentialExportFailureCode.DestinationUnavailable;
        }

        return File.Exists(destinationPath)
            ? CredentialExportFailureCode.DestinationExists
            : CredentialExportFailureCode.None;
    }

    private static async Task WriteExportFileAsync(
        CredentialExportFormatId format,
        string path,
        IReadOnlyList<CredentialExportMaterial> materials,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var writer = new Utf8CsvWriter(stream);
        switch (format)
        {
            case CredentialExportFormatId.GenericCsv:
                await WriteGenericCsvAsync(writer, materials, cancellationToken);
                break;
            case CredentialExportFormatId.BitwardenCsv:
                await WriteBitwardenCsvAsync(writer, materials, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown credential export format.");
        }

        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteGenericCsvAsync(
        Utf8CsvWriter writer,
        IReadOnlyList<CredentialExportMaterial> materials,
        CancellationToken cancellationToken)
    {
        await writer.WriteStringRowAsync(
            ["credential_id", "name", "login", "uri", "password"],
            cancellationToken);
        foreach (var material in materials)
        {
            await writer.WriteMixedRowAsync(
                [
                    material.Selection.Reference.CredentialId.ToString("D"),
                    material.Selection.Name.Trim(),
                    material.Selection.LoginIdentifier?.Trim() ?? string.Empty,
                    material.Selection.AccountUri?.Trim() ?? string.Empty,
                ],
                material.SecretLease.SecretUtf8,
                cancellationToken);
        }
    }

    private static async Task WriteBitwardenCsvAsync(
        Utf8CsvWriter writer,
        IReadOnlyList<CredentialExportMaterial> materials,
        CancellationToken cancellationToken)
    {
        await writer.WriteStringRowAsync(
        [
            "folder",
            "favorite",
            "type",
            "name",
            "notes",
            "fields",
            "reprompt",
            "login_uri",
            "login_username",
            "login_password",
            "login_totp",
        ],
        cancellationToken);
        foreach (var material in materials)
        {
            await writer.WriteMixedRowAsync(
                [
                    string.Empty,
                    "0",
                    "login",
                    material.Selection.Name.Trim(),
                    string.Empty,
                    string.Empty,
                    "0",
                    material.Selection.AccountUri?.Trim() ?? string.Empty,
                    material.Selection.LoginIdentifier?.Trim() ?? string.Empty,
                ],
                material.SecretLease.SecretUtf8,
                cancellationToken,
                trailingEmptyFields: 1);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CredentialExportMaterial(
        CredentialExportSelection selection,
        CredentialSecretLease secretLease) : IDisposable
    {
        public CredentialExportSelection Selection { get; } = selection;

        public CredentialSecretLease SecretLease { get; } = secretLease;

        public void Dispose() => SecretLease.Dispose();
    }

    private sealed class Utf8CsvWriter(Stream stream)
    {
        private static readonly byte[] Comma = [(byte)','];
        private static readonly byte[] Quote = [(byte)'"'];
        private static readonly byte[] DoubleQuote = [(byte)'"', (byte)'"'];
        private static readonly byte[] NewLine = [(byte)'\r', (byte)'\n'];
        private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        public async Task WriteStringRowAsync(
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken)
        {
            for (var index = 0; index < fields.Count; index++)
            {
                if (index > 0)
                {
                    await _stream.WriteAsync(Comma, cancellationToken);
                }

                await WriteStringFieldAsync(fields[index], cancellationToken);
            }

            await _stream.WriteAsync(NewLine, cancellationToken);
        }

        public async Task WriteMixedRowAsync(
            IReadOnlyList<string> leadingFields,
            ReadOnlyMemory<byte> secretUtf8,
            CancellationToken cancellationToken,
            int trailingEmptyFields = 0)
        {
            for (var index = 0; index < leadingFields.Count; index++)
            {
                if (index > 0)
                {
                    await _stream.WriteAsync(Comma, cancellationToken);
                }

                await WriteStringFieldAsync(leadingFields[index], cancellationToken);
            }

            await _stream.WriteAsync(Comma, cancellationToken);
            await WriteBytesFieldAsync(secretUtf8, cancellationToken);
            for (var index = 0; index < trailingEmptyFields; index++)
            {
                await _stream.WriteAsync(Comma, cancellationToken);
            }

            await _stream.WriteAsync(NewLine, cancellationToken);
        }

        private async Task WriteStringFieldAsync(
            string value,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                await WriteBytesFieldAsync(bytes, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private async Task WriteBytesFieldAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken)
        {
            var requiresQuotes = value.Span.IndexOfAny((byte)',', (byte)'"', (byte)'\r', (byte)'\n') >= 0;
            if (!requiresQuotes)
            {
                await _stream.WriteAsync(value, cancellationToken);
                return;
            }

            await _stream.WriteAsync(Quote, cancellationToken);
            var start = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (value.Span[index] != (byte)'"')
                {
                    continue;
                }

                if (index > start)
                {
                    await _stream.WriteAsync(value[start..index], cancellationToken);
                }

                await _stream.WriteAsync(DoubleQuote, cancellationToken);
                start = index + 1;
            }

            if (start < value.Length)
            {
                await _stream.WriteAsync(value[start..], cancellationToken);
            }

            await _stream.WriteAsync(Quote, cancellationToken);
        }
    }
}
