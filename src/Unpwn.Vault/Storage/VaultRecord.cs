using System.Security.Cryptography;
using Unpwn.Vault.Cryptography;

namespace Unpwn.Vault.Storage;

public sealed class VaultRecord(VaultRecordDescriptor descriptor, byte[] plaintext) : IDisposable
{
    private byte[]? _plaintext = plaintext ?? throw new ArgumentNullException(nameof(plaintext));

    public VaultRecordDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

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
