using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.Vault.Credentials;

public sealed class RecoveryVaultGeneratedCredentialRepository(
    RecoveryVault vault,
    ICredentialPasswordGenerator? passwordGenerator = null,
    Func<DateTimeOffset>? clock = null) : IGeneratedCredentialRepository, IDisposable
{
    private const string RecordType = "generated-credential";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
    private readonly RecoveryVault _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private readonly ICredentialPasswordGenerator _passwordGenerator =
        passwordGenerator ?? new CryptographicCredentialPasswordGenerator();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public bool IsUnlocked => !_disposed && !_vault.IsLocked;

    public async Task<GeneratedCredentialCreationResult> GenerateAsync(
        Guid accountId,
        CredentialGenerationPolicy policy,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryValidateIdentifiers(accountId, operationId) || policy is null)
        {
            return GeneratedCredentialCreationResult.Failure(GeneratedCredentialFailureCode.InvalidInput);
        }

        try
        {
            policy.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return GeneratedCredentialCreationResult.Failure(GeneratedCredentialFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return GeneratedCredentialCreationResult.Failure(GeneratedCredentialFailureCode.Locked);
            }

            var existing = FindByOperation(
                operationId,
                GeneratedCredentialAuditEventType.Generated);
            if (existing is not null)
            {
                using (existing)
                {
                    if (existing.Metadata.AccountId != accountId || existing.Metadata.IsDeleted ||
                        existing.SecretUtf8 is not { Length: > 0 } existingSecret)
                    {
                        return GeneratedCredentialCreationResult.Failure(
                            GeneratedCredentialFailureCode.Conflict);
                    }

                    return GeneratedCredentialCreationResult.Success(
                        existing.Metadata,
                        new CredentialSecretLease(existingSecret.ToArray()));
                }
            }

            var credentialId = Guid.NewGuid();
            var metadata = GeneratedCredentialMetadata.Create(
                credentialId,
                accountId,
                operationId,
                _clock());
            var secret = _passwordGenerator.GenerateUtf8(policy);
            try
            {
                using var persisted = new PersistedGeneratedCredential(metadata, secret.ToArray());
                var result = PersistSingle(persisted);
                return result == GeneratedCredentialFailureCode.None
                    ? GeneratedCredentialCreationResult.Success(
                        metadata,
                        new CredentialSecretLease(secret.ToArray()))
                    : GeneratedCredentialCreationResult.Failure(result);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return [];
            }

            var results = new List<GeneratedCredentialMetadata>();
            foreach (var descriptor in CredentialDescriptors())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var persisted = ReadPersisted(descriptor);
                results.Add(persisted.Metadata);
            }

            return results
                .OrderBy(metadata => metadata.GeneratedAt)
                .ThenBy(metadata => metadata.CredentialId)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GeneratedCredentialMetadata?> GetMetadataAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryValidateReference(reference))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return null;
            }

            using var persisted = TryRead(reference);
            return persisted?.Metadata;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialSecretLease?> ReadSecretAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryValidateReference(reference))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return null;
            }

            using var persisted = TryRead(reference);
            return persisted is { Metadata.IsDeleted: false, SecretUtf8.Length: > 0 }
                ? new CredentialSecretLease(persisted.SecretUtf8.ToArray())
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<GeneratedCredentialOperationResult> MarkUsedAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken) =>
        MutateSingleAsync(
            reference,
            operationId,
            (metadata, occurredAt) => metadata.MarkUsed(operationId, occurredAt),
            cancellationToken);

    public Task<GeneratedCredentialOperationResult> ConfirmAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken) =>
        MutateSingleAsync(
            reference,
            operationId,
            (metadata, occurredAt) => metadata.Confirm(operationId, occurredAt),
            cancellationToken);

    public async Task<GeneratedCredentialBatchResult> MarkExportedAsync(
        IReadOnlyCollection<GeneratedCredentialReference> references,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (operationId == Guid.Empty || references is null || references.Count == 0 ||
            references.Any(reference => !TryValidateReference(reference)) ||
            references.Select(reference => reference.CredentialId).Distinct().Count() != references.Count)
        {
            return GeneratedCredentialBatchResult.Failure(GeneratedCredentialFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return GeneratedCredentialBatchResult.Failure(GeneratedCredentialFailureCode.Locked);
            }

            var persistedCredentials = new List<PersistedGeneratedCredential>(references.Count);
            var serialized = new List<byte[]>(references.Count);
            try
            {
                foreach (var reference in references)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var persisted = TryRead(reference);
                    if (persisted is null)
                    {
                        return GeneratedCredentialBatchResult.Failure(
                            GeneratedCredentialFailureCode.NotFound);
                    }

                    persistedCredentials.Add(persisted);
                    if (persisted.Metadata.IsDeleted || persisted.SecretUtf8 is not { Length: > 0 })
                    {
                        return GeneratedCredentialBatchResult.Failure(
                            GeneratedCredentialFailureCode.Deleted);
                    }
                }

                var occurredAt = _clock();
                var updated = persistedCredentials.Select(persisted =>
                    new PersistedGeneratedCredential(
                        persisted.Metadata.MarkExported(operationId, occurredAt),
                        persisted.SecretUtf8!.ToArray())).ToArray();
                try
                {
                    var writes = new List<VaultRecordWrite>(updated.Length);
                    foreach (var persisted in updated)
                    {
                        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
                            persisted,
                            SerializerOptions);
                        serialized.Add(plaintext);
                        writes.Add(new VaultRecordWrite(
                            Descriptor(persisted.Metadata.CredentialId),
                            plaintext));
                    }

                    _vault.UpsertRecords(writes);
                    return GeneratedCredentialBatchResult.Success(
                        updated.Select(persisted => persisted.Metadata).ToArray());
                }
                finally
                {
                    foreach (var persisted in updated)
                    {
                        persisted.Dispose();
                    }
                }
            }
            catch (Exception exception) when (IsSafeRepositoryFailure(exception))
            {
                return GeneratedCredentialBatchResult.Failure(MapFailure(exception));
            }
            finally
            {
                foreach (var plaintext in serialized)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }

                foreach (var persisted in persistedCredentials)
                {
                    persisted.Dispose();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GeneratedCredentialOperationResult> DeleteAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryValidateReference(reference) || operationId == Guid.Empty)
        {
            return GeneratedCredentialOperationResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.Locked);
            }

            using var persisted = TryRead(reference);
            if (persisted is null)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.NotFound);
            }

            GeneratedCredentialMetadata updated;
            try
            {
                updated = persisted.Metadata.Delete(operationId, _clock());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.Conflict);
            }

            persisted.ClearSecret();
            persisted.Metadata = updated;
            var result = PersistSingle(persisted);
            return result == GeneratedCredentialFailureCode.None
                ? GeneratedCredentialOperationResult.Success(updated)
                : GeneratedCredentialOperationResult.Failure(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<GeneratedCredentialOperationResult> MutateSingleAsync(
        GeneratedCredentialReference reference,
        Guid operationId,
        Func<GeneratedCredentialMetadata, DateTimeOffset, GeneratedCredentialMetadata> mutation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mutation);
        if (!TryValidateReference(reference) || operationId == Guid.Empty)
        {
            return GeneratedCredentialOperationResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_vault.IsLocked)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.Locked);
            }

            using var persisted = TryRead(reference);
            if (persisted is null)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.NotFound);
            }

            if (persisted.Metadata.IsDeleted)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.Deleted);
            }

            try
            {
                persisted.Metadata = mutation(persisted.Metadata, _clock());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return GeneratedCredentialOperationResult.Failure(
                    GeneratedCredentialFailureCode.Conflict);
            }

            var result = PersistSingle(persisted);
            return result == GeneratedCredentialFailureCode.None
                ? GeneratedCredentialOperationResult.Success(persisted.Metadata)
                : GeneratedCredentialOperationResult.Failure(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This encrypted credential repository maps storage failures to language-neutral safe result codes.")]
    private GeneratedCredentialFailureCode PersistSingle(PersistedGeneratedCredential persisted)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(persisted, SerializerOptions);
        try
        {
            _vault.UpsertRecord(Descriptor(persisted.Metadata.CredentialId), plaintext);
            return GeneratedCredentialFailureCode.None;
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private PersistedGeneratedCredential? FindByOperation(
        Guid operationId,
        GeneratedCredentialAuditEventType eventType)
    {
        foreach (var descriptor in CredentialDescriptors())
        {
            var persisted = ReadPersisted(descriptor);
            if (persisted.Metadata.HasOperation(operationId, eventType))
            {
                return persisted;
            }

            persisted.Dispose();
        }

        return null;
    }

    private PersistedGeneratedCredential? TryRead(GeneratedCredentialReference reference)
    {
        using var record = _vault.ReadRecord(RecordType, reference.CredentialId.ToString("D"));
        if (record is null)
        {
            return null;
        }

        var persisted = JsonSerializer.Deserialize<PersistedGeneratedCredential>(
            record.Plaintext.Span,
            SerializerOptions)
            ?? throw new JsonException("The generated credential record is empty.");
        persisted.Validate();
        if (persisted.Metadata.AccountId != reference.AccountId)
        {
            persisted.Dispose();
            return null;
        }

        return persisted;
    }

    private PersistedGeneratedCredential ReadPersisted(VaultRecordDescriptor descriptor)
    {
        using var record = _vault.ReadRecord(descriptor.RecordType, descriptor.RecordId)
            ?? throw new InvalidOperationException("The generated credential record is unavailable.");
        var persisted = JsonSerializer.Deserialize<PersistedGeneratedCredential>(
            record.Plaintext.Span,
            SerializerOptions)
            ?? throw new JsonException("The generated credential record is empty.");
        persisted.Validate();
        return persisted;
    }

    private IEnumerable<VaultRecordDescriptor> CredentialDescriptors() =>
        _vault.ListRecords().Where(descriptor =>
            string.Equals(descriptor.RecordType, RecordType, StringComparison.Ordinal));

    private static VaultRecordDescriptor Descriptor(Guid credentialId) =>
        new(RecordType, credentialId.ToString("D"), 1);

    private static bool TryValidateIdentifiers(Guid accountId, Guid operationId) =>
        accountId != Guid.Empty && operationId != Guid.Empty;

    private static bool TryValidateReference(GeneratedCredentialReference? reference)
    {
        if (reference is null)
        {
            return false;
        }

        try
        {
            reference.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsSafeRepositoryFailure(Exception exception) => exception is
        ArgumentException or
        InvalidOperationException or
        IOException or
        JsonException or
        NotSupportedException or
        SqliteException;

    private static GeneratedCredentialFailureCode MapFailure(Exception exception) => exception switch
    {
        InvalidOperationException when exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) =>
            GeneratedCredentialFailureCode.Locked,
        ArgumentException or JsonException or NotSupportedException =>
            GeneratedCredentialFailureCode.Conflict,
        _ => GeneratedCredentialFailureCode.PersistenceFailure,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PersistedGeneratedCredential : IDisposable
    {
        public PersistedGeneratedCredential(
            GeneratedCredentialMetadata metadata,
            byte[]? secretUtf8)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            SecretUtf8 = secretUtf8;
        }

        public GeneratedCredentialMetadata Metadata { get; set; }

        public byte[]? SecretUtf8 { get; private set; }

        public void Validate()
        {
            Metadata.Validate();
            if (Metadata.IsDeleted == (SecretUtf8 is { Length: > 0 }))
            {
                throw new InvalidOperationException("Generated credential metadata and secret retention are inconsistent.");
            }
        }

        public void ClearSecret()
        {
            if (SecretUtf8 is not null)
            {
                CryptographicOperations.ZeroMemory(SecretUtf8);
                SecretUtf8 = null;
            }
        }

        public void Dispose() => ClearSecret();
    }
}
