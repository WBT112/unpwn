using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;

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
            var sessionVaultBridge = new RecoverySessionVaultBridge(
                vaultLifecycle,
                recoverySession,
                accountInventory);
            var confirmationDialog = new AvaloniaConfirmationDialogService(() => mainWindow);
            var screenFactory = new AppScreenFactory(
                confirmationDialog,
                vaultLifecycle,
                wizard,
                recoverySession,
                accountInventory,
                localization);
            var shell = new ShellViewModel(screenFactory, vaultLifecycle, localization);

            mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            mainWindow.AttachInactivityMonitor(vaultLifecycle);
            desktop.Exit += (_, _) =>
            {
                sessionVaultBridge.Dispose();
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

    private static async Task InitializeVaultReferencesAsync(
        IVaultLifecycleService vaultLifecycle)
    {
        try
        {
            await vaultLifecycle.InitializeAsync(CancellationToken.None);
        }
        catch (IOException)
        {
            // Recent-vault references are convenience metadata; vault entry remains usable.
        }
        catch (UnauthorizedAccessException)
        {
            // Recent-vault references are convenience metadata; vault entry remains usable.
        }
    }
}
