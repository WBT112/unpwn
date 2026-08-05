using System.Security.Cryptography;
using Unpwn.Vault.Cryptography;

namespace Unpwn.Vault.Storage;

public sealed class VaultRecord : IDisposable
{
    private byte[]? _plaintext;

    public VaultRecord(VaultRecordDescriptor descriptor, byte[] plaintext)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _plaintext = plaintext ?? throw new ArgumentNullException(nameof(plaintext));
    }

    public VaultRecordDescriptor Descriptor { get; }

    public ReadOnlyMemory<byte> Plaintext =>
        _plaintext ?? throw new ObjectDisposedException(nameof(VaultRecord));

    public void Dispose()
    {
        if (_plaintext is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_plaintext);
        _plaintext = null;
    }
}
