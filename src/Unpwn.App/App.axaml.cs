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
            var shellContext = new LockedShellContextService();
            var confirmationDialog = new AvaloniaConfirmationDialogService(() => mainWindow);
            var screenFactory = new AppScreenFactory(confirmationDialog, shellContext, localization);
            var shell = new ShellViewModel(screenFactory, shellContext, localization);

            mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
