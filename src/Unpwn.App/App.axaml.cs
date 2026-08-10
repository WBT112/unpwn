using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Automation.Recovery;
using Unpwn.Export.Credentials;

namespace Unpwn.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? mainWindow = null;
            var localization = new ResourceLocalizationService();
            _ = new AvaloniaLocalizationResourceBridge(localization);
            var wizard = new RecoveryWizardSessionService();
            var workspaceMutations = new WorkspaceMutationCoordinator();
            var vaultLifecycle = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(),
                wizard);
            var recoverySession = new RecoverySessionService(
                vaultLifecycle,
                vaultLifecycle,
                mutationCoordinator: workspaceMutations);
            var accountInventory = new AccountInventoryService(
                vaultLifecycle,
                recoverySession,
                mutationCoordinator: workspaceMutations);
            var accountRecovery = new AccountRecoveryExecutionService(
                vaultLifecycle,
                recoverySession,
                workspaceMutations);
            var locationDiscovery = HttpRecoveryLocationDiscoveryService.CreateDefault();
            var sessionVaultBridge = new RecoverySessionVaultBridge(
                vaultLifecycle,
                recoverySession,
                accountInventory);
            var confirmationDialog = new AvaloniaConfirmationDialogService(() => mainWindow);
            var externalNavigation = new AvaloniaExternalNavigationService(() => mainWindow);
            var credentialExport = new GeneratedCredentialExportService(vaultLifecycle);
            var credentialClipboard = new AvaloniaCredentialClipboardService(() => mainWindow);
            var guidedWizard = new GuidedRecoveryWizardService(
                vaultLifecycle,
                wizard,
                recoverySession,
                accountInventory,
                workspaceMutations);
            var screenFactory = new AppScreenFactory(
                confirmationDialog,
                vaultLifecycle,
                wizard,
                recoverySession,
                accountInventory,
                accountRecovery,
                locationDiscovery,
                externalNavigation,
                vaultLifecycle,
                credentialExport,
                credentialClipboard,
                localization);
            var shell = new ShellViewModel(
                screenFactory,
                vaultLifecycle,
                recoverySession,
                accountInventory,
                localization,
                guidedWizard);

            mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            mainWindow.AttachInactivityMonitor(vaultLifecycle);
            desktop.Exit += (_, _) =>
            {
                sessionVaultBridge.Dispose();
                guidedWizard.Dispose();
                locationDiscovery.Dispose();
                accountInventory.Dispose();
                recoverySession.Dispose();
                workspaceMutations.Dispose();
                vaultLifecycle.Dispose();
            };
            desktop.MainWindow = mainWindow;
            _ = InitializeVaultReferencesAsync(vaultLifecycle);
        }

        base.OnFrameworkInitializationCompleted();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Recent-vault references are non-sensitive convenience metadata; startup must remain usable if that metadata is unavailable or malformed.")]
    private static async Task InitializeVaultReferencesAsync(
        RecoveryVaultLifecycleService vaultLifecycle)
    {
        try
        {
            await vaultLifecycle.InitializeAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Vault selection and creation remain usable without recent-vault convenience metadata.
        }
    }
}
