using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Unpwn.App.Services;
using Unpwn.App.Views;
using Unpwn.Application.Recovery;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class RecoveryBrowserStartupTests
{
    [Fact]
    public async Task ConstructingManagedBrowserDoesNotRequireXamlServiceProviderForDynamicResources()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var root = CreateRoot();
            try
            {
                using var lifecycle = new RecoveryBrowserSessionLifecycle(root);
                var platform = new StartupPlatformAdapter(configureOnAttach: true);
                using var view = new RecoveryBrowserView(lifecycle, _ => platform);

                Assert.NotNull(view);
                Assert.Equal(
                    RecoveryBrowserSessionLifecycleState.Idle,
                    view.SessionSnapshot.State);
            }
            finally
            {
                DeleteRoot(root);
            }

            await Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AttachedManagedBrowserReportsSuccessOnlyAfterPlatformHardeningIsReady()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var root = CreateRoot();
            try
            {
                using var lifecycle = new RecoveryBrowserSessionLifecycle(root);
                var platform = new StartupPlatformAdapter(configureOnAttach: true);
                using var view = new RecoveryBrowserView(lifecycle, _ => platform);
                var window = new Window { Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var started = await view.StartAsync(Request());
                Dispatcher.UIThread.RunJobs();

                Assert.True(started);
                Assert.True(platform.EnvironmentConfigured);
                Assert.True(platform.IsConfigured);
                Assert.Equal(
                    RecoveryBrowserSessionLifecycleState.Active,
                    lifecycle.Current.State);

                Assert.True(await view.CloseSessionAsync());
                window.Close();
            }
            finally
            {
                DeleteRoot(root);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AttachedManagedBrowserFailsClosedWhenPlatformHardeningCannotBeEstablished()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var root = CreateRoot();
            try
            {
                using var lifecycle = new RecoveryBrowserSessionLifecycle(root);
                var platform = new StartupPlatformAdapter(configureOnAttach: false);
                using var view = new RecoveryBrowserView(lifecycle, _ => platform);
                var window = new Window { Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var started = await view.StartAsync(Request());
                Dispatcher.UIThread.RunJobs();

                Assert.False(started);
                Assert.True(platform.EnvironmentConfigured);
                Assert.False(platform.IsConfigured);
                Assert.Equal(
                    RecoveryBrowserSessionLifecycleState.Idle,
                    lifecycle.Current.State);
                Assert.Null(lifecycle.Current.ActiveSession);
                window.Close();
            }
            finally
            {
                DeleteRoot(root);
            }
        }, CancellationToken.None);
    }

    private static RecoveryBrowserSessionStartRequest Request()
    {
        var destination = new Uri("http://127.0.0.1:43230/password-change");
        var origin = destination.GetLeftPart(UriPartial.Authority);
        return new RecoveryBrowserSessionStartRequest(
            Guid.NewGuid(),
            new RecoveryNavigationHandoff(
                destination,
                origin,
                [origin],
                RecoveryLocationResolutionSource.ProviderDefined,
                RequiresVisibleConfirmation: true),
            RecoveryBrowserContentMode.SyntheticTest);
    }

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(),
        $"unpwn-browser-start-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StartupPlatformAdapter(bool configureOnAttach)
        : IRecoveryBrowserPlatformAdapter
    {
        public event EventHandler<RecoveryBrowserSecurityEventCode>? SecurityEvent
        {
            add { }
            remove { }
        }

        public bool EnvironmentConfigured { get; private set; }

        public bool IsConfigured { get; private set; }

        public void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
        {
            args.EnableDevTools = false;
            EnvironmentConfigured = true;
        }

        public void Attach(IPlatformHandle? platformHandle)
        {
            IsConfigured = configureOnAttach && platformHandle is not null;
        }

        public Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose()
        {
            IsConfigured = false;
        }
    }
}
