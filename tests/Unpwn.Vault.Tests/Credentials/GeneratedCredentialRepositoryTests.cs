using System.Text;
using Microsoft.Data.Sqlite;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Vault.Credentials;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.Vault.Tests.Credentials;

public sealed class GeneratedCredentialRepositoryTests : IDisposable
{
    private static readonly Argon2idParameters TestParameters = new(19 * 1024, 2, 1);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "unpwn-credential-tests",
        Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _startedAt = new(2026, 8, 6, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GeneratedCredentialPersistsEncryptedAndReopens()
    {
        var path = VaultPath();
        var accountId = Guid.NewGuid();
        GeneratedCredentialReference reference;
        byte[] firstSecret;
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        using (var repository = new RecoveryVaultGeneratedCredentialRepository(
                   vault,
                   clock: () => _startedAt))
        using (var created = await repository.GenerateAsync(
                   accountId,
                   CredentialGenerationPolicy.Default,
                   Guid.NewGuid(),
                   CancellationToken.None))
        {
            Assert.True(created.Succeeded);
            reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;
            firstSecret = created.SecretLease!.SecretUtf8.ToArray();
            Assert.Equal(24, firstSecret.Length);
        }

        var databaseBytes = await File.ReadAllBytesAsync(path);
        Assert.DoesNotContain(
            Convert.ToHexString(firstSecret),
            Convert.ToHexString(databaseBytes),
            StringComparison.Ordinal);

        using var reopenedVault = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        using var reopened = new RecoveryVaultGeneratedCredentialRepository(reopenedVault);
        using var lease = await reopened.ReadSecretAsync(reference, CancellationToken.None);
        Assert.NotNull(lease);
        Assert.Equal(firstSecret, lease.SecretUtf8.ToArray());
        Array.Clear(firstSecret);
    }

    [Fact]
    public async Task DeleteRemovesRevealableSecretButRetainsAuditMetadata()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        var current = _startedAt;
        using var repository = new RecoveryVaultGeneratedCredentialRepository(
            vault,
            clock: () => current);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(),
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            CancellationToken.None);
        var reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;

        current = current.AddMinutes(1);
        var deleted = await repository.DeleteAsync(
            reference,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(deleted.Succeeded);
        Assert.True(deleted.Metadata?.IsDeleted);
        Assert.Null(await repository.ReadSecretAsync(reference, CancellationToken.None));
        var metadata = await repository.GetMetadataAsync(reference, CancellationToken.None);
        Assert.Equal(GeneratedCredentialStage.Deleted, metadata?.Stage);
        Assert.Contains(metadata!.AuditEvents, auditEvent =>
            auditEvent.EventType == GeneratedCredentialAuditEventType.Deleted);
    }

    [Fact]
    public async Task ExportStateBatchRollsBackWhenLaterCredentialFails()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        var current = _startedAt;
        using var repository = new RecoveryVaultGeneratedCredentialRepository(
            vault,
            clock: () => current);
        using var first = await repository.GenerateAsync(
            Guid.NewGuid(),
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            CancellationToken.None);
        using var second = await repository.GenerateAsync(
            Guid.NewGuid(),
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            CancellationToken.None);
        var firstReference = Assert.IsType<GeneratedCredentialMetadata>(first.Metadata).Reference;
        var secondReference = Assert.IsType<GeneratedCredentialMetadata>(second.Metadata).Reference;
        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TRIGGER fail_second_credential_update
                BEFORE UPDATE ON vault_records
                WHEN NEW.record_id = '{secondReference.CredentialId:D}'
                BEGIN
                    SELECT RAISE(ABORT, 'synthetic credential update failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        current = current.AddMinutes(1);
        var result = await repository.MarkExportedAsync(
            [firstReference, secondReference],
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(GeneratedCredentialFailureCode.PersistenceFailure, result.FailureCode);
        Assert.Equal(0, (await repository.GetMetadataAsync(firstReference, CancellationToken.None))?.ExportCount);
        Assert.Equal(0, (await repository.GetMetadataAsync(secondReference, CancellationToken.None))?.ExportCount);
    }

    [Fact]
    public async Task LifecycleOperationsAreIdempotentByOperationId()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        var current = _startedAt;
        using var repository = new RecoveryVaultGeneratedCredentialRepository(
            vault,
            clock: () => current);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(),
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            CancellationToken.None);
        var reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;
        var operationId = Guid.NewGuid();

