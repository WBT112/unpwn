using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public sealed record CompletionNavigationRequest(
    AppRoute Route,
    Guid? AccountId,
    string? ActionId);

public sealed class RecoveryCompletionIssueViewModel
{
    public RecoveryCompletionIssueViewModel(
        RecoveryCompletionIssue issue,
        ILocalizationService localization,
        Action<RecoveryCompletionIssue> navigate,
        bool canNavigate)
    {
        Issue = issue ?? throw new ArgumentNullException(nameof(issue));
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(navigate);
        Title = localization.GetString($"Completion.Issue.{issue.Kind}");
        Detail = localization.Format(
            "Completion.Issue.Detail",
            issue.ProviderId ?? localization.GetString("Completion.Issue.SessionLevel"),
            issue.Count);
        Severity = localization.GetString($"Completion.Severity.{issue.Severity}");
        NavigateCommand = new RelayCommand(
            () => navigate(issue),
            () => canNavigate && issue.AccountId is not null);
    }

    public RecoveryCompletionIssue Issue { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Severity { get; }

    public RelayCommand NavigateCommand { get; }
}

public sealed class CompletionScreenViewModel : LocalizedScreenViewModel
{
    private readonly IRecoveryCompletionService _completionService;
    private readonly IRecoveryCompletionReportWriter _reportWriter;
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IShellContextService _shellContext;
    private RecoveryCompletionPreflight? _preflight;
    private RecoveryCompletionReport? _report;
    private IReadOnlyList<RecoveryCompletionIssueViewModel> _issues = [];
    private bool _acceptUnresolvedRisks;
    private string? _destinationPath;
    private bool _isReadOnly;

    public CompletionScreenViewModel(
        IRecoveryCompletionService completionService,
        IRecoveryCompletionReportWriter reportWriter,
        IConfirmationDialogService confirmationDialog,
        IShellContextService shellContext,
        ILocalizationService localization)
        : base(
            AppRoute.Completion,
            localization,
            "Screen.Completion.Title",
            "Screen.Completion.Description",
            AppVisualState.UnresolvedRisk,
            "Screen.Completion.StatusTitle",
            "Screen.Completion.StatusMessage")
    {
        _completionService = completionService ?? throw new ArgumentNullException(nameof(completionService));
        _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _shellContext = shellContext ?? throw new ArgumentNullException(nameof(shellContext));
        ReviewCompletionCommand = Command(ReviewAsync, () => HasUnlockedVault);
        CompleteCommand = Command(token => CompleteAsync(archive: false, token), CanComplete);
        ArchiveCommand = Command(token => CompleteAsync(archive: true, token), CanComplete);
        ExportReportCommand = Command(ExportReportAsync, CanExportReport);
        OpenCredentialsCommand = new RelayCommand(
            () => RequestNavigation(AppRoute.CredentialsExport, null, null),
            () => !IsReadOnly);
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
    }

    public event EventHandler<CompletionNavigationRequest>? NavigationRequested;

    public event EventHandler? CompletionReviewSucceeded;

    public AsyncCommand ReviewCompletionCommand { get; }

    public AsyncCommand CompleteCommand { get; }

    public AsyncCommand ArchiveCommand { get; }

    public AsyncCommand ExportReportCommand { get; }

    public RelayCommand OpenCredentialsCommand { get; }

    public bool HasUnlockedVault => _shellContext.Current.IsVaultUnlocked;

    public bool HasReview => _preflight is not null && _report is not null;

    public bool IsClean => _preflight?.IsClean == true;

    public bool RequiresRiskAcceptance => _preflight?.RequiresExplicitRiskAcceptance == true;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set => SetProperty(ref _isReadOnly, value);
    }

