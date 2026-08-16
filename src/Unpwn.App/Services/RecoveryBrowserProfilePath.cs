namespace Unpwn.App.Services;

public static class RecoveryBrowserProfilePath
{
    public static string GetOwnedProfilesRoot(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        return Path.GetFullPath(Path.Combine(
            applicationDataRoot,
            "unpwn",
            "recovery-browser",
            "profiles"));
    }

    public static string GetOwnedProfileRoot(
        string applicationDataRoot,
        Guid sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A Recovery Browser session requires a non-empty identifier.",
                nameof(sessionId));
        }

        return Path.Combine(
            GetOwnedProfilesRoot(applicationDataRoot),
            sessionId.ToString("N"));
    }

    public static string CreateOwnedProfileRoot(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        return GetOwnedProfileRoot(applicationDataRoot, Guid.NewGuid());
    }

    public static void ValidateOwnedProfileRoot(string path, string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);

        var expectedRoot = GetOwnedProfilesRoot(applicationDataRoot);
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

        ValidateOwnedProfilesRoot(applicationDataRoot, nameof(path));
        EnsureExistingPathIsNotRedirected(candidate, nameof(path));
    }

    internal static void ValidateOwnedProfilesRoot(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        ValidateOwnedProfilesRoot(applicationDataRoot, nameof(applicationDataRoot));
    }

    private static void ValidateOwnedProfilesRoot(
        string applicationDataRoot,
        string parameterName)
    {
        var fullRoot = Path.GetFullPath(applicationDataRoot);
        EnsureExistingPathIsNotRedirected(
            Path.Combine(fullRoot, "unpwn"),
            parameterName);
        EnsureExistingPathIsNotRedirected(
            Path.Combine(fullRoot, "unpwn", "recovery-browser"),
            parameterName);
        EnsureExistingPathIsNotRedirected(
            GetOwnedProfilesRoot(fullRoot),
            parameterName);
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
