using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Unpwn.Vault.Cryptography;

namespace Unpwn.Vault.Storage;

public sealed class RecoveryVault : IDisposable
{
    private const int VaultSchemaVersion = 1;
    private readonly string _path;
    private byte[]? _dataKey;
    private bool _disposed;

    private RecoveryVault(string path, byte[] dataKey)
    {
        _path = path;
        _dataKey = dataKey;
    }

    public bool IsLocked => _dataKey is null;

    public static RecoveryVault Create(string path, string vaultPassword, Argon2idParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            throw new IOException("A recovery vault already exists at the selected path.");
        }

        using var crypto = new VaultCryptoPrototype();
        var envelope = crypto.CreateVault(vaultPassword, parameters);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

        using var connection = OpenConnection(path, SqliteOpenMode.ReadWriteCreate);
        using var transaction = connection.BeginTransaction();
        CreateSchema(connection, transaction);
        InsertEnvelope(connection, transaction, envelope);
        transaction.Commit();

        var dataKey = VaultCryptoPrototype.UnwrapDataKey(vaultPassword, envelope);
        return new RecoveryVault(path, dataKey);
    }

    public static RecoveryVault Open(string path, string vaultPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Recovery vault file was not found.", path);
        }

        return new RecoveryVault(path, UnlockDataKey(path, vaultPassword));
    }

    public void Lock()
    {
        ThrowIfDisposed();
        if (_dataKey is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_dataKey);
        _dataKey = null;
    }

    public void Unlock(string vaultPassword)
    {
        ThrowIfDisposed();
        if (_dataKey is not null)
        {
            throw new InvalidOperationException("Recovery vault is already unlocked.");
        }

        _dataKey = UnlockDataKey(_path, vaultPassword);
    }

    public void ChangePassword(string currentVaultPassword, string newVaultPassword, Argon2idParameters parameters)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        var verifiedDataKey = UnlockDataKey(_path, currentVaultPassword);
        try
        {
            var activeDataKey = GetDataKey();
            if (!CryptographicOperations.FixedTimeEquals(activeDataKey, verifiedDataKey))
            {
                throw new CryptographicException("Recovery vault data key verification failed.");
            }

            using var crypto = new VaultCryptoPrototype();
            var newEnvelope = crypto.WrapExistingDataKey(newVaultPassword, parameters, activeDataKey);
            using var connection = OpenConnection(_path, SqliteOpenMode.ReadWrite);
            using var transaction = connection.BeginTransaction();
            UpsertEnvelope(connection, transaction, newEnvelope);
            transaction.Commit();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifiedDataKey);
        }
    }

    public void UpsertRecord(VaultRecordDescriptor descriptor, ReadOnlySpan<byte> plaintext)
    {
        var copy = plaintext.ToArray();
        try
        {
            UpsertRecords([new VaultRecordWrite(descriptor, copy)]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    public void UpsertRecords(IReadOnlyCollection<VaultRecordWrite> writes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count == 0)
        {
            throw new ArgumentException("At least one recovery vault record is required.", nameof(writes));
        }

        foreach (var write in writes)
        {
            ArgumentNullException.ThrowIfNull(write);
            write.Validate();
        }

        if (writes
            .Select(write => (write.Descriptor.RecordType, write.Descriptor.RecordId))
            .Distinct()
            .Count() != writes.Count)
        {
            throw new ArgumentException("An atomic recovery vault write cannot contain duplicate records.", nameof(writes));
        }

        var dataKey = GetDataKey();
        using var crypto = new VaultCryptoPrototype();
        var encryptedWrites = writes
            .Select(write => crypto.EncryptRecord(dataKey, write.Descriptor, write.Plaintext.Span))
            .ToArray();
        using var connection = OpenConnection(_path, SqliteOpenMode.ReadWrite);
        using var transaction = connection.BeginTransaction();
        var updatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var encrypted in encryptedWrites)
        {
            UpsertEncryptedRecord(connection, transaction, encrypted, updatedAt);
        }

        transaction.Commit();
    }

    public VaultRecord? ReadRecord(string recordType, string recordId)
    {
        ThrowIfDisposed();
        var dataKey = GetDataKey();
        var descriptor = new VaultRecordDescriptor(recordType, recordId, 1);
        descriptor.Validate();

        using var connection = OpenConnection(_path, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_version, nonce, ciphertext, tag
            FROM vault_records
            WHERE record_type = $record_type AND record_id = $record_id;
            """;
        command.Parameters.AddWithValue("$record_type", recordType);
        command.Parameters.AddWithValue("$record_id", recordId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storedDescriptor = new VaultRecordDescriptor(recordType, recordId, reader.GetInt32(0));
        storedDescriptor.Validate();
        var encrypted = new EncryptedVaultRecord(storedDescriptor, (byte[])reader[1], (byte[])reader[2], (byte[])reader[3]);
        var plaintext = VaultCryptoPrototype.DecryptRecord(dataKey, encrypted);
        return new VaultRecord(storedDescriptor, plaintext);
    }

    public IReadOnlyList<VaultRecordDescriptor> ListRecords()
    {
        ThrowIfDisposed();
        _ = GetDataKey();
        using var connection = OpenConnection(_path, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT record_type, record_id, schema_version FROM vault_records ORDER BY record_type, record_id;";
        using var reader = command.ExecuteReader();
        var descriptors = new List<VaultRecordDescriptor>();
        while (reader.Read())
        {
            var descriptor = new VaultRecordDescriptor(reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
            descriptor.Validate();
            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    public bool DeleteRecord(string recordType, string recordId)
    {
        ThrowIfDisposed();
        _ = GetDataKey();
        var descriptor = new VaultRecordDescriptor(recordType, recordId, 1);
        descriptor.Validate();
        using var connection = OpenConnection(_path, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vault_records WHERE record_type = $record_type AND record_id = $record_id;";
        command.Parameters.AddWithValue("$record_type", recordType);
        command.Parameters.AddWithValue("$record_id", recordId);
        return command.ExecuteNonQuery() > 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Lock();
        _disposed = true;
    }

    private static byte[] UnlockDataKey(string path, string vaultPassword)
    {
        using var connection = OpenConnection(path, SqliteOpenMode.ReadOnly);
        var envelope = ReadEnvelope(connection);
        try
        {
            return VaultCryptoPrototype.UnwrapDataKey(vaultPassword, envelope);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Recovery vault could not be unlocked with the supplied password.", exception);
        }
    }

    private byte[] GetDataKey()
    {
        ThrowIfDisposed();
        return _dataKey ?? throw new InvalidOperationException("Recovery vault is locked.");
    }

    private static SqliteConnection OpenConnection(string path, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS vault_key_envelope(
                id INTEGER PRIMARY KEY CHECK (id = 1),
                schema_version INTEGER NOT NULL,
                argon2_memory_kib INTEGER NOT NULL,
                argon2_iterations INTEGER NOT NULL,
                argon2_parallelism INTEGER NOT NULL,
                salt BLOB NOT NULL,
                nonce BLOB NOT NULL,
                encrypted_data_key BLOB NOT NULL,
                tag BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS vault_records(
                record_type TEXT NOT NULL,
                record_id TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                nonce BLOB NOT NULL,
                ciphertext BLOB NOT NULL,
                tag BLOB NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(record_type, record_id)
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertEnvelope(SqliteConnection connection, SqliteTransaction transaction, VaultKeyEnvelope envelope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO vault_key_envelope(id, schema_version, argon2_memory_kib, argon2_iterations, argon2_parallelism, salt, nonce, encrypted_data_key, tag)
            VALUES(1, $schema_version, $memory, $iterations, $parallelism, $salt, $nonce, $encrypted_data_key, $tag);
            """;
        AddEnvelopeParameters(command, envelope);
        command.ExecuteNonQuery();
    }

    private static void UpsertEnvelope(SqliteConnection connection, SqliteTransaction transaction, VaultKeyEnvelope envelope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE vault_key_envelope
            SET schema_version = $schema_version,
                argon2_memory_kib = $memory,
                argon2_iterations = $iterations,
                argon2_parallelism = $parallelism,
                salt = $salt,
                nonce = $nonce,
                encrypted_data_key = $encrypted_data_key,
                tag = $tag
            WHERE id = 1;
            """;
        AddEnvelopeParameters(command, envelope);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("Recovery vault key envelope could not be updated.");
        }
    }

    private static void UpsertEncryptedRecord(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EncryptedVaultRecord encrypted,
        string updatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO vault_records(record_type, record_id, schema_version, nonce, ciphertext, tag, updated_utc)
            VALUES($record_type, $record_id, $schema_version, $nonce, $ciphertext, $tag, $updated_utc)
            ON CONFLICT(record_type, record_id) DO UPDATE SET
                schema_version = excluded.schema_version,
                nonce = excluded.nonce,
                ciphertext = excluded.ciphertext,
                tag = excluded.tag,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$record_type", encrypted.Descriptor.RecordType);
        command.Parameters.AddWithValue("$record_id", encrypted.Descriptor.RecordId);
        command.Parameters.AddWithValue("$schema_version", encrypted.Descriptor.SchemaVersion);
        command.Parameters.Add("$nonce", SqliteType.Blob).Value = encrypted.Nonce;
        command.Parameters.Add("$ciphertext", SqliteType.Blob).Value = encrypted.Ciphertext;
        command.Parameters.Add("$tag", SqliteType.Blob).Value = encrypted.Tag;
        command.Parameters.AddWithValue("$updated_utc", updatedAt);
        command.ExecuteNonQuery();
    }

    private static void AddEnvelopeParameters(SqliteCommand command, VaultKeyEnvelope envelope)
    {
        command.Parameters.AddWithValue("$schema_version", VaultSchemaVersion);
        command.Parameters.AddWithValue("$memory", envelope.Parameters.MemorySizeKiB);
        command.Parameters.AddWithValue("$iterations", envelope.Parameters.Iterations);
        command.Parameters.AddWithValue("$parallelism", envelope.Parameters.DegreeOfParallelism);
        command.Parameters.Add("$salt", SqliteType.Blob).Value = envelope.Salt;
        command.Parameters.Add("$nonce", SqliteType.Blob).Value = envelope.Nonce;
        command.Parameters.Add("$encrypted_data_key", SqliteType.Blob).Value = envelope.EncryptedDataKey;
        command.Parameters.Add("$tag", SqliteType.Blob).Value = envelope.Tag;
    }

    private static VaultKeyEnvelope ReadEnvelope(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_version, argon2_memory_kib, argon2_iterations, argon2_parallelism, salt, nonce, encrypted_data_key, tag
            FROM vault_key_envelope WHERE id = 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Recovery vault key envelope is missing.");
        }

        if (reader.GetInt32(0) != VaultSchemaVersion)
        {
            throw new NotSupportedException("Recovery vault schema version is not supported.");
        }

        return new VaultKeyEnvelope(
            new Argon2idParameters(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            (byte[])reader[4],
            (byte[])reader[5],
            (byte[])reader[6],
            (byte[])reader[7]);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