    public bool AcceptUnresolvedRisks
    {
        get => _acceptUnresolvedRisks;
        set
        {
            if (SetProperty(ref _acceptUnresolvedRisks, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string? DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                ExportReportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<RecoveryCompletionIssueViewModel> Issues
    {
        get => _issues;
        private set => SetProperty(ref _issues, value);
    }

    public string AccountsSummary => _report is null
        ? Localization.GetString("Completion.Summary.Unavailable")
        : Localization.Format("Completion.Summary.Accounts", _report.AccountsReviewed, _report.AccountsTotal);

    public string CriticalSummary => _report is null
        ? Localization.GetString("Completion.Summary.Unavailable")
        : Localization.Format(
            "Completion.Summary.Critical",
            _report.CriticalAccountsReady,
            _report.CriticalAccountsTotal);

    public string ActionSummary => _report is null
        ? Localization.GetString("Completion.Summary.Unavailable")
        : Localization.Format(
            "Completion.Summary.Actions",
            _report.RequiredActionsCompleted,
            _report.RequiredActionsOpen,
            _report.RequiredActionsInProgress,
            _report.RequiredActionsAwaitingUser,
            _report.RequiredActionsNotApplicable,
            _report.BlockedActions,
            _report.FailedActions,
            _report.AcceptedRiskActions,
            _report.AccountsWaitingForDependencies);

    public string CredentialSummary => _report is null
        ? Localization.GetString("Completion.Summary.Unavailable")
        : Localization.Format(
            "Completion.Summary.Credentials",
            _report.CredentialsNotExported,
            _report.PasswordManagerImportsUnconfirmed,
            _report.RetainedCredentials,
            _report.DeletedCredentials,
            _report.PlaintextCleanupPending);

    public override void Activate()
    {
        if (ReviewCompletionCommand.CanExecute(null))
        {
            ReviewCompletionCommand.Execute(null);
        }
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        RebuildIssues();
        OnPropertyChanged(nameof(AccountsSummary));
        OnPropertyChanged(nameof(CriticalSummary));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(CredentialSummary));
    }

    private AsyncCommand Command(Func<CancellationToken, Task> execute, Func<bool> canExecute) =>
        new(execute, () => Localization.GetString("Completion.Command.Error"), canExecute);

    private async Task ReviewAsync(CancellationToken cancellationToken)
    {
        var result = await _completionService.ReviewAsync(cancellationToken);
        if (!result.Succeeded || result.Preflight is null || result.Report is null)
        {
            SetLocalizedStatus(
                AppVisualState.Error,
                "Completion.ReviewFailed.Title",
                $"Completion.Error.{result.FailureCode}");
            ClearReview();
            return;
        }

        _preflight = result.Preflight;
        _report = result.Report;
        IsReadOnly = result.ExistingCompletion is not null;
        AcceptUnresolvedRisks = false;
        RebuildIssues();
        NotifyReviewChanged();
        if (result.ExistingCompletion is not null)
        {
            SetLocalizedStatus(
                AppVisualState.Normal,
                "Completion.ReadOnly.Title",
                $"Completion.Outcome.{result.ExistingCompletion.Outcome}");
        }
        else if (result.Preflight.IsClean)
        {
            SetLocalizedStatus(
                AppVisualState.Success,
                "Completion.Clean.Title",
                "Completion.Clean.Message");
        }
        else if (!result.Preflight.RequiresExplicitRiskAcceptance)
        {
            SetLocalizedStatus(
                AppVisualState.Warning,
                "Completion.Warnings.Title",
                "Completion.Warnings.Message");
        }
        else
        {
            SetLocalizedStatus(
                AppVisualState.UnresolvedRisk,
                "Completion.Risks.Title",
                "Completion.Risks.Message");
        }

        if (result.ExistingCompletion is null)
        {
            CompletionReviewSucceeded?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task CompleteAsync(bool archive, CancellationToken cancellationToken)
    {
        if (_preflight is null)
        {
            return;
        }

        var actionKey = archive
            ? "Completion.Archive.Confirmation.Action"
            : "Completion.Confirmation.Action";
        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString(actionKey),
                _shellContext.Current.SessionDisplayName ?? Localization.GetString("Shell.Context.NoSession"),
                Localization.GetString(RequiresRiskAcceptance
                    ? "Completion.Confirmation.UnresolvedConsequence"
                    : "Completion.Confirmation.CleanConsequence"),
                Localization.GetString(archive
                    ? "Completion.Archive.Confirmation.Confirm"
                    : "Completion.Confirmation.Confirm"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: archive),
            cancellationToken);
        if (!confirmed)
        {
            SetLocalizedStatus(
                AppVisualState.Normal,
                "Completion.Canceled.Title",
                "Completion.Canceled.Message");
            return;
        }

        var result = await _completionService.CompleteAsync(
            _preflight,
            AcceptUnresolvedRisks,
            archive,
            cancellationToken);
        if (!result.Succeeded || result.Completion is null)
        {
            SetLocalizedStatus(
                result.FailureCode == RecoveryCompletionFailureCode.RiskAcceptanceRequired
                    ? AppVisualState.UnresolvedRisk
                    : AppVisualState.Error,
                "Completion.Failed.Title",
                $"Completion.Error.{result.FailureCode}");
            if (result.FailureCode == RecoveryCompletionFailureCode.StateChanged)
            {
                await ReviewAsync(cancellationToken);
            }

            return;
        }

        IsReadOnly = true;
        RebuildIssues();
        SetLocalizedStatus(
            AppVisualState.Success,
            "Completion.Confirmed.Title",
            $"Completion.Outcome.{result.Completion.Outcome}");
        RaiseCommandStates();
    }

    private async Task ExportReportAsync(CancellationToken cancellationToken)
    {
        if (_report is null || string.IsNullOrWhiteSpace(DestinationPath))
        {
            return;
        }

        var result = await _reportWriter.WriteAsync(_report, DestinationPath, cancellationToken);
        SetLocalizedStatus(
            result.Succeeded ? AppVisualState.Success : AppVisualState.Error,
            result.Succeeded ? "Completion.Export.Success.Title" : "Completion.Export.Failed.Title",
            result.Succeeded
                ? "Completion.Export.Success.Message"
                : $"Completion.Export.Error.{result.FailureCode}");
    }

    private bool CanComplete() =>
        HasUnlockedVault && HasReview && !IsReadOnly &&
        (!RequiresRiskAcceptance || AcceptUnresolvedRisks);

    private bool CanExportReport() =>
        HasReview && !string.IsNullOrWhiteSpace(DestinationPath);

    private void Navigate(RecoveryCompletionIssue issue)
    {
        var route = issue.Kind is
            RecoveryCompletionIssueKind.CredentialNotExported or
            RecoveryCompletionIssueKind.PasswordManagerImportUnconfirmed or
            RecoveryCompletionIssueKind.CredentialRetainedInVault or
            RecoveryCompletionIssueKind.PlaintextExportCleanupPending
                ? AppRoute.CredentialsExport
                : issue.Kind is RecoveryCompletionIssueKind.UnconfirmedRoleInference or
                    RecoveryCompletionIssueKind.DependencyIssue
                    ? AppRoute.Accounts
                    : AppRoute.Workflow;
        RequestNavigation(route, issue.AccountId, issue.ActionId);
    }

    private void RequestNavigation(AppRoute route, Guid? accountId, string? actionId) =>
        NavigationRequested?.Invoke(this, new CompletionNavigationRequest(route, accountId, actionId));

    private void RebuildIssues() => Issues = _preflight is null
        ? []
        : [.. _preflight.Issues.Select(issue =>
            new RecoveryCompletionIssueViewModel(issue, Localization, Navigate, !IsReadOnly))];

    private void ClearReview()
    {
        _preflight = null;
        _report = null;
        IsReadOnly = false;
        Issues = [];
        NotifyReviewChanged();
    }

    private void NotifyReviewChanged()
    {
        OnPropertyChanged(nameof(HasReview));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(RequiresRiskAcceptance));
        OnPropertyChanged(nameof(AccountsSummary));
        OnPropertyChanged(nameof(CriticalSummary));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(CredentialSummary));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        CompleteCommand.RaiseCanExecuteChanged();
        ArchiveCommand.RaiseCanExecuteChanged();
        ExportReportCommand.RaiseCanExecuteChanged();
        OpenCredentialsCommand.RaiseCanExecuteChanged();
    }

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasUnlockedVault));
        ReviewCompletionCommand.RaiseCanExecuteChanged();
        RaiseCommandStates();
    }
}
