namespace Unpwn.App.Services;

public sealed record ShellContext(
    bool IsVaultUnlocked,
    string VaultDisplayName,
    string? SessionDisplayName)
{
    public static ShellContext Locked { get; } = new(false, string.Empty, null);

    public static ShellContext Unlocked(
        string vaultDisplayName,
        string? sessionDisplayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDisplayName);
        return new ShellContext(true, vaultDisplayName, sessionDisplayName);
    }
}
