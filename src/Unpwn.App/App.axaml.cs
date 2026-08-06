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
            var vaultLifecycle = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(),
                wizard);
            var confirmationDialog = new AvaloniaConfirmationDialogService(() => mainWindow);
            var screenFactory = new AppScreenFactory(
                confirmationDialog,
                vaultLifecycle,
                wizard,
                localization);
            var shell = new ShellViewModel(screenFactory, vaultLifecycle, localization);

            mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            mainWindow.AttachInactivityMonitor(vaultLifecycle);
            desktop.Exit += (_, _) => vaultLifecycle.Dispose();
            desktop.MainWindow = mainWindow;
            _ = vaultLifecycle.InitializeAsync(CancellationToken.None);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
