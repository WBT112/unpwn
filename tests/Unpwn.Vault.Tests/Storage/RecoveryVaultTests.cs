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
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "unpwn-vault-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatedVaultPersistsEncryptedRecordsAcrossReopen()
    {
        var path = VaultPath();
        var descriptor = new VaultRecordDescriptor("generated-credential", "account-1", 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password");

        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(descriptor, plaintext);
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        var record = reopened.ReadRecord("generated-credential", "account-1");

        Assert.NotNull(record);
        Assert.Equal(descriptor, record.Descriptor);
        Assert.Equal(plaintext, record.Plaintext);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_generated-password", File.ReadAllText(path));
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
    public void RecordMetadataIsQueryableWithoutDecryptingPlaintext()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", "account-2", 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_account-state"));

        var descriptors = vault.ListRecords();

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("account-state", descriptor.RecordType);
        Assert.Equal("account-2", descriptor.RecordId);
        Assert.Equal(1, descriptor.SchemaVersion);
    }

    [Fact]
    public void UpsertingRecordRotatesNonceAndCiphertext()
    {
        var path = VaultPath();
        var descriptor = new VaultRecordDescriptor("note", "account-3", 1);
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);

        vault.UpsertRecord(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first"));
        var firstEncryptedBytes = ReadEncryptedRecordBytes(path, "note", "account-3");
        vault.UpsertRecord(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first"));
        var secondEncryptedBytes = ReadEncryptedRecordBytes(path, "note", "account-3");

        Assert.NotEqual(Convert.ToHexString(firstEncryptedBytes), Convert.ToHexString(secondEncryptedBytes));
    }

    [Fact]
    public void DeletedRecordIsNotReturnedAfterReopen()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", "account-4", 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));
            Assert.True(vault.DeleteRecord("account-state", "account-4"));
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        Assert.Null(reopened.ReadRecord("account-state", "account-4"));
    }

    [Fact]
    public void LockedVaultRejectsSensitiveRecordOperationsUntilUnlocked()
    {
        var path = VaultPath();
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", "account-locked", 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));

        vault.Lock();

        Assert.True(vault.IsLocked);
        Assert.Throws<InvalidOperationException>(() => vault.ReadRecord("account-state", "account-locked"));
        Assert.Throws<InvalidOperationException>(() => vault.UpsertRecord(
            new VaultRecordDescriptor("account-state", "account-locked", 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_new-state")));

        vault.Unlock("UNPWN_TEST_SECRET_vault-password");

        Assert.False(vault.IsLocked);
        var record = vault.ReadRecord("account-state", "account-locked");
        Assert.NotNull(record);
        Assert.Equal("UNPWN_TEST_SECRET_state", Encoding.UTF8.GetString(record.Plaintext));
    }

    [Fact]
    public void PasswordChangeRewrapsDataKeyWithoutReencryptingRecords()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_old-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("generated-credential", "account-password-change", 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password"));

            var firstEnvelopeBytes = ReadEnvelopeBytes(path);
            var encryptedRecordBytes = ReadEncryptedRecordBytes(path, "generated-credential", "account-password-change");

            vault.ChangePassword(
                "UNPWN_TEST_SECRET_old-password",
                "UNPWN_TEST_SECRET_new-password",
                TestParameters);

            var secondEnvelopeBytes = ReadEnvelopeBytes(path);
            var encryptedRecordBytesAfterChange = ReadEncryptedRecordBytes(path, "generated-credential", "account-password-change");

            Assert.NotEqual(Convert.ToHexString(firstEnvelopeBytes), Convert.ToHexString(secondEnvelopeBytes));
            Assert.Equal(Convert.ToHexString(encryptedRecordBytes), Convert.ToHexString(encryptedRecordBytesAfterChange));
        }

        Assert.Throws<InvalidOperationException>(() => RecoveryVault.Open(path, "UNPWN_TEST_SECRET_old-password"));
        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_new-password");
        var record = reopened.ReadRecord("generated-credential", "account-password-change");

        Assert.NotNull(record);
        Assert.Equal("UNPWN_TEST_SECRET_generated-password", Encoding.UTF8.GetString(record.Plaintext));
    }

    [Fact]
    public void PasswordChangeRequiresCurrentPasswordAndLeavesOldPasswordValidOnFailure()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_old-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", "account-password-failure", 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));

            Assert.Throws<InvalidOperationException>(() => vault.ChangePassword(
                "UNPWN_TEST_SECRET_wrong-password",
                "UNPWN_TEST_SECRET_new-password",
                TestParameters));
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_old-password");
        Assert.NotNull(reopened.ReadRecord("account-state", "account-password-failure"));
    }

    [Fact]
    public void TamperedRecordIsRejectedDuringRead()
    {
        var path = VaultPath();
        using (var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters))
        {
            vault.UpsertRecord(
                new VaultRecordDescriptor("account-state", "account-5", 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_state"));
        }

        using (var connection = OpenConnection(path))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE vault_records
                SET ciphertext = CAST(x'00' || substr(ciphertext, 2) AS BLOB)
                WHERE record_type = 'account-state' AND record_id = 'account-5';
                """;
            command.ExecuteNonQuery();
        }

        using var reopened = RecoveryVault.Open(path, "UNPWN_TEST_SECRET_vault-password");
        Assert.ThrowsAny<CryptographicException>(() => reopened.ReadRecord("account-state", "account-5"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

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
