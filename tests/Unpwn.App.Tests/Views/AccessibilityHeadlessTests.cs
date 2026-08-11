using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
    private static readonly HeadlessUnitTestSession Session =
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
    public async Task MainWindowKeepsDocumentedMinimumSizeAndStableShellSelectors()
    {
        await Session.Dispatch(() =>
        {
            var window = new global::Unpwn.App.MainWindow();

            Assert.Equal(760, window.MinWidth);
            Assert.Equal(560, window.MinHeight);
            Assert.NotNull(FindByAutomationId(window, "shell-language"));
            Assert.NotNull(FindByAutomationId(window, "shell-navigation"));
            Assert.NotNull(FindByAutomationId(window, "shell-lock-vault"));
            Assert.NotNull(FindByAutomationId(window, "shell-assistant-primary"));
            Assert.NotNull(FindByAutomationId(window, "shell-workspace-toggle"));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AssistantTaskReceivesFocusInitiallyAndWhenCanonicalGuidanceChanges()
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
            var guided = new ShellViewModelTests.TestGuidedRecoveryWizardService(
                ShellViewModelTests.WizardAt(RecoveryWizardStepId.AccountInventory),
                new GuidedRecoveryDecision(
                    RecoveryWizardStepId.AccountInventory,
                    null,
                    GuidedRecoveryBlockCode.AccountsRequired));
            var shell = ShellViewModelTests.CreateGuidedShell(
                vault,
                session,
                new ShellViewModelTests.TestAccountInventoryService(),
                guided);
            var window = new global::Unpwn.App.MainWindow { DataContext = shell };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var primary = FindByAutomationId(window, "shell-assistant-primary");
            Assert.Same(primary, window.FocusManager?.GetFocusedElement());
            var previousFocusRequest = shell.AssistantFocusRequest;

            guided.SetGuidance(
                ShellViewModelTests.WizardAt(RecoveryWizardStepId.IdentityReview),
                new GuidedRecoveryDecision(
                    RecoveryWizardStepId.IdentityReview,
                    RecoveryWizardStepId.RecoveryPlan,
                    GuidedRecoveryBlockCode.None));
            Dispatcher.UIThread.RunJobs();

            Assert.True(shell.AssistantFocusRequest > previousFocusRequest);
            Assert.Same(primary, window.FocusManager?.GetFocusedElement());
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
            var banner = new StatusBannerView();
            var window = new Window { Content = banner };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var liveRegion = FindByAutomationId(banner, "status-banner");
            Assert.Equal(
                AutomationLiveSetting.Polite,
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

    private sealed class TestApplication : global::Avalonia.Application;
}
