using System.Text;
using Microsoft.Data.Sqlite;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.Vault.Tests.Storage;

public sealed class AtomicRecoveryVaultTests : IDisposable
{
    private static readonly Argon2idParameters TestParameters = new(19 * 1024, 2, 1);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "unpwn-vault-atomic-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AtomicBatchWritesEveryRecord()
    {
        var path = VaultPath();
        var first = Descriptor("account-state", 1);
        var second = Descriptor("note", 2);
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);

        vault.UpsertRecords(
        [
            new VaultRecordWrite(first, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first")),
            new VaultRecordWrite(second, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_second")),
        ]);

        using var firstRecord = Assert.IsType<VaultRecord>(vault.ReadRecord(first.RecordType, first.RecordId));
        using var secondRecord = Assert.IsType<VaultRecord>(vault.ReadRecord(second.RecordType, second.RecordId));
        Assert.Equal("UNPWN_TEST_SECRET_first", Encoding.UTF8.GetString(firstRecord.Plaintext.Span));
        Assert.Equal("UNPWN_TEST_SECRET_second", Encoding.UTF8.GetString(secondRecord.Plaintext.Span));
    }

    [Fact]
    public void DuplicateRecordKeysAreRejectedBeforeWriting()
    {
        var path = VaultPath();
        var descriptor = Descriptor("account-state", 3);
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);

        Assert.Throws<ArgumentException>(() => vault.UpsertRecords(
        [
            new VaultRecordWrite(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first")),
            new VaultRecordWrite(descriptor, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_second")),
        ]));

        Assert.Null(vault.ReadRecord(descriptor.RecordType, descriptor.RecordId));
    }

    [Fact]
    public void SqlFailureRollsBackEarlierRecordsInTheBatch()
    {
        var path = VaultPath();
        var first = Descriptor("account-state", 4);
        var second = Descriptor("note", 5);
        using var vault = RecoveryVault.Create(path, "UNPWN_TEST_SECRET_vault-password", TestParameters);
        using (var connection = OpenConnection(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER fail_note_write
                BEFORE INSERT ON vault_records
                WHEN NEW.record_type = 'note'
                BEGIN
                    SELECT RAISE(ABORT, 'synthetic batch failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => vault.UpsertRecords(
        [
            new VaultRecordWrite(first, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_first")),
            new VaultRecordWrite(second, Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_second")),
        ]));

        Assert.Null(vault.ReadRecord(first.RecordType, first.RecordId));
        Assert.Null(vault.ReadRecord(second.RecordType, second.RecordId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static VaultRecordDescriptor Descriptor(string recordType, int suffix) =>
        new(recordType, new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)suffix).ToString("N"), 1);

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
