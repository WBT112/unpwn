using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Providers.Workflows;

namespace Unpwn.App.Presentation;

public sealed record DashboardNavigationRequest(
    AppRoute Route,
    Guid? AccountId,
    string? ActionId);

public sealed class DashboardScreenViewModel : LocalizedScreenViewModel
{
    private readonly IRecoverySessionService _sessionService;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly RecoveryWizardSessionService _wizard;
    private readonly IConfirmationDialogService _confirmationDialog;
    private string _sessionName = string.Empty;
    private bool _compromisedRecoveryChannel;
    private bool _securityWarningAcknowledged;
    private string? _validationKey;

    public DashboardScreenViewModel(
        IRecoverySessionService sessionService,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        Func<string?>? localUserName = null)
        : base(
            AppRoute.Dashboard,
            localization,
            "Screen.Dashboard.Title",
            "Screen.Dashboard.Description",
            AppVisualState.Normal,
            "Screen.Dashboard.StatusTitle",
            "Screen.Dashboard.StatusMessage")
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _vaultLifecycle = vaultLifecycle ?? throw new ArgumentNullException(nameof(vaultLifecycle));
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _sessionName = localUserName is null
            ? RecoverySessionNameSuggestion.CreateForCurrentUser()
            : RecoverySessionNameSuggestion.Create(localUserName());

        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => Localization.GetString("Dashboard.Command.Error"),
            () => _vaultLifecycle.Current.IsVaultUnlocked);
        CreateSessionCommand = new AsyncCommand(
            CreateSessionAsync,
            () => Localization.GetString("Dashboard.Command.Error"),
            CanCreateSession);
        PauseCommand = new AsyncCommand(
            PauseAsync,
            () => Localization.GetString("Dashboard.Command.Error"),
            () => IsActiveSession);
        ResumeCommand = new AsyncCommand(
            ResumeAsync,
            () => Localization.GetString("Dashboard.Command.Error"),
            () => IsPausedSession);
        ArchiveCommand = new AsyncCommand(
            ArchiveAsync,
            () => Localization.GetString("Dashboard.Command.Error"),
            () => IsActiveSession || IsPausedSession);
        OpenBlockedCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.BlockedAction),
            () => HasBlockedActions);
        OpenFailedCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.FailedAction),
            () => HasFailedActions);
        OpenUnresolvedRiskCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.UnresolvedRisk),
            () => HasUnresolvedRisks);
        OpenLostAccessCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.LostAccess),
            () => HasLostAccess);
        OpenCredentialExportCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.CredentialExport),
            () => HasCredentialExports);
        OpenCredentialDeletionCommand = new RelayCommand(
            () => NavigateToAlert(RecoveryDashboardAlertKind.CredentialDeletion),
            () => HasCredentialDeletions);
        OpenRecommendationCommand = new RelayCommand(OpenRecommendation, () => Dashboard is not null);
        OpenCompletionCommand = new RelayCommand(
            () => RequestNavigation(AppRoute.Completion, null, null),
            () => IsActiveSession || IsPausedSession);

        _sessionService.SessionChanged += SessionService_OnSessionChanged;
        _vaultLifecycle.ContextChanged += VaultLifecycle_OnContextChanged;
        _wizard.StateChanged += Wizard_OnStateChanged;
        RefreshState();
    }

    public event EventHandler<DashboardNavigationRequest>? NavigationRequested;

    public string SessionName
    {
        get => _sessionName;
        set
        {
            if (SetProperty(ref _sessionName, value ?? string.Empty))
            {
                ClearValidation();
                CreateSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CompromisedRecoveryChannel
    {
        get => _compromisedRecoveryChannel;
        set => SetProperty(ref _compromisedRecoveryChannel, value);
    }

    public bool SecurityWarningAcknowledged
    {
        get => _securityWarningAcknowledged;
        set
        {
            if (SetProperty(ref _securityWarningAcknowledged, value))
            {
                ClearValidation();
                CreateSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLockedState => _sessionService.LoadState == RecoverySessionLoadState.Locked;

    public bool IsLoadingState => _sessionService.LoadState == RecoverySessionLoadState.Loading;

    public bool IsEmptyState => _sessionService.LoadState == RecoverySessionLoadState.Empty;

    public bool IsCorruptedState => _sessionService.LoadState == RecoverySessionLoadState.Corrupted;

    public bool IsDashboardState => _sessionService.LoadState == RecoverySessionLoadState.Loaded;

    public bool IsActiveSession => Session?.Status == RecoveryWorkspaceLifecycleStatus.Active;

    public bool IsPausedSession => Session?.Status == RecoveryWorkspaceLifecycleStatus.Paused;

    public bool IsArchivedSession => Session?.Status == RecoveryWorkspaceLifecycleStatus.Archived;

    public bool HasEmergencyAdvisory => Session?.Incident.RequiresEmergencyAttention == true;

    public bool HasBlockedActions => Dashboard?.BlockedRequiredActions > 0;

    public bool HasFailedActions => Dashboard?.FailedRequiredActions > 0;

    public bool HasUnresolvedRisks => Dashboard?.UnresolvedRisks > 0;

    public bool HasLostAccess => Dashboard?.AccountsWithLostAccess > 0;

    public bool HasCredentialExports => Dashboard?.CredentialsAwaitingExport > 0;

    public bool HasCredentialDeletions => Dashboard?.CredentialsAwaitingDeletion > 0;

    public string? ValidationMessage => _validationKey is null
        ? null
        : Localization.GetString(_validationKey);

    public bool HasValidationMessage => _validationKey is not null;

    public string CurrentSessionName => Session?.Name ?? string.Empty;

    public string SessionStatusText => Localization.GetString(Session?.Status switch
    {
        RecoveryWorkspaceLifecycleStatus.Active => "Dashboard.Session.Status.Active",
        RecoveryWorkspaceLifecycleStatus.Paused => "Dashboard.Session.Status.Paused",
        RecoveryWorkspaceLifecycleStatus.Archived => "Dashboard.Session.Status.Archived",
        _ => "Dashboard.Session.Status.None",
    });

    public string LastSavedText => Session is null
        ? Localization.GetString("Dashboard.Session.LastSaved.None")
        : Localization.Format("Dashboard.Session.LastSaved", Session.UpdatedAt);

    public string VaultText => Localization.Format(
        "Dashboard.Session.Vault",
        _vaultLifecycle.Current.IsVaultUnlocked
            ? _vaultLifecycle.Current.VaultDisplayName
            : Localization.GetString("Shell.Context.NoVault"));

    public string WizardPhaseText => Localization.Format(
        "Dashboard.Session.WizardPhase",
        Localization.GetString(GetWizardStepKey(_wizard.Current.CurrentStep)));

    public string CriticalReadinessText => Dashboard is null
        ? Localization.GetString("Dashboard.Metric.Unavailable")
        : Localization.Format(
            "Dashboard.Critical.Readiness",
            Dashboard.CriticalAccountsReady,
            Dashboard.CriticalAccountsTotal);

    public string AccountCoverageText => Dashboard is null
        ? Localization.GetString("Dashboard.Metric.Unavailable")
        : Localization.Format(
            "Dashboard.Accounts.Coverage",
            Dashboard.AccountsFullyReviewed,
            Dashboard.AccountsTotal);

    public string WeightedProgressText => Dashboard is null
        ? Localization.GetString("Dashboard.Metric.Unavailable")
        : Localization.Format(
            "Dashboard.Progress.Weighted",
            Dashboard.WeightedRequiredActionProgress);

    public string BlockedActionsText => FormatCount("Dashboard.Alert.Blocked.Count", Dashboard?.BlockedRequiredActions ?? 0);

    public string FailedActionsText => FormatCount("Dashboard.Alert.Failed.Count", Dashboard?.FailedRequiredActions ?? 0);

    public string UnresolvedRisksText => FormatCount("Dashboard.Alert.Unresolved.Count", Dashboard?.UnresolvedRisks ?? 0);

    public string LostAccessText => FormatCount("Dashboard.Alert.LostAccess.Count", Dashboard?.AccountsWithLostAccess ?? 0);

    public string CredentialExportsText => FormatCount("Dashboard.Alert.Export.Count", Dashboard?.CredentialsAwaitingExport ?? 0);

    public string CredentialDeletionsText => FormatCount("Dashboard.Alert.Deletion.Count", Dashboard?.CredentialsAwaitingDeletion ?? 0);

    public string RecommendationText
    {
        get
        {
            var recommendation = Dashboard?.Recommendation;
            if (recommendation is null)
            {
                return Localization.GetString("Dashboard.Recommendation.Unavailable");
            }

            return Localization.GetString($"Dashboard.Recommendation.{recommendation.Code}");
        }
    }

    public bool HasRecommendationTarget =>
        !string.IsNullOrWhiteSpace(Dashboard?.Recommendation.ProviderId);

    public string RecommendationTargetText
    {
        get
        {
            var providerId = Dashboard?.Recommendation.ProviderId;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return string.Empty;
            }

            var workflow = ResolveRecommendedWorkflow(providerId);
            return Localization.Format(
                "Dashboard.Recommendation.Target",
                workflow?.ProviderName ?? providerId);
        }
    }

    public bool HasRecommendationCategory => RecommendedAccount is not null;

    public string RecommendationCategoryText => RecommendedAccount is null
        ? string.Empty
        : Localization.Format(
            "Dashboard.Recommendation.Category",
            Localization.GetString($"Accounts.Category.{RecommendedAccount.Category}"));

    public bool HasRecommendationAction =>
        ResolveRecommendedAction() is not null;

    public string RecommendationActionText
    {
        get
        {
            var action = ResolveRecommendedAction();
            return action is null
                ? string.Empty
                : Localization.Format(
                    "Dashboard.Recommendation.Action",
                    Localization.GetString(action.Guidance.TitleKey));
        }
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand CreateSessionCommand { get; }

    public AsyncCommand PauseCommand { get; }

    public AsyncCommand ResumeCommand { get; }

    public AsyncCommand ArchiveCommand { get; }

    public void ShowPlanFeedback(string feedbackResourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackResourceKey);
        SetLocalizedStatus(
            AppVisualState.Normal,
            "Workflow.Plan.Recalculated.Title",
            feedbackResourceKey,
            StatusPresentation.TransientResult);
    }

    public RelayCommand OpenBlockedCommand { get; }

    public RelayCommand OpenFailedCommand { get; }

    public RelayCommand OpenUnresolvedRiskCommand { get; }

    public RelayCommand OpenLostAccessCommand { get; }

    public RelayCommand OpenCredentialExportCommand { get; }

    public RelayCommand OpenCredentialDeletionCommand { get; }

    public RelayCommand OpenRecommendationCommand { get; }

    public RelayCommand OpenCompletionCommand { get; }

    public override void Activate() => _ = RefreshCommand.ExecuteAsync();

    private RecoverySessionWorkspace? Session => _sessionService.CurrentSession;

    private RecoveryDashboardSnapshot? Dashboard => _sessionService.Dashboard;

    private RecoveryAccountDashboardEntry? RecommendedAccount =>
        Session?.Accounts.SingleOrDefault(account =>
            account.AccountId == Dashboard?.Recommendation.AccountId);

    private bool CanCreateSession() =>
        _sessionService.LoadState == RecoverySessionLoadState.Empty &&
        !string.IsNullOrWhiteSpace(SessionName) &&
        SecurityWarningAcknowledged;

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        NotifyLocalizedProperties();
        RefreshVisualStatus();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _sessionService.InitializeAsync(cancellationToken);
    }

    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
        ClearValidation();
        if (string.IsNullOrWhiteSpace(SessionName))
        {
            SetValidation("Dashboard.Validation.NameRequired");
            return;
        }

        if (!SecurityWarningAcknowledged)
        {
            SetValidation("Dashboard.Validation.AcknowledgementRequired");
            return;
        }

        var result = await _sessionService.CreateAsync(
            new RecoverySessionCreateRequest(
                SessionName,
                BuildIndicators(),
                SecurityWarningAcknowledged),
            cancellationToken);
        if (!result.Succeeded)
        {
            ShowOperationFailure(result.FailureCode);
            return;
        }

        ClearIntakeInputs();
        SetLocalizedStatus(
            HasEmergencyAdvisory ? AppVisualState.UnresolvedRisk : AppVisualState.Success,
            HasEmergencyAdvisory
                ? "Dashboard.Status.Emergency.Title"
                : "Dashboard.Status.Created.Title",
            HasEmergencyAdvisory
                ? "Dashboard.Status.Emergency.Message"
                : "Dashboard.Status.Created.Message",
            StatusPresentation.TransientResult);
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        var result = await _sessionService.PauseAsync(cancellationToken);
        if (!result.Succeeded)
        {
            ShowOperationFailure(result.FailureCode);
        }
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        var result = await _sessionService.ResumeAsync(cancellationToken);
        if (!result.Succeeded)
        {
            ShowOperationFailure(result.FailureCode);
        }
    }

    private async Task ArchiveAsync(CancellationToken cancellationToken)
    {
        if (Session is null)
        {
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Dashboard.Archive.Confirmation.Action"),
                Session.Name,
                Localization.GetString("Dashboard.Archive.Confirmation.Consequence"),
                Localization.GetString("Dashboard.Archive.Confirmation.Confirm"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        var result = await _sessionService.ArchiveAsync(cancellationToken);
        if (!result.Succeeded)
        {
            ShowOperationFailure(result.FailureCode);
        }
    }

    private void OpenRecommendation()
    {
        var recommendation = Dashboard?.Recommendation;
        if (recommendation is null)
        {
            return;
        }

        var route = recommendation.Code switch
        {
            RecoveryDashboardRecommendationCode.ImportAccounts => AppRoute.CsvImport,
            RecoveryDashboardRecommendationCode.SecureRecoveryChannel or
            RecoveryDashboardRecommendationCode.RestoreCriticalAccess => AppRoute.Accounts,
            RecoveryDashboardRecommendationCode.ExportGeneratedCredentials => AppRoute.CredentialsExport,
            RecoveryDashboardRecommendationCode.ResumeSession or
            RecoveryDashboardRecommendationCode.ArchivedSession => AppRoute.Dashboard,
            _ => AppRoute.Workflow,
        };
        RequestNavigation(route, recommendation.AccountId, recommendation.ActionId);
    }

    private void NavigateToAlert(RecoveryDashboardAlertKind kind)
    {
        var alert = Dashboard?.Alerts.FirstOrDefault(candidate => candidate.Kind == kind);
        var route = kind switch
        {
            RecoveryDashboardAlertKind.LostAccess => AppRoute.Accounts,
            RecoveryDashboardAlertKind.CredentialExport or
            RecoveryDashboardAlertKind.CredentialDeletion => AppRoute.CredentialsExport,
            _ => AppRoute.Workflow,
        };
        RequestNavigation(route, alert?.AccountId, alert?.ActionId);
    }

    private void RequestNavigation(AppRoute route, Guid? accountId, string? actionId) =>
        NavigationRequested?.Invoke(this, new DashboardNavigationRequest(route, accountId, actionId));

    private IncidentIndicator BuildIndicators()
    {
        var indicators = IncidentIndicator.None;
        indicators = CompromisedRecoveryChannel
            ? indicators | IncidentIndicator.CompromisedRecoveryChannel
            : indicators;
        return indicators;
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsLockedState));
        OnPropertyChanged(nameof(IsLoadingState));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsCorruptedState));
        OnPropertyChanged(nameof(IsDashboardState));
        OnPropertyChanged(nameof(IsActiveSession));
        OnPropertyChanged(nameof(IsPausedSession));
        OnPropertyChanged(nameof(IsArchivedSession));
        OnPropertyChanged(nameof(HasEmergencyAdvisory));
        OnPropertyChanged(nameof(HasBlockedActions));
        OnPropertyChanged(nameof(HasFailedActions));
        OnPropertyChanged(nameof(HasUnresolvedRisks));
        OnPropertyChanged(nameof(HasLostAccess));
        OnPropertyChanged(nameof(HasCredentialExports));
        OnPropertyChanged(nameof(HasCredentialDeletions));
        NotifyLocalizedProperties();
        RaiseCommandStates();
        RefreshVisualStatus();
    }

    private void NotifyLocalizedProperties()
    {
        OnPropertyChanged(nameof(CurrentSessionName));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(LastSavedText));
        OnPropertyChanged(nameof(VaultText));
        OnPropertyChanged(nameof(WizardPhaseText));
        OnPropertyChanged(nameof(CriticalReadinessText));
        OnPropertyChanged(nameof(AccountCoverageText));
        OnPropertyChanged(nameof(WeightedProgressText));
        OnPropertyChanged(nameof(BlockedActionsText));
        OnPropertyChanged(nameof(FailedActionsText));
        OnPropertyChanged(nameof(UnresolvedRisksText));
        OnPropertyChanged(nameof(LostAccessText));
        OnPropertyChanged(nameof(CredentialExportsText));
        OnPropertyChanged(nameof(CredentialDeletionsText));
        OnPropertyChanged(nameof(RecommendationText));
        OnPropertyChanged(nameof(HasRecommendationTarget));
        OnPropertyChanged(nameof(RecommendationTargetText));
        OnPropertyChanged(nameof(HasRecommendationCategory));
        OnPropertyChanged(nameof(RecommendationCategoryText));
        OnPropertyChanged(nameof(HasRecommendationAction));
        OnPropertyChanged(nameof(RecommendationActionText));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void RefreshVisualStatus()
    {
        var (state, titleKey, messageKey) = _sessionService.LoadState switch
        {
            RecoverySessionLoadState.Locked => (
                AppVisualState.Warning,
                "Dashboard.Status.Locked.Title",
                "Dashboard.Status.Locked.Message"),
            RecoverySessionLoadState.Loading => (
                AppVisualState.Normal,
                "Dashboard.Status.Loading.Title",
                "Dashboard.Status.Loading.Message"),
            RecoverySessionLoadState.Empty => (
                AppVisualState.Warning,
                "Dashboard.Status.Empty.Title",
                "Dashboard.Status.Empty.Message"),
            RecoverySessionLoadState.Corrupted => (
                AppVisualState.Error,
                "Dashboard.Status.Corrupted.Title",
                "Dashboard.Status.Corrupted.Message"),
            RecoverySessionLoadState.Loaded when IsArchivedSession => (
                AppVisualState.Normal,
                "Dashboard.Status.Archived.Title",
                "Dashboard.Status.Archived.Message"),
            RecoverySessionLoadState.Loaded when IsPausedSession => (
                AppVisualState.Warning,
                "Dashboard.Status.Paused.Title",
                "Dashboard.Status.Paused.Message"),
            RecoverySessionLoadState.Loaded when HasEmergencyAdvisory => (
                AppVisualState.UnresolvedRisk,
                "Dashboard.Status.Emergency.Title",
                "Dashboard.Status.Emergency.Message"),
            _ => (
                AppVisualState.Normal,
                "Dashboard.Status.Active.Title",
                "Dashboard.Status.Active.Message"),
        };
        SetLocalizedStatus(state, titleKey, messageKey);
    }

    private void ShowOperationFailure(RecoverySessionOperationFailureCode failureCode)
    {
        SetValidation(failureCode switch
        {
            RecoverySessionOperationFailureCode.Locked => "Dashboard.Validation.Locked",
            RecoverySessionOperationFailureCode.InvalidInput => "Dashboard.Validation.InvalidInput",
            RecoverySessionOperationFailureCode.Corrupted => "Dashboard.Validation.Corrupted",
            RecoverySessionOperationFailureCode.Conflict => "Dashboard.Validation.Conflict",
            _ => "Dashboard.Validation.PersistenceFailure",
        });
        SetLocalizedStatus(
            AppVisualState.Error,
            "Dashboard.Status.OperationFailed.Title",
            "Dashboard.Status.OperationFailed.Message",
            StatusPresentation.TransientResult);
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        CreateSessionCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        ArchiveCommand.RaiseCanExecuteChanged();
        OpenBlockedCommand.RaiseCanExecuteChanged();
        OpenFailedCommand.RaiseCanExecuteChanged();
        OpenUnresolvedRiskCommand.RaiseCanExecuteChanged();
        OpenLostAccessCommand.RaiseCanExecuteChanged();
        OpenCredentialExportCommand.RaiseCanExecuteChanged();
        OpenCredentialDeletionCommand.RaiseCanExecuteChanged();
        OpenRecommendationCommand.RaiseCanExecuteChanged();
        OpenCompletionCommand.RaiseCanExecuteChanged();
    }

    private void ClearIntakeInputs()
    {
        SessionName = string.Empty;
        CompromisedRecoveryChannel = false;
        SecurityWarningAcknowledged = false;
        ClearValidation();
    }

    private void ClearValidation()
    {
        if (_validationKey is null)
        {
            return;
        }

        _validationKey = null;
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
    }

    private void SetValidation(string key)
    {
        _validationKey = key;
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
    }

    private string FormatCount(string key, int count) => Localization.Format(key, count);

    private static RecoveryWorkflowDefinition? ResolveRecommendedWorkflow(string providerId) =>
        RepositoryWorkflowCatalog.Workflows.SingleOrDefault(workflow =>
            string.Equals(workflow.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workflow.ProviderName, providerId, StringComparison.OrdinalIgnoreCase));

    private RecoveryActionDefinition? ResolveRecommendedAction()
    {
        var recommendation = Dashboard?.Recommendation;
        if (string.IsNullOrWhiteSpace(recommendation?.ProviderId) ||
            string.IsNullOrWhiteSpace(recommendation.ActionId))
        {
            return null;
        }

        return ResolveRecommendedWorkflow(recommendation.ProviderId)?.Actions.SingleOrDefault(action =>
            string.Equals(action.Id, recommendation.ActionId, StringComparison.Ordinal));
    }

    private static string GetWizardStepKey(RecoveryWizardStepId step) => step.Value switch
    {
        "welcome" => "Dashboard.WizardStep.Welcome",
        "trusted-device-check" => "Dashboard.WizardStep.TrustedDeviceCheck",
        "trusted-device-guidance" => "Dashboard.WizardStep.TrustedDeviceGuidance",
        "vault-entry" => "Dashboard.WizardStep.VaultEntry",
        "incident-intake" => "Dashboard.WizardStep.IncidentIntake",
        "account-inventory" => "Dashboard.WizardStep.AccountInventory",
        "account-triage" => "Dashboard.WizardStep.AccountTriage",
        "recovery-plan" => "Dashboard.WizardStep.RecoveryPlan",
        "account-recovery" => "Dashboard.WizardStep.AccountRecovery",
        "credential-export" => "Dashboard.WizardStep.CredentialExport",
        "completion-preflight" => "Dashboard.WizardStep.CompletionPreflight",
        "final-report" => "Dashboard.WizardStep.FinalReport",
        _ => "Dashboard.WizardStep.Unknown",
    };

    private void SessionService_OnSessionChanged(object? sender, EventArgs eventArgs) => RefreshState();

    private void VaultLifecycle_OnContextChanged(object? sender, EventArgs eventArgs) => RefreshState();

    private void Wizard_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(WizardPhaseText));
    }
}
