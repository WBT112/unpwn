using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using Unpwn.App.Services;
using Unpwn.Application.Recovery;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class RecoveryBrowserCredentialAssistanceTests
{
    [Fact]
    public async Task SyntheticReviewedContractInsertsIntoExpectedFieldsWithoutSubmitting()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            using var host = new AvaloniaRecoveryBrowserHost(
                webView,
                _ => new TestPlatformAdapter());
            var handoff = Handoff(new Uri("http://127.0.0.1:43301/password-change"));
            var request = Request(handoff);
            Assert.True(host.Start(request));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            await InstallExpectedPasswordPageAsync(webView);
            Assert.True(RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance.TryResolve(
                "synthetic",
                "change-password",
                isReviewedProviderWorkflow: false,
                handoff,
                RecoveryBrowserContentMode.SyntheticTest,
                out var contract));

            var inspection = await host.InspectCredentialInsertionAsync(
                contract!,
                CancellationToken.None);
            Assert.Equal(
                RecoveryBrowserCredentialAssistanceState.ReadyForAuthorization,
                inspection.State);

            byte[] secret = [65, 66, 67, 33];
            var inserted = await host.InsertCredentialAsync(
                contract!,
                secret,
                CancellationToken.None);

            Assert.True(inserted.Succeeded);
            var firstLength = await webView.InvokeScript(
                "document.querySelector('[data-testid=\"new-password\"]').value.length.toString()");
            var secondLength = await webView.InvokeScript(
                "document.querySelector('[data-testid=\"confirm-password\"]').value.length.toString()");
            var submitted = await webView.InvokeScript(
                "document.querySelectorAll('[data-unpwn-outcome=\"submitted\"]').length.toString()");
            Assert.Contains("4", firstLength, StringComparison.Ordinal);
            Assert.Contains("4", secondLength, StringComparison.Ordinal);
            Assert.Contains("0", submitted, StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("mfa", RecoveryBrowserCredentialAssistanceState.PausedForMfa)]
    [InlineData("captcha", RecoveryBrowserCredentialAssistanceState.PausedForCaptcha)]
    [InlineData("email-link", RecoveryBrowserCredentialAssistanceState.PausedForEmailLink)]
    public async Task StopMarkersPreventSecretInsertionBeforeVaultHandoff(
        string stopReason,
        RecoveryBrowserCredentialAssistanceState expected)
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            using var host = new AvaloniaRecoveryBrowserHost(
                webView,
                _ => new TestPlatformAdapter());
            var handoff = Handoff(new Uri("http://127.0.0.1:43302/password-change"));
            Assert.True(host.Start(Request(handoff)));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            await InstallExpectedPasswordPageAsync(webView);
            await webView.InvokeScript(
                $"document.body.insertAdjacentHTML('beforeend', '<div data-unpwn-stop-reason=\"{stopReason}\"></div>')");
            Assert.True(RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance.TryResolve(
                "synthetic",
                "change-password",
                isReviewedProviderWorkflow: false,
                handoff,
                RecoveryBrowserContentMode.SyntheticTest,
                out var contract));

            var result = await host.InspectCredentialInsertionAsync(
                contract!,
                CancellationToken.None);

            Assert.Equal(expected, result.State);
            Assert.False(result.Succeeded);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChangedPageStopsInsteadOfGuessingPasswordFields()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            using var host = new AvaloniaRecoveryBrowserHost(
                webView,
                _ => new TestPlatformAdapter());
            var handoff = Handoff(new Uri("http://127.0.0.1:43303/password-change"));
            Assert.True(host.Start(Request(handoff)));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            await webView.InvokeScript(
                "document.body.setAttribute('data-unpwn-provider','synthetic');" +
                "document.body.setAttribute('data-unpwn-workflow','password-change');" +
                "document.body.innerHTML='<input data-testid=\"new-password\">';");
            Assert.True(RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance.TryResolve(
                "synthetic",
                "change-password",
                isReviewedProviderWorkflow: false,
                handoff,
                RecoveryBrowserContentMode.SyntheticTest,
                out var contract));

            var result = await host.InspectCredentialInsertionAsync(
                contract!,
                CancellationToken.None);

            Assert.Equal(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                result.State);
            Assert.Equal(
                RecoveryBrowserCredentialAssistanceFailureCode.UnexpectedContent,
                result.FailureCode);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ContractOriginMustStillMatchCurrentReviewedOrigin()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var webView = new NativeWebView();
            using var host = new AvaloniaRecoveryBrowserHost(
                webView,
                _ => new TestPlatformAdapter());
            var handoff = Handoff(new Uri("http://127.0.0.1:43304/password-change"));
            Assert.True(host.Start(Request(handoff)));
            var window = new Window { Content = webView };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            await InstallExpectedPasswordPageAsync(webView);

            var wrongOriginContract = new RecoveryBrowserCredentialInsertionContract(
                "synthetic",
                "change-password",
                RecoveryBrowserContentMode.SyntheticTest,
                ["http://127.0.0.1:49999"],
                "body[data-unpwn-provider='synthetic'][data-unpwn-workflow='password-change']",
                "[data-testid='new-password']",
                "[data-testid='confirm-password']",
                "[data-unpwn-stop-reason='mfa']",
                "[data-unpwn-stop-reason='captcha']",
                "[data-unpwn-stop-reason='email-link']");

            var result = await host.InspectCredentialInsertionAsync(
                wrongOriginContract,
                CancellationToken.None);

            Assert.Equal(
                RecoveryBrowserCredentialAssistanceFailureCode.WrongOrigin,
                result.FailureCode);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void CatalogNeverOffersGenericProductionDomInsertion()
    {
        var handoff = new RecoveryNavigationHandoff(
            new Uri("https://unsupported.example.test/account"),
            "https://unsupported.example.test",
            ["https://unsupported.example.test"],
            RecoveryLocationResolutionSource.ProviderDefined,
            RequiresVisibleConfirmation: true);

        Assert.False(RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance.TryResolve(
            "unsupported.example",
            "change-password",
            isReviewedProviderWorkflow: false,
            handoff,
            RecoveryBrowserContentMode.Recovery,
            out var contract));
        Assert.Null(contract);
    }

    [Theory]
    [InlineData("change-password")]
    [InlineData("reset-password")]
    public void CatalogOffersOnlyExplicitSyntheticPasswordActions(string actionDefinitionId)
    {
        var handoff = Handoff(new Uri("http://127.0.0.1:43305/password-change"));

        Assert.True(RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance.TryResolve(
            "synthetic",
            actionDefinitionId,
            isReviewedProviderWorkflow: false,
            handoff,
            RecoveryBrowserContentMode.SyntheticTest,
            out var contract));
        Assert.Equal(actionDefinitionId, contract!.ActionDefinitionId);
    }

    private static async Task InstallExpectedPasswordPageAsync(NativeWebView webView) =>
        await webView.InvokeScript(
            "document.body.setAttribute('data-unpwn-provider','synthetic');" +
            "document.body.setAttribute('data-unpwn-workflow','password-change');" +
            "document.body.innerHTML='<input data-testid=\"new-password\"><input data-testid=\"confirm-password\"><button data-testid=\"submit-password-change\"></button>';");

    private static RecoveryBrowserHostRequest Request(RecoveryNavigationHandoff handoff) => new(
        handoff,
        RecoveryBrowserContentMode.SyntheticTest,
        RecoveryBrowserProfilePath.CreateOwnedProfileRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    private static RecoveryNavigationHandoff Handoff(Uri destination)
    {
        var origin = destination.GetLeftPart(UriPartial.Authority);
        return new RecoveryNavigationHandoff(
            destination,
            origin,
            [origin],
            RecoveryLocationResolutionSource.ProviderDefined,
            RequiresVisibleConfirmation: true);
    }

    private sealed class TestPlatformAdapter : IRecoveryBrowserPlatformAdapter
    {
        public event EventHandler<RecoveryBrowserSecurityEventCode>? SecurityEvent
        {
            add { }
            remove { }
        }

        public bool IsConfigured { get; private set; }

        public void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args) =>
            args.EnableDevTools = false;

        public void Attach(IPlatformHandle? platformHandle) =>
            IsConfigured = platformHandle is not null;

        public Task ClearBrowsingDataAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() => IsConfigured = false;
    }
}
