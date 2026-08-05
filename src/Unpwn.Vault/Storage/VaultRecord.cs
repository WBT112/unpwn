using Unpwn.Vault.Cryptography;

namespace Unpwn.Vault.Storage;

public sealed record VaultRecord(VaultRecordDescriptor Descriptor, byte[] Plaintext);
