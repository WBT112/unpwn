using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.App.Tests.Presentation;
using Unpwn.App.Views;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class PostImportNavigationHeadlessTests
{
    private const string VaultPassword = "UNPWN_TEST_SECRET_post-import-navigation";

    [Fact]
    public async Task SuccessfulReviewedImportAutomaticallyOpensRenderedAccountsWorkspaceOnLinux()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            using var directory = new TemporaryDirectory();
            var time = DateTimeOffset.UnixEpoch;
            var wizard = new RecoveryWizardSessionService(time);
            wizard.BeginTrustedDeviceCheck(time);
            wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, time);
            using var mutations = new WorkspaceMutationCoordinator();
            using var vault = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
                wizard,
                clock: () => time);
            Assert.True((await vault.CreateAsync(
                Path.Combine(directory.Path, "recovery.sqlite"),
                VaultPassword,
                CancellationToken.None)).Succeeded);

            using var session = new RecoverySessionService(vault, vault, () => time, mutations);
            using var inventory = new AccountInventoryService(vault, session, () => time, mutations);
            await session.InitializeAsync(CancellationToken.None);
            Assert.True((await session.CreateAsync(
                new RecoverySessionCreateRequest(
                    "Synthetic recovery",
                    IncidentIndicator.None,
                    SecurityWarningAcknowledged: true),
                CancellationToken.None)).Succeeded);
            await inventory.InitializeAsync(CancellationToken.None);
            using var flow = new RecoveryFlowService(
                vault,
                wizard,
                session,
                inventory,
                mutations,
                () => time);
            var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
            var confirmation = new RejectingConfirmationService();
            var import = new CsvImportScreenViewModel(inventory, localization, flow);
            var accounts = new AccountInventoryScreenViewModel(
                inventory,
                confirmation,
                localization,
                flow);
            var shell = new ShellViewModel(
                new TestScreenFactory(localization, import, accounts),
                vault,
                session,
                inventory,
                localization,
                flow);
            var window = new global::Unpwn.App.MainWindow { DataContext = shell };
            window.Show();
            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);

            var importView = Assert.Single(window.GetLogicalDescendants().OfType<CsvImportView>());
            const string csv =
                "name,url,username,password,account_name\n" +
                "Example Mail,https://mail.example.test/login,alex@example.test,old-password-not-real,Primary email\n" +
                "Example Shop,https://shop.example.test/account,alex@example.test,another-fake-password,Shopping\n" +
                "\"Müller, Demo GmbH\",https://portal.example.test/login,demo.user@example.test,synthetic-only,Business portal\n" +
                "Example Mail,https://mail.example.test/settings,alex@example.test,duplicate-fake-value,Duplicate candidate\n";
            await importView.LoadCsvAsync(
                "unpwn-sample.csv",
                () => Task.FromResult<Stream>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv), writable: false)));
            var importButton = Assert.IsType<Button>(FindByAutomationId(importView, "import-reviewed"), exactMatch: false);
            importButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => shell.CurrentScreen.Route == AppRoute.Accounts);
            Dispatcher.UIThread.RunJobs();

            var accountsView = Assert.Single(
                window.GetLogicalDescendants().OfType<AccountsView>());
            var list = Assert.IsType<ListBox>(
                accountsView.GetLogicalDescendants()
                    .OfType<Control>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == "accounts-triage-list"),
                exactMatch: false);
            Assert.Equal(3, list.Items.Count);
            Assert.Equal(3, inventory.CurrentInventory!.Accounts.Length);
            Assert.Equal(RecoveryWizardStepId.AccountTriage, flow.Current.CurrentStep);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedReviewedImportRemainsInImportWorkspaceWithoutAdvancingFlow()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            var sessionId = Guid.NewGuid();
            var vault = new ShellViewModelTests.TestVaultLifecycleService();
            vault.Unlock("Synthetic vault", "Synthetic recovery");
            var session = new ShellViewModelTests.TestRecoverySessionService();
            session.SetSession(RecoverySessionWorkspace.Create(
                sessionId,
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch));
            var inventory = new ShellViewModelTests.TestAccountInventoryService();
            inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch));
            var flow = new ShellViewModelTests.TestRecoveryFlowService(
                ShellViewModelTests.WizardAt(RecoveryWizardStepId.AccountInventory),
                new NextUserTask(
                    RecoveryWizardStepId.AccountInventory,
                    NextUserTaskState.ActionAvailable,
                    NextUserTaskCode.ImportAccounts,
                    NextUserTaskTarget.CsvImport));
            var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
            var import = new CsvImportScreenViewModel(inventory, localization, flow);
            var accounts = new AccountInventoryScreenViewModel(
                inventory,
                new RejectingConfirmationService(),
                localization,
                flow);
            var shell = new ShellViewModel(
                new TestScreenFactory(localization, import, accounts),
                vault,
                session,
                inventory,
                localization,
                flow);
            var window = new global::Unpwn.App.MainWindow { DataContext = shell };
            window.Show();
            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);

            var importView = Assert.Single(window.GetLogicalDescendants().OfType<CsvImportView>());
            const string csv = "service,username\nExample,person@example.invalid\n";
            await importView.LoadCsvAsync(
                "synthetic.csv",
                () => Task.FromResult<Stream>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv), writable: false)));
            var importButton = Assert.IsType<Button>(FindByAutomationId(importView, "import-reviewed"), exactMatch: false);
            importButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => FindByAutomationId(importView, "import-result") is TextBlock
            {
                Text.Length: > 0,
            });

            Assert.Equal(AppRoute.CsvImport, shell.CurrentScreen.Route);
            Assert.Equal(0, flow.AdvanceCalls);
            Assert.Empty(inventory.CurrentInventory!.Accounts);
            Assert.Contains(
                "conflicts with the current inventory",
                ((TextBlock)FindByAutomationId(importView, "import-result")).Text,
                StringComparison.OrdinalIgnoreCase);
            window.Close();
        }, CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static StyledElement FindByAutomationId(Control root, string automationId) =>
        Assert.Single(root.GetLogicalDescendants().OfType<StyledElement>(), element =>
            AutomationProperties.GetAutomationId(element) == automationId);

    private sealed class TestScreenFactory(
        ILocalizationService localization,
        CsvImportScreenViewModel import,
        AccountInventoryScreenViewModel accounts) : IScreenFactory
    {
        private readonly Dictionary<AppRoute, ScreenViewModel> _screens =
            Enum.GetValues<AppRoute>().ToDictionary(
                route => route,
                route => (ScreenViewModel)(route switch
                {
                    AppRoute.CsvImport => import,
                    AppRoute.Accounts => accounts,
                    _ => new PlaceholderScreenViewModel(
                        route,
                        localization,
                        "Screen.Import.Title",
                        "Screen.Import.Description",
                        AppVisualState.Normal,
                        "Screen.Import.StatusTitle",
                        "Screen.Import.StatusMessage"),
                }));

        public ScreenViewModel Create(AppRoute route) => _screens[route];
    }

    private sealed class RejectingConfirmationService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unpwn-post-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
