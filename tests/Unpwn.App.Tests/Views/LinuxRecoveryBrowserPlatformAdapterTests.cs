using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class LinuxRecoveryBrowserPlatformAdapterTests
{
    [Fact]
    public void WpeEnvironmentUsesOwnedProfileAndKeepsAutomaticGtkFallbackAvailable()
    {
        var profilePath = CreateProfilePath();
        try
        {
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var args = (LinuxWpeWebViewEnvironmentRequestedEventArgs)
                RuntimeHelpers.GetUninitializedObject(
                    typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs));

            adapter.ConfigureEnvironment(args);

            Assert.Equal(Path.Combine(profilePath, "data"), args.DataDirectory);
            Assert.Equal(Path.Combine(profilePath, "cache"), args.CacheDirectory);
            Assert.False(args.PreferWebKitGtkInstead);
            Assert.False(args.EnableDevTools);
        }
        finally
        {
            DeleteProfilePath(profilePath);
        }
    }

    [Fact]
    public void GtkEnvironmentIsEphemeralAndDisablesDiskCacheAndDeveloperTools()
    {
        var profilePath = CreateProfilePath();
        try
        {
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var args = (GtkWebViewEnvironmentRequestedEventArgs)
                RuntimeHelpers.GetUninitializedObject(
                    typeof(GtkWebViewEnvironmentRequestedEventArgs));

            adapter.ConfigureEnvironment(args);

            Assert.True(args.EphemeralDataManager);
            Assert.True(args.DisableCache);
            Assert.False(args.EnableDevTools);
            Assert.Null(args.BaseDataDirectory);
            Assert.Null(args.BaseCacheDirectory);
        }
        finally
        {
            DeleteProfilePath(profilePath);
        }
    }

    [Fact]
    public void GtkHandleTypeIsRecognizedWithoutTreatingItAsWpe()
    {
        using var adapter = new LinuxRecoveryBrowserPlatformAdapter(CreateProfilePath());
        var handle = new TestGtkHandle(IntPtr.Zero);

        adapter.Attach(handle);

        Assert.False(adapter.IsConfigured);
        Assert.Equal(LinuxRecoveryBrowserBackend.None, adapter.Backend);
    }

    private static string CreateProfilePath() => Path.Combine(
        Path.GetTempPath(),
        "unpwn-linux-browser-adapter-test",
        Guid.NewGuid().ToString("N"));

    private static void DeleteProfilePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class TestGtkHandle(IntPtr webKitWebView) : IGtkWebViewPlatformHandle
    {
        public IntPtr WebKitWebView { get; } = webKitWebView;

        public IntPtr Handle => WebKitWebView;

        public string HandleDescriptor => "WebKitWebView";
    }
}
