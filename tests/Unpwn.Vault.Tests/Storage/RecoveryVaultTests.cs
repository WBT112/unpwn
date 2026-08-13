using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.Vault.Tests.Storage;

public sealed class RecoveryVaultTests : IDisposable
{
    private static readonly Argon2idParameters TestParameters = new(19 * 1024, 2, 1);
    private static readonly string CredentialId = Id(1);
    private static readonly string StateId = Id(2);
    private static readonly string NoteId = Id(3);
    private static readonly string DeleteId = Id(4);
    private static readonly string LockedId = Id(5);
    private static readonly string PasswordChangeId = Id(6);
    private static readonly string PasswordFailureId = Id(7);
    private static readonly string TamperedId = Id(8);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "unpwn-vault-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatedVaultPersistsEncryptedRecordsAcrossReopen()
    {
        var path = VaultPath();
        var descriptor = new VaultRecordDescriptor("generated-credential", CredentialId, 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password");

        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(descriptor, plaintext);
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        using var record = Assert.IsType<VaultRecord>(reopened.ReadRecord("generated-credential", CredentialId));

        Assert.Equal(descriptor, record.Descriptor);
        Assert.Equal(plaintext, record.Plaintext.ToArray());
        Assert.DoesNotContain("UNPWN_TEST_SECRET_generated-password", File.ReadAllText(path));
    }

    [Fact]
    public void CreatingVaultDoesNotOverwriteAnExistingFile()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);

