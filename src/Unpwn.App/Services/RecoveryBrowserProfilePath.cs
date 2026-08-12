namespace Unpwn.App.Services;

public static class RecoveryBrowserProfilePath
{
    public static string CreateOwnedProfileRoot(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        return Path.GetFullPath(Path.Combine(
            applicationDataRoot,
            "unpwn",
            "recovery-browser",
            "profiles",
            Guid.NewGuid().ToString("N")));
    }

    public static void ValidateOwnedProfileRoot(string path, string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);

        var expectedRoot = Path.GetFullPath(Path.Combine(
            applicationDataRoot,
            "unpwn",
            "recovery-browser",
            "profiles"));
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var candidateParent = Path.GetDirectoryName(candidate);
        var opaqueId = Path.GetFileName(candidate);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(candidateParent, expectedRoot, comparison) ||
            opaqueId.Length != 32 ||
            opaqueId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Recovery Browser profiles must use the unpwn-owned profile root.",
                nameof(path));
        }

        EnsureExistingPathIsNotRedirected(
            Path.Combine(applicationDataRoot, "unpwn"),
            nameof(path));
        EnsureExistingPathIsNotRedirected(
            Path.Combine(applicationDataRoot, "unpwn", "recovery-browser"),
            nameof(path));
        EnsureExistingPathIsNotRedirected(expectedRoot, nameof(path));
        EnsureExistingPathIsNotRedirected(candidate, nameof(path));
    }

    private static void EnsureExistingPathIsNotRedirected(
        string component,
        string parameterName)
    {
        try
        {
            var attributes = File.GetAttributes(component);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
            {
                throw new ArgumentException(
                    "Recovery Browser profile paths must not traverse redirected storage.",
                    parameterName);
            }
        }
        catch (FileNotFoundException)
        {
            // The dedicated profile hierarchy is created by the selected platform adapter.
        }
        catch (DirectoryNotFoundException)
        {
            // The dedicated profile hierarchy is created by the selected platform adapter.
        }
    }
}