        current = current.AddMinutes(1);
        var first = await repository.MarkUsedAsync(reference, operationId, CancellationToken.None);
        current = current.AddMinutes(1);
        var repeated = await repository.MarkUsedAsync(reference, operationId, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(repeated.Succeeded);
        Assert.Equal(first.Metadata?.Revision, repeated.Metadata?.Revision);
        Assert.Single(
            repeated.Metadata!.AuditEvents,
            auditEvent => auditEvent.EventType == GeneratedCredentialAuditEventType.Used);
    }

    [Fact]
    public async Task PasswordManagerHandoffStatePersistsEncryptedAndCanBeCorrected()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        var current = _startedAt;
        using var repository = new RecoveryVaultGeneratedCredentialRepository(
            vault,
            clock: () => current);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(),
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            CancellationToken.None);
        var reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;

        current = current.AddMinutes(1);
        Assert.True((await repository.MarkExportedAsync(
            [reference], Guid.NewGuid(), CancellationToken.None)).Succeeded);
        current = current.AddMinutes(1);
        Assert.True((await repository.ConfirmPasswordManagerImportAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).Succeeded);
        current = current.AddMinutes(1);
        Assert.True((await repository.ConfirmPlaintextExportCleanupAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).Succeeded);
        current = current.AddMinutes(1);
        Assert.True((await repository.RevokePasswordManagerImportConfirmationAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).Succeeded);

        var metadata = await repository.GetMetadataAsync(reference, CancellationToken.None);
        Assert.Equal(GeneratedCredentialStage.Exported, metadata?.Stage);
        Assert.Null(metadata?.PasswordManagerImportConfirmedAt);
        Assert.False(metadata?.IsPlaintextExportCleanupPending);
        Assert.Contains(metadata!.AuditEvents, item =>
            item.EventType == GeneratedCredentialAuditEventType.PasswordManagerImportConfirmationRevoked);
    }

    [Fact]
    public void PublicRepositoryApiCannotAcceptOldPasswordStrings()
    {
        var forbiddenParameters = typeof(IGeneratedCredentialRepository)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(string))
            .ToArray();

        Assert.Empty(forbiddenParameters);
    }

    [Fact]
    public void GeneratorIncludesEverySelectedCharacterClass()
    {
        var generator = new CryptographicCredentialPasswordGenerator();
        var bytes = generator.GenerateUtf8(CredentialGenerationPolicy.Default);
        try
        {
            var value = Encoding.UTF8.GetString(bytes);
            Assert.Contains(value, char.IsLower);
            Assert.Contains(value, char.IsUpper);
            Assert.Contains(value, char.IsDigit);
            Assert.Contains(value, character => !char.IsLetterOrDigit(character));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task GenerationRejectsInvalidInputWithoutWritingASecret(
        bool emptyAccountId,
        bool emptyOperationId,
        bool nullPolicy)
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);

        using var result = await repository.GenerateAsync(
            emptyAccountId ? Guid.Empty : Guid.NewGuid(),
            nullPolicy ? null! : CredentialGenerationPolicy.Default,
            emptyOperationId ? Guid.Empty : Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, result.FailureCode);
        Assert.Empty(await repository.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(11, true, true, true, true)]
    [InlineData(129, true, true, true, true)]
    [InlineData(24, false, false, false, false)]
    public async Task GenerationRejectsInvalidPolicies(
        int length,
        bool lowercase,
        bool uppercase,
        bool digits,
        bool symbols)
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);

        using var result = await repository.GenerateAsync(
            Guid.NewGuid(),
            new CredentialGenerationPolicy(length, lowercase, uppercase, digits, symbols),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, result.FailureCode);
    }

    [Fact]
    public async Task LockedVaultFailsClosedForEveryRepositoryReadAndMutation()
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, Guid.NewGuid(), CancellationToken.None);
        var reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;
        vault.Lock();

        using var generated = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(GeneratedCredentialFailureCode.Locked, generated.FailureCode);
        Assert.Empty(await repository.ListAsync(CancellationToken.None));
        Assert.Null(await repository.GetMetadataAsync(reference, CancellationToken.None));
        Assert.Null(await repository.ReadSecretAsync(reference, CancellationToken.None));
        Assert.Equal(GeneratedCredentialFailureCode.Locked, (await repository.MarkUsedAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.Locked, (await repository.MarkExportedAsync(
            [reference], Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.Locked, (await repository.DeleteAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task InvalidAndMismatchedReferencesFailClosed()
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, Guid.NewGuid(), CancellationToken.None);
        var valid = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;
        var invalid = new GeneratedCredentialReference(Guid.Empty, Guid.NewGuid());
        var missing = new GeneratedCredentialReference(Guid.NewGuid(), Guid.NewGuid());
        var wrongAccount = valid with { AccountId = Guid.NewGuid() };

        Assert.Null(await repository.GetMetadataAsync(null!, CancellationToken.None));
        Assert.Null(await repository.GetMetadataAsync(invalid, CancellationToken.None));
        Assert.Null(await repository.ReadSecretAsync(wrongAccount, CancellationToken.None));
        Assert.Null(await repository.ReadSecretAsync(missing, CancellationToken.None));
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkUsedAsync(
            invalid, Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkUsedAsync(
            valid, Guid.Empty, CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.NotFound, (await repository.MarkUsedAsync(
            missing, Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.NotFound, (await repository.DeleteAsync(
            wrongAccount, Guid.NewGuid(), CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task ExportBatchRejectsInvalidDuplicateMissingAndDeletedCredentials()
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);
        using var created = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, Guid.NewGuid(), CancellationToken.None);
        var reference = Assert.IsType<GeneratedCredentialMetadata>(created.Metadata).Reference;

        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkExportedAsync(
            null!, Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkExportedAsync(
            [], Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkExportedAsync(
            [reference], Guid.Empty, CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.InvalidInput, (await repository.MarkExportedAsync(
            [reference, reference], Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.NotFound, (await repository.MarkExportedAsync(
            [new GeneratedCredentialReference(Guid.NewGuid(), Guid.NewGuid())],
            Guid.NewGuid(), CancellationToken.None)).FailureCode);

        Assert.True((await repository.DeleteAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).Succeeded);
        Assert.Equal(GeneratedCredentialFailureCode.Deleted, (await repository.MarkExportedAsync(
            [reference], Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.Equal(GeneratedCredentialFailureCode.Deleted, (await repository.MarkUsedAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).FailureCode);
        Assert.True((await repository.DeleteAsync(
            reference, Guid.NewGuid(), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task GenerationOperationCannotBeReusedForAnotherAccount()
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using var repository = new RecoveryVaultGeneratedCredentialRepository(vault);
        var operationId = Guid.NewGuid();
        using var first = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, operationId, CancellationToken.None);
        using var conflict = await repository.GenerateAsync(
            Guid.NewGuid(), CredentialGenerationPolicy.Default, operationId, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(GeneratedCredentialFailureCode.Conflict, conflict.FailureCode);
    }

    [Fact]
    public async Task DisposedRepositoryRejectsFurtherUse()
    {
        using var vault = RecoveryVault.Create(
            VaultPath(), "UNPWN_TEST_SECRET_vault-password", TestParameters);
        var repository = new RecoveryVaultGeneratedCredentialRepository(vault);
        repository.Dispose();
        repository.Dispose();

        Assert.False(repository.IsUnlocked);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => repository.ListAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string VaultPath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, "recovery-vault.sqlite");
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }
}
