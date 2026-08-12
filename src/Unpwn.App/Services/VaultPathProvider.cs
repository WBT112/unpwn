namespace Unpwn.App.Services;

public interface IVaultPathProvider
{
    string GetNextDefaultVaultPath();
}

public static class VaultPathPolicy
{
    public static string ResolveLocalDataRoot(
        bool isWindows,
        string? localApplicationData,
        string? xdgDataHome,
        string? userProfile)
    {
        if (isWindows)
        {
            return RequireAbsoluteRoot(localApplicationData, "LocalApplicationData");
        }

        if (!string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathFullyQualified(xdgDataHome))
        {
            return Path.GetFullPath(xdgDataHome);
        }

        if (!string.IsNullOrWhiteSpace(localApplicationData) &&
            Path.IsPathFullyQualified(localApplicationData))
        {
            return Path.GetFullPath(localApplicationData);
        }

        if (!string.IsNullOrWhiteSpace(userProfile) && Path.IsPathFullyQualified(userProfile))
        {
            return Path.Combine(Path.GetFullPath(userProfile), ".local", "share");
        }

        throw new InvalidOperationException("A user-local application-data directory is unavailable.");
    }

    public static string SelectAvailableVaultPath(
        string vaultDirectory,
        Func<string, bool> pathExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(pathExists);

        var candidate = Path.Combine(vaultDirectory, "unpwn-recovery.db");
        if (!pathExists(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            candidate = Path.Combine(vaultDirectory, $"unpwn-recovery-{suffix}.db");
            if (!pathExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No available default recovery-vault filename could be selected.");
    }

    private static string RequireAbsoluteRoot(string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{source} does not provide an absolute user-local data directory.");
        }

        return Path.GetFullPath(path);
    }
}

public sealed class PlatformVaultPathProvider : IVaultPathProvider
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _dataRoot;
    private readonly Func<string, bool> _pathExists;
    private readonly Action<string> _ensureDirectory;

    public PlatformVaultPathProvider(
        string? dataRootOverride = null,
        Func<string, bool>? pathExists = null,
        Action<string>? ensureDirectory = null)
    {
        _dataRoot = dataRootOverride is null
            ? ResolveCurrentLocalDataRoot()
            : Path.GetFullPath(dataRootOverride);
        _pathExists = pathExists ?? (path => File.Exists(path) || Directory.Exists(path));
        _ensureDirectory = ensureDirectory ?? EnsurePrivateDirectory;
    }

    public string GetNextDefaultVaultPath()
    {
        var applicationDirectory = Path.Combine(_dataRoot, "unpwn");
        var vaultDirectory = Path.Combine(applicationDirectory, "vaults");
        _ensureDirectory(applicationDirectory);
        _ensureDirectory(vaultDirectory);
        return VaultPathPolicy.SelectAvailableVaultPath(vaultDirectory, _pathExists);
    }

    private static string ResolveCurrentLocalDataRoot() => VaultPathPolicy.ResolveLocalDataRoot(
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path, PrivateDirectoryMode);
            return;
        }

        File.SetUnixFileMode(path, PrivateDirectoryMode);
    }
}
