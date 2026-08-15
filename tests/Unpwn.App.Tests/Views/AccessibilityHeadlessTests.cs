using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

public sealed class AccessibilityHeadlessTests
{
    internal static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(TestApplication));

    [Fact]
    public async Task NavigatedScreenFocusesEntryThenVisibleValidationSummary()
    {
        await Session.Dispatch(() =>
        {
            var entry = new Button
            {
                Content = "Synthetic action",
                Classes = { "initial-focus" },
            };
            var validation = new Border
            {
                Focusable = true,
                IsVisible = false,
                Classes = { "focus-on-visible" },
                Child = new TextBlock { Text = "Synthetic validation failure" },
            };
            var screen = new AccessibleScreen
            {
                Content = new StackPanel
                {
                    Children = { entry, validation },
                },
            };
            var window = new Window { Content = screen };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(entry, window.FocusManager?.GetFocusedElement());

            validation.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(validation, window.FocusManager?.GetFocusedElement());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EveryCoreScreenDeclaresALanguageNeutralEntryTarget()
    {
        await Session.Dispatch(() =>
        {
            AccessibleScreen[] screens =
            [
                new VaultEntryView(),
                new DashboardView(),
                new AccountsView(),
                new CsvImportView(),
                new WorkflowExecutionView(),
                new CredentialExportView(),
                new CompletionScreenView(),
            ];

            foreach (var screen in screens)
            {
                var entryTargets = screen.GetLogicalDescendants()
                    .OfType<Control>()
                    .Where(control => control.Classes.Contains("initial-focus"))
                    .ToArray();
                Assert.NotEmpty(entryTargets);
                Assert.All(entryTargets, entryTarget =>
                    Assert.False(string.IsNullOrWhiteSpace(
                        AutomationProperties.GetAutomationId(entryTarget))));
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AccountReviewExposesSingleCategoryDecisionAndContinuationAction()
    {
        await Session.Dispatch(() =>
        {
            var view = new AccountsView();

            Assert.NotNull(FindByAutomationId(view, "accounts-triage-list"));
            Assert.NotNull(FindByAutomationId(view, "accounts-category"));
            Assert.NotNull(FindByAutomationId(view, "accounts-category-save"));
            Assert.NotNull(FindByAutomationId(view, "accounts-continue-recovery"));
            Assert.NotNull(FindByAutomationId(view, "accounts-new"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AccountsNavigationRendersEmptySingleAndMultiAccountPostImportStates(
        int accountCount)
    {
        await Session.Dispatch(() =>
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
            var shell = ShellViewModelTests.CreateFlowShell(vault, session, inventory, flow);
            var window = new global::Unpwn.App.MainWindow { DataContext = shell };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);
            var importedAccounts = Enumerable.Range(1, accountCount)
                .Select(index => new AccountInventoryEntry(
                    Guid.NewGuid(),
                    index == 1 ? "google.com" : $"service-{index}.example.test",
                    $"Synthetic imported account {index}",
                    $"person-{index}@example.invalid",
                    $"https://service-{index}.example.test/account",
                    AccountRecoveryCategory.Unknown,
                    RepositoryAccountClassificationCatalog.CurrentVersion,
                    ConfirmedCategory: null,
                    CategoryConfirmedRevision: null,
                    DateTimeOffset.UnixEpoch))
                .ToArray();
            inventory.SetInventory(AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
                .ReplaceAccounts(importedAccounts, DateTimeOffset.UnixEpoch.AddSeconds(1)));
            if (accountCount > 0)
            {
                flow.SetTask(
                    ShellViewModelTests.WizardAt(RecoveryWizardStepId.AccountInventory),
                    new NextUserTask(
                        RecoveryWizardStepId.AccountInventory,
                        NextUserTaskState.ActionAvailable,
                        NextUserTaskCode.ReviewAccountCategories,
                        NextUserTaskTarget.AccountTriage,
                        RecoveryWizardStepId.AccountTriage));
            }

            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);
            Dispatcher.UIThread.RunJobs();

            var accountsView = new AccountsView { DataContext = shell.CurrentScreen };
            var workspaceWindow = new Window { Content = accountsView };
            workspaceWindow.Show();
            Dispatcher.UIThread.RunJobs();
            var list = Assert.IsType<ListBox>(
                FindByAutomationId(accountsView, "accounts-triage-list"),
                exactMatch: false);
            var viewModel = Assert.IsType<AccountInventoryScreenViewModel>(shell.CurrentScreen);
            Assert.True(list.Items.Count == accountCount);
            Assert.True(viewModel.Accounts.Count == accountCount);
            Assert.Equal(
                importedAccounts.Select(account => account.Id).OrderBy(id => id),
                viewModel.Accounts.Select(account => account.Id).OrderBy(id => id));
            var automaticCategorySeen = false;
            foreach (var item in viewModel.Accounts)
            {
                if (item.Account.RequiresCategoryReview)
                {
                    Assert.False(automaticCategorySeen);
                }
                else
                {
                    automaticCategorySeen = true;
                }
            }

            if (viewModel.Accounts.Any(item => item.Account.RequiresCategoryReview))
            {
                Assert.True(viewModel.Accounts[0].Account.RequiresCategoryReview);
            }

            workspaceWindow.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportAndCredentialWorkspacesExposeTheirContinuationActions()
    {
        await Session.Dispatch(() =>
        {
            Assert.NotNull(FindByAutomationId(
                new CsvImportView(),
                "import-continue-account-review"));
            Assert.NotNull(FindByAutomationId(
                new CredentialExportView(),
                "credentials-continue-completion"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryExecutionExposesOneGuidedActionAndProgressiveOutcomeControls()
    {
        await Session.Dispatch(() =>
        {
            var view = new WorkflowExecutionView();

            var currentAction = FindByAutomationId(view, "workflow-current-action");
            Assert.True(Assert.IsType<Control>(currentAction, exactMatch: false).Focusable);
            Assert.NotNull(FindByAutomationId(view, "workflow-primary-action"));
            Assert.DoesNotContain(
                view.GetLogicalDescendants().OfType<StyledElement>(),
                element => AutomationProperties.GetAutomationId(element) == "workflow-recovery-path");
            var browserWorkspace = FindByAutomationId(view, "workflow-browser-workspace");
            Assert.True(Assert.IsType<Control>(browserWorkspace, exactMatch: false).Focusable);
            Assert.NotNull(FindByAutomationId(view, "workflow-open-external-fallback"));
            Assert.NotNull(FindByAutomationId(view, "workflow-criteria-acknowledge"));
            Assert.NotNull(FindByAutomationId(view, "workflow-done"));
            Assert.NotNull(FindByAutomationId(view, "workflow-cannot-continue"));
            Assert.NotNull(FindByAutomationId(view, "workflow-problem-choice"));
            Assert.NotNull(FindByAutomationId(view, "workflow-problem-apply"));
            Assert.NotNull(FindByAutomationId(view, "workflow-generate-credential"));
            Assert.NotNull(FindByAutomationId(view, "workflow-show-advanced"));
            Assert.NotNull(FindByAutomationId(view, "workflow-show-guided"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryActionChangeDoesNotStealFocusFromActiveBrowserWorkspace()
    {
        await Session.Dispatch(() =>
        {
            var view = new WorkflowExecutionView();
            var window = new Window { Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var browserWorkspace = Assert.IsType<Control>(
                FindByAutomationId(view, "workflow-browser-workspace"),
                exactMatch: false);
            browserWorkspace.IsVisible = true;
            browserWorkspace.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            view.FocusCurrentActionUnlessBrowserHasFocus();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(browserWorkspace, window.FocusManager?.GetFocusedElement());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindowKeepsDocumentedMinimumSizeAndStableShellSelectors()
    {
        await Session.Dispatch(() =>
        {
            var window = new global::Unpwn.App.MainWindow();

            Assert.Equal(760, window.MinWidth);
            Assert.Equal(560, window.MinHeight);
            Assert.NotNull(FindByAutomationId(window, "shell-settings"));
            Assert.NotNull(FindByAutomationId(window, "shell-navigation"));
            Assert.NotNull(FindByAutomationId(window, "shell-lock-vault"));
            Assert.NotNull(FindByAutomationId(window, "shell-workspace-toggle"));
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<StyledElement>(),
                element => AutomationProperties.GetAutomationId(element) == "shell-assistant-primary");

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WorkspaceAndShellRenderOnlyTheirExplicitStatusPurpose()
    {
        await Session.Dispatch(() =>
        {
            var vault = new ShellViewModelTests.TestVaultLifecycleService();
            vault.Unlock("Synthetic vault", "Synthetic recovery");
            var session = new ShellViewModelTests.TestRecoverySessionService();
            session.SetSession(RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch));
            var flow = new ShellViewModelTests.TestRecoveryFlowService(
                ShellViewModelTests.WizardAt(RecoveryWizardStepId.AccountInventory),
                new NextUserTask(
                    RecoveryWizardStepId.AccountInventory,
                    NextUserTaskState.ActionAvailable,
                    NextUserTaskCode.ImportAccounts,
                    NextUserTaskTarget.CsvImport));
            var shell = ShellViewModelTests.CreateFlowShell(
                vault,
                session,
                new ShellViewModelTests.TestAccountInventoryService(),
                flow);
            var window = new global::Unpwn.App.MainWindow { DataContext = shell };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.CsvImport);
            Dispatcher.UIThread.RunJobs();

            var shellStatuses = window.GetLogicalDescendants()
                .OfType<StatusBannerView>()
                .Select(view => Assert.IsType<VisualStatusViewModel>(view.DataContext))
                .ToArray();
            Assert.Single(shellStatuses, status => status.Presentation == StatusPresentation.GlobalContext);
            Assert.DoesNotContain(
                shellStatuses,
                status => status.Presentation == StatusPresentation.ScreenInstruction);

            var importView = new CsvImportView { DataContext = shell.CurrentScreen };
            var workspaceWindow = new Window { Content = importView };
            workspaceWindow.Show();
            Dispatcher.UIThread.RunJobs();
            var workspaceStatus = Assert.IsType<VisualStatusViewModel>(
                Assert.Single(importView.GetLogicalDescendants().OfType<StatusBannerView>()).DataContext);
            Assert.Equal(StatusPresentation.ScreenInstruction, workspaceStatus.Presentation);

            shell.SelectedLanguage = shell.LanguageOptions.Single(option => option.Code == "qps-ploc");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(StatusPresentation.ScreenInstruction, shell.CurrentScreen.Status.Presentation);
            Assert.Equal(StatusPresentation.GlobalContext, shell.CurrentStatus.Presentation);
            workspaceWindow.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DashboardOwnsAccountStartAndDeferActionsWithoutShellDuplication()
    {
        await Session.Dispatch(() =>
        {
            var vault = new ShellViewModelTests.TestVaultLifecycleService();
            vault.Unlock("Synthetic vault", "Synthetic recovery");
            var session = new ShellViewModelTests.TestRecoverySessionService();
            session.SetSession(RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch));
            var flow = new ShellViewModelTests.TestRecoveryFlowService(
                ShellViewModelTests.WizardAt(RecoveryWizardStepId.AccountInventory),
                new NextUserTask(
                    RecoveryWizardStepId.AccountInventory,
                    NextUserTaskState.ActionAvailable,
                    NextUserTaskCode.ImportAccounts,
                    NextUserTaskTarget.CsvImport));
            var shell = ShellViewModelTests.CreateFlowShell(
                vault,
                session,
                new ShellViewModelTests.TestAccountInventoryService(),
                flow);
            shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Dashboard);
            var dashboard = new DashboardView { DataContext = shell.CurrentScreen };
            var window = new Window { Content = dashboard };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(FindByAutomationId(dashboard, "dashboard-recommendation-open"));
            Assert.NotNull(FindByAutomationId(dashboard, "dashboard-recommendation-skip"));
            var mainWindow = new global::Unpwn.App.MainWindow { DataContext = shell };
            mainWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(mainWindow.GetLogicalDescendants().OfType<StyledElement>(), element =>
                AutomationProperties.GetAutomationId(element) == "shell-assistant-primary");
            mainWindow.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ConfirmationDialogStartsOnSafeActionAndExposesAutomationContract()
    {
        await Session.Dispatch(() =>
        {
            var owner = new Window();
            owner.Show();
            var dialog = new ConfirmationDialog(new SensitiveConfirmationRequest(
                "Synthetic sensitive action",
                "Synthetic account",
                "Synthetic consequence",
                "Confirm synthetic action",
                "Sensitive",
                isDestructive: true));

            dialog.Show(owner);
            Dispatcher.UIThread.RunJobs();

            var focused = Assert.IsType<StyledElement>(
                dialog.FocusManager?.GetFocusedElement(),
                exactMatch: false);
            Assert.Equal(
                "confirmation-cancel",
                AutomationProperties.GetAutomationId(focused));

            dialog.Close(false);
            owner.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ConfirmationServiceRestoresInvokingFocusAfterDialogCloses()
    {
        await Session.Dispatch(async () =>
        {
            var invokingButton = new Button { Content = "Synthetic invoking action" };
            var owner = new Window { Content = invokingButton };
            owner.Show();
            invokingButton.Focus(NavigationMethod.Tab);
            var service = new AvaloniaConfirmationDialogService(() => owner);

            var confirmation = service.ConfirmAsync(
                new SensitiveConfirmationRequest(
                    "Synthetic sensitive action",
                    "Synthetic account",
                    "Synthetic consequence",
                    "Confirm synthetic action",
                    "Sensitive",
                    isDestructive: true),
                CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.IsType<ConfirmationDialog>(Assert.Single(owner.OwnedWindows));

            dialog.Close(false);
            Assert.False(await confirmation);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(invokingButton, owner.FocusManager?.GetFocusedElement());
            owner.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StatusBannerIsAPoliteNamedLiveRegion()
    {
        await Session.Dispatch(() =>
        {
            var localization = new ResourceLocalizationService(
                System.Globalization.CultureInfo.GetCultureInfo("en"));
            var banner = new StatusBannerView
            {
                DataContext = VisualStatusViewModel.Create(
                    AppVisualState.Normal,
                    localization,
                    "Screen.Vault.StatusTitle",
                    "Screen.Vault.StatusMessage"),
            };
            var window = new Window { Content = banner };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var liveRegion = FindByAutomationId(banner, "status-banner");
            Assert.Equal(
                AutomationLiveSetting.Polite,
                AutomationProperties.GetLiveSetting(liveRegion));

            banner.DataContext = VisualStatusViewModel.Create(
                AppVisualState.Error,
                localization,
                "Shell.Lock.FailedTitle",
                "Shell.Lock.Error",
                StatusPresentation.GlobalWarning);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                AutomationLiveSetting.Assertive,
                AutomationProperties.GetLiveSetting(liveRegion));

            window.Close();
        }, CancellationToken.None);
    }

    private static StyledElement FindByAutomationId(Control root, string automationId)
    {
        var descendants = root.GetLogicalDescendants().OfType<StyledElement>();
        return Assert.Single(descendants, element =>
            AutomationProperties.GetAutomationId(element) == automationId);
    }

    internal sealed class TestApplication : global::Avalonia.Application;
}
