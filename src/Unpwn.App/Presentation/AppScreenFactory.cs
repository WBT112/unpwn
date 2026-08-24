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
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        ArgumentNullException.ThrowIfNull(wizard);
        ArgumentNullException.ThrowIfNull(recoverySession);
        ArgumentNullException.ThrowIfNull(accountInventory);
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(locationDiscovery);
        ArgumentNullException.ThrowIfNull(externalNavigation);
        ArgumentNullException.ThrowIfNull(credentialRepository);
        ArgumentNullException.ThrowIfNull(credentialExportService);
        ArgumentNullException.ThrowIfNull(credentialClipboard);
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
            [AppRoute.Workflow] = new WorkflowExecutionScreenViewModel(
                accountInventory,
                recoverySession,
                executionService,
                locationDiscovery,
                externalNavigation,
                confirmationDialog,
                localization,
                credentialRepository,
                browserSessions,
                browserContentMode),
            [AppRoute.CredentialsExport] = new CredentialExportScreenViewModel(
                credentialRepository,
                credentialExportService,
                accountInventory,
                vaultLifecycle,
                credentialClipboard,
                confirmationDialog,
                localization,
                recoveryFlow: recoveryFlow),
            [AppRoute.Completion] = new CompletionScreenViewModel(
                new RecoveryCompletionService(
                    recoverySession,
                    accountInventory,
                    credentialRepository),
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
