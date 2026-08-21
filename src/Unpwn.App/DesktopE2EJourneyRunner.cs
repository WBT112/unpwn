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
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(30);
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
        await StepAsync("trust-gate", "vault-begin", async () =>
        {
            await ClickAsync("vault-begin");
            await ClickAsync("vault-trusted-yes");
        });

        await StepAsync("create-vault", "vault-primary-action", async () =>
        {
            await ClickAsync("vault-primary-action");
            var path = await WaitForControlAsync<TextBox>("vault-create-path");
            if (string.IsNullOrWhiteSpace(path.Text) ||
                !IsWithinDirectory(path.Text, _configuration.DataRoot))
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

        await StepAsync("create-session", "dashboard-create-session", async () =>
        {
            var name = await WaitForControlAsync<TextBox>("dashboard-session-name");
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                throw Failure("The automatic recovery session name was empty.");
            }

            await SetCheckedAsync("dashboard-security-acknowledge", true);
            await ClickAsync("dashboard-create-session");
            await WaitUntilAsync(
                () => Shell.CurrentScreen.Route == AppRoute.CsvImport,
                "dashboard-session-ready");
        });

        await StepAsync("open-csv-import", "import-open-csv", async () =>
        {
            await ClickAsync("import-open-csv");
            await WaitForControlAsync<Button>("import-reviewed", control => control.IsEnabled);
            await ClickAsync("import-reviewed");
        });

        await StepAsync("categorize-accounts", "accounts-triage-list", async () =>
        {
            var accounts = await WaitForControlAsync<ListBox>(
                "accounts-triage-list",
                control => control.Items.Count == 1);
            accounts.SelectedIndex = 0;
            var category = await WaitForControlAsync<ComboBox>(
                "accounts-category",
                control => control.Items.Count > 0);
            category.SelectedIndex = 0;
            await Task.Yield();
            await ClickAsync("accounts-category-save");
            await ClickAsync("accounts-continue-recovery");
        });

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
        });

        await CompleteRecoveryActionsAsync();
        await CompleteRecoverySessionAsync();
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
        await ClickAsync("credentials-continue-completion");
        await WaitUntilAsync(
            () => Shell.CurrentScreen is CompletionScreenViewModel,
            "completion-workspace");
        Record(
            "credential-handoff-transition",
            "credentials-continue-completion",
            $"route={Shell.CurrentScreen.Route};status={Shell.CurrentStatus.State}");

        var completion = (CompletionScreenViewModel)Shell.CurrentScreen;
        if (!completion.HasReview)
        {
            await ClickAsync("completion-review");
        }

        await WaitUntilAsync(() => completion.HasReview, "completion-report");
        Record("completion-report", "completion-review", "visible");
        if (completion.RequiresRiskAcceptance)
        {
            await SetCheckedAsync("completion-accept-risks", true);
        }

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
            Environment.ProcessId,
            OperatingSystem.IsWindows() ? "windows" : "linux",
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
        int ProcessId,
        string Platform,
        string BrowserBackend,
        string LastStep,
        int StepCount,
        long DurationMilliseconds);
}
