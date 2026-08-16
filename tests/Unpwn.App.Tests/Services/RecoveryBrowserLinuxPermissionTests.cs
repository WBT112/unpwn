using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserLinuxPermissionTests
{
    [Fact]
    public void NewSessionUsesOwnerOnlyDirectoriesAndMetadataOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            using var lifecycle = new RecoveryBrowserSessionLifecycle(root);

            var result = lifecycle.Start(Guid.NewGuid());

            Assert.True(result.Succeeded);
            var session = Assert.IsType<RecoveryBrowserSession>(result.Session);
            var recoveryBrowserRoot = Path.Combine(root, "unpwn", "recovery-browser");
            var profilesRoot = RecoveryBrowserProfilePath.GetOwnedProfilesRoot(root);
            var marker = Path.Combine(session.ProfileDataPath, ".unpwn-session");
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(recoveryBrowserRoot));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(profilesRoot));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(session.ProfileDataPath));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateFileMode,
                File.GetUnixFileMode(marker));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExistingOwnedProfilePermissionsAreTightenedBeforeOrphanUseOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = CreateRoot();
        var sessionId = Guid.NewGuid();
        var profile = RecoveryBrowserProfilePath.GetOwnedProfileRoot(root, sessionId);
        var recoveryBrowserRoot = Path.Combine(root, "unpwn", "recovery-browser");
        var profilesRoot = RecoveryBrowserProfilePath.GetOwnedProfilesRoot(root);
        var marker = Path.Combine(profile, ".unpwn-session");
        try
        {
            Directory.CreateDirectory(profile);
            File.WriteAllText(marker, "version=1\nstate=active\n");
            File.SetUnixFileMode(recoveryBrowserRoot, BroadDirectoryMode);
            File.SetUnixFileMode(profilesRoot, BroadDirectoryMode);
            File.SetUnixFileMode(profile, BroadDirectoryMode);
            File.SetUnixFileMode(marker, BroadFileMode);

            using var lifecycle = new RecoveryBrowserSessionLifecycle(root);
            var orphan = Assert.Single(lifecycle.Current.OrphanedSessions);

            Assert.Equal(sessionId, orphan.SessionId);
            Assert.Equal(
                RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
                lifecycle.Current.State);
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(recoveryBrowserRoot));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(profilesRoot));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(profile));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateFileMode,
                File.GetUnixFileMode(marker));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void RedirectedOwnedProfileFailsClosedAsStorageUnavailableOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = CreateRoot();
        var outside = CreateRoot();
        var sessionId = Guid.NewGuid();
        var profile = RecoveryBrowserProfilePath.GetOwnedProfileRoot(root, sessionId);
        try
        {
            Directory.CreateDirectory(RecoveryBrowserProfilePath.GetOwnedProfilesRoot(root));
            Directory.CreateSymbolicLink(profile, outside);

            using var lifecycle = new RecoveryBrowserSessionLifecycle(root);

            Assert.Equal(
                RecoveryBrowserSessionLifecycleState.CleanupFailed,
                lifecycle.Current.State);
            Assert.Equal(
                RecoveryBrowserSessionFailureCode.StorageUnavailable,
                lifecycle.Current.FailureCode);
            Assert.True(lifecycle.Current.HasUncleanSessionData);
            Assert.Equal(
                RecoveryBrowserSessionFailureCode.StorageUnavailable,
                lifecycle.Start(Guid.NewGuid()).FailureCode);
        }
        finally
        {
            if (Directory.Exists(profile) || File.Exists(profile))
            {
                Directory.Delete(profile);
            }
            DeleteRoot(root);
            DeleteRoot(outside);
        }
    }

    [Fact]
    public void CleanupPendingMarkerReplacementRemainsPrivateOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var storage = new FileRecoveryBrowserSessionStorage(root);
            var session = storage.Create(Guid.NewGuid());
            var marker = Path.Combine(session.ProfileDataPath, ".unpwn-session");
            File.SetUnixFileMode(marker, BroadFileMode);

            storage.MarkCleanupPending(session);

            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateFileMode,
                File.GetUnixFileMode(marker));
            Assert.Equal(
                "version=1\nstate=cleanup-pending\n",
                File.ReadAllText(marker));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-browser-permissions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
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

    private static UnixFileMode BroadFileMode =>
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherWrite;
}
