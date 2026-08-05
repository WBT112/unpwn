using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Unpwn.Vault.Cryptography;

/// <summary>
/// Focused recovery-vault cryptography prototype used to validate the planned
/// Argon2id key hierarchy and AES-256-GCM record-encryption design.
/// </summary>
public sealed class VaultCryptoPrototype : IDisposable
{
    public const int SaltSizeBytes = 16;
    public const int DataKeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;
    public const int DerivedKeySizeBytes = 32;

    private readonly RandomNumberGenerator _randomNumberGenerator;

    public VaultCryptoPrototype()
        : this(RandomNumberGenerator.Create())
    {
    }

    internal VaultCryptoPrototype(RandomNumberGenerator randomNumberGenerator)
    {
        _randomNumberGenerator = randomNumberGenerator ?? throw new ArgumentNullException(nameof(randomNumberGenerator));
    }

    public VaultKeyEnvelope CreateVault(string vaultPassword, Argon2idParameters parameters)
    {
        ValidateVaultPassword(vaultPassword);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        var salt = RandomBytes(SaltSizeBytes);
        var dataKey = RandomBytes(DataKeySizeBytes);
        var wrappingKey = DeriveWrappingKey(vaultPassword, salt, parameters);

        try
        {
            var nonce = RandomBytes(NonceSizeBytes);
            var encryptedDataKey = new byte[DataKeySizeBytes];
            var tag = new byte[TagSizeBytes];
            var associatedData = BuildAssociatedData("vault-data-key", "vault-key", 1);

            using var aes = new AesGcm(wrappingKey, TagSizeBytes);
            aes.Encrypt(nonce, dataKey, encryptedDataKey, tag, associatedData);

            return new VaultKeyEnvelope(
                parameters,
                salt,
                nonce,
                encryptedDataKey,
                tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public byte[] UnwrapDataKey(string vaultPassword, VaultKeyEnvelope envelope)
    {
        ValidateVaultPassword(vaultPassword);
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.Validate();

        var wrappingKey = DeriveWrappingKey(vaultPassword, envelope.Salt, envelope.Parameters);
        var dataKey = new byte[DataKeySizeBytes];

        try
        {
            var associatedData = BuildAssociatedData("vault-data-key", "vault-key", 1);
            using var aes = new AesGcm(wrappingKey, TagSizeBytes);
            aes.Decrypt(envelope.Nonce, envelope.EncryptedDataKey, envelope.Tag, dataKey, associatedData);
            return dataKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public EncryptedVaultRecord EncryptRecord(byte[] dataKey, VaultRecordDescriptor descriptor, ReadOnlySpan<byte> plaintext)
    {
        ValidateDataKey(dataKey);
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Validate();

        var nonce = RandomBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        var associatedData = BuildAssociatedData(descriptor.RecordType, descriptor.RecordId, descriptor.SchemaVersion);

        using var aes = new AesGcm(dataKey, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedVaultRecord(descriptor, nonce, ciphertext, tag);
    }

    public byte[] DecryptRecord(byte[] dataKey, EncryptedVaultRecord record)
    {
        ValidateDataKey(dataKey);
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();

        var plaintext = new byte[record.Ciphertext.Length];
        var associatedData = BuildAssociatedData(
            record.Descriptor.RecordType,
            record.Descriptor.RecordId,
            record.Descriptor.SchemaVersion);

        try
        {
            using var aes = new AesGcm(dataKey, TagSizeBytes);
            aes.Decrypt(record.Nonce, record.Ciphertext, record.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    public void Dispose()
    {
        _randomNumberGenerator.Dispose();
    }

    private static byte[] DeriveWrappingKey(string vaultPassword, byte[] salt, Argon2idParameters parameters)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(vaultPassword);
        try
        {
            using var argon2id = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = parameters.DegreeOfParallelism,
                Iterations = parameters.Iterations,
                MemorySize = parameters.MemorySizeKiB,
            };

            return argon2id.GetBytes(DerivedKeySizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private byte[] RandomBytes(int size)
    {
        var bytes = new byte[size];
        _randomNumberGenerator.GetBytes(bytes);
        return bytes;
    }

    private static byte[] BuildAssociatedData(string recordType, string recordId, int schemaVersion) =>
        Encoding.UTF8.GetBytes($"unpwn:vault-record:v{schemaVersion}:{recordType}:{recordId}");

    private static void ValidateVaultPassword(string vaultPassword)
    {
        if (string.IsNullOrWhiteSpace(vaultPassword))
        {
            throw new ArgumentException("Vault password is required.", nameof(vaultPassword));
        }
    }

    private static void ValidateDataKey(byte[] dataKey)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        if (dataKey.Length != DataKeySizeBytes)
        {
            throw new ArgumentException("Vault data key must be 32 bytes for AES-256-GCM.", nameof(dataKey));
        }
    }
}
