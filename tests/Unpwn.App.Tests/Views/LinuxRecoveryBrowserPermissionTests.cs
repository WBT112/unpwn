using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class LinuxRecoveryBrowserPermissionTests
{
    [Fact]
    public void WpeEnvironmentTightensProfileDataAndCacheDirectoriesOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var profilePath = CreateProfilePath();
        var dataPath = Path.Combine(profilePath, "data");
        var cachePath = Path.Combine(profilePath, "cache");
        try
        {
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(cachePath);
            File.SetUnixFileMode(profilePath, BroadDirectoryMode);
            File.SetUnixFileMode(dataPath, BroadDirectoryMode);
            File.SetUnixFileMode(cachePath, BroadDirectoryMode);
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var args = CreateWpeEnvironmentArgs();

            adapter.ConfigureEnvironment(args);

            Assert.Equal(dataPath, args.DataDirectory);
            Assert.Equal(cachePath, args.CacheDirectory);
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(profilePath));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(dataPath));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(cachePath));
        }
        finally
        {
            DeletePath(profilePath);
        }
    }

    [Fact]
    public void GtkEnvironmentTightensOwnedProfileEvenThoughWebsiteDataIsEphemeralOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var profilePath = CreateProfilePath();
        try
        {
            File.SetUnixFileMode(profilePath, BroadDirectoryMode);
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var args = (GtkWebViewEnvironmentRequestedEventArgs)
                RuntimeHelpers.GetUninitializedObject(
                    typeof(GtkWebViewEnvironmentRequestedEventArgs));

            adapter.ConfigureEnvironment(args);

            Assert.True(args.EphemeralDataManager);
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(profilePath));
        }
        finally
        {
            DeletePath(profilePath);
        }
    }

    [Fact]
    public void RedirectedWpeDataDirectoryFailsClosedWithoutAssigningBrowserStorageOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var profilePath = CreateProfilePath();
        var outsidePath = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-browser-outside-{Guid.NewGuid():N}");
        var dataPath = Path.Combine(profilePath, "data");
        try
        {
            Directory.CreateDirectory(outsidePath);
            Directory.CreateSymbolicLink(dataPath, outsidePath);
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var securityEvents = new List<RecoveryBrowserSecurityEventCode>();
            adapter.SecurityEvent += (_, code) => securityEvents.Add(code);
            var args = CreateWpeEnvironmentArgs();

            adapter.ConfigureEnvironment(args);

            Assert.Contains(
                RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable,
                securityEvents);
            Assert.Null(args.DataDirectory);
            Assert.Null(args.CacheDirectory);
            Assert.False(adapter.IsConfigured);
            Assert.Equal(LinuxRecoveryBrowserBackend.None, adapter.Backend);
        }
        finally
        {
            if (Directory.Exists(dataPath) || File.Exists(dataPath))
            {
                Directory.Delete(dataPath);
            }
            DeletePath(profilePath);
            DeletePath(outsidePath);
        }
    }

    private static LinuxWpeWebViewEnvironmentRequestedEventArgs CreateWpeEnvironmentArgs() =>
        (LinuxWpeWebViewEnvironmentRequestedEventArgs)
            RuntimeHelpers.GetUninitializedObject(
                typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs));

    private static string CreateProfilePath()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "unpwn-linux-browser-permission-test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static UnixFileMode BroadDirectoryMode =>
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;
}
