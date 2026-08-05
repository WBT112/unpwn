namespace Unpwn.App.Services;

public sealed record ShellContext(
    bool IsVaultUnlocked,
    string VaultDisplayName,
    string SessionDisplayName)
{
    public static ShellContext Locked { get; } = new(false, string.Empty, string.Empty);

    public static ShellContext Unlocked(string vaultDisplayName, string sessionDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDisplayName);
        return new ShellContext(true, vaultDisplayName, sessionDisplayName);
    }
}
