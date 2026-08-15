using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Providers.Workflows;

namespace Unpwn.App.Presentation;

public sealed record RecoveryPathOptionViewModel(RecoveryPath Path, string Label);

public sealed record WorkflowActionItemViewModel(
    string DefinitionId,
    string Title,
    string Status,
    string Symbol,
    AppVisualState VisualState,
    bool IsRequired,
    bool HasUnresolvedRisk);

public sealed record WorkflowOverviewReturnRequest(string FeedbackResourceKey);

public sealed record RecoveryBrowserWorkspaceRequest(
    Guid AccountId,
    RecoveryNavigationHandoff Handoff,
    RecoveryBrowserContentMode ContentMode);

public sealed class WorkflowCompletionCriterionViewModel : ObservableObject
{
    private bool _isAcknowledged;

    internal WorkflowCompletionCriterionViewModel(
        string resourceKey,
        string text,
        bool isAcknowledged,
        Func<string, bool, CancellationToken, Task> persist,
        Func<string> failureMessage)
    {
        ResourceKey = resourceKey;
        Text = text;
        _isAcknowledged = isAcknowledged;
        ToggleCommand = new AsyncCommand(
            token => persist(ResourceKey, !IsAcknowledged, token),
            failureMessage);
    }

    public string ResourceKey { get; }

    public string Text { get; private set; }

    public bool IsAcknowledged
    {
        get => _isAcknowledged;
        internal set => SetProperty(ref _isAcknowledged, value);
    }

    public AsyncCommand ToggleCommand { get; }

    internal void RefreshText(string text)
    {
        Text = text;
        OnPropertyChanged(nameof(Text));
    }
}

public enum GuidedRecoveryProblem
{
    LostAccess,
    WaitingForProvider,
    MissingPrerequisite,
    ProviderStepFailed,
    TrulyNotApplicable,
    AcceptUnresolvedRisk,
    ReviewAdvancedDetails,
}

public sealed record GuidedRecoveryProblemOption(
    GuidedRecoveryProblem Value,
    string Label,
    string Explanation);

public sealed class WorkflowExecutionScreenViewModel : LocalizedScreenViewModel
{
    private readonly IAccountInventoryService _inventory;
    private readonly IRecoverySessionService _session;
    private readonly IAccountRecoveryExecutionService _executionService;
    private readonly IRecoveryLocationDiscoveryService _locationDiscovery;
    private readonly IExternalNavigationService _externalNavigation;
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IGeneratedCredentialRepository? _generatedCredentials;
    private readonly IRecoveryBrowserSessionLifecycle? _browserSessions;
    private Guid? _requestedAccountId;
    private string? _requestedActionId;
    private AccountInventoryEntry? _account;
    private AccountRecoveryOrderItem? _orderItem;
    private RecoveryWorkflowDefinition? _workflow;
    private AccountRecoveryExecutionState? _execution;
    private RecoveryNavigationHandoff? _preparedNavigation;
    private RecoveryPathOptionViewModel? _selectedPath;
    private WorkflowActionItemViewModel[] _actions = [];
    private WorkflowActionItemViewModel? _selectedAction;
    private string _reason = string.Empty;
    private string _notes = string.Empty;
    private WorkflowCompletionCriterionViewModel[] _completionCriteria = [];
    private string? _validationKey;
    private string? _navigationStatusKey;
    private ExternalNavigationFailureCode _navigationFailureCode;
    private GuidedRecoveryProblemOption[] _problemOptions = [];
    private GuidedRecoveryProblemOption? _selectedProblem;
    private bool _isProblemReviewVisible;
    private bool _isAdvancedStatusVisible;
    private bool _isBrowserWorkspaceVisible;
    private long _currentActionFocusRequest;

    public WorkflowExecutionScreenViewModel(
        IAccountInventoryService inventory,
        IRecoverySessionService session,
        IAccountRecoveryExecutionService executionService,
        IRecoveryLocationDiscoveryService locationDiscovery,
        IExternalNavigationService externalNavigation,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        IGeneratedCredentialRepository? generatedCredentials = null,
        IRecoveryBrowserSessionLifecycle? browserSessions = null)
        : base(
            AppRoute.Workflow,
            localization,
            "Screen.Workflow.Title",
            "Screen.Workflow.Description",
            AppVisualState.Blocked,
            "Screen.Workflow.StatusTitle",
            "Screen.Workflow.StatusMessage")
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _locationDiscovery = locationDiscovery ?? throw new ArgumentNullException(nameof(locationDiscovery));
        _externalNavigation = externalNavigation ?? throw new ArgumentNullException(nameof(externalNavigation));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _generatedCredentials = generatedCredentials;
        _browserSessions = browserSessions;

