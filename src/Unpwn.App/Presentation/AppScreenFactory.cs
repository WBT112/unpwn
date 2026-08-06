using Unpwn.App.Localization;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class AppScreenFactory : IScreenFactory
{
    private readonly Dictionary<AppRoute, ScreenViewModel> _screens;

    public AppScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService recoverySession,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        ArgumentNullException.ThrowIfNull(wizard);
        ArgumentNullException.ThrowIfNull(recoverySession);
        ArgumentNullException.ThrowIfNull(localization);

        _screens = new Dictionary<AppRoute, ScreenViewModel>
        {
            [AppRoute.VaultEntry] = new VaultEntryScreenViewModel(
                vaultLifecycle,
                wizard,
                confirmationDialog,
                localization),
            [AppRoute.Dashboard] = new DashboardScreenViewModel(
                recoverySession,
                vaultLifecycle,
                wizard,
                confirmationDialog,
                localization),
            [AppRoute.Accounts] = new PlaceholderScreenViewModel(
                AppRoute.Accounts,
                localization,
                "Screen.Accounts.Title",
                "Screen.Accounts.Description",
                AppVisualState.Normal,
                "Screen.Accounts.StatusTitle",
                "Screen.Accounts.StatusMessage"),
            [AppRoute.Workflow] = new PlaceholderScreenViewModel(
                AppRoute.Workflow,
                localization,
                "Screen.Workflow.Title",
                "Screen.Workflow.Description",
                AppVisualState.Blocked,
                "Screen.Workflow.StatusTitle",
                "Screen.Workflow.StatusMessage"),
            [AppRoute.CredentialsExport] = new PlaceholderScreenViewModel(
                AppRoute.CredentialsExport,
                localization,
                "Screen.Credentials.Title",
                "Screen.Credentials.Description",
                AppVisualState.Warning,
                "Screen.Credentials.StatusTitle",
                "Screen.Credentials.StatusMessage"),
            [AppRoute.Completion] = new CompletionScreenViewModel(
                confirmationDialog,
                vaultLifecycle,
                localization),
            [AppRoute.CsvImport] = new CsvImportScreenViewModel(localization),
        };
    }

    public ScreenViewModel Create(AppRoute route) => _screens.TryGetValue(route, out var screen)
        ? screen
        : throw new ArgumentOutOfRangeException(nameof(route));
}
