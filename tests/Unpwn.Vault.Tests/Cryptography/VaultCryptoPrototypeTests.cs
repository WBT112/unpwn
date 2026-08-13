using System.Security.Cryptography;
using System.Text;
using Unpwn.Vault.Cryptography;
using Xunit;

namespace Unpwn.Vault.Tests.Cryptography;

public sealed class VaultCryptoPrototypeTests
{
    private static readonly Argon2idParameters TestParameters = new(19 * 1024, 2, 1);
    private static readonly string RecordId = Guid.Parse("00000000-0000-0000-0000-000000000123").ToString("N");
    private static readonly string OtherRecordId = Guid.Parse("00000000-0000-0000-0000-000000000456").ToString("N");

    [Fact]
    public void VaultPasswordWrapsAndUnwrapsRandomDataKey()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);

        try
        {
            Assert.Equal(VaultCryptoPrototype.DataKeySizeBytes, dataKey.Length);
            Assert.Equal(VaultCryptoPrototype.SaltSizeBytes, envelope.Salt.Length);
            Assert.Equal(VaultCryptoPrototype.NonceSizeBytes, envelope.Nonce.Length);
            Assert.Equal(VaultCryptoPrototype.TagSizeBytes, envelope.Tag.Length);
            Assert.NotEqual(new byte[VaultCryptoPrototype.DataKeySizeBytes], dataKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    [Fact]
    public void WrongVaultPasswordCannotUnwrapDataKey()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_correct-password", TestParameters);

        Assert.ThrowsAny<CryptographicException>(
            () => VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_wrong-password", envelope));
    }

    [Fact]
    public void RecordsRoundTripWithAuthenticatedDescriptorMetadata()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var descriptor = new VaultRecordDescriptor("generated-credential", RecordId, 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password");

        try
        {
            var record = prototype.EncryptRecord(dataKey, descriptor, plaintext);
            var decrypted = VaultCryptoPrototype.DecryptRecord(dataKey, record);
            try
            {
                Assert.Equal(plaintext, decrypted);
                Assert.NotEqual(Convert.ToHexString(plaintext), Convert.ToHexString(record.Ciphertext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public void RecordTamperingIsRejected()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        try
        {
            var record = prototype.EncryptRecord(
                dataKey,
                new VaultRecordDescriptor("note", RecordId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_note"));
            record.Ciphertext[0] ^= 0xff;

            Assert.ThrowsAny<CryptographicException>(() => VaultCryptoPrototype.DecryptRecord(dataKey, record));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    [Fact]
    public void AssociatedDataBindsRecordTypeIdentifierAndSchemaVersion()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        try
        {
            var record = prototype.EncryptRecord(
                dataKey,
                new VaultRecordDescriptor("generated-credential", RecordId, 1),
                Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_record"));
            var reboundRecord = record with
            {
                Descriptor = new VaultRecordDescriptor("generated-credential", OtherRecordId, 1),
            };

            Assert.ThrowsAny<CryptographicException>(() => VaultCryptoPrototype.DecryptRecord(dataKey, reboundRecord));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    [Fact]
    public void EveryEncryptionUsesAFreshNonce()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = VaultCryptoPrototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var descriptor = new VaultRecordDescriptor("generated-credential", RecordId, 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_record");

        try
        {
            var first = prototype.EncryptRecord(dataKey, descriptor, plaintext);
            var second = prototype.EncryptRecord(dataKey, descriptor, plaintext);

            Assert.NotEqual(first.Nonce, second.Nonce);
            Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public void RecordDescriptorRejectsSensitiveOrUserControlledMetadata()
    {
        Assert.Throws<ArgumentException>(() =>
            new VaultRecordDescriptor("user@example.test", RecordId, 1).Validate());
        Assert.Throws<ArgumentException>(() =>
            new VaultRecordDescriptor("account-state", "user@example.test", 1).Validate());
    }

    [Fact]
    public void AccountExecutionIsAnAllowedRepositoryRecordCategory()
    {
        var descriptor = new VaultRecordDescriptor("account-execution", RecordId, 1);

        descriptor.Validate();
    }

    [Fact]
    public void Argon2idParametersRejectUnsupportedSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(1024, 2, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(19 * 1024, 1, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(19 * 1024, 2, 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(
            VaultResourceLimits.MaximumArgon2MemorySizeKiB + 1,
            3,
            2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(
            64 * 1024,
            VaultResourceLimits.MaximumArgon2Iterations + 1,
            2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(
            64 * 1024,
            3,
            VaultResourceLimits.MaximumArgon2DegreeOfParallelism + 1).Validate());

        Argon2idParameters.Interactive.Validate();
    }
}