        Assert.Throws<IOException>(() =>
            RecoveryVault.Create(path, "UNPWN_TEST_SECRET_other-password", TestParameters));
    }

    [Fact]
    public void WrongPasswordCannotUnlockVault()
    {
        var path = VaultPath();
        using (RecoveryVault.Create(path, "UNPWN_TEST_SECRET_correct-password", TestParameters))
        {
        }

        Assert.Throws<InvalidOperationException>(() => RecoveryVault.Open(path, "UNPWN_TEST_SECRET_wrong-password"));
    }

    [Fact]
    public void RecordMetadataUsesOnlyRepositoryTypesAndOpaqueIdentifiers()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", StateId, 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_account-state"));

        var descriptors = vault.ListRecords();

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("account-state", descriptor.RecordType);
        Assert.Equal(StateId, descriptor.RecordId);
        Assert.True(Guid.TryParse(descriptor.RecordId, out _));
        Assert.Equal(1, descriptor.SchemaVersion);
    }

    [Fact]
    public void UpsertingRecordRotatesNonceAndCiphertext()
    {
        var path = VaultPath();
        var descriptor = new VaultRecordDescriptor("note", NoteId, 1);
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);

        vault.UpsertRecord(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first"));
        var firstEncryptedBytes = ReadEncryptedRecordBytes(path, "note", NoteId);
        vault.UpsertRecord(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first"));
        var secondEncryptedBytes = ReadEncryptedRecordBytes(path, "note", NoteId);

        Assert.NotEqual(Convert.ToHexString(firstEncryptedBytes), Convert.ToHexString(secondEncryptedBytes));
    }

    [Fact]
    public void DeletedRecordIsNotReturnedAfterReopen()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", DeleteId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));
            Assert.True(vault.DeleteRecord("account-state", DeleteId));
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        Assert.Null(reopened.ReadRecord("account-state", DeleteId));
    }

    [Fact]
    public void LockedVaultRejectsSensitiveAndMetadataOperationsUntilUnlocked()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", LockedId, 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));

        vault.Lock();

        Assert.True(vault.IsLocked);
        Assert.Throws<InvalidOperationException>(() => vault.ListRecords());
        Assert.Throws<InvalidOperationException>(() => vault.ReadRecord("account-state", LockedId));
        Assert.Throws<InvalidOperationException>(() => vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", LockedId, 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_new-state")));

        vault.Unlock("UNPWN_TEST_SECRET_vault-password");

        Assert.False(vault.IsLocked);
        using var record = Assert.IsType<VaultRecord>(vault.ReadRecord("account-state", LockedId));
        Assert.Equal("UNPWN_TEST_SECRET_state", Encoding.UTF8.GetString(record.Plaintext.Span));
    }

    [Fact]
    public void DecryptedRecordBecomesUnavailableAfterDispose()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        vault.UpsertRecord(
            new VaultRecordDescriptor("generated-credential", CredentialId, 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password"));
        var record = Assert.IsType<VaultRecord>(vault.ReadRecord("generated-credential", CredentialId));

        record.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = record.Plaintext);
    }

    [Fact]
    public void PasswordChangeRewrapsDataKeyWithoutReencryptingRecords()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_old-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("generated-credential", PasswordChangeId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password"));

            var firstEnvelopeBytes = ReadEnvelopeBytes(path);
            var encryptedRecordBytes = ReadEncryptedRecordBytes(path, "generated-credential", PasswordChangeId);

            vault.ChangePassword(
                "UNPWN_TEST_SECRET_old-password",
                "UNPWN_TEST_SECRET_new-password",
                TestParameters);

            var secondEnvelopeBytes = ReadEnvelopeBytes(path);
            var encryptedRecordBytesAfterChange = ReadEncryptedRecordBytes(path, "generated-credential", PasswordChangeId);

            Assert.NotEqual(Convert.ToHexString(firstEnvelopeBytes), Convert.ToHexString(secondEnvelopeBytes));
            Assert.Equal(Convert.ToHexString(encryptedRecordBytes), Convert.ToHexString(encryptedRecordBytesAfterChange));
        }

        Assert.Throws<InvalidOperationException>(() => RecoveryVault.Open(path, "UNPWN_TEST_SECRET_old-password"));
        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_new-password");
        using var record = Assert.IsType<VaultRecord>(reopened.ReadRecord("generated-credential", PasswordChangeId));

        Assert.Equal("UNPWN_TEST_SECRET_generated-password", Encoding.UTF8.GetString(record.Plaintext.Span));
    }

    [Fact]
    public void PasswordChangeRequiresCurrentPasswordAndLeavesOldPasswordValidOnFailure()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_old-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", PasswordFailureId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));

            Assert.Throws<InvalidOperationException>(() => vault.ChangePassword(
                "UNPWN_TEST_SECRET_wrong-password",
                "UNPWN_TEST_SECRET_new-password",
                TestParameters));
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_old-password");
        using var record = Assert.IsType<VaultRecord>(reopened.ReadRecord("account-state", PasswordFailureId));
        Assert.Equal("UNPWN_TEST_SECRET_state", Encoding.UTF8.GetString(record.Plaintext.Span));
    }

    [Fact]
    public void TamperedRecordIsRejectedDuringRead()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", TamperedId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));
        }

        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE vault_records
                SET ciphertext = CAST(x'00' || substr(ciphertext, 2) AS BLOB)
                WHERE record_type = $record_type AND record_id = $record_id;
                """;
            command.Parameters.AddWithValue("$record_type", "account-state");
            command.Parameters.AddWithValue("$record_id", TamperedId);
            command.ExecuteNonQuery();
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        Assert.ThrowsAny<CryptographicException>(() => reopened.ReadRecord("account-state", TamperedId));
    }

    [Fact]
    public void VaultOpenRejectsArgonCostAboveCurrentFormatBeforeKeyDerivation()
    {
        var path = VaultPath();
        using (RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
        }

        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE vault_key_envelope SET argon2_memory_kib = $memory WHERE id = 1;";
            command.Parameters.AddWithValue("$memory", int.MaxValue);
            command.ExecuteNonQuery();
        }

        Assert.ThrowsAny<InvalidOperationException>(() =>
            RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password"));
    }

    [Fact]
    public void VaultOpenRejectsUnexpectedEnvelopeBlobLengthBeforeMaterializingIt()
    {
        var path = VaultPath();
        using (RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
        }

        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE vault_key_envelope SET salt = zeroblob(1024) WHERE id = 1;";
            command.ExecuteNonQuery();
        }

        Assert.ThrowsAny<InvalidOperationException>(() =>
            RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password"));
    }

    [Fact]
    public void RecordReadRejectsCiphertextAboveCurrentFormatBeforePlaintextAllocation()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", StateId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));
        }

        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE vault_records
                SET ciphertext = zeroblob($length)
                WHERE record_type = $record_type AND record_id = $record_id;
                """;
            command.Parameters.AddWithValue("$length", VaultResourceLimits.MaximumRecordBytes + 1);
            command.Parameters.AddWithValue("$record_type", "account-state");
            command.Parameters.AddWithValue("$record_id", StateId);
            command.ExecuteNonQuery();
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        Assert.ThrowsAny<InvalidOperationException>(() => reopened.ReadRecord("account-state", StateId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string Id(int suffix) =>
        new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)suffix).ToString("N");

    private static byte[] ReadEncryptedRecordBytes(string path, string recordType, string recordId)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT nonce, ciphertext
            FROM vault_records
            WHERE record_type = $record_type AND record_id = $record_id;
            """;
        command.Parameters.AddWithValue("$record_type", recordType);
        command.Parameters.AddWithValue("$record_id", recordId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Encrypted recovery-vault record was not found.");
        }

        var nonce = (byte[])reader[0];
        var ciphertext = (byte[])reader[1];
        return [.. nonce, .. ciphertext];
    }

    private static byte[] ReadEnvelopeBytes(string path)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT salt, nonce, encrypted_data_key, tag FROM vault_key_envelope WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Recovery-vault key envelope was not found.");
        }

        return [.. (byte[])reader[0], .. (byte[])reader[1], .. (byte[])reader[2], .. (byte[])reader[3]];
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
