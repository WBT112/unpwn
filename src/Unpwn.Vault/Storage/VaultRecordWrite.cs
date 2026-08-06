using Unpwn.Vault.Cryptography;

namespace Unpwn.Vault.Storage;

public sealed record VaultRecordWrite(
    VaultRecordDescriptor Descriptor,
    ReadOnlyMemory<byte> Plaintext)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Descriptor);
        Descriptor.Validate();
    }
}
