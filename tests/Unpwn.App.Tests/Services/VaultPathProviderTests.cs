using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class VaultPathProviderTests
{
    [Fact]
    public void WindowsPolicyUsesUserLocalApplicationData()
    {
        using var directory = new TemporaryDirectory();
        var localData = Path.Combine(directory.Path, "local-data");
        var xdgData = Path.Combine(directory.Path, "xdg-data");

        var result = VaultPathPolicy.ResolveLocalDataRoot(
            isWindows: true,
            localData,
            xdgData,
            directory.Path);

        Assert.Equal(Path.GetFullPath(localData), result);
    }

    [Fact]
    public void LinuxPolicyPrefersAbsoluteXdgDataHome()
    {
        using var directory = new TemporaryDirectory();
        var localData = Path.Combine(directory.Path, "local-data");
        var xdgData = Path.Combine(directory.Path, "xdg-data");

        var result = VaultPathPolicy.ResolveLocalDataRoot(
            isWindows: false,
            localData,
            xdgData,
            directory.Path);

        Assert.Equal(Path.GetFullPath(xdgData), result);
    }

    [Fact]
    public void LinuxPolicyFallsBackToLocalApplicationDataThenUserProfile()
    {
        using var directory = new TemporaryDirectory();
        var localData = Path.Combine(directory.Path, "local-data");

        Assert.Equal(
            Path.GetFullPath(localData),
            VaultPathPolicy.ResolveLocalDataRoot(
                isWindows: false,
                localData,
                xdgDataHome: null,
                directory.Path));
        Assert.Equal(
            Path.Combine(Path.GetFullPath(directory.Path), ".local", "share"),
            VaultPathPolicy.ResolveLocalDataRoot(
                isWindows: false,
                localApplicationData: null,
                xdgDataHome: null,
                directory.Path));
    }

    [Fact]
    public void ProviderCreatesApplicationDirectoryAndChoosesNonExistingReadableFilename()
    {
        using var directory = new TemporaryDirectory();
        var provider = new PlatformVaultPathProvider(directory.Path);

        var first = provider.GetNextDefaultVaultPath();

        Assert.Equal(
            Path.Combine(directory.Path, "unpwn", "vaults", "unpwn-recovery.db"),
            first);
        Assert.True(Directory.Exists(Path.GetDirectoryName(first)));

        File.WriteAllText(first, "synthetic occupied candidate");
        var second = provider.GetNextDefaultVaultPath();

        Assert.Equal(
            Path.Combine(directory.Path, "unpwn", "vaults", "unpwn-recovery-2.db"),
            second);
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void UnixProviderRestrictsCreatedVaultDirectoryToCurrentUser()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var provider = new PlatformVaultPathProvider(directory.Path);
        var path = provider.GetNextDefaultVaultPath();
        var mode = File.GetUnixFileMode(Path.GetDirectoryName(path)!);
        var forbidden = UnixFileMode.GroupRead |
                        UnixFileMode.GroupWrite |
                        UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead |
                        UnixFileMode.OtherWrite |
                        UnixFileMode.OtherExecute;

        Assert.Equal(0, (int)(mode & forbidden));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unpwn-vault-path-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
