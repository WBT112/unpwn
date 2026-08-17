using Avalonia.Controls;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class LinuxRecoveryBrowserPlatformAdapterTests
{
    [Fact]
    public void GtkEnvironmentUsesEphemeralOffscreenX11InitializationBridge()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"unpwn-linux-browser-{Guid.NewGuid():N}");
        try
        {
            using var adapter = new LinuxRecoveryBrowserPlatformAdapter(root);
            var args = GtkEnvironmentArgsFactory.Create();

            adapter.ConfigureEnvironment(args);

            Assert.True(args.EphemeralDataManager);
            Assert.True(args.DisableCache);
            Assert.True(args.ExperimentalOffscreen);
            Assert.True(args.ForceX11GdkBackend);
            Assert.False(args.EnableDevTools);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static class GtkEnvironmentArgsFactory
    {
        public static GtkWebViewEnvironmentRequestedEventArgs Create()
        {
            var ctor = typeof(GtkWebViewEnvironmentRequestedEventArgs)
                .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Single();
            var parameter = ctor.GetParameters().Single();
            var deferralManager = Activator.CreateInstance(parameter.ParameterType, nonPublic: true)
                ?? throw new InvalidOperationException("Could not create WebView deferral manager.");
            return (GtkWebViewEnvironmentRequestedEventArgs)ctor.Invoke([deferralManager]);
        }
    }
}
