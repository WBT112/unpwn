using System.Security.Cryptography;
using Unpwn.Core;

namespace Unpwn.Application.Credentials;

public enum GeneratedCredentialFailureCode
{
    None,
    Locked,
    InvalidInput,
    NotFound,
    Deleted,
    Conflict,
    PersistenceFailure,
}

public sealed record GeneratedCredentialOperationResult(
    bool Succeeded,
    GeneratedCredentialFailureCode FailureCode,
    GeneratedCredentialMetadata? Metadata = null)
{
    public static GeneratedCredentialOperationResult Success(GeneratedCredentialMetadata metadata) =>
        new(true, GeneratedCredentialFailureCode.None, metadata);

    public static GeneratedCredentialOperationResult Failure(GeneratedCredentialFailureCode code) =>
        new(false, code);
}

public sealed class CredentialSecretLease : IDisposable
{
    private byte[]? _secretUtf8;

    public CredentialSecretLease(byte[] secretUtf8)
    {
        ArgumentNullException.ThrowIfNull(secretUtf8);
        if (secretUtf8.Length == 0)
        {
            throw new ArgumentException("A credential secret lease cannot be empty.", nameof(secretUtf8));
        }

        _secretUtf8 = secretUtf8;
    }

    public ReadOnlyMemory<byte> SecretUtf8 =>
        _secretUtf8 ?? throw new ObjectDisposedException(nameof(CredentialSecretLease));

    public void Dispose()
    {
        if (_secretUtf8 is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_secretUtf8);
        _secretUtf8 = null;
    }
}

public sealed record GeneratedCredentialCreationResult(
    bool Succeeded,
    GeneratedCredentialFailureCode FailureCode,
    GeneratedCredentialMetadata? Metadata,
    CredentialSecretLease? SecretLease) : IDisposable
{
    public static GeneratedCredentialCreationResult Success(
        GeneratedCredentialMetadata metadata,
        CredentialSecretLease secretLease) =>
        new(true, GeneratedCredentialFailureCode.None, metadata, secretLease);

    public static GeneratedCredentialCreationResult Failure(GeneratedCredentialFailureCode code) =>
        new(false, code, null, null);

    public void Dispose() => SecretLease?.Dispose();
}

public sealed record GeneratedCredentialBatchResult(
    bool Succeeded,
    GeneratedCredentialFailureCode FailureCode,
    IReadOnlyList<GeneratedCredentialMetadata> Credentials)
{
    public static GeneratedCredentialBatchResult Success(
        IReadOnlyList<GeneratedCredentialMetadata> credentials) =>
        new(true, GeneratedCredentialFailureCode.None, credentials);

    public static GeneratedCredentialBatchResult Failure(GeneratedCredentialFailureCode code) =>
        new(false, code, []);
}

public interface ICredentialPasswordGenerator
{
    byte[] GenerateUtf8(CredentialGenerationPolicy policy);
}

public interface IGeneratedCredentialRepository
{
    bool IsUnlocked { get; }

    Task<GeneratedCredentialCreationResult> GenerateAsync(
        Guid accountId,
        CredentialGenerationPolicy policy,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
        CancellationToken cancellationToken);

    Task<GeneratedCredentialMetadata?> GetMetadataAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken);

    Task<CredentialSecretLease?> ReadSecretAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken);

    Task<GeneratedCredentialOperationResult> MarkUsedAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<GeneratedCredentialOperationResult> ConfirmAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<GeneratedCredentialBatchResult> MarkExportedAsync(
        IReadOnlyCollection<GeneratedCredentialReference> references,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<GeneratedCredentialOperationResult> DeleteAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken);
}
