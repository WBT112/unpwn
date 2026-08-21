using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application.Diagnostics;
using Unpwn.Application.Recovery;
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
            var desktopE2E = Program.DesktopE2E;
            MainWindow? mainWindow = null;
            var localization = new ResourceLocalizationService();
            _ = new AvaloniaLocalizationResourceBridge(localization);
            var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
            var diagnostics = new SecretSafeDiagnostics(diagnosticStore);
            var runStateService = new ApplicationRunStateService(
                new FileApplicationRunMarkerStore(
                    desktopE2E?.RunMarkerPath ?? GetRunMarkerPath()),
                diagnostics);
            var runState = runStateService.Begin();
            var browserSessions = new RecoveryBrowserSessionLifecycle(
                desktopE2E?.DataRoot ??
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var wizard = new RecoveryWizardSessionService();
            var workspaceMutations = new WorkspaceMutationCoordinator();
            var vaultLifecycle = new RecoveryVaultLifecycleService(
                new JsonRecentVaultStore(desktopE2E?.RecentVaultsPath),
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
            IRecoveryLocationDiscoveryService locationDiscovery = desktopE2E is null
                ? HttpRecoveryLocationDiscoveryService.CreateDefault()
                : new DesktopE2ERecoveryLocationDiscoveryService(desktopE2E.PasswordChangeUri);
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
            var recoveryFlow = new RecoveryFlowService(
                resilientRecordStore,
                wizard,
                recoverySession,
                accountInventory,
                workspaceMutations);
            var diagnosticExport = new DiagnosticExportService(
                diagnosticStore,
                diagnostics,
                applicationVersion: typeof(App).Assembly.GetName().Version?.ToString());
            var settings = new SettingsScreenViewModel(localization, diagnosticExport);
            var applicationPreferences = desktopE2E is null
                ? FileApplicationPreferences.CreateDefault()
                : new FileApplicationPreferences(desktopE2E.PreferencesPath);
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
                browserSessions,
                recoveryFlow,
                desktopE2E is null ? null : new PlatformVaultPathProvider(desktopE2E.DataRoot),
                desktopE2E is null
                    ? RecoveryBrowserContentMode.Recovery
                    : RecoveryBrowserContentMode.SyntheticTest);
            var shell = new ShellViewModel(
                screenFactory,
                vaultLifecycle,
                recoverySession,
                accountInventory,
                localization,
                recoveryFlow,
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
            mainWindow.AttachApplicationPreferences(applicationPreferences);
            mainWindow.AttachSettings(settings);
            mainWindow.AttachInactivityMonitor(vaultLifecycle);
            desktop.Exit += (_, _) =>
            {
                AppDomain.CurrentDomain.UnhandledException -= domainFailureHandler;
                TaskScheduler.UnobservedTaskException -= taskFailureHandler;
                settings.Dispose();
                sessionVaultBridge.Dispose();
                recoveryFlow.Dispose();
                if (locationDiscovery is IDisposable disposableLocationDiscovery)
                {
                    disposableLocationDiscovery.Dispose();
                }
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
            if (desktopE2E is not null)
            {
                AttachDesktopE2ERunner(
                    desktop,
                    mainWindow,
                    desktopE2E,
                    browserSessions);
            }
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

    private static void AttachDesktopE2ERunner(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        DesktopE2EConfiguration configuration,
        IRecoveryBrowserSessionLifecycle browserSessions)
    {
        void run(object? sender, EventArgs eventArgs)
        {
            mainWindow.Opened -= run;
            Dispatcher.UIThread.Post(
                async () =>
                {
                    var succeeded = await new DesktopE2EJourneyRunner(
                        desktop,
                        mainWindow,
                        configuration,
                        browserSessions).RunAsync();
                    desktop.Shutdown(succeeded ? 0 : 1);
                },
                DispatcherPriority.Background);
        }

        mainWindow.Opened += run;
    }
}
