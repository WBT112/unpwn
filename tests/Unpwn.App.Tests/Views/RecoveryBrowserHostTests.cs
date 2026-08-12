using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unpwn.App.Services;
using Unpwn.App.Views;
using Unpwn.Application.Recovery;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class RecoveryBrowserHostTests
{
    [Fact]
    public async Task SyntheticProviderRendersInsideDesktopHostWithVisibleOrigin()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            var platform = new TestPlatformAdapter();
            using var host = new AvaloniaRecoveryBrowserHost(webView, _ => platform);
            var destination = new Uri("http://127.0.0.1:43217/password-change");
            var request = Request(destination, RecoveryBrowserContentMode.SyntheticTest);

            Assert.True(host.Start(request));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            Assert.True(platform.EnvironmentConfigured);
            Assert.True(platform.IsConfigured);
            Assert.Equal(destination, webView.Source);
            Assert.Equal("http://127.0.0.1:43217", host.Snapshot.VisibleOrigin);
            Assert.Equal(RecoveryBrowserHostState.Ready, host.Snapshot.State);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryBrowserSurfaceContainsSyntheticProviderAndRequiredControls()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var platform = new TestPlatformAdapter();
            using var view = new RecoveryBrowserView(_ => platform);
            var destination = new Uri("http://127.0.0.1:43218/password-change");
            Assert.True(view.Start(Request(
                destination,
                RecoveryBrowserContentMode.SyntheticTest)));
            var window = new Window { Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(view.GetVisualDescendants().OfType<NativeWebView>());
            Assert.Equal(destination, view.Snapshot?.Source);
            string[] controlIds =
            [
                "recovery-browser-back",
                "recovery-browser-forward",
                "recovery-browser-reload",
                "recovery-browser-stop",
                "recovery-browser-close",
                "recovery-browser-origin",
            ];
            var automationIds = view.GetVisualDescendants()
                .OfType<Control>()
                .Select(Avalonia.Automation.AutomationProperties.GetAutomationId)
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(controlIds, id => Assert.Contains(id, automationIds));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnexpectedOriginAndNewWindowFailClosed()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            using var host = new AvaloniaRecoveryBrowserHost(
                webView,
                _ => new TestPlatformAdapter());
            Assert.True(host.Start(Request(
                new Uri("https://accounts.example.test/recovery"),
                RecoveryBrowserContentMode.Recovery)));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);

            Assert.False(host.Navigate(new Uri("https://attacker.example/recovery")));
            Assert.Equal(
                RecoveryBrowserSecurityEventCode.UnexpectedOriginBlocked,
                host.Snapshot.LastSecurityEvent);

            await webView.InvokeScript("window.open('https://accounts.example.test/popup')");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                RecoveryBrowserSecurityEventCode.PopupBlocked,
                host.Snapshot.LastSecurityEvent);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RecoveryBrowserSecurityEventCode.DownloadBlocked)]
    [InlineData(RecoveryBrowserSecurityEventCode.PermissionBlocked)]
    [InlineData(RecoveryBrowserSecurityEventCode.ExternalProtocolBlocked)]
    [InlineData(RecoveryBrowserSecurityEventCode.TlsErrorBlocked)]
    public async Task PlatformSecurityDecisionsAreVisible(
        RecoveryBrowserSecurityEventCode securityEvent)
    {
        await AccessibilityHeadlessTests.Session.Dispatch(() =>
        {
            var webView = new NativeWebView();
            var platform = new TestPlatformAdapter();
            using var host = new AvaloniaRecoveryBrowserHost(webView, _ => platform);
            Assert.True(host.Start(Request(
                new Uri("https://accounts.example.test/recovery"),
                RecoveryBrowserContentMode.Recovery)));

            platform.Publish(securityEvent);

            Assert.Equal(securityEvent, host.Snapshot.LastSecurityEvent);
        }, CancellationToken.None);
    }

    [Fact]
    public void ProfilePathsAreOpaqueAndConstrainedToUnpwnOwnedStorage()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var first = RecoveryBrowserProfilePath.CreateOwnedProfileRoot(applicationData);
        var second = RecoveryBrowserProfilePath.CreateOwnedProfileRoot(applicationData);

        RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(first, applicationData);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("example.test", first, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() =>
            RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
                Path.Combine(applicationData, "normal-browser-profile"),
                applicationData));
        Assert.Throws<ArgumentException>(() =>
            RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
                Path.Combine(
                    applicationData,
                    "unpwn",
                    "recovery-browser",
                    "profiles",
                    "account@example.test"),
                applicationData));
    }

    [Fact]
    public void ProfilePathsRejectRedirectedOwnedStorage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-browser-profile-test-{Guid.NewGuid():N}");
        var redirectedTarget = Path.Combine(testRoot, "redirect-target");
        var unpwnRoot = Path.Combine(testRoot, "app-data", "unpwn");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(unpwnRoot)!);
            Directory.CreateDirectory(redirectedTarget);
            Directory.CreateSymbolicLink(unpwnRoot, redirectedTarget);
            var candidate = Path.Combine(
                unpwnRoot,
                "recovery-browser",
                "profiles",
                Guid.NewGuid().ToString("N"));

            Assert.Throws<ArgumentException>(() =>
                RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
                    candidate,
                    Path.Combine(testRoot, "app-data")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void PlatformEnvironmentAdaptersUseDedicatedStorageAndDisableSharedIdentityFeatures()
    {
        var profilePath = Path.Combine(
            Path.GetTempPath(),
            "unpwn-browser-adapter-test",
            Guid.NewGuid().ToString("N"));
        try
        {
            using var windows = new WindowsRecoveryBrowserPlatformAdapter(profilePath);
            var windowsArgs = (WindowsWebView2EnvironmentRequestedEventArgs)
                RuntimeHelpers.GetUninitializedObject(
                    typeof(WindowsWebView2EnvironmentRequestedEventArgs));
            windows.ConfigureEnvironment(windowsArgs);
            Assert.Equal(profilePath, windowsArgs.UserDataFolder);
            Assert.Equal("Recovery", windowsArgs.ProfileName);
            Assert.False(windowsArgs.AllowSingleSignOnUsingOSPrimaryAccount);
            Assert.False(windowsArgs.EnableDevTools);

            using var linux = new LinuxRecoveryBrowserPlatformAdapter(profilePath);
            var linuxArgs = (LinuxWpeWebViewEnvironmentRequestedEventArgs)
                RuntimeHelpers.GetUninitializedObject(
                    typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs));
            linux.ConfigureEnvironment(linuxArgs);
            Assert.Equal(Path.Combine(profilePath, "data"), linuxArgs.DataDirectory);
            Assert.Equal(Path.Combine(profilePath, "cache"), linuxArgs.CacheDirectory);
            Assert.False(linuxArgs.PreferWebKitGtkInstead);
            Assert.False(linuxArgs.EnableDevTools);
        }
        finally
        {
            if (Directory.Exists(profilePath))
            {
                Directory.Delete(profilePath, recursive: true);
            }
        }
    }

    private static RecoveryBrowserHostRequest Request(
        Uri destination,
        RecoveryBrowserContentMode mode)
    {
        var origin = destination.GetLeftPart(UriPartial.Authority);
        return new RecoveryBrowserHostRequest(
            new RecoveryNavigationHandoff(
                destination,
                origin,
                [origin],
                RecoveryLocationResolutionSource.ProviderDefined,
                RequiresVisibleConfirmation: true),
            mode,
            RecoveryBrowserProfilePath.CreateOwnedProfileRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    }

    private sealed class TestPlatformAdapter : IRecoveryBrowserPlatformAdapter
    {
        public event EventHandler<RecoveryBrowserSecurityEventCode>? SecurityEvent;

        public bool IsConfigured { get; private set; }

        public bool EnvironmentConfigured { get; private set; }

        public void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
        {
            EnvironmentConfigured = true;
            args.EnableDevTools = false;
        }

        public void Attach(IPlatformHandle? platformHandle) =>
            IsConfigured = platformHandle is not null;

        public void Publish(RecoveryBrowserSecurityEventCode code) =>
            SecurityEvent?.Invoke(this, code);

        public void Dispose() => IsConfigured = false;
    }
}
