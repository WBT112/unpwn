using Unpwn.Core;

namespace Unpwn.Application.Credentials;

public enum CredentialExportFormatId
{
    GenericCsv,
    BitwardenCsv,
}

public enum CredentialExportFailureCode
{
    None,
    InvalidInput,
    RiskAcknowledgementRequired,
    DestinationExists,
    DestinationUnavailable,
    CredentialUnavailable,
    AlreadyCompleted,
    WriteFailure,
    StateUpdateFailedAfterFileCreation,
}

public sealed record CredentialExportSelection(
    GeneratedCredentialReference Reference,
    string Name,
    string? LoginIdentifier,
    string? AccountUri)
{
    public void Validate()
    {
        Reference.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (Name.Trim().Length > 200 || LoginIdentifier?.Trim().Length > 320 ||
            AccountUri?.Trim().Length > 2048)
        {
            throw new InvalidOperationException("A credential export selection contains an overlong field.");
        }

        if (!string.IsNullOrWhiteSpace(AccountUri) &&
            (!Uri.TryCreate(AccountUri, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("https" or "http") ||
             string.IsNullOrWhiteSpace(uri.Host) ||
             !string.IsNullOrEmpty(uri.UserInfo)))
        {
            throw new InvalidOperationException(
                "A credential export URI must be absolute HTTP or HTTPS without embedded credentials.");
        }
    }
}

public sealed record CredentialExportRequest(
    Guid OperationId,
    CredentialExportFormatId Format,
    string DestinationPath,
    IReadOnlyList<CredentialExportSelection> Selections,
    bool PlaintextRiskAcknowledged);

public sealed record CredentialExportResult(
    bool Succeeded,
    CredentialExportFailureCode FailureCode,
    bool FileCreated,
    string? DestinationPath,
    int ExportedCredentials)
{
    public static CredentialExportResult Success(string destinationPath, int exportedCredentials) =>
        new(true, CredentialExportFailureCode.None, true, destinationPath, exportedCredentials);

    public static CredentialExportResult Failure(
        CredentialExportFailureCode failureCode,
        bool fileCreated = false,
        string? destinationPath = null) =>
        new(false, failureCode, fileCreated, destinationPath, 0);
}

public interface IGeneratedCredentialExportService
{
    Task<CredentialExportResult> ExportAsync(
        CredentialExportRequest request,
        CancellationToken cancellationToken);
}