        RefreshCommand = Command(LoadAsync, () => _inventory.CurrentInventory is not null);
        BeginCommand = Command(BeginAsync, () =>
            _account is not null && _workflow is not null && _execution is null && SelectedPath is not null);
        StartRecoveryCommand = Command(
            StartRecoveryTransactionAsync,
            () => _account is not null && _workflow is not null && HasSafeRecoveryPath);
        SetAccessAvailableCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.SetAccessAvailable,
            requiresReason: false,
            returnToPlan: false);
        SetAccessLostCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.SetAccessLost,
            requiresReason: true,
            returnToPlan: true);
        SetWaitingCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.SetWaitingForProviderReview,
            requiresReason: true,
            returnToPlan: true);
        StartActionCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.StartAction,
            requiresReason: false,
            returnToPlan: false,
            canExecute: CanStartCurrentAction);
        RetryActionCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.StartAction,
            requiresReason: false,
            returnToPlan: false,
            canExecute: CanRetryCurrentAction);
        CompleteActionCommand = Command(CompleteCurrentActionAsync, CanCompleteCurrentAction);
        RequireUserActionCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.RequireUserAction,
            requiresReason: true,
            returnToPlan: true,
            canExecute: CanRequireUserAction);
        BlockActionCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.BlockAction,
            requiresReason: true,
            returnToPlan: true,
            canExecute: CanBlockCurrentAction);
        FailActionCommand = TransitionCommand(
            AccountRecoveryExecutionTransitionKind.FailAction,
            requiresReason: true,
            returnToPlan: true,
            canExecute: CanFailCurrentAction);
        MarkNotApplicableCommand = Command(MarkNotApplicableAsync, CanTransitionCurrentAction);
        AcceptRiskCommand = Command(AcceptRiskAsync, CanAcceptRisk);
        SaveNotesCommand = Command(SaveNotesAsync, () => _execution is not null && CurrentActionState is not null);
        OpenRecoveryBrowserCommand = Command(OpenRecoveryBrowserAsync, () => HasNavigationOpportunity);
        OpenOfficialPageCommand = Command(OpenOfficialPageAsync, () => HasNavigationOpportunity);
        GenerateCredentialCommand = Command(GenerateCredentialAsync, CanGenerateCredential);
        GuidedPrimaryActionCommand = Command(GuidedPrimaryActionAsync, CanRunGuidedPrimaryAction);
        ShowProblemReviewCommand = new RelayCommand(
            () => IsProblemReviewVisible = true,
            CanReportProblem);
        CancelProblemReviewCommand = new RelayCommand(() => IsProblemReviewVisible = false);
        ApplyGuidedProblemCommand = Command(ApplyGuidedProblemAsync, CanApplyGuidedProblem);
        ShowAdvancedStatusCommand = new RelayCommand(() => IsAdvancedStatusVisible = true);
        ShowGuidedActionCommand = new RelayCommand(() => IsAdvancedStatusVisible = false);

        _inventory.InventoryChanged += Inventory_OnInventoryChanged;
        _session.SessionChanged += Session_OnSessionChanged;
        RefreshProblemOptions();
        RefreshProjection();
    }

    public event EventHandler<WorkflowOverviewReturnRequest>? OverviewReturnRequested;

    public event EventHandler<RecoveryBrowserWorkspaceRequest>? RecoveryBrowserRequested;

    internal IRecoveryBrowserSessionLifecycle? BrowserSessions => _browserSessions;

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand BeginCommand { get; }

    public AsyncCommand StartRecoveryCommand { get; }

    public AsyncCommand SetAccessAvailableCommand { get; }

    public AsyncCommand SetAccessLostCommand { get; }

    public AsyncCommand SetWaitingCommand { get; }

    public AsyncCommand StartActionCommand { get; }

    public AsyncCommand RetryActionCommand { get; }

    public AsyncCommand CompleteActionCommand { get; }

    public AsyncCommand RequireUserActionCommand { get; }

    public AsyncCommand BlockActionCommand { get; }

    public AsyncCommand FailActionCommand { get; }

    public AsyncCommand MarkNotApplicableCommand { get; }

    public AsyncCommand AcceptRiskCommand { get; }

    public AsyncCommand SaveNotesCommand { get; }

    public AsyncCommand OpenOfficialPageCommand { get; }

    public AsyncCommand OpenRecoveryBrowserCommand { get; }

    public AsyncCommand GenerateCredentialCommand { get; }

    public AsyncCommand GuidedPrimaryActionCommand { get; }

    public RelayCommand ShowProblemReviewCommand { get; }

    public RelayCommand CancelProblemReviewCommand { get; }

    public AsyncCommand ApplyGuidedProblemCommand { get; }

    public RelayCommand ShowAdvancedStatusCommand { get; }

    public RelayCommand ShowGuidedActionCommand { get; }

    public RecoveryPathOptionViewModel? SelectedPath => _selectedPath;

    public IReadOnlyList<WorkflowActionItemViewModel> Actions => _actions;

    public WorkflowActionItemViewModel? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value))
            {
                return;
            }

            _notes = CurrentActionState?.UserNotes ?? string.Empty;
            OnPropertyChanged(nameof(Notes));
            RefreshCompletionCriteria();
            _navigationStatusKey = null;
            _navigationFailureCode = ExternalNavigationFailureCode.None;
            _preparedNavigation = null;
            IsProblemReviewVisible = false;
            CurrentActionFocusRequest++;
            NotifyCurrentActionProperties();
            RaiseCommandStates();
        }
    }

    public string Reason
    {
        get => _reason;
        set
        {
            if (SetProperty(ref _reason, value ?? string.Empty))
            {
                ClearValidation();
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    public IReadOnlyList<WorkflowCompletionCriterionViewModel> CompletionCriteria =>
        _completionCriteria;

    public bool CompletionCriteriaAcknowledged =>
        _completionCriteria.Length > 0 && _completionCriteria.All(criterion => criterion.IsAcknowledged);

    public IReadOnlyList<GuidedRecoveryProblemOption> ProblemOptions => _problemOptions;

    public GuidedRecoveryProblemOption? SelectedProblem
    {
        get => _selectedProblem;
        set
        {
            if (SetProperty(ref _selectedProblem, value))
            {
                OnPropertyChanged(nameof(SelectedProblemExplanation));
                ApplyGuidedProblemCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedProblemExplanation => SelectedProblem?.Explanation ?? string.Empty;

    public bool IsProblemReviewVisible
    {
        get => _isProblemReviewVisible;
        private set => SetProperty(ref _isProblemReviewVisible, value);
    }

    public bool IsAdvancedStatusVisible
    {
        get => _isAdvancedStatusVisible;
        private set
        {
            if (SetProperty(ref _isAdvancedStatusVisible, value))
            {
                OnPropertyChanged(nameof(IsGuidedActionVisible));
            }
        }
    }

    public bool IsGuidedActionVisible => !IsAdvancedStatusVisible;

    public bool IsBrowserWorkspaceVisible
    {
        get => _isBrowserWorkspaceVisible;
        private set
        {
            if (SetProperty(ref _isBrowserWorkspaceVisible, value))
            {
                OnPropertyChanged(nameof(AssistantGridColumn));
                OnPropertyChanged(nameof(AssistantGridColumnSpan));
            }
        }
    }

    public int AssistantGridColumn => IsBrowserWorkspaceVisible ? 1 : 0;

    public int AssistantGridColumnSpan => IsBrowserWorkspaceVisible ? 1 : 2;

    public long CurrentActionFocusRequest
    {
        get => _currentActionFocusRequest;
        private set => SetProperty(ref _currentActionFocusRequest, value);
    }

    public bool HasAccount => _account is not null;

    public bool HasWorkflow => _workflow is not null;

    public bool IsGeneralManualWorkflow =>
        _workflow?.TrustLevel == RecoveryWorkflowTrustLevel.GeneralManualGuidance;

    public bool IsReviewedProviderWorkflow =>
        _workflow?.TrustLevel == RecoveryWorkflowTrustLevel.ReviewedProvider;

    public string WorkflowTrustTitle => Localization.GetString(IsGeneralManualWorkflow
        ? "Workflow.Trust.General.Title"
        : "Workflow.Trust.Reviewed.Title");

    public string WorkflowTrustMessage => Localization.GetString(IsGeneralManualWorkflow
        ? "Workflow.Trust.General.Message"
        : "Workflow.Trust.Reviewed.Message");

    public bool HasExecution => _execution is not null;

    public bool HasCurrentAction => CurrentDefinition is not null;

    public bool HasOfficialLocation => CurrentLocation is not null || _preparedNavigation is not null;

    public bool HasPreparedNavigation => _preparedNavigation is not null;

    public string NavigationLocationTitle => Localization.GetString(IsGeneralManualWorkflow
        ? "Workflow.Navigation.DiscoveredTitle"
        : "Workflow.Navigation.Title");

    public bool HasCredentialReference => CurrentActionState?.CredentialReference is not null;

    public bool IsPasswordCredentialAction => CurrentDefinition?.Type is
        RecoveryActionType.ChangePassword or RecoveryActionType.ResetPassword;

    public string AuthenticationGuidanceText => Localization.GetString(
        "Workflow.Guided.AuthenticationGuidance");

    public string ReplacementCredentialGuidanceText => Localization.GetString(
        HasCredentialReference
            ? "Workflow.Guided.Credential.ReplacementPending"
            : "Workflow.Guided.Credential.PreChangeLogin");

    public bool CanGenerateCredentialForCurrentAction => CanGenerateCredential();

    public bool CanRunGuidedPrimary => CanRunGuidedPrimaryAction();

    public bool CanReportCurrentProblem => CanReportProblem();

    public bool IsCurrentActionInProgress =>
        CurrentActionState?.Status == RecoveryActionStatus.InProgress;

    public bool HasCurrentActionFinished => CurrentActionState?.Status is
        RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable;

    public string GuidedPrimaryActionText => CanDiscoverCurrentLocation && _preparedNavigation is null
        ? Localization.GetString(CurrentActionState?.Status is
            RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction
                ? "Workflow.Guided.Primary.RetryAndDiscover"
                : "Workflow.Guided.Primary.StartAndDiscover")
        : CurrentActionState?.Status is
        RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction
            ? Localization.GetString(HasOfficialLocation
                ? "Workflow.Guided.Primary.RetryAndOpen"
                : "Workflow.Guided.Primary.Retry")
            : Localization.GetString(HasOfficialLocation
                ? "Workflow.Guided.Primary.StartAndOpen"
                : "Workflow.Guided.Primary.Start");

    public string CurrentActionWhyText => Localization.Format(
        "Workflow.Guided.Action.Why",
        RecommendationReasonText);

    public bool HasRecordedReason => CurrentActionState?.ReasonCode != RecoveryActionReasonCode.None;

    public bool HasAccessReason => !string.IsNullOrWhiteSpace(_execution?.AccessReason);

    public bool HasValidationMessage => _validationKey is not null;

    public bool HasNavigationStatus => _navigationStatusKey is not null ||
        _navigationFailureCode != ExternalNavigationFailureCode.None;

    public string ValidationMessage => _validationKey is null
        ? string.Empty
        : Localization.GetString(_validationKey);

    public string NavigationStatus => _navigationFailureCode != ExternalNavigationFailureCode.None
        ? Localization.GetString(_navigationFailureCode == ExternalNavigationFailureCode.Rejected
            ? "Workflow.Navigation.Rejected"
            : "Workflow.Navigation.Unavailable")
        : _navigationStatusKey is null
            ? string.Empty
            : Localization.GetString(_navigationStatusKey);

    public string AccountName => _account?.AccountName ??
        _account?.LoginIdentifier ??
        _account?.ProviderId ??
        Localization.GetString("Workflow.Account.Unavailable");

    public string ProviderName => _workflow?.ProviderName ?? _account?.ProviderId ?? string.Empty;

    public string LoginIdentifier => _account?.LoginIdentifier ??
        Localization.GetString("Workflow.Account.LoginUnavailable");

    public string CategoryText => _account is null
        ? string.Empty
        : Localization.GetString($"Accounts.Category.{_account.EffectiveCategory}");

    public string CategoryDecisionText
    {
        get
        {
            return _account is null
                ? string.Empty
                : Localization.GetString(_account.IsCategorized
                    ? "Accounts.Triage.Explicit"
                    : "Accounts.Triage.Suggested");
        }
    }

    public string WorkflowIndependenceText
    {
        get
        {
            return Localization.GetString("Workflow.Account.CategoryIndependentWorkflow");
        }
    }

    public bool HasSafeRecoveryPath => SelectedPath is not null &&
        _execution?.PathSelectionReason != RecoveryPathSelectionReasonCode.NoSafeSupportedPath;

    public string SelectedPathText => HasSafeRecoveryPath
        ? SelectedPath?.Label ?? string.Empty
        : Localization.GetString("Workflow.Path.Blocked");

    public string PathSelectionReasonText
    {
        get
        {
            var reason = _execution?.PathSelectionReason ??
                (_workflow is null
                    ? RecoveryPathSelectionReasonCode.NoSafeSupportedPath
                    : RecoveryPathSelector.Select(_workflow).ReasonCode);
            return Localization.GetString($"Workflow.Path.Reason.{reason}");
        }
    }

    public string RecommendationReasonText => _orderItem is null
        ? Localization.GetString("Workflow.Recommendation.Unavailable")
        : Localization.GetString($"Accounts.Queue.Reason.{_orderItem.ReasonCode}");

    public string AccessStateText => Localization.GetString(
        $"Workflow.Access.{_execution?.AccessState ?? RecoveryAccessState.Unknown}");

    public string AccessReasonText => _execution?.AccessReason ?? string.Empty;

    public string RecoveryStatusText => _execution is null
        ? Localization.GetString("Workflow.Status.NotStarted")
        : Localization.GetString($"Workflow.Status.{_execution.RecoveryStatus}");

    public string CurrentActionTitle => CurrentDefinition is null
        ? string.Empty
        : Localization.GetString(CurrentDefinition.Guidance.TitleKey);

    public string CurrentActionInstruction => CurrentDefinition is null
        ? string.Empty
        : Localization.GetString(CurrentDefinition.Guidance.InstructionKey);

    public string CurrentActionWarning => CurrentDefinition?.Guidance.WarningKey is { } key
        ? Localization.GetString(key)
        : string.Empty;

    public bool HasCurrentActionWarning => CurrentDefinition?.Guidance.WarningKey is not null;

    public string CurrentActionProgressText
    {
        get
        {
            if (SelectedAction is null || Actions.Count == 0)
            {
                return string.Empty;
            }

            var index = Actions.ToList().FindIndex(action =>
                string.Equals(action.DefinitionId, SelectedAction.DefinitionId, StringComparison.Ordinal));
            return Localization.Format("Workflow.Action.Progress", index + 1, Actions.Count);
        }
    }

    public string CurrentActionImportanceText => CurrentDefinition is null
        ? string.Empty
        : Localization.GetString($"Workflow.Importance.{CurrentDefinition.Importance}");

    public string CurrentActionAutomationText => CurrentDefinition is null
        ? string.Empty
        : Localization.GetString($"Workflow.Automation.{CurrentDefinition.AutomationSupport}");

    public string PrerequisitesText
    {
        get
        {
            if (CurrentDefinition is null || _workflow is null || CurrentDefinition.Prerequisites.Count == 0)
            {
                return Localization.GetString("Workflow.Action.NoPrerequisites");
            }

            return string.Join(", ", CurrentDefinition.Prerequisites.Select(id =>
                Localization.GetString(_workflow.Actions.Single(action => action.Id == id).Guidance.TitleKey)));
        }
    }

    public string OfficialLocationText =>
        _preparedNavigation?.Destination.AbsoluteUri ?? CurrentLocation?.Url.AbsoluteUri ?? string.Empty;

    public string ExpectedOriginsText => _preparedNavigation is not null
        ? string.Join(", ", _preparedNavigation.ExpectedOrigins)
        : CurrentLocation is null
            ? string.Empty
            : string.Join(", ", CurrentLocation.ExpectedOrigins);

    public string CredentialReferenceText => CurrentActionState?.CredentialReference is { } reference
        ? Localization.Format("Workflow.Credential.Reference", reference.CredentialId)
        : Localization.GetString("Workflow.Credential.None");

    public string RecordedReasonText
    {
        get
        {
            var action = CurrentActionState;
            if (action is null || action.ReasonCode == RecoveryActionReasonCode.None)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(action.UserReason))
            {
                return action.UserReason;
            }

            if (action.ReasonCode == RecoveryActionReasonCode.WaitingForPrerequisite && _workflow is not null)
            {
                var prerequisites = action.ReasonArguments.Select(id =>
                    Localization.GetString(_workflow.Actions.Single(definition => definition.Id == id).Guidance.TitleKey));
                return Localization.Format(
                    "Workflow.Reason.WaitingForPrerequisite",
                    string.Join(", ", prerequisites));
            }

            return Localization.GetString($"Workflow.Reason.{action.ReasonCode}");
        }
    }

    public void Activate(Guid? accountId, string? actionId, bool startRecovery = false)
    {
        _requestedAccountId = accountId;
        _requestedActionId = actionId;
        _ = ActivateAsync(startRecovery);
    }

    public void ReportRecoveryBrowserOpenResult(bool succeeded, bool workspaceVisible = false)
    {
        IsBrowserWorkspaceVisible = succeeded || workspaceVisible;
        _navigationStatusKey = succeeded
            ? "Workflow.Browser.Opened"
            : "Workflow.Browser.Unavailable";
        NotifyNavigationStatus();
    }

    public void ReportRecoveryBrowserClosed()
    {
        IsBrowserWorkspaceVisible = false;
        _navigationStatusKey = CompletionCriteriaAcknowledged
            ? "Workflow.Browser.ClosedConfirmed"
            : "Workflow.Browser.ClosedIncomplete";
        NotifyNavigationStatus();
    }

    public async Task AttachCredentialReferenceAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_execution is null || _workflow is null || SelectedAction is null)
        {
            throw new InvalidOperationException("An active workflow action is required.");
        }

        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.AttachCredentialReference,
            cancellationToken,
            credentialReference: reference,
            returnToPlan: false);
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        RefreshProblemOptions();
        RefreshPathSelection();
        RefreshActions();
        NotifyAccountProperties();
        NotifyCurrentActionProperties();
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(NavigationStatus));
    }

    private AsyncCommand Command(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null) =>
        new(execute, () => Localization.GetString("Workflow.Command.Error"), canExecute);

    private AsyncCommand TransitionCommand(
        AccountRecoveryExecutionTransitionKind transition,
        bool requiresReason,
        bool returnToPlan,
        Func<bool>? canExecute = null) =>
        Command(
            token => ApplyFromInputAsync(transition, requiresReason, returnToPlan, token),
            canExecute ?? (() => _execution is not null));

    private async Task ActivateAsync(bool startRecovery)
    {
        await RefreshCommand.ExecuteAsync();
        if (!startRecovery || _account is null)
        {
            return;
        }

        await StartRecoveryCommand.ExecuteAsync();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ClearValidation();
        _navigationStatusKey = null;
        _navigationFailureCode = ExternalNavigationFailureCode.None;
        _preparedNavigation = null;
        var inventory = _inventory.CurrentInventory;
        if (inventory is null)
        {
            SetUnavailable("Workflow.Validation.InventoryUnavailable");
            return;
        }

        _account = ResolveAccount(inventory);
        if (_account is null)
        {
            SetUnavailable("Workflow.Validation.AccountUnavailable");
            return;
        }

        _orderItem = _inventory.CurrentRecoveryOrder?.Items.SingleOrDefault(item => item.AccountId == _account.Id);
        var reviewedWorkflow = ResolveReviewedWorkflow(_account);
        var fullWorkflow = reviewedWorkflow ??
            RepositoryWorkflowCatalog.CreateGenericManualWorkflow(_account.ProviderId);
        _workflow = AccountRecoveryWorkflowScope.Project(
            fullWorkflow,
            _account.EffectiveCategory);
        _execution = null;

        var loaded = await _executionService.LoadAsync(_account.Id, _workflow, cancellationToken);
        if (loaded.Succeeded)
        {
            _execution = loaded.State;
        }
        else if (loaded.FailureCode != AccountRecoveryExecutionFailureCode.NotFound)
        {
            _validationKey = FailureKey(loaded.FailureCode);
        }

        if (_execution is null && !RecoveryPathSelector.Select(_workflow).HasSafePath)
        {
            _validationKey = "Workflow.Validation.NoSafeRecoveryPath";
        }

        RefreshProjection();
    }

    private async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_account is null || _workflow is null || SelectedPath is null)
        {
            SetValidation("Workflow.Validation.NoSafeRecoveryPath");
            return;
        }

        var result = await _executionService.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                _account.Id,
                _workflow,
                CreateProjectionContext()),
            cancellationToken);
        ApplyResult(result);
        if (result.Succeeded)
        {
            SetLocalizedStatus(
                AppVisualState.Normal,
                "Workflow.Status.Started.Title",
                "Workflow.Status.Started.Message");
        }
    }

    private async Task StartRecoveryTransactionAsync(CancellationToken cancellationToken)
    {
        if (_execution is null)
        {
            await BeginAsync(cancellationToken);
        }

        if (_execution is not null)
        {
            await ContinueCurrentActionAsync(cancellationToken);
        }
    }

    private async Task CompleteCurrentActionAsync(CancellationToken cancellationToken)
    {
        if (!CompletionCriteriaAcknowledged)
        {
            SetValidation("Workflow.Validation.CriteriaRequired");
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Workflow.Complete.Confirmation.Action"),
                CurrentActionTitle,
                Localization.GetString("Workflow.Complete.Confirmation.Consequence"),
                Localization.GetString("Workflow.Complete.Confirmation.Confirm"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.CompleteAction,
            cancellationToken,
            completionCriteriaAcknowledged: true,
            returnToPlan: false);

        if (_execution is not null && HasRemainingRecoveryAction())
        {
            SelectedAction = Actions.FirstOrDefault(action =>
                _execution.GetAction(action.DefinitionId).Status is not
                    (RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable));
            return;
        }

        OverviewReturnRequested?.Invoke(
            this,
            new WorkflowOverviewReturnRequest("Workflow.Queue.Changed"));
    }

    private async Task MarkNotApplicableAsync(CancellationToken cancellationToken)
    {
        if (!RequireReason())
        {
            return;
        }

        var confirmed = await ConfirmRiskDecisionAsync(
            "Workflow.NotApplicable.Confirmation.Action",
            "Workflow.NotApplicable.Confirmation.Consequence",
            "Workflow.NotApplicable.Confirmation.Confirm",
            cancellationToken);
        if (confirmed)
        {
            await ApplyAsync(
                AccountRecoveryExecutionTransitionKind.MarkTrulyNotApplicable,
                cancellationToken,
                userReason: Reason,
                returnToPlan: true);
        }
    }

    private async Task AcceptRiskAsync(CancellationToken cancellationToken)
    {
        if (!RequireReason())
        {
            return;
        }

        var confirmed = await ConfirmRiskDecisionAsync(
            "Workflow.Risk.Confirmation.Action",
            "Workflow.Risk.Confirmation.Consequence",
            "Workflow.Risk.Confirmation.Confirm",
            cancellationToken);
        if (confirmed)
        {
            await ApplyAsync(
                AccountRecoveryExecutionTransitionKind.AcceptUnresolvedRisk,
                cancellationToken,
                userReason: Reason,
                returnToPlan: true);
        }
    }

    private Task<bool> ConfirmRiskDecisionAsync(
        string actionKey,
        string consequenceKey,
        string confirmKey,
        CancellationToken cancellationToken) =>
        _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString(actionKey),
                CurrentActionTitle,
                Localization.GetString(consequenceKey),
                Localization.GetString(confirmKey),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);

    private async Task SaveNotesAsync(CancellationToken cancellationToken)
    {
        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.SetUserNotes,
            cancellationToken,
            userNotes: Notes,
            returnToPlan: false);
    }

    private async Task GenerateCredentialAsync(CancellationToken cancellationToken)
    {
        if (_generatedCredentials is null || _account is null)
        {
            return;
        }

        using var generated = await _generatedCredentials.GenerateAsync(
            _account.Id,
            CredentialGenerationPolicy.Default,
            Guid.NewGuid(),
            cancellationToken);
        if (!generated.Succeeded || generated.Metadata is null)
        {
            SetValidation(generated.FailureCode == GeneratedCredentialFailureCode.Locked
                ? "Workflow.Validation.Locked"
                : "Workflow.Credential.GenerationFailed");
            return;
        }

        await AttachCredentialReferenceAsync(generated.Metadata.Reference, cancellationToken);
        NotifyCurrentActionProperties();
        RaiseCommandStates();
    }

    private async Task GuidedPrimaryActionAsync(CancellationToken cancellationToken)
    {
        if (!CanRunGuidedPrimaryAction())
        {
            return;
        }

        await ContinueCurrentActionAsync(cancellationToken);
    }

    private async Task ContinueCurrentActionAsync(CancellationToken cancellationToken)
    {
        if (CurrentActionState?.Status is
            RecoveryActionStatus.Open or RecoveryActionStatus.Blocked or
            RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction)
        {
            await ApplyAsync(
                AccountRecoveryExecutionTransitionKind.StartAction,
                cancellationToken,
                returnToPlan: false);
        }

        if (CurrentActionState?.Status == RecoveryActionStatus.InProgress)
        {
            await OpenRecoveryBrowserAsync(
                allowBrowserEntryFallback: true,
                cancellationToken);
        }
    }

    private async Task SetCompletionCriterionAsync(
        string resourceKey,
        bool acknowledged,
        CancellationToken cancellationToken)
    {
        if (_execution is null || CurrentActionState?.Status != RecoveryActionStatus.InProgress)
        {
            return;
        }

        var acknowledgedCriteria = CurrentActionState.AcknowledgedCompletionCriteria.ToHashSet(
            StringComparer.Ordinal);
        if (acknowledged)
        {
            acknowledgedCriteria.Add(resourceKey);
        }
        else
        {
            acknowledgedCriteria.Remove(resourceKey);
        }

        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements,
            cancellationToken,
            acknowledgedCompletionCriteria: [.. acknowledgedCriteria],
            returnToPlan: false,
            preserveNavigation: true);
    }

    private Task OpenRecoveryBrowserAsync(CancellationToken cancellationToken) =>
        OpenRecoveryBrowserAsync(allowBrowserEntryFallback: false, cancellationToken);

    private async Task OpenRecoveryBrowserAsync(
        bool allowBrowserEntryFallback,
        CancellationToken cancellationToken)
    {
        var handoff = await PrepareNavigationAsync(
            allowBrowserEntryFallback,
            cancellationToken);
        if (handoff is null || _account is null || _browserSessions is null)
        {
            if (_browserSessions is null)
            {
                _navigationFailureCode = ExternalNavigationFailureCode.None;
                _navigationStatusKey = "Workflow.Browser.Unavailable";
                NotifyNavigationStatus();
            }
            return;
        }

        _navigationStatusKey = "Workflow.Browser.Opening";
        NotifyNavigationStatus();
        var request = new RecoveryBrowserWorkspaceRequest(
            _account.Id,
            handoff,
            RecoveryBrowserContentMode.Recovery);
        if (RecoveryBrowserRequested is null)
        {
            ReportRecoveryBrowserOpenResult(false);
            return;
        }

        RecoveryBrowserRequested.Invoke(this, request);
    }

    private async Task ApplyGuidedProblemAsync(CancellationToken cancellationToken)
    {
        if (SelectedProblem?.Value == GuidedRecoveryProblem.ReviewAdvancedDetails)
        {
            IsProblemReviewVisible = false;
            IsAdvancedStatusVisible = true;
            return;
        }

        if (!RequireReason() || SelectedProblem is null)
        {
            return;
        }

        switch (SelectedProblem.Value)
        {
            case GuidedRecoveryProblem.LostAccess:
                await ApplyAsync(
                    AccountRecoveryExecutionTransitionKind.SetAccessLost,
                    cancellationToken,
                    userReason: Reason,
                    returnToPlan: true);
                break;
            case GuidedRecoveryProblem.WaitingForProvider:
                await ApplyAsync(
                    AccountRecoveryExecutionTransitionKind.SetWaitingForProviderReview,
                    cancellationToken,
                    userReason: Reason,
                    returnToPlan: true);
                break;
            case GuidedRecoveryProblem.MissingPrerequisite:
                await ApplyAsync(
                    AccountRecoveryExecutionTransitionKind.BlockAction,
                    cancellationToken,
                    userReason: Reason,
                    returnToPlan: true);
                break;
            case GuidedRecoveryProblem.ProviderStepFailed:
                await ApplyAsync(
                    AccountRecoveryExecutionTransitionKind.FailAction,
                    cancellationToken,
                    userReason: Reason,
                    returnToPlan: true);
                break;
            case GuidedRecoveryProblem.TrulyNotApplicable:
                await MarkNotApplicableAsync(cancellationToken);
                break;
            case GuidedRecoveryProblem.AcceptUnresolvedRisk:
                await AcceptRiskAsync(cancellationToken);
                break;
            case GuidedRecoveryProblem.ReviewAdvancedDetails:
                break;
            default:
                throw new InvalidOperationException("The guided recovery problem is unsupported.");
        }
    }

    private async Task OpenOfficialPageAsync(CancellationToken cancellationToken)
    {
        var handoff = await PrepareNavigationAsync(cancellationToken);
        if (handoff is null)
        {
            return;
        }

        var opened = await _externalNavigation.OpenAsync(handoff.Destination, cancellationToken);
        _navigationFailureCode = opened.FailureCode;
        _navigationStatusKey = opened.Succeeded ? "Workflow.Navigation.OpenedExternal" : null;
        NotifyNavigationStatus();
    }

    private Task<RecoveryNavigationHandoff?> PrepareNavigationAsync(
        CancellationToken cancellationToken) =>
        PrepareNavigationAsync(allowBrowserEntryFallback: false, cancellationToken);

    private async Task<RecoveryNavigationHandoff?> PrepareNavigationAsync(
        bool allowBrowserEntryFallback,
        CancellationToken cancellationToken)
    {
        if (_workflow is null || CurrentDefinition is null)
        {
            return null;
        }

        if (_preparedNavigation is { } prepared)
        {
            return prepared;
        }

        var accountUri = Uri.TryCreate(_account?.AccountUrl, UriKind.Absolute, out var parsed)
            ? parsed
            : null;
        var providerLocationId = CurrentDefinition.RecoveryLocationId;
        var selectionPolicy = providerLocationId is null
            ? RecoveryLocationSelectionPolicy.WellKnownFirst
            : RecoveryLocationSelectionPolicy.ProviderDefinedOnly;

        if (allowBrowserEntryFallback &&
            providerLocationId is null &&
            !CanDiscoverCurrentLocation)
        {
            providerLocationId = ResolveBrowserEntryLocationId();
            if (providerLocationId is not null)
            {
                selectionPolicy = RecoveryLocationSelectionPolicy.ProviderDefinedOnly;
            }
            else if (_workflow.AllowsAccountOriginDiscovery && accountUri is not null)
            {
                selectionPolicy = RecoveryLocationSelectionPolicy.AccountOriginOnly;
            }
            else
            {
                SetBrowserEntryFailure(RecoveryLocationDiscoveryFailureCode.InvalidRequest);
                return null;
            }
        }

        var discovery = await _locationDiscovery.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                _workflow,
                providerLocationId,
                accountUri,
                selectionPolicy),
            cancellationToken);
        if (!discovery.Succeeded || discovery.Handoff is not { RequiresVisibleConfirmation: true } handoff)
        {
            if (allowBrowserEntryFallback)
            {
                SetBrowserEntryFailure(discovery.FailureCode);
            }
            else
            {
                _navigationFailureCode = ExternalNavigationFailureCode.Unavailable;
                _navigationStatusKey = null;
                NotifyNavigationStatus();
            }
            return null;
        }

        _preparedNavigation = handoff;
        _navigationFailureCode = ExternalNavigationFailureCode.None;
        _navigationStatusKey = "Workflow.Navigation.Prepared";
        NotifyCurrentActionProperties();
        NotifyNavigationStatus();
        RaiseCommandStates();
        return handoff;
    }

    private string? ResolveBrowserEntryLocationId()
    {
        if (_workflow is null || SelectedPath is null)
        {
            return null;
        }

        return _workflow.Actions
            .Where(action => action.SupportsPath(SelectedPath.Path))
            .Select(action => action.RecoveryLocationId)
            .FirstOrDefault(locationId => !string.IsNullOrWhiteSpace(locationId));
    }

    private void SetBrowserEntryFailure(RecoveryLocationDiscoveryFailureCode failureCode)
    {
        _navigationFailureCode = ExternalNavigationFailureCode.None;
        _navigationStatusKey = failureCode switch
        {
            RecoveryLocationDiscoveryFailureCode.InsecureAccountOrigin =>
                "Workflow.Browser.LocationInsecure",
            RecoveryLocationDiscoveryFailureCode.UnsafeNetworkTarget =>
                "Workflow.Browser.LocationRejected",
            RecoveryLocationDiscoveryFailureCode.InvalidRequest or
            RecoveryLocationDiscoveryFailureCode.ProviderLocationNotFound =>
                "Workflow.Browser.LocationMissing",
            _ => "Workflow.Browser.LocationUnavailable",
        };
        NotifyNavigationStatus();
        CurrentActionFocusRequest++;
    }

    private async Task ApplyFromInputAsync(
        AccountRecoveryExecutionTransitionKind transition,
        bool requiresReason,
        bool returnToPlan,
        CancellationToken cancellationToken)
    {
        if (requiresReason && !RequireReason())
        {
            return;
        }

        await ApplyAsync(
            transition,
            cancellationToken,
            userReason: requiresReason ? Reason : null,
            returnToPlan: returnToPlan);
    }

    private async Task ApplyAsync(
        AccountRecoveryExecutionTransitionKind transition,
        CancellationToken cancellationToken,
        string? userReason = null,
        string? userNotes = null,
        bool completionCriteriaAcknowledged = false,
        GeneratedCredentialReference? credentialReference = null,
        string[]? acknowledgedCompletionCriteria = null,
        bool returnToPlan = false,
        bool preserveNavigation = false)
    {
        if (_account is null || _workflow is null || _execution is null)
        {
            return;
        }

        var previousRecommendation = _session.Dashboard?.Recommendation;
        var result = await _executionService.ApplyAsync(
            new AccountRecoveryExecutionTransitionRequest(
                Guid.NewGuid(),
                _account.Id,
                _execution.Revision,
                _workflow,
                transition,
                SelectedAction?.DefinitionId,
                userReason,
                userNotes,
                completionCriteriaAcknowledged,
                credentialReference,
                CreateProjectionContext())
            { AcknowledgedCompletionCriteria = acknowledgedCompletionCriteria },
            cancellationToken);
        ApplyResult(result, preserveNavigation);
        var blockedByPrerequisite = transition == AccountRecoveryExecutionTransitionKind.StartAction &&
            result.State is not null &&
            SelectedAction is not null &&
            result.State.GetAction(SelectedAction.DefinitionId).Status == RecoveryActionStatus.Blocked;
        if (!result.Succeeded || (!returnToPlan && !blockedByPrerequisite))
        {
            return;
        }

        var currentRecommendation = _session.Dashboard?.Recommendation;
        var feedbackKey = Equals(previousRecommendation, currentRecommendation)
            ? "Workflow.Queue.Unchanged"
            : "Workflow.Queue.Changed";
        OverviewReturnRequested?.Invoke(this, new WorkflowOverviewReturnRequest(feedbackKey));
    }

    private void ApplyResult(
        AccountRecoveryExecutionResult result,
        bool preserveNavigation = false)
    {
        if (!result.Succeeded)
        {
            SetValidation(FailureKey(result.FailureCode));
            return;
        }

        var hadExecution = _execution is not null;
        _execution = result.State;
        _reason = string.Empty;
        if (!preserveNavigation)
        {
            _navigationStatusKey = null;
            _navigationFailureCode = ExternalNavigationFailureCode.None;
            _preparedNavigation = null;
        }
        IsProblemReviewVisible = false;
        ClearValidation();
        RefreshProjection();
        if (!hadExecution && _execution is not null)
        {
            CurrentActionFocusRequest++;
        }
    }

    private AccountRecoveryProjectionContext CreateProjectionContext()
    {
        return new AccountRecoveryProjectionContext(
            _account?.EffectiveCategory ?? AccountRecoveryCategory.Unknown);
    }

    private AccountInventoryEntry? ResolveAccount(AccountInventoryState inventory)
    {
        if (_requestedAccountId is { } requested)
        {
            return inventory.Accounts.SingleOrDefault(account => account.Id == requested);
        }

        var recommendedId = _session.Dashboard?.Recommendation.AccountId ??
            _inventory.CurrentRecoveryOrder?.Recommended?.AccountId;
        return recommendedId is { } accountId
            ? inventory.Accounts.SingleOrDefault(account => account.Id == accountId)
            : inventory.Accounts.FirstOrDefault();
    }

    private static RecoveryWorkflowDefinition? ResolveReviewedWorkflow(AccountInventoryEntry account)
    {
        var accountHost = Uri.TryCreate(account.AccountUrl, UriKind.Absolute, out var accountUri)
            ? accountUri.Host
            : null;
        return RepositoryWorkflowCatalog.Workflows.SingleOrDefault(workflow =>
            string.Equals(workflow.ProviderId, account.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workflow.ProviderName, account.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workflow.ProviderId, accountHost, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshProjection()
    {
        RefreshPathSelection();
        RefreshActions();
        NotifyAccountProperties();
        NotifyCurrentActionProperties();
        RaiseCommandStates();

        if (_execution is null)
        {
            var isBlocked = _workflow is null || !HasSafeRecoveryPath;
            SetLocalizedStatus(
                isBlocked ? AppVisualState.Blocked : AppVisualState.Normal,
                isBlocked ? "Workflow.Status.Blocked.Title" : "Workflow.Status.Ready.Title",
                isBlocked ? "Workflow.Status.Blocked.Message" : "Workflow.Status.Ready.Message");
            return;
        }

        var visual = _execution.RecoveryStatus switch
        {
            AccountRecoveryStatus.FullyReviewed => AppVisualState.Success,
            AccountRecoveryStatus.NotFullySecured => AppVisualState.UnresolvedRisk,
            AccountRecoveryStatus.AccessNotRestored => AppVisualState.Error,
            AccountRecoveryStatus.InProgress => AppVisualState.Warning,
            _ => AppVisualState.Normal,
        };
        SetLocalizedStatus(
            visual,
            "Workflow.Status.Active.Title",
            "Workflow.Status.Active.Message");
    }

    private void RefreshPathSelection()
    {
        var selected = _execution is null && _workflow is not null
            ? RecoveryPathSelector.Select(_workflow).Path
            : _execution?.SelectedPath;
        _selectedPath = selected is { } path
            ? new RecoveryPathOptionViewModel(
                path,
                Localization.GetString($"Workflow.Path.{path}"))
            : null;
        OnPropertyChanged(nameof(SelectedPath));
        OnPropertyChanged(nameof(SelectedPathText));
        OnPropertyChanged(nameof(PathSelectionReasonText));
        OnPropertyChanged(nameof(HasSafeRecoveryPath));
    }

    private void RefreshProblemOptions()
    {
        var selected = SelectedProblem?.Value ?? GuidedRecoveryProblem.LostAccess;
        _problemOptions =
        [
            .. Enum.GetValues<GuidedRecoveryProblem>().Select(value => new GuidedRecoveryProblemOption(
                value,
                Localization.GetString($"Workflow.Guided.Problem.{value}.Label"),
                Localization.GetString($"Workflow.Guided.Problem.{value}.Explanation"))),
        ];
        _selectedProblem = _problemOptions.Single(option => option.Value == selected);
        OnPropertyChanged(nameof(ProblemOptions));
        OnPropertyChanged(nameof(SelectedProblem));
        OnPropertyChanged(nameof(SelectedProblemExplanation));
    }

    private void RefreshActions()
    {
        var previousActionId = _selectedAction?.DefinitionId;
        var selectedId = SelectedAction?.DefinitionId ?? _requestedActionId;
        if (_workflow is null || SelectedPath is null)
        {
            _actions = [];
            _selectedAction = null;
        }
        else
        {
            _actions =
            [
                .. _workflow.Actions
                    .Where(definition => definition.SupportsPath(SelectedPath.Path))
                    .Select(definition => CreateActionItem(
                        definition,
                        _execution?.Actions.SingleOrDefault(action => action.DefinitionId == definition.Id))),
            ];
            _selectedAction = _actions.FirstOrDefault(action => action.DefinitionId == selectedId) ??
                _actions.FirstOrDefault(action =>
                    _execution?.GetAction(action.DefinitionId).Status is not
                        (RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable)) ??
                (_actions.Length > 0 ? _actions[0] : null);
        }

        _requestedActionId = null;
        _notes = CurrentActionState?.UserNotes ?? string.Empty;
        RefreshCompletionCriteria();
        OnPropertyChanged(nameof(Actions));
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(Notes));
        if (!string.Equals(previousActionId, _selectedAction?.DefinitionId, StringComparison.Ordinal))
        {
            CurrentActionFocusRequest++;
        }
    }

    private WorkflowActionItemViewModel CreateActionItem(
        RecoveryActionDefinition definition,
        RecoveryActionExecutionState? state)
    {
        var status = state?.Status ?? RecoveryActionStatus.Open;
        var visualState = state?.HasUnresolvedRisk == true
            ? AppVisualState.UnresolvedRisk
            : status switch
            {
                RecoveryActionStatus.Completed => AppVisualState.Success,
                RecoveryActionStatus.Blocked => AppVisualState.Blocked,
                RecoveryActionStatus.Failed => AppVisualState.Error,
                RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction => AppVisualState.Warning,
                _ => AppVisualState.Normal,
            };
        var symbol = visualState switch
        {
            AppVisualState.Success => "✓",
            AppVisualState.Blocked => "■",
            AppVisualState.Error => "×",
            AppVisualState.Warning => "!",
            AppVisualState.UnresolvedRisk => "?",
            _ => "○",
        };
        return new WorkflowActionItemViewModel(
            definition.Id,
            Localization.GetString(definition.Guidance.TitleKey),
            Localization.GetString(state?.HasUnresolvedRisk == true
                ? "Workflow.Action.Status.UnresolvedRisk"
                : $"Workflow.Action.Status.{status}"),
            symbol,
            visualState,
            definition.IsRequired,
            state?.HasUnresolvedRisk == true);
    }

    private RecoveryActionDefinition? CurrentDefinition => _workflow is null || SelectedAction is null
        ? null
        : _workflow.Actions.SingleOrDefault(action => action.Id == SelectedAction.DefinitionId);

    private RecoveryActionExecutionState? CurrentActionState => _execution is null || SelectedAction is null
        ? null
        : _execution.Actions.SingleOrDefault(action => action.DefinitionId == SelectedAction.DefinitionId);

    private RecoveryLocationDefinition? CurrentLocation => _workflow is null || CurrentDefinition?.RecoveryLocationId is not { } id
        ? null
        : _workflow.RecoveryLocations.SingleOrDefault(location => location.Id == id);

    private bool CanStartCurrentAction() => CurrentActionState?.Status == RecoveryActionStatus.Open;

    private bool CanRetryCurrentAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction;

    private bool CanCompleteCurrentAction() =>
        CurrentActionState?.Status == RecoveryActionStatus.InProgress && CompletionCriteriaAcknowledged;

    private bool HasRemainingRecoveryAction() =>
        _execution is not null &&
        _execution.Actions.Any(action => action.Status is not
            (RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable));

    private void RefreshCompletionCriteria()
    {
        var existing = _completionCriteria.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        var acknowledged = CurrentActionState?.AcknowledgedCompletionCriteria.ToHashSet(
            StringComparer.Ordinal) ?? [];
        _completionCriteria = CurrentDefinition?.Guidance.CompletionCriteriaKeys
            .Select(key =>
            {
                if (existing.TryGetValue(key, out var item))
                {
                    item.RefreshText(Localization.GetString(key));
                    item.IsAcknowledged = acknowledged.Contains(key);
                    return item;
                }

                return new WorkflowCompletionCriterionViewModel(
                    key,
                    Localization.GetString(key),
                    acknowledged.Contains(key),
                    SetCompletionCriterionAsync,
                    () => Localization.GetString("Workflow.Command.Error"));
            })
            .ToArray() ?? [];
        OnPropertyChanged(nameof(CompletionCriteria));
        OnPropertyChanged(nameof(CompletionCriteriaAcknowledged));
        CompleteActionCommand.RaiseCanExecuteChanged();
    }

    private bool CanRunGuidedPrimaryAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Open or RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or
        RecoveryActionStatus.NeedsUserAction;

    private bool CanReportProblem() => CurrentActionState?.Status is
        RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or
        RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction;

    private bool CanApplyGuidedProblem() => SelectedProblem?.Value switch
    {
        GuidedRecoveryProblem.LostAccess or GuidedRecoveryProblem.WaitingForProvider => _execution is not null,
        GuidedRecoveryProblem.MissingPrerequisite => CanBlockCurrentAction(),
        GuidedRecoveryProblem.ProviderStepFailed => CanFailCurrentAction(),
        GuidedRecoveryProblem.TrulyNotApplicable => CanTransitionCurrentAction(),
        GuidedRecoveryProblem.AcceptUnresolvedRisk => CanAcceptRisk(),
        GuidedRecoveryProblem.ReviewAdvancedDetails => true,
        _ => false,
    };

    private bool CanTransitionCurrentAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or
        RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction;

    private bool CanRequireUserAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked;

    private bool CanBlockCurrentAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.Failed or
        RecoveryActionStatus.NeedsUserAction;

    private bool CanFailCurrentAction() => CurrentActionState?.Status is
        RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction;

    private bool CanAcceptRisk() => CurrentDefinition?.IsRequired == true &&
        CurrentActionState?.Status is RecoveryActionStatus.InProgress or
            RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction;

    private bool RequireReason()
    {
        if (!string.IsNullOrWhiteSpace(Reason))
        {
            return true;
        }

        SetValidation("Workflow.Validation.ReasonRequired");
        return false;
    }

    private void SetUnavailable(string key)
    {
        _account = null;
        _orderItem = null;
        _workflow = null;
        _execution = null;
        _validationKey = key;
        RefreshProjection();
    }

    private void SetValidation(string key)
    {
        _validationKey = key;
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void ClearValidation()
    {
        if (_validationKey is null)
        {
            return;
        }

        _validationKey = null;
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void NotifyAccountProperties()
    {
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(HasWorkflow));
        OnPropertyChanged(nameof(IsGeneralManualWorkflow));
        OnPropertyChanged(nameof(IsReviewedProviderWorkflow));
        OnPropertyChanged(nameof(WorkflowTrustTitle));
        OnPropertyChanged(nameof(WorkflowTrustMessage));
        OnPropertyChanged(nameof(HasExecution));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(LoginIdentifier));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(CategoryDecisionText));
        OnPropertyChanged(nameof(WorkflowIndependenceText));
        OnPropertyChanged(nameof(HasSafeRecoveryPath));
        OnPropertyChanged(nameof(SelectedPathText));
        OnPropertyChanged(nameof(PathSelectionReasonText));
        OnPropertyChanged(nameof(RecommendationReasonText));
        OnPropertyChanged(nameof(AccessStateText));
        OnPropertyChanged(nameof(HasAccessReason));
        OnPropertyChanged(nameof(AccessReasonText));
        OnPropertyChanged(nameof(RecoveryStatusText));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void NotifyCurrentActionProperties()
    {
        OnPropertyChanged(nameof(HasCurrentAction));
        OnPropertyChanged(nameof(HasOfficialLocation));
        OnPropertyChanged(nameof(HasPreparedNavigation));
        OnPropertyChanged(nameof(NavigationLocationTitle));
        OnPropertyChanged(nameof(HasCredentialReference));
        OnPropertyChanged(nameof(IsPasswordCredentialAction));
        OnPropertyChanged(nameof(AuthenticationGuidanceText));
        OnPropertyChanged(nameof(ReplacementCredentialGuidanceText));
        OnPropertyChanged(nameof(CanGenerateCredentialForCurrentAction));
        OnPropertyChanged(nameof(CanRunGuidedPrimary));
        OnPropertyChanged(nameof(CanReportCurrentProblem));
        OnPropertyChanged(nameof(IsCurrentActionInProgress));
        OnPropertyChanged(nameof(HasCurrentActionFinished));
        OnPropertyChanged(nameof(GuidedPrimaryActionText));
        OnPropertyChanged(nameof(CurrentActionWhyText));
        OnPropertyChanged(nameof(HasRecordedReason));
        OnPropertyChanged(nameof(CurrentActionTitle));
        OnPropertyChanged(nameof(CurrentActionInstruction));
        OnPropertyChanged(nameof(CurrentActionWarning));
        OnPropertyChanged(nameof(HasCurrentActionWarning));
        OnPropertyChanged(nameof(CurrentActionProgressText));
        OnPropertyChanged(nameof(CurrentActionImportanceText));
        OnPropertyChanged(nameof(CurrentActionAutomationText));
        OnPropertyChanged(nameof(CompletionCriteria));
        OnPropertyChanged(nameof(PrerequisitesText));
        OnPropertyChanged(nameof(OfficialLocationText));
        OnPropertyChanged(nameof(ExpectedOriginsText));
        OnPropertyChanged(nameof(CredentialReferenceText));
        OnPropertyChanged(nameof(RecordedReasonText));
    }

    private void NotifyNavigationStatus()
    {
        OnPropertyChanged(nameof(HasNavigationStatus));
        OnPropertyChanged(nameof(NavigationStatus));
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[]
                 {
                     RefreshCommand, BeginCommand, StartRecoveryCommand, SetAccessAvailableCommand,
                     SetAccessLostCommand, SetWaitingCommand, StartActionCommand, RetryActionCommand,
                     CompleteActionCommand, RequireUserActionCommand, BlockActionCommand,
                     FailActionCommand, MarkNotApplicableCommand, AcceptRiskCommand,
                     SaveNotesCommand, OpenOfficialPageCommand,
                     OpenRecoveryBrowserCommand,
                     GenerateCredentialCommand, GuidedPrimaryActionCommand,
                     ApplyGuidedProblemCommand,
                 })
        {
            command.RaiseCanExecuteChanged();
        }

        OnPropertyChanged(nameof(CanGenerateCredentialForCurrentAction));
        OnPropertyChanged(nameof(CanRunGuidedPrimary));
        OnPropertyChanged(nameof(CanReportCurrentProblem));
        ShowProblemReviewCommand.RaiseCanExecuteChanged();
    }

    private bool CanGenerateCredential() =>
        _generatedCredentials?.IsUnlocked == true && _account is not null && _execution is not null &&
        CurrentDefinition?.Type is RecoveryActionType.ChangePassword or RecoveryActionType.ResetPassword &&
        CurrentActionState?.CredentialReference is null;

    private bool CanDiscoverCurrentLocation =>
        _workflow?.AllowsAccountOriginDiscovery == true &&
        CurrentDefinition?.Type == RecoveryActionType.ChangePassword &&
        Uri.TryCreate(_account?.AccountUrl, UriKind.Absolute, out _);

    private bool HasNavigationOpportunity =>
        _preparedNavigation is not null || CurrentLocation is not null || CanDiscoverCurrentLocation;

    private void Inventory_OnInventoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_inventory.CurrentInventory is null)
        {
            _executionService.ClearForLock();
            SetUnavailable("Workflow.Validation.InventoryUnavailable");
            return;
        }

        if (_account is not null &&
            _inventory.CurrentInventory.Accounts.All(account => account.Id != _account.Id))
        {
            SetUnavailable("Workflow.Validation.AccountUnavailable");
        }
    }

    private void Session_OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(RecommendationReasonText));
    }

    private static string FailureKey(AccountRecoveryExecutionFailureCode failureCode) => failureCode switch
    {
        AccountRecoveryExecutionFailureCode.Locked => "Workflow.Validation.Locked",
        AccountRecoveryExecutionFailureCode.NotFound => "Workflow.Validation.NotFound",
        AccountRecoveryExecutionFailureCode.Conflict => "Workflow.Validation.Conflict",
        AccountRecoveryExecutionFailureCode.Corrupted => "Workflow.Validation.Corrupted",
        AccountRecoveryExecutionFailureCode.NoSafeRecoveryPath => "Workflow.Validation.NoSafeRecoveryPath",
        AccountRecoveryExecutionFailureCode.PersistenceFailure => "Workflow.Validation.PersistenceFailure",
        _ => "Workflow.Validation.InvalidInput",
    };
}
