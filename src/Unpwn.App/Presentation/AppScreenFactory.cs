using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application.Recovery;

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
        : this(
            confirmationDialog,
            vaultLifecycle,
            wizard,
            recoverySession,
            new UnavailableAccountInventoryService(),
            localization)
    {
    }

    public AppScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        ILocalizationService localization)
        : this(
            confirmationDialog,
            vaultLifecycle,
            wizard,
            recoverySession,
            accountInventory,
            executionService: null,
            locationDiscovery: null,
            externalNavigation: null,
            localization,
            functionalWorkflow: false)
    {
    }

    public AppScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        IAccountRecoveryExecutionService executionService,
        IRecoveryLocationDiscoveryService locationDiscovery,
        IExternalNavigationService externalNavigation,
        ILocalizationService localization)
        : this(
            confirmationDialog,
            vaultLifecycle,
            wizard,
            recoverySession,
            accountInventory,
            executionService ?? throw new ArgumentNullException(nameof(executionService)),
            locationDiscovery ?? throw new ArgumentNullException(nameof(locationDiscovery)),
            externalNavigation ?? throw new ArgumentNullException(nameof(externalNavigation)),
            localization,
            functionalWorkflow: true)
    {
    }

    private AppScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        IAccountRecoveryExecutionService? executionService,
        IRecoveryLocationDiscoveryService? locationDiscovery,
        IExternalNavigationService? externalNavigation,
        ILocalizationService localization,
        bool functionalWorkflow = false)
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        ArgumentNullException.ThrowIfNull(wizard);
        ArgumentNullException.ThrowIfNull(recoverySession);
        ArgumentNullException.ThrowIfNull(accountInventory);
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
            [AppRoute.Accounts] = new AccountInventoryScreenViewModel(
                accountInventory,
                confirmationDialog,
                localization),
            [AppRoute.Workflow] = functionalWorkflow
                ? new WorkflowExecutionScreenViewModel(
                    accountInventory,
                    recoverySession,
                    executionService!,
                    locationDiscovery!,
                    externalNavigation!,
                    confirmationDialog,
                    localization)
                : new PlaceholderScreenViewModel(
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
            [AppRoute.CsvImport] = new CsvImportScreenViewModel(accountInventory, localization),
        };
    }

    public ScreenViewModel Create(AppRoute route) => _screens.TryGetValue(route, out var screen)
        ? screen
        : throw new ArgumentOutOfRangeException(nameof(route));
}
