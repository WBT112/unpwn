using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class AppScreenFactory : IScreenFactory
{
    private readonly Dictionary<AppRoute, ScreenViewModel> _screens;

    public AppScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IShellContextService shellContext)
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(shellContext);

        _screens = new Dictionary<AppRoute, ScreenViewModel>
        {
            [AppRoute.VaultEntry] = new VaultEntryScreenViewModel(),
            [AppRoute.Dashboard] = new PlaceholderScreenViewModel(
                AppRoute.Dashboard,
                "Recovery dashboard",
                "Critical-account readiness, progress, and blocked work will be summarized here.",
                VisualStatusViewModel.Create(
                    AppVisualState.Normal,
                    "Dashboard placeholder",
                    "Unlock a recovery vault to load session progress.")),
            [AppRoute.Accounts] = new PlaceholderScreenViewModel(
                AppRoute.Accounts,
                "Accounts",
                "Review imported accounts, priorities, and recovery dependencies.",
                VisualStatusViewModel.Create(
                    AppVisualState.Normal,
                    "Account inventory placeholder",
                    "No account data is loaded while the vault is locked.")),
            [AppRoute.Workflow] = new PlaceholderScreenViewModel(
                AppRoute.Workflow,
                "Recovery workflow",
                "Work through required actions with dependencies and user-visible automation boundaries.",
                VisualStatusViewModel.Create(
                    AppVisualState.Blocked,
                    "Workflow unavailable",
                    "Select an account from an unlocked recovery session before starting a workflow.")),
            [AppRoute.CredentialsExport] = new PlaceholderScreenViewModel(
                AppRoute.CredentialsExport,
                "Credentials and export",
                "Review newly generated credentials and export them to an established password manager.",
                VisualStatusViewModel.Create(
                    AppVisualState.Warning,
                    "Plaintext exports require care",
                    "Export destinations and cleanup must be explicitly confirmed.")),
            [AppRoute.Completion] = new CompletionScreenViewModel(confirmationDialog, shellContext),
            [AppRoute.CsvImport] = new CsvImportScreenViewModel(),
        };
    }

    public ScreenViewModel Create(AppRoute route) => _screens.TryGetValue(route, out var screen)
        ? screen
        : throw new ArgumentOutOfRangeException(nameof(route));
}
