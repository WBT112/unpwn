using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserPermissionSecurityRegressionTests
{
    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void LinuxSessionProfileAndMarkerRemainOwnerOnly()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-security-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var lifecycle = new RecoveryBrowserSessionLifecycle(root);

            var result = lifecycle.Start(Guid.NewGuid());

            Assert.True(result.Succeeded);
            var session = Assert.IsType<RecoveryBrowserSession>(result.Session);
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateDirectoryMode,
                File.GetUnixFileMode(session.ProfileDataPath));
            Assert.Equal(
                RecoveryBrowserFilePermissions.PrivateFileMode,
                File.GetUnixFileMode(Path.Combine(session.ProfileDataPath, ".unpwn-session")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
