using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
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
            credentialRepository: null,
            credentialExportService: null,
            credentialClipboard: null,
            localization,
            functionalWorkflow: false,
            functionalCredentials: false)
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
            credentialRepository: null,
            credentialExportService: null,
            credentialClipboard: null,
            localization,
            functionalWorkflow: true,
            functionalCredentials: false)
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
        IGeneratedCredentialRepository credentialRepository,
        IGeneratedCredentialExportService credentialExportService,
        ICredentialClipboardService credentialClipboard,
        ILocalizationService localization,
        IRecoveryBrowserSessionLifecycle? browserSessions = null,
        IRecoveryFlowService? recoveryFlow = null,
        IVaultPathProvider? vaultPathProvider = null,
        RecoveryBrowserContentMode browserContentMode = RecoveryBrowserContentMode.Recovery)
        : this(
            confirmationDialog,
            vaultLifecycle,
            wizard,
            recoverySession,
            accountInventory,
            executionService ?? throw new ArgumentNullException(nameof(executionService)),
            locationDiscovery ?? throw new ArgumentNullException(nameof(locationDiscovery)),
            externalNavigation ?? throw new ArgumentNullException(nameof(externalNavigation)),
            credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository)),
            credentialExportService ?? throw new ArgumentNullException(nameof(credentialExportService)),
            credentialClipboard ?? throw new ArgumentNullException(nameof(credentialClipboard)),
            localization,
            functionalWorkflow: true,
            functionalCredentials: true,
            browserSessions: browserSessions,
            recoveryFlow: recoveryFlow,
            vaultPathProvider: vaultPathProvider,
            browserContentMode: browserContentMode)
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
        IGeneratedCredentialRepository? credentialRepository,
        IGeneratedCredentialExportService? credentialExportService,
        ICredentialClipboardService? credentialClipboard,
        ILocalizationService localization,
        bool functionalWorkflow = false,
        bool functionalCredentials = false,
        IRecoveryBrowserSessionLifecycle? browserSessions = null,
        IRecoveryFlowService? recoveryFlow = null,
        IVaultPathProvider? vaultPathProvider = null,
        RecoveryBrowserContentMode browserContentMode = RecoveryBrowserContentMode.Recovery)
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
                localization,
                vaultPathProvider: vaultPathProvider),
            [AppRoute.Dashboard] = new DashboardScreenViewModel(
                recoverySession,
                vaultLifecycle,
                confirmationDialog,
                localization),
            [AppRoute.Accounts] = new AccountInventoryScreenViewModel(
                accountInventory,
                confirmationDialog,
                localization,
                recoveryFlow),
            [AppRoute.Workflow] = functionalWorkflow
                ? new WorkflowExecutionScreenViewModel(
                    accountInventory,
                    recoverySession,
                    executionService!,
                    locationDiscovery!,
                    externalNavigation!,
                    confirmationDialog,
                    localization,
                    functionalCredentials ? credentialRepository : null,
                    browserSessions,
                    browserContentMode)
                : new PlaceholderScreenViewModel(
                    AppRoute.Workflow,
                    localization,
                    "Screen.Workflow.Title",
                    "Screen.Workflow.Description",
                    AppVisualState.Blocked,
                    "Screen.Workflow.StatusTitle",
                    "Screen.Workflow.StatusMessage"),
            [AppRoute.CredentialsExport] = functionalCredentials
                ? new CredentialExportScreenViewModel(
                    credentialRepository!,
                    credentialExportService!,
                    accountInventory,
                    vaultLifecycle,
                    credentialClipboard!,
                    confirmationDialog,
                    localization,
                    recoveryFlow: recoveryFlow)
                : new PlaceholderScreenViewModel(
                    AppRoute.CredentialsExport,
                    localization,
                    "Screen.Credentials.Title",
                    "Screen.Credentials.Description",
                    AppVisualState.Warning,
                    "Screen.Credentials.StatusTitle",
                    "Screen.Credentials.StatusMessage"),
            [AppRoute.Completion] = new CompletionScreenViewModel(
                functionalCredentials
                    ? new RecoveryCompletionService(
                        recoverySession,
                        accountInventory,
                        credentialRepository!)
                    : new UnavailableRecoveryCompletionService(),
                new JsonRecoveryCompletionReportWriter(),
                confirmationDialog,
                vaultLifecycle,
                localization),
            [AppRoute.CsvImport] = new CsvImportScreenViewModel(
                accountInventory,
                localization,
                recoveryFlow),
        };
    }

    public ScreenViewModel Create(AppRoute route) => _screens.TryGetValue(route, out var screen)
        ? screen
        : throw new ArgumentOutOfRangeException(nameof(route));
}
