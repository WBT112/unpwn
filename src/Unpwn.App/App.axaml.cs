using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application.Diagnostics;
using Unpwn.Automation.Recovery;
using Unpwn.Export.Credentials;

namespace Unpwn.App;

public partial class App : Avalonia.Application
{
    internal RecoveryCredentialHandoffServices? RecoveryCredentialHandoffServices { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? mainWindow = null;
            var localization = new ResourceLocalizationService();
            _ = new AvaloniaLocalizationResourceBridge(localization);
            var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
            var diagnostics = new SecretSafeDiagnostics(diagnosticStore);
            var runStateService = new ApplicationRunStateService(
                new FileApplicationRunMarkerStore(GetRunMarkerPath()),
                diagnostics);
            var runState = runStateService.Begin();
            var browserSessions = new RecoveryBrowserSessionLifecycle(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var wizard = new RecoveryWizardSessionService();
            var workspaceMutations = new WorkspaceMutationCoordinator();
            var vaultLifecycle = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(),
                wizard);
            var resilientRecordStore = new ResilientWorkspaceRecordStore(
                vaultLifecycle,
                diagnostics);
            var recoverySession = new RecoverySessionService(
                resilientRecordStore,
                vaultLifecycle,
                mutationCoordinator: workspaceMutations);
            var accountInventory = new AccountInventoryService(
                resilientRecordStore,
                recoverySession,
                mutationCoordinator: workspaceMutations);
            var accountRecovery = new AccountRecoveryExecutionService(
                resilientRecordStore,
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
            RecoveryCredentialHandoffServices = new RecoveryCredentialHandoffServices(
                vaultLifecycle,
                credentialClipboard,
                vaultLifecycle,
                accountInventory,
                accountRecovery,
                confirmationDialog,
                RepositoryRecoveryBrowserCredentialAssistanceCatalog.Instance);
            var guidedWizard = new GuidedRecoveryWizardService(
                resilientRecordStore,
                wizard,
                recoverySession,
                accountInventory,
                workspaceMutations);
            var diagnosticExport = new DiagnosticExportService(
                diagnosticStore,
                diagnostics,
                applicationVersion: typeof(App).Assembly.GetName().Version?.ToString());
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
                localization,
                diagnosticExport,
                browserSessions);
            var shell = new ShellViewModel(
                screenFactory,
                vaultLifecycle,
                recoverySession,
                accountInventory,
                localization,
                guidedWizard,
                resilientRecordStore,
                runState,
                browserSessions);

            var crashBoundary = new ApplicationCrashBoundary(vaultLifecycle, diagnostics);
            void domainFailureHandler(object _, UnhandledExceptionEventArgs eventArgs)
            {
                if (eventArgs.ExceptionObject is Exception exception)
                {
                    crashBoundary.Handle(exception);
                }
            }
            void taskFailureHandler(object? _, UnobservedTaskExceptionEventArgs eventArgs)
            {
                crashBoundary.Handle(eventArgs.Exception);
                eventArgs.SetObserved();
            }
            AppDomain.CurrentDomain.UnhandledException += domainFailureHandler;
            TaskScheduler.UnobservedTaskException += taskFailureHandler;

            mainWindow = new MainWindow
            {
                DataContext = shell,
            };
            mainWindow.AttachInactivityMonitor(vaultLifecycle);
            desktop.Exit += (_, _) =>
            {
                AppDomain.CurrentDomain.UnhandledException -= domainFailureHandler;
                TaskScheduler.UnobservedTaskException -= taskFailureHandler;
                sessionVaultBridge.Dispose();
                guidedWizard.Dispose();
                locationDiscovery.Dispose();
                accountInventory.Dispose();
                recoverySession.Dispose();
                workspaceMutations.Dispose();
                vaultLifecycle.Dispose();
                browserSessions.Dispose();
                RecoveryCredentialHandoffServices = null;
                runStateService.Complete();
            };
            desktop.MainWindow = mainWindow;
            _ = InitializeVaultReferencesAsync(vaultLifecycle, diagnostics);
        }

        base.OnFrameworkInitializationCompleted();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Recent-vault references are non-sensitive convenience metadata; startup must remain usable if that metadata is unavailable or malformed.")]
    private static async Task InitializeVaultReferencesAsync(
        RecoveryVaultLifecycleService vaultLifecycle,
        SecretSafeDiagnostics diagnostics)
    {
        try
        {
            await vaultLifecycle.InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            diagnostics.ReportFailure(DiagnosticOperation.WorkspaceLoad, exception);
            // Vault selection and creation remain usable without recent-vault convenience metadata.
        }
    }

    private static string GetRunMarkerPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unpwn",
        "run-state",
        "active.marker");
}
