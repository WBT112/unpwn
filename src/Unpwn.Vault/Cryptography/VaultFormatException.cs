namespace Unpwn.Vault.Cryptography;

public sealed class VaultFormatException : InvalidOperationException
{
    public VaultFormatException(string message)
        : base(message)
    {
    }

    public VaultFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
