namespace Unpwn.Vault.Cryptography;

public sealed record VaultKeyEnvelope(
    Argon2idParameters Parameters,
    byte[] Salt,
    byte[] Nonce,
    byte[] EncryptedDataKey,
    byte[] Tag)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Parameters);
        Parameters.Validate();
        ValidateLength(Salt, VaultCryptoPrototype.SaltSizeBytes, nameof(Salt));
        ValidateLength(Nonce, VaultCryptoPrototype.NonceSizeBytes, nameof(Nonce));
        ValidateLength(EncryptedDataKey, VaultCryptoPrototype.DataKeySizeBytes, nameof(EncryptedDataKey));
        ValidateLength(Tag, VaultCryptoPrototype.TagSizeBytes, nameof(Tag));
    }

    private static void ValidateLength(byte[] value, int expectedLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != expectedLength)
        {
            throw new ArgumentException($"Expected {expectedLength} bytes.", parameterName);
        }
    }
}
