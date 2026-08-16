namespace Unpwn.App.Services;

internal static class RecoveryBrowserFilePermissions
{
    internal const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute;

    internal const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite;

    internal static void EnsurePrivateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(path);
            return;
        }

        EnsureExistingDirectoryIsNotRedirected(path);
        Directory.CreateDirectory(path, PrivateDirectoryMode);
        EnsureExistingDirectoryIsNotRedirected(path);
        File.SetUnixFileMode(path, PrivateDirectoryMode);
        if (File.GetUnixFileMode(path) != PrivateDirectoryMode)
        {
            throw new IOException(
                "The Recovery Browser directory could not be restricted to the current user.");
        }
    }

    internal static FileStream CreatePrivateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        return new FileStream(path, options);
    }

    internal static void EnsurePrivateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException(
                "Recovery Browser metadata must be a regular unredirected file.");
        }

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        File.SetUnixFileMode(path, PrivateFileMode);
        if (File.GetUnixFileMode(path) != PrivateFileMode)
        {
            throw new IOException(
                "The Recovery Browser metadata file could not be restricted to the current user.");
        }
    }

    internal static void EnsurePrivateOwnedProfileHierarchy(
        string applicationDataRoot,
        string profileDataPath)
    {
        RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
            profileDataPath,
            applicationDataRoot);

        var recoveryBrowserRoot = Path.Combine(
            Path.GetFullPath(applicationDataRoot),
            "unpwn",
            "recovery-browser");
        EnsurePrivateDirectory(recoveryBrowserRoot);
        EnsurePrivateDirectory(
            RecoveryBrowserProfilePath.GetOwnedProfilesRoot(applicationDataRoot));
        EnsurePrivateDirectory(profileDataPath);

        RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
            profileDataPath,
            applicationDataRoot);
    }

    private static void EnsureExistingDirectoryIsNotRedirected(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException(
                    "Recovery Browser directories must not use redirected storage.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
