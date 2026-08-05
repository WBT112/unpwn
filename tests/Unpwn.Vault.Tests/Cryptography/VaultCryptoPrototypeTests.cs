using System.Security.Cryptography;
using System.Text;
using Unpwn.Vault.Cryptography;
using Xunit;

namespace Unpwn.Vault.Tests.Cryptography;

public sealed class VaultCryptoPrototypeTests
{
    private static readonly Argon2idParameters TestParameters = new(19 * 1024, 2, 1);

    [Fact]
    public void VaultPasswordWrapsAndUnwrapsRandomDataKey()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);

        var dataKey = prototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);

        Assert.Equal(VaultCryptoPrototype.DataKeySizeBytes, dataKey.Length);
        Assert.Equal(VaultCryptoPrototype.SaltSizeBytes, envelope.Salt.Length);
        Assert.Equal(VaultCryptoPrototype.NonceSizeBytes, envelope.Nonce.Length);
        Assert.Equal(VaultCryptoPrototype.TagSizeBytes, envelope.Tag.Length);
        Assert.NotEqual(new byte[VaultCryptoPrototype.DataKeySizeBytes], dataKey);
    }

    [Fact]
    public void WrongVaultPasswordCannotUnwrapDataKey()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_correct-password", TestParameters);

        Assert.Throws<CryptographicException>(
            () => prototype.UnwrapDataKey("UNPWN_TEST_SECRET_wrong-password", envelope));
    }

    [Fact]
    public void RecordsRoundTripWithAuthenticatedDescriptorMetadata()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = prototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var descriptor = new VaultRecordDescriptor("generated-credential", "account-123", 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_generated-password");

        var record = prototype.EncryptRecord(dataKey, descriptor, plaintext);
        var decrypted = prototype.DecryptRecord(dataKey, record);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(Convert.ToHexString(plaintext), Convert.ToHexString(record.Ciphertext));
    }

    [Fact]
    public void RecordTamperingIsRejected()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = prototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var record = prototype.EncryptRecord(
            dataKey,
            new VaultRecordDescriptor("account-note", "account-123", 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_note"));
        record.Ciphertext[0] ^= 0xff;

        Assert.Throws<CryptographicException>(() => prototype.DecryptRecord(dataKey, record));
    }

    [Fact]
    public void AssociatedDataBindsRecordTypeIdentifierAndSchemaVersion()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = prototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var record = prototype.EncryptRecord(
            dataKey,
            new VaultRecordDescriptor("credential", "account-123", 1),
            Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_record"));
        var reboundRecord = record with
        {
            Descriptor = new VaultRecordDescriptor("credential", "account-456", 1),
        };

        Assert.Throws<CryptographicException>(() => prototype.DecryptRecord(dataKey, reboundRecord));
    }

    [Fact]
    public void EveryEncryptionUsesAFreshNonce()
    {
        using var prototype = new VaultCryptoPrototype();
        var envelope = prototype.CreateVault("UNPWN_TEST_SECRET_vault-password", TestParameters);
        var dataKey = prototype.UnwrapDataKey("UNPWN_TEST_SECRET_vault-password", envelope);
        var descriptor = new VaultRecordDescriptor("credential", "account-123", 1);
        var plaintext = Encoding.UTF8.GetBytes("UNPWN_TEST_SECRET_record");

        var first = prototype.EncryptRecord(dataKey, descriptor, plaintext);
        var second = prototype.EncryptRecord(dataKey, descriptor, plaintext);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }

    [Fact]
    public void Argon2idParametersRejectUnsafePrototypeSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(1024, 2, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(19 * 1024, 1, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Argon2idParameters(19 * 1024, 2, 0).Validate());
    }
}
