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

public sealed record WorkflowPlanReturnRequest(string FeedbackResourceKey);

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
    private Guid? _requestedAccountId;
    private string? _requestedActionId;
    private AccountInventoryEntry? _account;
    private AccountInventoryPlanItem? _planItem;
    private RecoveryWorkflowDefinition? _workflow;
    private AccountRecoveryExecutionState? _execution;
    private RecoveryPathOptionViewModel[] _pathOptions = [];
    private RecoveryPathOptionViewModel? _selectedPath;
    private WorkflowActionItemViewModel[] _actions = [];
    private WorkflowActionItemViewModel? _selectedAction;
    private string _reason = string.Empty;
    private string _notes = string.Empty;
    private bool _completionCriteriaAcknowledged;
    private string? _validationKey;
    private string? _navigationStatusKey;
    private ExternalNavigationFailureCode _navigationFailureCode;
    private GuidedRecoveryProblemOption[] _problemOptions = [];
    private GuidedRecoveryProblemOption? _selectedProblem;
    private bool _isProblemReviewVisible;
    private bool _isAdvancedStatusVisible;
    private long _currentActionFocusRequest;

    public WorkflowExecutionScreenViewModel(
        IAccountInventoryService inventory,
        IRecoverySessionService session,
        IAccountRecoveryExecutionService executionService,
        IRecoveryLocationDiscoveryService locationDiscovery,
        IExternalNavigationService externalNavigation,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        IGeneratedCredentialRepository? generatedCredentials = null)
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

        RefreshCommand = Command(LoadAsync, () => _inventory.CurrentInventory is not null);
        BeginCommand = Command(BeginAsync, () => _account is not null && _workflow is not null && _execution is null && SelectedPath is not null);
        ChangePathCommand = Command(ChangePathAsync, CanChangePath);
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
        OpenOfficialPageCommand = Command(OpenOfficialPageAsync, () => CurrentLocation is not null);
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

    public event EventHandler<WorkflowPlanReturnRequest>? PlanReturnRequested;

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand BeginCommand { get; }

    public AsyncCommand ChangePathCommand { get; }

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

    public AsyncCommand GenerateCredentialCommand { get; }

    public AsyncCommand GuidedPrimaryActionCommand { get; }

    public RelayCommand ShowProblemReviewCommand { get; }

    public RelayCommand CancelProblemReviewCommand { get; }

    public AsyncCommand ApplyGuidedProblemCommand { get; }

    public RelayCommand ShowAdvancedStatusCommand { get; }

    public RelayCommand ShowGuidedActionCommand { get; }

    public IReadOnlyList<RecoveryPathOptionViewModel> PathOptions => _pathOptions;

    public RecoveryPathOptionViewModel? SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(ref _selectedPath, value))
            {
                if (_execution is null)
                {
                    RefreshActions();
                }

                RaiseCommandStates();
            }
        }
    }

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
            CompletionCriteriaAcknowledged = false;
            _navigationStatusKey = null;
            _navigationFailureCode = ExternalNavigationFailureCode.None;
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

    public bool CompletionCriteriaAcknowledged
    {
        get => _completionCriteriaAcknowledged;
        set
        {
            if (SetProperty(ref _completionCriteriaAcknowledged, value))
            {
                ClearValidation();
                CompleteActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

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

    public long CurrentActionFocusRequest
    {
        get => _currentActionFocusRequest;
        private set => SetProperty(ref _currentActionFocusRequest, value);
    }

    public bool HasAccount => _account is not null;

    public bool HasWorkflow => _workflow is not null;

    public bool HasExecution => _execution is not null;

    public bool HasCurrentAction => CurrentDefinition is not null;

    public bool HasOfficialLocation => CurrentLocation is not null;

    public bool HasCredentialReference => CurrentActionState?.CredentialReference is not null;

    public bool CanGenerateCredentialForCurrentAction => CanGenerateCredential();

    public bool CanRunGuidedPrimary => CanRunGuidedPrimaryAction();

    public bool CanReportCurrentProblem => CanReportProblem();

    public bool IsCurrentActionInProgress =>
        CurrentActionState?.Status == RecoveryActionStatus.InProgress;

    public bool HasCurrentActionFinished => CurrentActionState?.Status is
        RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable;

    public string GuidedPrimaryActionText => CurrentActionState?.Status is
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

    public bool CanChangeRecoveryPath => CanChangePath();

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

    public string PriorityText => _account is null
        ? string.Empty
        : Localization.GetString($"Accounts.Priority.{_account.Priority}");

    public string RolesText
    {
        get
        {
            var roles = _account?.Roles
                .Where(role => role.Decision == AccountRoleDecision.Confirmed)
                .Select(role => Localization.GetString($"Accounts.Role.{role.Role}"))
                .ToArray() ?? [];
            return roles.Length == 0
                ? Localization.GetString("Workflow.Account.NoConfirmedRoles")
                : string.Join(", ", roles);
        }
    }

    public string DependenciesText
    {
        get
        {
            if (_account is null || _inventory.CurrentInventory is null || _account.Dependencies.Length == 0)
            {
                return Localization.GetString("Workflow.Account.NoDependencies");
            }

            var byId = _inventory.CurrentInventory.Accounts.ToDictionary(account => account.Id);
            return string.Join(", ", _account.Dependencies.Select(dependency =>
                byId.TryGetValue(dependency.DependsOnAccountId, out var account)
                    ? account.AccountName ?? account.LoginIdentifier ?? account.ProviderId
                    : Localization.GetString("Workflow.Account.MissingDependency")));
        }
    }

    public string RecommendationReasonText => _planItem is null
        ? Localization.GetString("Workflow.Recommendation.Unavailable")
        : AreDependenciesSatisfied
            ? Localization.GetString("Workflow.Recommendation.DependenciesSatisfied")
            : Localization.GetString($"Accounts.Plan.Reason.{_planItem.ReasonCode}");

    public string PlanStatusText => _planItem is null
        ? Localization.GetString("Workflow.Plan.Status.Unavailable")
        : Localization.GetString(AreDependenciesSatisfied
            ? "Workflow.Plan.Status.ReadyNow"
            : $"Workflow.Plan.Status.{_planItem.Status}");

    public string DependentsText
    {
        get
        {
            var count = _account is null || _inventory.CurrentInventory is null
                ? 0
                : _inventory.CurrentInventory.Accounts.Count(candidate =>
                    candidate.Dependencies.Any(dependency =>
                        !dependency.IsOverride && dependency.DependsOnAccountId == _account.Id));
            return Localization.FormatPlural("Workflow.Account.Dependents", count, count);
        }
    }

    public string DependentAccountNamesText
    {
        get
        {
            if (_account is null || _inventory.CurrentInventory is null)
            {
                return string.Empty;
            }

            var names = _inventory.CurrentInventory.Accounts
                .Where(candidate => candidate.Dependencies.Any(dependency =>
                    !dependency.IsOverride && dependency.DependsOnAccountId == _account.Id))
                .Select(candidate => candidate.AccountName ?? candidate.LoginIdentifier ?? candidate.ProviderId)
                .ToArray();
            return names.Length == 0
                ? string.Empty
                : Localization.Format("Workflow.Account.DependentNames", string.Join(", ", names));
        }
    }

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

    public IReadOnlyList<string> CompletionCriteria => CurrentDefinition?.Guidance.CompletionCriteriaKeys
        .Select(Localization.GetString)
        .ToArray() ?? [];

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

    public string OfficialLocationText => CurrentLocation?.Url.AbsoluteUri ?? string.Empty;

    public string ExpectedOriginsText => CurrentLocation is null
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

    public void Activate(Guid? accountId, string? actionId)
    {
        _requestedAccountId = accountId;
        _requestedActionId = actionId;
        _ = RefreshCommand.ExecuteAsync();
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
        RefreshPathOptions();
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ClearValidation();
        _navigationStatusKey = null;
        _navigationFailureCode = ExternalNavigationFailureCode.None;
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

        _planItem = _inventory.CurrentPlan?.Items.SingleOrDefault(item => item.AccountId == _account.Id);
        _workflow = ResolveWorkflow(_account);
        _execution = null;
        if (_workflow is null)
        {
            _validationKey = "Workflow.Validation.ProviderUnsupported";
            RefreshProjection();
            return;
        }

        var loaded = await _executionService.LoadAsync(_account.Id, _workflow, cancellationToken);
        if (loaded.Succeeded)
        {
            _execution = loaded.State;
        }
        else if (loaded.FailureCode != AccountRecoveryExecutionFailureCode.NotFound)
        {
            _validationKey = FailureKey(loaded.FailureCode);
        }

        RefreshProjection();
    }

    private async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_account is null || _workflow is null || SelectedPath is null)
        {
            SetValidation("Workflow.Validation.PathRequired");
            return;
        }

        var result = await _executionService.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                _account.Id,
                _workflow,
                SelectedPath.Path,
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

    private async Task ChangePathAsync(CancellationToken cancellationToken)
    {
        if (_execution is null || SelectedPath is null)
        {
            return;
        }

        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.ChangeRecoveryPath,
            cancellationToken,
            selectedPath: SelectedPath.Path,
            returnToPlan: true);
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
            returnToPlan: true);
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

        await ApplyAsync(
            AccountRecoveryExecutionTransitionKind.StartAction,
            cancellationToken,
            returnToPlan: false);
        if (CurrentActionState?.Status == RecoveryActionStatus.InProgress && HasOfficialLocation)
        {
            await OpenOfficialPageAsync(cancellationToken);
        }
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
        if (_workflow is null || CurrentDefinition is null || CurrentLocation is null)
        {
            return;
        }

        var accountUri = Uri.TryCreate(_account?.AccountUrl, UriKind.Absolute, out var parsed)
            ? parsed
            : null;
        var discovery = await _locationDiscovery.DiscoverAsync(
            new RecoveryLocationDiscoveryRequest(
                _workflow,
                CurrentDefinition.RecoveryLocationId,
                accountUri,
                RecoveryLocationSelectionPolicy.ProviderDefinedOnly),
            cancellationToken);
        if (!discovery.Succeeded || discovery.Handoff is not { RequiresVisibleConfirmation: true } handoff)
        {
            _navigationFailureCode = ExternalNavigationFailureCode.Unavailable;
            _navigationStatusKey = null;
            NotifyNavigationStatus();
            return;
        }

        var opened = await _externalNavigation.OpenAsync(handoff.Destination, cancellationToken);
        _navigationFailureCode = opened.FailureCode;
        _navigationStatusKey = opened.Succeeded ? "Workflow.Navigation.Opened" : null;
        NotifyNavigationStatus();
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
        RecoveryPath? selectedPath = null,
        bool returnToPlan = false)
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
            {
                SelectedPath = selectedPath,
            },
            cancellationToken);
        ApplyResult(result);
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
            ? "Workflow.Plan.Unchanged"
            : "Workflow.Plan.Changed";
        PlanReturnRequested?.Invoke(this, new WorkflowPlanReturnRequest(feedbackKey));
    }

    private void ApplyResult(AccountRecoveryExecutionResult result)
    {
        if (!result.Succeeded)
        {
            SetValidation(FailureKey(result.FailureCode));
            return;
        }

        var hadExecution = _execution is not null;
        _execution = result.State;
        _reason = string.Empty;
        _completionCriteriaAcknowledged = false;
        _navigationStatusKey = null;
        _navigationFailureCode = ExternalNavigationFailureCode.None;
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
        var planItem = _planItem;
        var completedById = _session.CurrentSession?.Accounts.ToDictionary(
            account => account.AccountId,
            account => account.IsFullyReviewed) ?? [];
        var waitingFor = planItem?.WaitingForAccountIds
            .Where(accountId => !completedById.GetValueOrDefault(accountId))
            .ToArray() ?? [];
        return new AccountRecoveryProjectionContext(
            _account?.DashboardCriticality ?? AccountCriticality.Routine,
            planItem?.DependencyDepth ?? 0,
            waitingFor)
        {
            InventoryBlockedIssues = planItem?.Status is
                AccountInventoryPlanStatus.BlockedCycle or
                AccountInventoryPlanStatus.BlockedMissingDependency
                    ? 1
                    : 0,
            InventoryUnresolvedRisks = planItem?.HasDependencyOverride == true ? 1 : 0,
        };
    }

    private AccountInventoryEntry? ResolveAccount(AccountInventoryState inventory)
    {
        if (_requestedAccountId is { } requested)
        {
            return inventory.Accounts.SingleOrDefault(account => account.Id == requested);
        }

        var recommendedId = _session.Dashboard?.Recommendation.AccountId ??
            _inventory.CurrentPlan?.Recommended?.AccountId;
        return recommendedId is { } accountId
            ? inventory.Accounts.SingleOrDefault(account => account.Id == accountId)
            : inventory.Accounts.FirstOrDefault();
    }

    private static RecoveryWorkflowDefinition? ResolveWorkflow(AccountInventoryEntry account)
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
        RefreshPathOptions();
        RefreshActions();
        NotifyAccountProperties();
        NotifyCurrentActionProperties();
        RaiseCommandStates();

        if (_execution is null)
        {
            SetLocalizedStatus(
                _workflow is null ? AppVisualState.Blocked : AppVisualState.Normal,
                _workflow is null ? "Screen.Workflow.StatusTitle" : "Workflow.Status.Ready.Title",
                _workflow is null ? "Screen.Workflow.StatusMessage" : "Workflow.Status.Ready.Message");
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

    private void RefreshPathOptions()
    {
        var selected = _execution?.SelectedPath ?? SelectedPath?.Path;
        _pathOptions = _workflow?.Actions
            .SelectMany(action => action.RecoveryPaths)
            .Distinct()
            .Order()
            .Select(path => new RecoveryPathOptionViewModel(
                path,
                Localization.GetString($"Workflow.Path.{path}")))
            .ToArray() ?? [];
        _selectedPath = selected is { } selectedPath
            ? _pathOptions.SingleOrDefault(option => option.Path == selectedPath)
            : _pathOptions.Length > 0 ? _pathOptions[0] : null;
        OnPropertyChanged(nameof(PathOptions));
        OnPropertyChanged(nameof(SelectedPath));
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

    private bool AreDependenciesSatisfied => _planItem is { WaitingForAccountIds.Length: > 0 } &&
        _planItem.WaitingForAccountIds.All(dependencyId =>
            _session.CurrentSession?.Accounts.SingleOrDefault(account => account.AccountId == dependencyId)
                ?.IsFullyReviewed == true);

    private bool CanChangePath() => _execution is not null &&
        SelectedPath is not null &&
        SelectedPath.Path != _execution.SelectedPath &&
        _execution.Actions.All(action =>
            action.Status == RecoveryActionStatus.Open &&
            action.StartedAt is null &&
            action.CompletedAt is null &&
            action.UserReason is null &&
            action.UserNotes is null &&
            action.CredentialReference is null);

    private bool CanStartCurrentAction() => CurrentActionState?.Status == RecoveryActionStatus.Open;

    private bool CanRetryCurrentAction() => CurrentActionState?.Status is
        RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NeedsUserAction;

    private bool CanCompleteCurrentAction() =>
        CurrentActionState?.Status == RecoveryActionStatus.InProgress && CompletionCriteriaAcknowledged;

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
        _planItem = null;
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
        OnPropertyChanged(nameof(HasExecution));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(LoginIdentifier));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(RolesText));
        OnPropertyChanged(nameof(DependenciesText));
        OnPropertyChanged(nameof(RecommendationReasonText));
        OnPropertyChanged(nameof(PlanStatusText));
        OnPropertyChanged(nameof(DependentsText));
        OnPropertyChanged(nameof(DependentAccountNamesText));
        OnPropertyChanged(nameof(AccessStateText));
        OnPropertyChanged(nameof(HasAccessReason));
        OnPropertyChanged(nameof(AccessReasonText));
        OnPropertyChanged(nameof(RecoveryStatusText));
        OnPropertyChanged(nameof(CanChangeRecoveryPath));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void NotifyCurrentActionProperties()
    {
        OnPropertyChanged(nameof(HasCurrentAction));
        OnPropertyChanged(nameof(HasOfficialLocation));
        OnPropertyChanged(nameof(HasCredentialReference));
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
                     RefreshCommand, BeginCommand, ChangePathCommand, SetAccessAvailableCommand,
                     SetAccessLostCommand, SetWaitingCommand, StartActionCommand, RetryActionCommand,
                     CompleteActionCommand, RequireUserActionCommand, BlockActionCommand,
                     FailActionCommand, MarkNotApplicableCommand, AcceptRiskCommand,
                     SaveNotesCommand, OpenOfficialPageCommand,
                     GenerateCredentialCommand, GuidedPrimaryActionCommand,
                     ApplyGuidedProblemCommand,
                 })
        {
            command.RaiseCanExecuteChanged();
        }

        OnPropertyChanged(nameof(CanChangeRecoveryPath));
        OnPropertyChanged(nameof(CanGenerateCredentialForCurrentAction));
        OnPropertyChanged(nameof(CanRunGuidedPrimary));
        OnPropertyChanged(nameof(CanReportCurrentProblem));
        ShowProblemReviewCommand.RaiseCanExecuteChanged();
    }

    private bool CanGenerateCredential() =>
        _generatedCredentials?.IsUnlocked == true && _account is not null && _execution is not null &&
        CurrentDefinition?.Type is RecoveryActionType.ChangePassword or RecoveryActionType.ResetPassword &&
        CurrentActionState?.CredentialReference is null;

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
        AccountRecoveryExecutionFailureCode.PersistenceFailure => "Workflow.Validation.PersistenceFailure",
        _ => "Workflow.Validation.InvalidInput",
    };
}
