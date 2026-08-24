using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.App.Views;

namespace Unpwn.App;

internal sealed class DesktopE2EJourneyRunner(
    IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow mainWindow,
    DesktopE2EConfiguration configuration,
    IRecoveryBrowserSessionLifecycle browserSessions)
{
    private const string SyntheticVaultPassword = "desktop-e2e-only-482!";
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(75);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IClassicDesktopStyleApplicationLifetime _desktop = desktop;
    private readonly MainWindow _mainWindow = mainWindow;
    private readonly DesktopE2EConfiguration _configuration = configuration;
    private readonly IRecoveryBrowserSessionLifecycle _browserSessions = browserSessions;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly List<DesktopE2EStepLog> _steps = [];
    private string _currentStep = "startup";
    private string _browserBackend = "not-created";

    private ShellViewModel Shell => _mainWindow.DataContext as ShellViewModel ??
        throw Failure("The main window does not expose the application shell.");

    public async Task<bool> RunAsync()
    {
        Shell.PropertyChanged += Shell_OnPropertyChanged;
        try
        {
            await RunJourneyAsync();
            await WaitUntilAsync(
                () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "browser-session-cleanup",
                BrowserTimeout);
            _currentStep = "journey-complete";
            Record("journey-complete", "unpwn-main-window", "passed");
            WriteResult(succeeded: true, failureCode: null);
            return true;
        }
        catch (Exception exception)
        {
            var failureCode = exception is DesktopE2EFailure failure
                ? failure.SafeReason
                : $"unexpected-{exception.GetType().Name}";
            if (Shell.CurrentScreen is WorkflowExecutionScreenViewModel workflow)
            {
                failureCode +=
                    $" WorkflowState(actions={workflow.Actions.Count}," +
                    $"selected={workflow.SelectedAction?.DefinitionId}," +
                    $"refresh={workflow.RefreshCommand.LastOutcome}," +
                    $"refreshError={workflow.RefreshCommand.HasError}," +
                    $"start={workflow.StartRecoveryCommand.LastOutcome}," +
                    $"startError={workflow.StartRecoveryCommand.HasError}).";
            }
            Record(_currentStep, CurrentVisibleControlIds(), $"failed:{failureCode}");
            TryCaptureScreenshot();
            WriteResult(succeeded: false, failureCode);
            return false;
        }
        finally
        {
            Shell.PropertyChanged -= Shell_OnPropertyChanged;
        }
    }

    private void Shell_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ShellViewModel.CurrentScreen))
        {
            Record("shell-route", "unpwn-main-window", $"route={Shell.CurrentScreen.Route}");
        }
    }

    private async Task RunJourneyAsync()
    {
        switch (_configuration.Scenario)
        {
            case DesktopE2EScenarioCatalog.Golden:
                await RunGoldenJourneyAsync();
                return;
            case DesktopE2EScenarioCatalog.SafetyStopAndRetry:
                await RunSafetyStopAndRetryAsync();
                return;
            case DesktopE2EScenarioCatalog.VaultWrongPasswordAndResume:
                await RunVaultWrongPasswordAndResumeAsync();
                return;
            case DesktopE2EScenarioCatalog.ImportCorrectionAndRetry:
                await RunImportCorrectionAndRetryAsync();
                return;
            case DesktopE2EScenarioCatalog.DeferAccount:
                await RunDeferAccountAsync();
                return;
            case DesktopE2EScenarioCatalog.BrowserClosePreservesRecovery:
                await RunBrowserClosePreservesRecoveryAsync();
                return;
            case DesktopE2EScenarioCatalog.ReviewedBrowserStepHierarchy:
                await RunReviewedBrowserStepHierarchyAsync();
                return;
            case DesktopE2EScenarioCatalog.BrowserStartupFailureFallback:
                await RunBrowserStartupFailureFallbackAsync();
                return;
            case DesktopE2EScenarioCatalog.MultiAccountTransition:
                await RunMultiAccountTransitionAsync();
                return;
            case DesktopE2EScenarioCatalog.InterruptionMidRecovery:
                await RunInterruptionScenarioAsync(expectOrphanedBrowserSession: false);
                return;
            case DesktopE2EScenarioCatalog.InterruptionActiveBrowser:
                await RunInterruptionScenarioAsync(expectOrphanedBrowserSession: true);
                return;
            default:
                throw Failure("The configured desktop scenario was not recognized.");
        }
    }

    private async Task RunGoldenJourneyAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();
        await CategorizeImportedAccountAsync();
        await StartRecoveryAsync();
        await CompleteRecoveryActionsAsync();
        await CompleteRecoverySessionAsync();
    }

    private async Task RunSafetyStopAndRetryAsync()
    {
        await StepAsync("trust-gate", "vault-begin", async () =>
        {
            await ClickAsync("vault-begin");
            await ClickAsync("vault-trusted-no-or-unsure");
            var vault = Shell.CurrentScreen as VaultEntryScreenViewModel ??
                throw Failure("The trusted-device guidance did not remain in the vault workspace.");
            await WaitUntilAsync(
                () => vault.Stage == VaultEntryStage.TrustedDeviceGuidance,
                "trusted-device-guidance");
            if (Directory.EnumerateFiles(_configuration.DataRoot, "*.unpwn", SearchOption.AllDirectories).Any())
            {
                throw Failure("Sensitive vault work started before the trusted-device gate passed.");
            }

            await ClickAsync("vault-end-for-device-safety");
            await WaitUntilAsync(
                () => vault.Stage == VaultEntryStage.SafetyStopped,
                "safety-stop");
            await ClickAsync("vault-restart-safety-check");
            await ClickAsync("vault-trusted-yes");
        });

        await CreateVaultAsync();
    }

    private async Task RunVaultWrongPasswordAndResumeAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();

        await StepAsync("lock-vault", "shell-lock-vault", async () =>
        {
            await ClickAsync("shell-lock-vault");
            await WaitUntilAsync(
                () => Shell.CurrentScreen is VaultEntryScreenViewModel
                { Stage: VaultEntryStage.LockedVault },
                "locked-vault-entry");
        });

        await StepAsync("wrong-password", "vault-locked-unlock", async () =>
        {
            await SetTextAsync("vault-locked-password", "synthetic-wrong-password");
            await ClickAsync("vault-locked-unlock");
            var vault = (VaultEntryScreenViewModel)Shell.CurrentScreen;
            await WaitUntilAsync(
                () => vault.HasValidationMessage && !vault.UnlockCurrentVaultCommand.IsRunning,
                "wrong-password-feedback");
            var password = await WaitForControlAsync<TextBox>("vault-locked-password");
            if (!string.IsNullOrEmpty(password.Text) || !string.IsNullOrEmpty(vault.OpenPassword))
            {
                throw Failure("The rejected vault password remained in the UI or view-model memory.");
            }
        });

        await StepAsync("unlock-and-resume", "vault-locked-unlock", async () =>
        {
            var vault = (VaultEntryScreenViewModel)Shell.CurrentScreen;
            await SetTextAsync("vault-locked-password", SyntheticVaultPassword);
            await ClickAsync("vault-locked-unlock");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard,
                "resumed-dashboard");
            if (!string.IsNullOrEmpty(vault.OpenPassword))
            {
                throw Failure("The accepted vault password remained in the deactivated view model.");
            }
        });
    }

    private async Task RunImportCorrectionAndRetryAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();

        await StepAsync("invalid-import", "import-open-csv", async () =>
        {
            await ClickAsync("import-open-csv");
            var review = await WaitForControlAsync<Button>("import-reviewed");
            var mapping = await WaitForControlAsync<Border>("import-mapping-panel");
            await WaitUntilAsync(
                () => mapping.IsVisible && !review.IsEnabled,
                "invalid-import-feedback");
            if (Shell.CurrentScreen.Route != AppRoute.CsvImport)
            {
                throw Failure("An invalid import left the import workspace.");
            }
        });

        await StepAsync("correct-and-retry-import", "import-choose-another", async () =>
        {
            await File.WriteAllTextAsync(
                _configuration.CsvFixturePath,
                "service,username,url,password\n" +
                $"synthetic,user@example.invalid,{_configuration.PasswordChangeUri},synthetic-ignored-value\n");
            await ClickAsync("import-choose-another");
            await WaitForControlAsync<Button>("import-reviewed", control => control.IsEnabled);
            await ClickAsync("import-reviewed");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Accounts,
                "corrected-import-reviewed");
        });
    }

    private async Task RunBrowserClosePreservesRecoveryAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();
        await CategorizeImportedAccountAsync();
        var workflow = await StartRecoveryAsync();
        var actionId = workflow.SelectedAction?.DefinitionId;

        await StepAsync("close-browser-without-completion", "recovery-browser-close", async () =>
        {
            await ClickAsync("recovery-browser-close");
            await WaitUntilAsync(
                () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "browser-close-cleanup",
                BrowserTimeout);
            if (Shell.CurrentScreen is not WorkflowExecutionScreenViewModel current ||
                !string.Equals(current.SelectedAction?.DefinitionId, actionId, StringComparison.Ordinal) ||
                !current.IsCurrentActionInProgress)
            {
                throw Failure("Closing the browser incorrectly changed canonical recovery completion.");
            }
        });
    }

    private async Task RunReviewedBrowserStepHierarchyAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();
        await CategorizeImportedAccountAsync();
        var workflow = await StartRecoveryAsync();
        if (!workflow.IsReviewedProviderWorkflow)
        {
            throw Failure("The reviewed-provider desktop scenario did not select a reviewed workflow.");
        }

        await StepAsync("close-reviewed-browser", "recovery-browser-close", async () =>
        {
            await ClickAsync("recovery-browser-close");
            await WaitUntilAsync(
                () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "reviewed-browser-cleanup",
                BrowserTimeout);
        });
    }

    private async Task RunBrowserStartupFailureFallbackAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();
        await CategorizeImportedAccountAsync();

        await StepAsync("controlled-browser-startup-failure", "workflow-primary-action", async () =>
        {
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard,
                "recovery-overview");
            await ClickAsync("dashboard-recommendation-open");
            var workflow = await WaitForWorkflowAsync();
            if (!workflow.HasExecution)
            {
                await ClickAsync("workflow-begin");
                await WaitUntilAsync(() => workflow.HasExecution, "recovery-execution-created");
            }

            await ClickAsync("workflow-primary-action", allowOffscreen: true);
            await WaitUntilAsync(
                () => workflow.HasBrowserLaunchFailure,
                "controlled-browser-startup-failure");

            if (!workflow.IsGeneralManualWorkflow ||
                !workflow.CanUseExternalBrowserFallback ||
                _browserSessions.Current.State != RecoveryBrowserSessionLifecycleState.Idle)
            {
                throw Failure("The controlled generic-browser failure did not expose the safe degraded mode.");
            }

            await WaitForControlAsync<Button>(
                "workflow-open-external-fallback",
                button => button.IsVisible && button.IsEnabled);
            await WaitForControlAsync<Border>(
                "workflow-browser-launch-failure",
                border => border.IsVisible);
            if (FindControl<Control>("workflow-security-details") is not null ||
                FindControl<Control>("workflow-progress-details") is not null)
            {
                throw Failure("Progressive details expanded during the browser failure.");
            }
        });
    }

    private async Task RunDeferAccountAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();
        await CategorizeImportedAccountAsync();

        await StepAsync("defer-account", "dashboard-recommendation-skip", async () =>
        {
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard,
                "recovery-overview-before-defer");
            var dashboard = Shell.CurrentScreen as DashboardScreenViewModel ??
                throw Failure("The recovery overview was not active before defer.");
            await ClickAsync("dashboard-recommendation-skip");
            await WaitUntilAsync(
                () => !dashboard.SkipRecommendationCommand.IsRunning &&
                    dashboard.SkipRecommendationCommand.LastOutcome == AsyncCommandOutcome.Completed,
                "account-deferred");
            if (dashboard.Status.State != AppVisualState.Warning)
            {
                throw Failure("The explicit deferral did not remain visibly unresolved.");
            }
        });
    }

    private async Task RunInterruptionScenarioAsync(bool expectOrphanedBrowserSession)
    {
        if (_configuration.IsPreparePhase)
        {
            await AcceptTrustedDeviceAsync();
            await CreateVaultAsync();
            await CreateSessionAsync();
            await ImportReviewedCsvAsync();
            await CategorizeImportedAccountAsync();
            var workflow = await StartRecoveryAsync();
            if (!expectOrphanedBrowserSession)
            {
                await ClickAsync("recovery-browser-close");
                await WaitUntilAsync(
                    () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                    "pre-interruption-browser-cleanup",
                    BrowserTimeout);
            }

            var actionId = workflow.SelectedAction?.DefinitionId ??
                throw Failure("The interrupted recovery action was not available.");
            Record(
                "interruption-checkpoint-ready",
                "workflow-current-action",
                expectOrphanedBrowserSession ? "active-browser" : "mid-recovery");
            await File.WriteAllTextAsync(_configuration.InterruptionReadyPath, actionId);
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }

        if (!_configuration.IsResumePhase)
        {
            throw Failure("The interruption scenario did not specify a valid phase.");
        }

        await StepAsync("conservative-restart-boundary", "shell-startup-recovery-warning", async () =>
        {
            await WaitUntilAsync(
                () => Shell.HasStartupRecoveryWarning,
                "unexpected-exit-warning");
            await WaitForControlAsync<Border>(
                "shell-startup-recovery-warning",
                warning => warning.IsVisible);

            if (expectOrphanedBrowserSession)
            {
                await WaitUntilAsync(
                    () => Shell.HasBrowserSessionCleanupWarning &&
                        _browserSessions.Current.State ==
                            RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
                    "orphaned-browser-session-warning");
                await ClickAsync("shell-retry-browser-session-cleanup");
                await WaitUntilAsync(
                    () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle &&
                        !Shell.HasBrowserSessionCleanupWarning,
                    "orphaned-browser-session-cleanup",
                    BrowserTimeout);
            }
            else if (Shell.HasBrowserSessionCleanupWarning ||
                     _browserSessions.Current.State != RecoveryBrowserSessionLifecycleState.Idle)
            {
                throw Failure("A cleanly closed browser was incorrectly restored as an orphan.");
            }

            await ClickAsync("shell-dismiss-recovery-warning");
        });

        await UnlockExistingVaultAfterRestartAsync();
        await StepAsync("verify-conservative-resume", "workflow-current-action", async () =>
        {
            var workflow = await WaitForWorkflowAsync();
            var expectedActionId = await File.ReadAllTextAsync(_configuration.InterruptionReadyPath);
            if (!workflow.IsCurrentActionInProgress ||
                !string.Equals(
                    workflow.SelectedAction?.DefinitionId,
                    expectedActionId,
                    StringComparison.Ordinal) ||
                workflow.CompletionCriteria.Any(criterion => criterion.IsAcknowledged) ||
                _browserSessions.Current.State != RecoveryBrowserSessionLifecycleState.Idle)
            {
                throw Failure("Restart fabricated recovery truth or restored an unsafe browser session.");
            }

            await ClickAsync("workflow-primary-action", allowOffscreen: true);
            await WaitForNativeBrowserAsync();
            await ClickAsync("recovery-browser-close");
            await WaitUntilAsync(
                () => _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "resumed-browser-cleanup",
                BrowserTimeout);
        });
    }

    private async Task RunMultiAccountTransitionAsync()
    {
        await AcceptTrustedDeviceAsync();
        await CreateVaultAsync();
        await CreateSessionAsync();
        await ImportReviewedCsvAsync();

        await StepAsync("review-only-unrecognized-account", "accounts-current-triage-task", async () =>
        {
            await WaitUntilAsync(
                () => Shell.CurrentScreen is AccountInventoryScreenViewModel
                {
                    HasAccounts: true,
                    RemainingCategoryCount: 1,
                },
                "single-unrecognized-account-review");
            var accounts = (AccountInventoryScreenViewModel)Shell.CurrentScreen;
            var category = await WaitForControlAsync<ComboBox>("accounts-category");
            category.SelectedItem = accounts.Categories.Single(option =>
                option.Value == Unpwn.Core.AccountRecoveryCategory.NonCritical);
            await Task.Yield();
            await ClickAsync("accounts-category-save");
            await WaitUntilAsync(
                () => accounts.IsCategoryReviewComplete,
                "multi-account-triage-complete");
            await ClickAsync("accounts-continue-recovery");
        });

        await StepAsync("defer-email-account", "dashboard-recommendation-skip", async () =>
        {
            var dashboard = await WaitForDashboardAsync();
            if (!dashboard.RecommendationTargetText.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("The category queue did not put the email account first.");
            }

            await ClickAsync("dashboard-recommendation-skip");
            await WaitUntilAsync(
                () => !dashboard.SkipRecommendationCommand.IsRunning &&
                    dashboard.RecommendationTargetText.Contains("github", StringComparison.OrdinalIgnoreCase),
                "critical-account-after-email-deferral");
        });

        Guid criticalBrowserAccountId = Guid.Empty;
        await StepAsync("fail-critical-account", "workflow-problem-apply", async () =>
        {
            var workflow = await OpenAndStartRecommendedRecoveryAsync();
            criticalBrowserAccountId = _browserSessions.Current.ActiveSession?.AccountId ?? Guid.Empty;
            if (criticalBrowserAccountId == Guid.Empty)
            {
                throw Failure("The critical account did not bind an isolated browser session.");
            }

            await ClickAsync("workflow-cannot-continue", allowOffscreen: true);
            var problem = await WaitForControlAsync<ComboBox>("workflow-problem-choice");
            problem.SelectedItem = workflow.ProblemOptions.Single(option =>
                option.Value == GuidedRecoveryProblem.ProviderStepFailed);
            await WaitUntilAsync(
                () => workflow.SelectedProblem?.Value == GuidedRecoveryProblem.ProviderStepFailed,
                "provider-failure-problem-selection");
            await SetTextAsync("workflow-problem-reason", "Synthetic provider step failed.");
            await ClickAsync("workflow-problem-apply");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard &&
                    _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "failed-account-return-and-browser-cleanup",
                BrowserTimeout);
        });

        await StepAsync("move-past-failed-account", "dashboard-recommendation-skip", async () =>
        {
            var dashboard = await WaitForDashboardAsync();
            await ClickAsync("dashboard-recommendation-skip");
            await WaitUntilAsync(
                () => dashboard.RecommendationTargetText.Contains("synthetic", StringComparison.OrdinalIgnoreCase),
                "unknown-account-after-critical-deferral");
        });

        await StepAsync("rebind-next-account-browser", "workflow-defer-account", async () =>
        {
            await OpenAndStartRecommendedRecoveryAsync();
            var nextBrowserAccountId = _browserSessions.Current.ActiveSession?.AccountId ?? Guid.Empty;
            if (nextBrowserAccountId == Guid.Empty || nextBrowserAccountId == criticalBrowserAccountId)
            {
                throw Failure("The browser session leaked or failed to rebind across accounts.");
            }

            await ClickAsync("workflow-defer-account", allowOffscreen: true);
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard &&
                    _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Idle,
                "deferred-account-browser-cleanup",
                BrowserTimeout);
            var dashboard = (DashboardScreenViewModel)Shell.CurrentScreen;
            if (!dashboard.RecommendationTargetText.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Deferred work did not return through the canonical queue.");
            }
        });

        await StepAsync("completion-preflight-shows-open-work", "completion-before-finish", async () =>
        {
            await ClickAsync("shell-workspace-toggle");
            var navigation = await WaitForControlAsync<ListBox>("shell-navigation");
            navigation.SelectedItem = Shell.NavigationItems.Single(item => item.Route == AppRoute.Completion);
            await WaitUntilAsync(
                () => Shell.CurrentScreen is CompletionScreenViewModel { HasReview: true },
                "multi-account-completion-preflight");
            var completion = (CompletionScreenViewModel)Shell.CurrentScreen;
            if (!completion.Issues.Any(issue =>
                    issue.Issue.Kind == Unpwn.Core.RecoveryCompletionIssueKind.DeferredAccount) ||
                !completion.Issues.Any(issue =>
                    issue.Issue.Kind is
                        Unpwn.Core.RecoveryCompletionIssueKind.RequiredActionFailed or
                        Unpwn.Core.RecoveryCompletionIssueKind.RequiredActionIncomplete))
            {
                throw Failure(
                    "Completion preflight hid deferred or unresolved multi-account work: " +
                    string.Join(',', completion.Issues.Select(issue => issue.Issue.Kind)));
            }
        });
    }

    private async Task UnlockExistingVaultAfterRestartAsync()
    {
        await StepAsync("unlock-after-interruption", "vault-open-submit", async () =>
        {
            await ClickAsync("vault-begin");
            await ClickAsync("vault-trusted-yes");
            var vault = Shell.CurrentScreen as VaultEntryScreenViewModel ??
                throw Failure("The vault workspace was unavailable after restart.");
            await WaitUntilAsync(() => vault.HasPrimaryRecentVault, "recent-vault-after-restart");
            await ClickAsync("vault-primary-action");
            await SetTextAsync("vault-unlock-password", SyntheticVaultPassword);
            await ClickAsync("vault-open-submit");
            await WaitUntilAsync(
                () => Shell.IsVaultUnlocked && Shell.CurrentScreen.Route != AppRoute.VaultEntry,
                "workspace-resume-after-unlock");
        });
    }

    private async Task<DashboardScreenViewModel> WaitForDashboardAsync()
    {
        DashboardScreenViewModel? dashboard = null;
        await WaitUntilAsync(
            () =>
            {
                dashboard = Shell.CurrentScreen as DashboardScreenViewModel;
                return dashboard is not null && !dashboard.RefreshCommand.IsRunning;
            },
            "stable-recovery-overview");
        return dashboard!;
    }

    private async Task<WorkflowExecutionScreenViewModel> OpenAndStartRecommendedRecoveryAsync()
    {
        await WaitForDashboardAsync();
        await ClickAsync("dashboard-recommendation-open");
        var workflow = await WaitForWorkflowAsync();
        if (!workflow.HasExecution)
        {
            await ClickAsync("workflow-begin");
            await WaitUntilAsync(() => workflow.HasExecution, "multi-account-execution-created");
        }

        if (!workflow.IsCurrentActionInProgress || !workflow.IsBrowserWorkspaceVisible)
        {
            await ClickAsync("workflow-primary-action", allowOffscreen: true);
        }

        await WaitUntilAsync(
            () => workflow.IsCurrentActionInProgress,
            "multi-account-action-started");
        if (!workflow.IsBrowserWorkspaceVisible)
        {
            await ClickAsync("workflow-primary-action", allowOffscreen: true);
        }

        await WaitForNativeBrowserAsync();
        return workflow;
    }

    private async Task AcceptTrustedDeviceAsync()
    {
        await StepAsync("trust-gate", "vault-begin", async () =>
        {
            await ClickAsync("vault-begin");
            await ClickAsync("vault-trusted-yes");
        });
    }

    private async Task CreateVaultAsync()
    {
        await StepAsync("create-vault", "vault-primary-action", async () =>
        {
            await AssertPrimaryActionAsync("vault-primary-action");
            await ClickAsync("vault-primary-action");
            var vault = Shell.CurrentScreen as VaultEntryScreenViewModel ??
                throw Failure("The vault workspace was not active after choosing creation.");
            if (string.IsNullOrWhiteSpace(vault.CreatePath) ||
                !IsWithinDirectory(vault.CreatePath, _configuration.DataRoot))
            {
                throw Failure("The default vault path escaped the isolated data root.");
            }

            await SetTextAsync("vault-create-password", SyntheticVaultPassword);
            await SetTextAsync("vault-create-password-confirm", SyntheticVaultPassword);
            await SetCheckedAsync("vault-create-acknowledge", true);
            await ClickAsync("vault-create-submit");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard,
                "vault-created");
        });
    }

    private async Task CreateSessionAsync()
    {
        await StepAsync("create-session", "dashboard-create-session", async () =>
        {
            var dashboard = Shell.CurrentScreen as DashboardScreenViewModel ??
                throw Failure("The recovery overview was not active for session creation.");
            if (string.IsNullOrWhiteSpace(dashboard.SessionName))
            {
                throw Failure("The automatic recovery session name was empty.");
            }

            await AssertPrimaryActionAsync("dashboard-create-session");
            await ClickAsync("dashboard-create-session");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.CsvImport,
                "dashboard-session-ready");
        });
    }

    private async Task ImportReviewedCsvAsync()
    {
        await StepAsync("open-csv-import", "import-open-csv", async () =>
        {
            await AssertPrimaryActionAsync("import-open-csv");
            await ClickAsync("import-open-csv");
            await WaitForControlAsync<Button>("import-reviewed", control => control.IsEnabled);
            await AssertPrimaryActionAsync("import-reviewed");
            await ClickAsync("import-reviewed");
        });
    }

    private async Task CategorizeImportedAccountAsync()
    {
        await StepAsync("categorize-accounts", "accounts-current-triage-task", async () =>
        {
            await WaitUntilAsync(
                () => Shell.CurrentScreen is AccountInventoryScreenViewModel { HasAccounts: true },
                "account-triage-workspace");
            var accounts = (AccountInventoryScreenViewModel)Shell.CurrentScreen;
            if (accounts.HasRemainingCategoryReview)
            {
                var category = await WaitForControlAsync<ComboBox>(
                    "accounts-category",
                    control => control.Items.Count > 0);
                category.SelectedIndex = 0;
                await Task.Yield();
                await AssertPrimaryActionAsync("accounts-category-save");
                await ClickAsync("accounts-category-save");
            }
            else
            {
                if (FindControl<Control>("accounts-category") is not null)
                {
                    throw Failure("An automatically categorized account exposed mandatory manual triage.");
                }

                Record("automatic-category", "accounts-continue-recovery", "no-manual-edit-required");
            }

            await AssertPrimaryActionAsync("accounts-continue-recovery");
            await ClickAsync("accounts-continue-recovery");
        });
    }

    private async Task<WorkflowExecutionScreenViewModel> StartRecoveryAsync()
    {
        WorkflowExecutionScreenViewModel? startedWorkflow = null;
        await StepAsync("start-recovery", "dashboard-recommendation-open", async () =>
        {
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.Dashboard,
                "recovery-overview");
            await ClickAsync("dashboard-recommendation-open");
            var workflow = await WaitForWorkflowAsync();
            if (workflow.SelectedPath is null)
            {
                throw Failure("The automatic recovery path was not visible.");
            }
            if (workflow.SelectedPath.Path != Unpwn.Core.RecoveryPath.PasswordReset)
            {
                throw Failure("The deterministic account did not receive the expected automatic recovery path.");
            }

            Record(
                "automatic-order-and-path",
                "workflow-begin",
                $"account=synthetic;path={workflow.SelectedPath.Path}");
            if (!workflow.HasExecution)
            {
                await ClickAsync("workflow-begin");
                await WaitUntilAsync(() => workflow.HasExecution, "recovery-execution-created");
            }

            await AssertRecoveryStepHierarchyAsync(workflow);

            Record(
                "recovery-start-state",
                "workflow-current-action",
                $"action={workflow.SelectedAction?.DefinitionId};" +
                $"status={workflow.SelectedAction?.Status};" +
                $"can-run={workflow.CanRunGuidedPrimary};" +
                $"in-progress={workflow.IsCurrentActionInProgress}");

            if (!workflow.IsCurrentActionInProgress)
            {
                await ClickAsync("workflow-primary-action", allowOffscreen: true);
            }

            await WaitUntilAsync(
                () => workflow.HasExecution && workflow.IsCurrentActionInProgress,
                "automatic-recovery-start");
            if (!workflow.IsBrowserWorkspaceVisible)
            {
                await ClickAsync("workflow-primary-action", allowOffscreen: true);
            }
            await WaitForNativeBrowserAsync();
            startedWorkflow = workflow;
        });
        return startedWorkflow!;
    }

    private async Task AssertRecoveryStepHierarchyAsync(
        WorkflowExecutionScreenViewModel workflow)
    {
        await WaitForControlAsync<Button>(
            "workflow-primary-action",
            button => button.IsVisible && button.IsEnabled);
        if (!workflow.IsGuidedPrimaryActionVisible ||
            workflow.CanUseExternalBrowserFallback ||
            workflow.IsSecurityDetailsVisible ||
            workflow.IsProgressDetailsVisible ||
            FindControl<Control>("workflow-open-external-fallback") is not null ||
            FindControl<Control>("workflow-security-details") is not null ||
            FindControl<Control>("workflow-progress-details") is not null ||
            FindControl<Control>("workflow-open-discovered-page", includeHidden: true) is not null)
        {
            throw Failure("The recovery step did not render one primary task with collapsed details.");
        }
    }

    private async Task CompleteRecoveryActionsAsync()
    {
        var actionCount = 0;
        while (Shell.CurrentScreen is WorkflowExecutionScreenViewModel workflow)
        {
            if (++actionCount > 12)
            {
                throw Failure("The recovery action loop exceeded its deterministic bound.");
            }

            var actionId = workflow.SelectedAction?.DefinitionId ??
                throw Failure("The recovery action was not visible.");
            _currentStep = $"recovery-action-{actionId}";
            Record(_currentStep, "workflow-current-action", "started");

            if (workflow.IsPasswordCredentialAction && !workflow.HasCredentialReference)
            {
                await ClickAsync("workflow-generate-credential");
                await WaitUntilAsync(
                    () => workflow.HasCredentialReference,
                    "generated-credential-reference");
            }

            if (!workflow.IsCurrentActionInProgress)
            {
                await ClickAsync("workflow-primary-action");
                await WaitUntilAsync(
                    () => workflow.IsCurrentActionInProgress,
                    $"action-start-{actionId}");
                await WaitForNativeBrowserAsync();
            }

            if (workflow.IsPasswordCredentialAction)
            {
                await WaitForControlAsync<Button>(
                    "workflow-credential-assisted-insert",
                    control => control.IsEnabled);
                await ClickAsync("workflow-credential-assisted-insert");
                await ConfirmDialogAsync();
                await WaitForControlAsync<TextBlock>(
                    "workflow-credential-status",
                    control => !string.IsNullOrWhiteSpace(control.Text));
                await ClickAsync("workflow-credential-confirm");
            }

            foreach (var criterion in workflow.CompletionCriteria)
            {
                if (criterion.IsAcknowledged)
                {
                    continue;
                }

                var checkBox = await WaitForCriterionAsync(criterion);
                InvokeButton(checkBox);
                await WaitUntilAsync(
                    () => criterion.IsAcknowledged,
                    $"criterion-{actionId}");
            }

            if (actionId.StartsWith("document-completion", StringComparison.Ordinal) &&
                _browserSessions.Current.State == RecoveryBrowserSessionLifecycleState.Active)
            {
                await ClickAsync("recovery-browser-close");
                await WaitUntilAsync(
                    () => _browserSessions.Current.State ==
                        RecoveryBrowserSessionLifecycleState.Idle,
                    "explicit-browser-session-cleanup",
                    BrowserTimeout);
                Record(
                    "browser-session-cleanup",
                    "recovery-browser-close",
                    "passed");
            }

            await ClickAsync("workflow-done");
            await ConfirmDialogAsync();
            await WaitUntilAsync(
                () => Shell.CurrentScreen is not WorkflowExecutionScreenViewModel current ||
                    !string.Equals(
                        current.SelectedAction?.DefinitionId,
                        actionId,
                        StringComparison.Ordinal),
                $"action-complete-{actionId}");
            Record(_currentStep, "workflow-done", "passed");
        }
    }

    private async Task CompleteRecoverySessionAsync()
    {
        _currentStep = "completion-preflight";
        DashboardScreenViewModel? dashboard = null;
        await WaitUntilAsync(
            () =>
            {
                dashboard = Shell.CurrentScreen as DashboardScreenViewModel;
                return dashboard is not null &&
                    !dashboard.RefreshCommand.IsRunning &&
                    dashboard.RecommendationCode ==
                        Unpwn.Core.RecoveryDashboardRecommendationCode.ExportGeneratedCredentials;
            },
            "credential-handoff-recommendation");
        await ClickAsync("dashboard-recommendation-open");
        await WaitUntilAsync(
            () => Shell.CurrentScreen.Route == AppRoute.CredentialsExport,
            "credential-handoff-workspace");
        Record("credential-handoff", "credentials-continue-completion", "opened");
        await AssertPrimaryActionAsync("credentials-continue-completion");
        await ClickAsync("credentials-continue-completion");
        await WaitUntilAsync(
            () => Shell.CurrentScreen is CompletionScreenViewModel,
            "completion-workspace");
        Record(
            "credential-handoff-transition",
            "credentials-continue-completion",
            $"route={Shell.CurrentScreen.Route};status={Shell.CurrentStatus.State}");

        var completion = (CompletionScreenViewModel)Shell.CurrentScreen;
        await WaitUntilAsync(() => completion.HasReview, "completion-report");
        Record("completion-report", "completion-loading", "visible");
        if (completion.RequiresRiskAcceptance)
        {
            await SetCheckedAsync("completion-accept-risks", true);
        }

        await AssertPrimaryActionAsync("completion-complete");
        await ClickAsync("completion-complete");
        await ConfirmDialogAsync();
        await WaitUntilAsync(() => completion.IsReadOnly, "completion-finalized");
        Record("completion-finalized", "completion-complete", "passed");
    }

    private async Task WaitForNativeBrowserAsync()
    {
        var workflow = Shell.CurrentScreen as WorkflowExecutionScreenViewModel ??
            throw Failure("The workflow workspace was not active for browser startup.");
        RecoveryBrowserView? browser = null;
        if (!await TryWaitUntilAsync(
                () =>
                {
                    browser = FindVisibleControl<WorkflowExecutionView>()?.RecoveryBrowser;
                    return browser?.IsNativeBackendReady == true &&
                        browser.Snapshot?.State == RecoveryBrowserHostState.Ready;
                },
                BrowserTimeout))
        {
            throw Failure(
                $"Native browser unavailable: backend={browser?.NativeBackendStatus ?? "view-unavailable"};" +
                $"host={browser?.Snapshot?.State.ToString() ?? "view-unavailable"};" +
                $"workspace={workflow.IsBrowserWorkspaceVisible};" +
                $"prepared={workflow.HasPreparedNavigation};" +
                $"session={_browserSessions.Current.State};" +
                $"failure={_browserSessions.Current.FailureCode}.");
        }
        _browserBackend = browser!.NativeBackendStatus;
        if (string.Equals(_browserBackend, "not-created", StringComparison.Ordinal) ||
            string.Equals(_browserBackend, "unsupported", StringComparison.Ordinal))
        {
            throw Failure("A supported native recovery-browser backend was not active.");
        }

        Record(
            "native-browser-ready",
            "recovery-browser-content",
            $"backend={_browserBackend};state={browser.Snapshot?.State}");
    }

    private async Task<WorkflowExecutionScreenViewModel> WaitForWorkflowAsync()
    {
        WorkflowExecutionScreenViewModel? workflow = null;
        for (var stableChecks = 0; stableChecks < 2; stableChecks++)
        {
            await WaitUntilAsync(
                () =>
                {
                    workflow = Shell.CurrentScreen as WorkflowExecutionScreenViewModel;
                    return workflow is not null &&
                        !workflow.RefreshCommand.IsRunning &&
                        workflow.HasAccount && workflow.HasWorkflow &&
                        workflow.SelectedPath is not null && workflow.SelectedAction is not null;
                },
                "workflow-workspace");
            await Task.Delay(150);
        }

        await WaitUntilAsync(
            () => workflow?.SelectedAction is not null && !workflow.RefreshCommand.IsRunning,
            "stable-workflow-workspace");

        return workflow!;
    }

    private async Task<CheckBox> WaitForCriterionAsync(
        WorkflowCompletionCriterionViewModel criterion)
    {
        CheckBox? match = null;
        await WaitUntilAsync(() =>
        {
            var items = FindControl<ItemsControl>("workflow-criteria-acknowledge");
            match = items?.GetLogicalDescendants()
                .OfType<CheckBox>()
                .SingleOrDefault(candidate => ReferenceEquals(candidate.DataContext, criterion));
            return match is not null;
        }, "workflow-criterion-control");
        return match!;
    }

    private async Task ConfirmDialogAsync() => await ClickAsync("confirmation-confirm");

    private async Task AssertPrimaryActionAsync(string automationId)
    {
        var button = await WaitForControlAsync<Button>(
            automationId,
            control => control.IsEnabled && control.IsVisible);
        if (!button.Classes.Contains("primary"))
        {
            throw Failure($"The normal action '{automationId}' was not presented as primary.");
        }
    }

    private async Task StepAsync(
        string step,
        string controlId,
        Func<Task> execute)
    {
        _currentStep = step;
        Record(step, controlId, "started");
        await execute();
        Record(step, controlId, "passed");
    }

    private async Task ClickAsync(string automationId, bool allowOffscreen = false)
    {
        var button = await WaitForControlAsync<Button>(
            automationId,
            control => control.IsEnabled && control.IsVisible,
            includeHidden: allowOffscreen);
        if (allowOffscreen)
        {
            button.BringIntoView();
            await Task.Yield();
        }

        InvokeButton(button);
    }

    private static void InvokeButton(Button button)
    {
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
            return;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private async Task SetTextAsync(string automationId, string value)
    {
        var textBox = await WaitForControlAsync<TextBox>(automationId);
        textBox.Text = value;
        await Task.Yield();
    }

    private async Task SetCheckedAsync(string automationId, bool value)
    {
        var checkBox = await WaitForControlAsync<CheckBox>(automationId);
        checkBox.IsChecked = value;
        await Task.Yield();
    }

    private async Task<T> WaitForControlAsync<T>(
        string automationId,
        Func<T, bool>? predicate = null,
        TimeSpan? timeout = null,
        bool findAncestor = false,
        bool includeHidden = false)
        where T : Control
    {
        T? control = null;
        await WaitUntilAsync(() =>
        {
            var found = FindControl<Control>(automationId, includeHidden);
            control = findAncestor
                ? found?.GetLogicalAncestors().OfType<T>().FirstOrDefault() ??
                    found?.GetVisualAncestors().OfType<T>().FirstOrDefault()
                : found as T;
            return control is not null && (predicate?.Invoke(control) ?? true);
        }, $"control-{automationId}", timeout);
        return control!;
    }

    private T? FindControl<T>(string automationId, bool includeHidden = false)
        where T : Control => _desktop.Windows
        .Where(window => window.IsVisible)
        .SelectMany(window => window.GetVisualDescendants().Prepend(window))
        .OfType<T>()
        .LastOrDefault(control =>
            string.Equals(
                AutomationProperties.GetAutomationId(control),
                automationId,
                StringComparison.Ordinal) &&
            (includeHidden || control.IsEffectivelyVisible));

    private T? FindVisibleControl<T>()
        where T : Control => _desktop.Windows
        .Where(window => window.IsVisible)
        .SelectMany(window => window.GetVisualDescendants().Prepend(window))
        .OfType<T>()
        .LastOrDefault(control => control.IsEffectivelyVisible);

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string waitName,
        TimeSpan? timeout = null)
    {
        if (!await TryWaitUntilAsync(predicate, timeout ?? ControlTimeout))
        {
            throw Failure($"Timed out while waiting for {waitName}.");
        }
    }

    private static async Task<bool> TryWaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return predicate();
    }

    private void Record(string step, string controlId, string status)
    {
        _steps.Add(new DesktopE2EStepLog(
            DateTimeOffset.UtcNow,
            step,
            Shell.CurrentScreen.Route.ToString(),
            controlId,
            status));
        WriteLog();
    }

    private string CurrentVisibleControlIds()
    {
        var ids = _desktop.Windows
            .Where(window => window.IsVisible)
            .SelectMany(window => window.GetVisualDescendants().Prepend(window))
            .OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(40);
        return string.Join(',', ids);
    }

    private void WriteLog()
    {
        var path = Path.Combine(_configuration.ArtifactDirectory, "desktop-e2e-steps.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_steps, JsonOptions));
    }

    private void WriteResult(bool succeeded, string? failureCode)
    {
        var result = new DesktopE2EResult(
            succeeded,
            failureCode,
            _configuration.Scenario,
            Environment.ProcessId,
            Environment.OSVersion.VersionString,
            ReadDistribution(),
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ??
                Environment.GetEnvironmentVariable("DISPLAY") ??
                (OperatingSystem.IsWindows() ? "windows-desktop" : "unknown"),
            _browserBackend,
            _currentStep,
            _steps.Count,
            _elapsed.ElapsedMilliseconds);
        File.WriteAllText(
            Path.Combine(_configuration.ArtifactDirectory, "desktop-e2e-result.json"),
            JsonSerializer.Serialize(result, JsonOptions));
    }

    private void TryCaptureScreenshot()
    {
        try
        {
            var width = Math.Max(1, (int)Math.Ceiling(_mainWindow.Bounds.Width));
            var height = Math.Max(1, (int)Math.Ceiling(_mainWindow.Bounds.Height));
            using var bitmap = new RenderTargetBitmap(
                new PixelSize(width, height),
                new Vector(96, 96));
            bitmap.Render(_mainWindow);
            bitmap.Save(
                Path.Combine(
                    _configuration.ArtifactDirectory,
                    "desktop-e2e-failure.png"),
                PngBitmapEncoderOptions.Default);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The structured failure result remains available if rendering itself is unavailable.
        }
    }

    private static DesktopE2EFailure Failure(string reason) => new(reason);

    private static string ReadDistribution()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/etc/os-release"))
        {
            return OperatingSystem.IsWindows() ? "windows" : "unknown";
        }

        var prettyName = File.ReadLines("/etc/os-release")
            .FirstOrDefault(line => line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
        return prettyName is null
            ? "linux-unknown"
            : prettyName["PRETTY_NAME=".Length..].Trim('"');
    }

    private static bool IsWithinDirectory(string candidate, string directory)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(directory),
            Path.GetFullPath(candidate));
        return !Path.IsPathFullyQualified(relative) &&
            !string.Equals(relative, "..", PathComparison) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed class DesktopE2EFailure(string safeReason) : Exception
    {
        public string SafeReason { get; } = safeReason;
    }

    private sealed record DesktopE2EStepLog(
        DateTimeOffset Timestamp,
        string Step,
        string Workspace,
        string ControlId,
        string Status);

    private sealed record DesktopE2EResult(
        bool Succeeded,
        string? FailureCode,
        string Scenario,
        int ProcessId,
        string OperatingSystem,
        string Distribution,
        string Display,
        string BrowserBackend,
        string LastStep,
        int StepCount,
        long DurationMilliseconds);
}
