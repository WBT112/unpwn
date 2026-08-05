namespace Unpwn.Vault.Cryptography;

public sealed record EncryptedVaultRecord(
    VaultRecordDescriptor Descriptor,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Descriptor);
        Descriptor.Validate();
        ValidateLength(Nonce, VaultCryptoPrototype.NonceSizeBytes, nameof(Nonce));
        ArgumentNullException.ThrowIfNull(Ciphertext);
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
