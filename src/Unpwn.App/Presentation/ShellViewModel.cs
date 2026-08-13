using System.ComponentModel;
using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Providers.Workflows;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly IRecoverySessionService? _recoverySession;
    private readonly IAccountInventoryService? _accountInventory;
    private readonly ILocalizationService _localization;
    private readonly IGuidedRecoveryWizardService? _guidedWizard;
    private readonly IWorkspacePersistenceStatus? _persistenceStatus;
    private readonly IRecoveryBrowserSessionLifecycle? _browserSessions;
    private readonly bool _enforceNavigationPrerequisites;
    private IReadOnlyList<NavigationItemViewModel> _navigationItems;
    private readonly LanguageOptionViewModel[] _languageOptions;
    private NavigationItemViewModel _selectedNavigation;
    private LanguageOptionViewModel _selectedLanguage;
    private ScreenViewModel _currentScreen;
    private VisualStatusViewModel _currentStatus;
    private Guid? _navigationAccountId;
    private string? _navigationActionId;
    private bool _hasStartupRecoveryWarning;
    private bool _isWorkspaceNavigationExpanded;
    private bool _hadRecoverySession;
    private string? _guidanceFocusKey;
    private long _assistantFocusRequest;

    public ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        ILocalizationService localization)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession: null,
            accountInventory: null,
            guidedWizard: null,
            persistenceStatus: null,
            runState: null,
            browserSessions: null,
            localization,
            enforceNavigationPrerequisites: false)
    {
    }

    public ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        ILocalizationService localization,
        IGuidedRecoveryWizardService? guidedWizard = null,
        IWorkspacePersistenceStatus? persistenceStatus = null,
        ApplicationRunState? runState = null,
        IRecoveryBrowserSessionLifecycle? browserSessions = null)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession ?? throw new ArgumentNullException(nameof(recoverySession)),
            accountInventory ?? throw new ArgumentNullException(nameof(accountInventory)),
            guidedWizard,
            persistenceStatus,
            runState,
            browserSessions,
            localization,
            enforceNavigationPrerequisites: true)
    {
    }

    private ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService? recoverySession,
        IAccountInventoryService? accountInventory,
        IGuidedRecoveryWizardService? guidedWizard,
        IWorkspacePersistenceStatus? persistenceStatus,
        ApplicationRunState? runState,
        IRecoveryBrowserSessionLifecycle? browserSessions,
        ILocalizationService localization,
        bool enforceNavigationPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(screenFactory);
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        ArgumentNullException.ThrowIfNull(localization);

        _screenFactory = screenFactory;
        _vaultLifecycle = vaultLifecycle;
        _recoverySession = recoverySession;
        _accountInventory = accountInventory;
        _guidedWizard = guidedWizard;
        _persistenceStatus = persistenceStatus;
        _browserSessions = browserSessions;
        _hasStartupRecoveryWarning = runState?.PreviousExitWasAbnormal == true;
        _localization = localization;
        _enforceNavigationPrerequisites = enforceNavigationPrerequisites;
        _isWorkspaceNavigationExpanded = recoverySession?.CurrentSession is null;
        _hadRecoverySession = recoverySession?.CurrentSession is not null;
        _navigationItems = BuildNavigationItems();
        _languageOptions = BuildLanguageOptions();
        _selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        _selectedNavigation = _navigationItems[0];
        _currentScreen = _screenFactory.Create(_selectedNavigation.Route);
        _currentScreen.Activate();
        SubscribeToScreen(_currentScreen);
        _currentStatus = BuildContextualStatus();
        LockCommand = new AsyncCommand(
            LockAsync,
            () => _localization.GetString("Shell.Lock.Error"),
            () => IsVaultUnlocked);
        LockCommand.PropertyChanged += LockCommand_OnPropertyChanged;
        GuidedOpenCommand = new RelayCommand(OpenGuidedStep, () => IsGuidedWizardVisible);
        GuidedAdvanceCommand = new AsyncCommand(
            AdvanceGuidedStepAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedWizardVisible && !_guidedWizard!.Current.IsTerminal);
        GuidedBackCommand = new AsyncCommand(
            GoBackGuidedStepAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedWizardVisible && _guidedWizard!.PreviousDecision.CanMove);
        GuidedPrimaryCommand = new AsyncCommand(
            ExecuteGuidedPrimaryActionAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedWizardVisible);
        PauseSessionCommand = new AsyncCommand(
            PauseSessionAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedPauseAvailable);
        ToggleWorkspaceNavigationCommand = new RelayCommand(ToggleWorkspaceNavigation);
        DismissStartupRecoveryCommand = new RelayCommand(
            DismissStartupRecovery,
            () => HasStartupRecoveryWarning);
        RetryBrowserSessionCleanupCommand = new AsyncCommand(
            RetryBrowserSessionCleanupAsync,
            () => _localization.GetString("RecoveryBrowser.Session.CleanupFailed"),
            () => HasBrowserSessionCleanupWarning);
        _vaultLifecycle.ContextChanged += ShellContext_OnContextChanged;
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
        _recoverySession?.SessionChanged += RecoverySession_OnSessionChanged;
        _accountInventory?.InventoryChanged += AccountInventory_OnInventoryChanged;
        _guidedWizard?.GuidanceChanged += GuidedWizard_OnGuidanceChanged;
        _persistenceStatus?.StatusChanged += PersistenceStatus_OnStatusChanged;
        if (_browserSessions is { } activeBrowserSessions)
        {
            activeBrowserSessions.StateChanged += BrowserSessions_OnStateChanged;
        }

        _localization.CultureChanged += Localization_OnCultureChanged;
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems => _navigationItems;

    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions => _languageOptions;

    public LanguageOptionViewModel SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            if (!string.Equals(
                    value.Code,
                    _localization.CurrentLanguageCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _localization.SetLanguage(value.Code);
            }
        }
    }

    public NavigationItemViewModel SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!value.IsEnabled)
            {
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedNavigation, value))
            {
                return;
            }

            ShowScreen(value.Route);
        }
    }

    public ScreenViewModel CurrentScreen
    {
        get => _currentScreen;
        private set
        {
            if (ReferenceEquals(_currentScreen, value))
            {
                return;
            }

            UnsubscribeFromScreen(_currentScreen);
            _currentScreen.Deactivate();
            _currentScreen = value;
            SubscribeToScreen(_currentScreen);
            OnPropertyChanged();
        }
    }

    public VisualStatusViewModel CurrentStatus
    {
        get => _currentStatus;
        private set => SetProperty(ref _currentStatus, value);
    }

    public Guid? NavigationAccountId
    {
        get => _navigationAccountId;
        private set => SetProperty(ref _navigationAccountId, value);
    }

    public string? NavigationActionId
    {
        get => _navigationActionId;
        private set => SetProperty(ref _navigationActionId, value);
    }

    public bool IsVaultUnlocked => _vaultLifecycle.Current.IsVaultUnlocked;

    public string VaultContextLabel => _vaultLifecycle.Current.IsVaultUnlocked
        ? _vaultLifecycle.Current.VaultDisplayName
        : _localization.GetString("Shell.Context.NoVault");

    public string SessionContextLabel =>
        _vaultLifecycle.Current.IsVaultUnlocked &&
        !string.IsNullOrWhiteSpace(_vaultLifecycle.Current.SessionDisplayName)
            ? _vaultLifecycle.Current.SessionDisplayName
            : _localization.GetString("Shell.Context.NoSession");

    public AsyncCommand LockCommand { get; }

    public RelayCommand GuidedOpenCommand { get; }

    public AsyncCommand GuidedAdvanceCommand { get; }

    public AsyncCommand GuidedBackCommand { get; }

    public AsyncCommand GuidedPrimaryCommand { get; }

    public AsyncCommand PauseSessionCommand { get; }

    public RelayCommand ToggleWorkspaceNavigationCommand { get; }

    public RelayCommand DismissStartupRecoveryCommand { get; }

    public AsyncCommand RetryBrowserSessionCleanupCommand { get; }

    public bool HasStartupRecoveryWarning => _hasStartupRecoveryWarning;

    public bool HasBrowserSessionCleanupWarning =>
        _browserSessions?.Current.HasUncleanSessionData == true;

    public string BrowserSessionCleanupText => _localization.GetString(
        _browserSessions?.Current.State == RecoveryBrowserSessionLifecycleState.CleanupFailed
            ? "RecoveryBrowser.Session.CleanupFailed"
            : "RecoveryBrowser.Session.Orphaned");

    public bool IsWorkspaceNavigationExpanded
    {
        get => _isWorkspaceNavigationExpanded;
        private set
        {
            if (SetProperty(ref _isWorkspaceNavigationExpanded, value))
            {
                OnPropertyChanged(nameof(WorkspaceNavigationToggleText));
            }
        }
    }

    public string WorkspaceNavigationToggleText => _localization.GetString(
        IsWorkspaceNavigationExpanded
            ? "Shell.Workspace.Hide"
            : "Shell.Workspace.Show");

    public bool IsPersistenceStatusVisible =>
        _persistenceStatus?.Current.State != WorkspacePersistenceState.Idle;

    public bool IsPersistenceFailure =>
        _persistenceStatus?.Current.State == WorkspacePersistenceState.SaveFailed;

    public string PersistenceStatusSymbol => _persistenceStatus?.Current.State switch
    {
        WorkspacePersistenceState.Saving => "…",
        WorkspacePersistenceState.Retrying => "↻",
        WorkspacePersistenceState.Saved => "✓",
        WorkspacePersistenceState.SaveFailed => "!",
        WorkspacePersistenceState.Canceled => "×",
        _ => "•",
    };

    public string PersistenceStatusText => _persistenceStatus is null
        ? string.Empty
        : _localization.GetString(GetPersistenceStatusKey(_persistenceStatus.Current));

    public bool IsGuidedWizardVisible =>
        _guidedWizard is not null && IsVaultUnlocked && _recoverySession?.CurrentSession is not null;

    public bool IsGuidedPauseAvailable =>
        IsGuidedWizardVisible &&
        _recoverySession?.CurrentSession?.Status == RecoveryWorkspaceLifecycleStatus.Active;

    public long AssistantFocusRequest
    {
        get => _assistantFocusRequest;
        private set => SetProperty(ref _assistantFocusRequest, value);
    }

    public string GuidedStepText => _guidedWizard is null
        ? string.Empty
        : _localization.GetString(GetWizardStepKey(_guidedWizard.Current.CurrentStep));

    public string GuidedRecommendationText => _guidedWizard is null
        ? string.Empty
        : _localization.GetString(GetGuidanceKey(_guidedWizard.NextDecision));

    public string GuidedWhyText
    {
        get
        {
            if (_guidedWizard is null)
            {
                return string.Empty;
            }

            var decision = _guidedWizard.NextDecision;
            if (decision.BlockCode != GuidedRecoveryBlockCode.None)
            {
                return _localization.GetString(GetGuidedBlockWhyKey(decision.BlockCode));
            }

            var dashboardRecommendation = _recoverySession?.Dashboard?.Recommendation;
            if ((decision.TargetStep == RecoveryWizardStepId.AccountRecovery ||
                 decision.CurrentStep == RecoveryWizardStepId.AccountRecovery) &&
                dashboardRecommendation is not null)
            {
                return _localization.GetString(
                    $"Dashboard.Recommendation.{dashboardRecommendation.Code}");
            }

            return _localization.GetString(GetGuidedWhyKey(decision.TargetStep ?? decision.CurrentStep));
        }
    }

    public bool HasGuidedTarget => !string.IsNullOrWhiteSpace(GuidedTargetText);

    public string GuidedTargetText
    {
        get
        {
            var target = ResolveGuidedTarget();
            return target switch
            {
                { Account: not null, Action: not null } => _localization.Format(
                    "Shell.Assistant.TargetWithAction",
                    target.Account,
                    target.Action),
                { Account: not null } => _localization.Format(
                    "Shell.Assistant.Target",
                    target.Account),
                _ => string.Empty,
            };
        }
    }

    public string GuidedPrimaryActionText => _localization.GetString(
        IsGuidedPaused
            ? "Shell.Assistant.Resume"
            : IsGuidedTerminal
                ? "Shell.Assistant.OpenReport"
                : _guidedWizard?.NextDecision.CanMove == true
                    ? "Shell.Guided.Continue"
                    : _guidedWizard?.NextDecision.BlockCode == GuidedRecoveryBlockCode.AccountsRequired
                        ? "Shell.Assistant.OpenCsvImport"
                        : "Shell.Assistant.OpenTask");

    private bool IsGuidedPaused =>
        _recoverySession?.CurrentSession?.Status == RecoveryWorkspaceLifecycleStatus.Paused;

    private bool IsGuidedTerminal =>
        _recoverySession?.CurrentSession?.IsReadOnly == true ||
        _guidedWizard?.Current.IsTerminal == true;

    private async Task LockAsync(CancellationToken cancellationToken)
    {
        await _vaultLifecycle.LockAsync(cancellationToken);
        NavigateTo(AppRoute.VaultEntry);
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Success,
            _localization,
            "Shell.Lock.StatusTitle",
            "Shell.Lock.StatusMessage",
            StatusPresentation.TransientResult);
    }

    private async Task AdvanceGuidedStepAsync(CancellationToken cancellationToken)
    {
        if (_guidedWizard is null)
        {
            return;
        }

        var result = await _guidedWizard.AdvanceAsync(cancellationToken);
        if (!result.Succeeded)
        {
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Warning,
                _localization,
                "Shell.Guided.Blocked.Title",
                GetGuidanceKey(result.Decision),
                StatusPresentation.GlobalWarning);
            RefreshGuidance();
            return;
        }

        NavigateToGuidedStep(result.Decision.TargetStep, result.Decision.AccountId, result.Decision.ActionId);
    }

    private async Task GoBackGuidedStepAsync(CancellationToken cancellationToken)
    {
        if (_guidedWizard is null)
        {
            return;
        }

        var result = await _guidedWizard.GoBackAsync(cancellationToken);
        if (result.Succeeded)
        {
            NavigateToGuidedStep(result.Decision.TargetStep, null, null);
        }
    }

    private async Task ExecuteGuidedPrimaryActionAsync(CancellationToken cancellationToken)
    {
        if (_guidedWizard is null)
        {
            return;
        }

        if (IsGuidedPaused)
        {
            if (_recoverySession is null)
            {
                return;
            }

            var resumed = await _recoverySession.ResumeAsync(cancellationToken);
            if (!resumed.Succeeded)
            {
                ShowGuidedSessionFailure();
            }

            return;
        }

        if (_guidedWizard.NextDecision.CanMove)
        {
            await AdvanceGuidedStepAsync(cancellationToken);
            return;
        }

        OpenGuidedStep();
    }

    private async Task PauseSessionAsync(CancellationToken cancellationToken)
    {
        if (_recoverySession is null)
        {
            return;
        }

        var paused = await _recoverySession.PauseAsync(cancellationToken);
        if (!paused.Succeeded)
        {
            ShowGuidedSessionFailure();
        }
    }

    private void ShowGuidedSessionFailure() =>
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Error,
            _localization,
            "Shell.Guided.Error.Title",
            "Shell.Guided.Error",
            StatusPresentation.GlobalWarning);

    private void ToggleWorkspaceNavigation() =>
        IsWorkspaceNavigationExpanded = !IsWorkspaceNavigationExpanded;

    private void OpenGuidedStep()
    {
        if (_guidedWizard is not null)
        {
            NavigateToGuidedStep(_guidedWizard.Current.CurrentStep, null, null);
        }
    }

    private void NavigateTo(AppRoute route) =>
        SelectedNavigation = NavigationItems.Single(item => item.Route == route);

    private void ShowScreen(AppRoute route)
    {
        var screen = _screenFactory.Create(route);
        CurrentScreen = screen;
        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.Activate(NavigationAccountId, NavigationActionId);
        }
        else
        {
            screen.Activate();
        }

    }

    private void RefreshNavigationAvailability()
    {
        var currentRoute = SelectedNavigation.Route;
        _navigationItems = BuildNavigationItems();
        var selected = _navigationItems.Single(item => item.Route == currentRoute);
        if (!selected.IsEnabled)
        {
            var fallbackRoute = !IsVaultUnlocked
                ? AppRoute.VaultEntry
                : _recoverySession?.CurrentSession is null ||
                  _recoverySession.CurrentSession.Status == RecoveryWorkspaceLifecycleStatus.Paused ||
                  _recoverySession.CurrentSession.IsReadOnly
                    ? AppRoute.Dashboard
                    : AppRoute.Accounts;
            selected = _navigationItems.Single(item => item.Route == fallbackRoute);
        }

        OnPropertyChanged(nameof(NavigationItems));
        _selectedNavigation = selected;
        OnPropertyChanged(nameof(SelectedNavigation));
        if (CurrentScreen.Route != selected.Route)
        {
            NavigationAccountId = null;
            NavigationActionId = null;
            ShowScreen(selected.Route);
        }
    }

    private IReadOnlyList<NavigationItemViewModel> BuildNavigationItems()
    {
        var hasVault = !_enforceNavigationPrerequisites || IsVaultUnlocked;
        var hasSession = !_enforceNavigationPrerequisites ||
            (hasVault && _recoverySession?.CurrentSession is not null);
        var canMutateInventory = !_enforceNavigationPrerequisites ||
            (hasSession && _accountInventory?.LoadState is
                AccountInventoryLoadState.Empty or AccountInventoryLoadState.Loaded);
        var hasAccounts = !_enforceNavigationPrerequisites ||
            (canMutateInventory && _accountInventory?.CurrentInventory?.Accounts.Length > 0);
        var isTerminal = _enforceNavigationPrerequisites &&
            _recoverySession?.CurrentSession?.IsReadOnly == true;
        var isPaused = _enforceNavigationPrerequisites &&
            _recoverySession?.CurrentSession?.Status == RecoveryWorkspaceLifecycleStatus.Paused;
        return
    [
        new(
            AppRoute.VaultEntry,
            _localization.GetString("Shell.Navigation.Vault.Label"),
            _localization.GetString("Shell.Navigation.Vault.Description"),
            "V",
            IsEnabled: true),
        new(
            AppRoute.Dashboard,
            _localization.GetString("Shell.Navigation.Dashboard.Label"),
            _localization.GetString("Shell.Navigation.Dashboard.Description"),
            "D",
            hasVault),
        new(
            AppRoute.CsvImport,
            _localization.GetString("Shell.Navigation.Import.Label"),
            _localization.GetString("Shell.Navigation.Import.Description"),
            "I",
            canMutateInventory && !isPaused && !isTerminal),
        new(
            AppRoute.Accounts,
            _localization.GetString("Shell.Navigation.Accounts.Label"),
            _localization.GetString("Shell.Navigation.Accounts.Description"),
            "A",
            hasSession && !isPaused && !isTerminal),
        new(
            AppRoute.Workflow,
            _localization.GetString("Shell.Navigation.Workflow.Label"),
            _localization.GetString("Shell.Navigation.Workflow.Description"),
            "W",
            hasAccounts && !isPaused && !isTerminal),
        new(
            AppRoute.CredentialsExport,
            _localization.GetString("Shell.Navigation.Credentials.Label"),
            _localization.GetString("Shell.Navigation.Credentials.Description"),
            "C",
            hasAccounts && !isPaused && !isTerminal),
        new(
            AppRoute.Completion,
            _localization.GetString("Shell.Navigation.Completion.Label"),
            _localization.GetString("Shell.Navigation.Completion.Description"),
            "✓",
            (hasAccounts && !isPaused) || isTerminal),
    ];
    }

    private LanguageOptionViewModel[] BuildLanguageOptions() =>
    [
        .. _localization.SupportedLanguages.Select(language => new LanguageOptionViewModel(
            language.Code,
            _localization.GetString(language.DisplayNameKey))),
    ];

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsVaultUnlocked));
        OnPropertyChanged(nameof(VaultContextLabel));
        OnPropertyChanged(nameof(SessionContextLabel));
        OnPropertyChanged(nameof(PersistenceStatusText));
        OnPropertyChanged(nameof(BrowserSessionCleanupText));
        OnPropertyChanged(nameof(WorkspaceNavigationToggleText));
        LockCommand.RaiseCanExecuteChanged();
        RefreshGuidance();
        RefreshNavigationAvailability();
        CurrentStatus = BuildContextualStatus();

        if (!IsVaultUnlocked && CurrentScreen.Route != AppRoute.VaultEntry)
        {
            NavigationAccountId = null;
            NavigationActionId = null;
            NavigateTo(AppRoute.VaultEntry);
        }
    }

    private void VaultLifecycle_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        var snapshot = _vaultLifecycle.Snapshot;
        if (snapshot.IsInactivityWarningVisible && snapshot.InactivityLocksAt is { } locksAt)
        {
            CurrentStatus = new VisualStatusViewModel(
                AppVisualState.Warning,
                _localization.GetString("Status.Warning"),
                "!",
                _localization.GetString("Vault.Inactivity.Warning.Title"),
                _localization.Format("Vault.Inactivity.Warning.Message", locksAt),
                StatusPresentation.GlobalWarning);
        }
        else if (snapshot.Status == VaultLifecycleStatus.Locked &&
                 snapshot.LastLockReason == VaultLockReason.Inactivity)
        {
            NavigateTo(AppRoute.VaultEntry);
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Warning,
                _localization,
                "Vault.Inactivity.Locked.Title",
                "Vault.Inactivity.Locked.Message",
                StatusPresentation.GlobalWarning);
        }
    }

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        RefreshNavigationAvailability();

        foreach (var language in _localization.SupportedLanguages)
        {
            var option = _languageOptions.Single(candidate => candidate.Code == language.Code);
            option.UpdateDisplayName(_localization.GetString(language.DisplayNameKey));
        }

        var selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        if (!ReferenceEquals(_selectedLanguage, selectedLanguage))
        {
            _selectedLanguage = selectedLanguage;
            OnPropertyChanged(nameof(SelectedLanguage));
        }

        OnPropertyChanged(nameof(VaultContextLabel));
        OnPropertyChanged(nameof(SessionContextLabel));
        OnPropertyChanged(nameof(PersistenceStatusText));
        OnPropertyChanged(nameof(WorkspaceNavigationToggleText));
        RefreshGuidance();

        if (_vaultLifecycle.Snapshot is
            { IsInactivityWarningVisible: true, InactivityLocksAt: { } locksAt })
        {
            CurrentStatus = new VisualStatusViewModel(
                AppVisualState.Warning,
                _localization.GetString("Status.Warning"),
                "!",
                _localization.GetString("Vault.Inactivity.Warning.Title"),
                _localization.Format("Vault.Inactivity.Warning.Message", locksAt),
                StatusPresentation.GlobalWarning);
        }
        else if (LockCommand.LastOutcome == AsyncCommandOutcome.Failed)
        {
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Error,
                _localization,
                "Shell.Lock.FailedTitle",
                "Shell.Lock.Error",
                StatusPresentation.GlobalWarning);
        }
        else
        {
            CurrentStatus = BuildContextualStatus();
        }
    }

    private void RecoverySession_OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        var hasRecoverySession = _recoverySession?.CurrentSession is not null;
        if (hasRecoverySession && !_hadRecoverySession)
        {
            IsWorkspaceNavigationExpanded = false;
        }
        else if (!hasRecoverySession)
        {
            IsWorkspaceNavigationExpanded = true;
        }

        _hadRecoverySession = hasRecoverySession;
        RefreshNavigationAvailability();
        RefreshGuidance();
        CurrentStatus = BuildContextualStatus();
    }

    private void AccountInventory_OnInventoryChanged(object? sender, EventArgs eventArgs)
    {
        RefreshNavigationAvailability();
        RefreshGuidance();
    }

    private void VaultEntry_OnContinueRequested(object? sender, EventArgs eventArgs) =>
        NavigateTo(AppRoute.Dashboard);

    private async void Dashboard_OnNavigationRequested(
        object? sender,
        DashboardNavigationRequest eventArgs)
    {
        if (eventArgs.Route == AppRoute.Completion &&
            _guidedWizard is not null &&
            !_guidedWizard.Current.IsTerminal)
        {
            var result = await _guidedWizard.BeginCompletionReviewAsync(CancellationToken.None);
            if (!result.Succeeded)
            {
                CurrentStatus = VisualStatusViewModel.Create(
                    AppVisualState.Error,
                    _localization,
                    "Shell.Guided.Error.Title",
                    "Shell.Guided.Error",
                    StatusPresentation.GlobalWarning);
                return;
            }
        }

        NavigationAccountId = eventArgs.AccountId;
        NavigationActionId = eventArgs.ActionId;
        NavigateTo(eventArgs.Route);
    }

    private async void Workflow_OnPlanReturnRequested(
        object? sender,
        WorkflowPlanReturnRequest eventArgs)
    {
        if (_guidedWizard?.Current.CurrentStep == RecoveryWizardStepId.AccountRecovery)
        {
            await _guidedWizard.AdvanceAsync(CancellationToken.None);
        }

        NavigationAccountId = null;
        NavigationActionId = null;
        NavigateTo(AppRoute.Dashboard);
        if (CurrentScreen is DashboardScreenViewModel dashboard)
        {
            dashboard.ShowPlanFeedback(eventArgs.FeedbackResourceKey);
        }
    }

    private void SubscribeToScreen(ScreenViewModel screen)
    {
        if (screen is VaultEntryScreenViewModel vaultEntry)
        {
            vaultEntry.ContinueRequested += VaultEntry_OnContinueRequested;
        }

        if (screen is DashboardScreenViewModel dashboard)
        {
            dashboard.NavigationRequested += Dashboard_OnNavigationRequested;
        }


        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.PlanReturnRequested += Workflow_OnPlanReturnRequested;
        }


        if (screen is CompletionScreenViewModel completion)
        {
            completion.NavigationRequested += Completion_OnNavigationRequested;
            completion.CompletionReviewSucceeded += Completion_OnReviewSucceeded;
        }
    }

    private void UnsubscribeFromScreen(ScreenViewModel screen)
    {
        if (screen is VaultEntryScreenViewModel vaultEntry)
        {
            vaultEntry.ContinueRequested -= VaultEntry_OnContinueRequested;
        }

        if (screen is DashboardScreenViewModel dashboard)
        {
            dashboard.NavigationRequested -= Dashboard_OnNavigationRequested;
        }


        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.PlanReturnRequested -= Workflow_OnPlanReturnRequested;
        }


        if (screen is CompletionScreenViewModel completion)
        {
            completion.NavigationRequested -= Completion_OnNavigationRequested;
            completion.CompletionReviewSucceeded -= Completion_OnReviewSucceeded;
        }
    }

    private void Completion_OnNavigationRequested(
        object? sender,
        CompletionNavigationRequest eventArgs)
    {
        NavigationAccountId = eventArgs.AccountId;
        NavigationActionId = eventArgs.ActionId;
        NavigateTo(eventArgs.Route);
    }

    private async void Completion_OnReviewSucceeded(object? sender, EventArgs eventArgs)
    {
        if (_guidedWizard is null)
        {
            return;
        }

        var result = await _guidedWizard.MarkCompletionReviewReadyAsync(CancellationToken.None);
        if (!result.Succeeded)
        {
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Error,
                _localization,
                "Shell.Guided.Error.Title",
                "Shell.Guided.Error",
                StatusPresentation.GlobalWarning);
        }
    }

    private void GuidedWizard_OnGuidanceChanged(object? sender, EventArgs eventArgs) =>
        RefreshGuidance();

    private void PersistenceStatus_OnStatusChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsPersistenceStatusVisible));
        OnPropertyChanged(nameof(IsPersistenceFailure));
        OnPropertyChanged(nameof(PersistenceStatusSymbol));
        OnPropertyChanged(nameof(PersistenceStatusText));
    }

    private void DismissStartupRecovery()
    {
        _hasStartupRecoveryWarning = false;
        OnPropertyChanged(nameof(HasStartupRecoveryWarning));
        DismissStartupRecoveryCommand.RaiseCanExecuteChanged();
    }

    private async Task RetryBrowserSessionCleanupAsync(CancellationToken cancellationToken)
    {
        if (_browserSessions is null)
        {
            return;
        }

        if (_browserSessions.Current.OrphanedSessions.Count == 0)
        {
            _browserSessions.InspectStartup();
            return;
        }

        foreach (var orphan in _browserSessions.Current.OrphanedSessions.ToArray())
        {
            var result = await _browserSessions.RetryOrphanCleanupAsync(
                orphan.SessionId,
                cancellationToken);
            if (!result.Succeeded)
            {
                break;
            }
        }
    }

    private void BrowserSessions_OnStateChanged(
        object? sender,
        RecoveryBrowserSessionLifecycleSnapshot snapshot)
    {
        OnPropertyChanged(nameof(HasBrowserSessionCleanupWarning));
        OnPropertyChanged(nameof(BrowserSessionCleanupText));
        RetryBrowserSessionCleanupCommand.RaiseCanExecuteChanged();
    }

    private void RefreshGuidance()
    {
        OnPropertyChanged(nameof(IsGuidedWizardVisible));
        OnPropertyChanged(nameof(IsGuidedPauseAvailable));
        OnPropertyChanged(nameof(GuidedStepText));
        OnPropertyChanged(nameof(GuidedRecommendationText));
        OnPropertyChanged(nameof(GuidedWhyText));
        OnPropertyChanged(nameof(HasGuidedTarget));
        OnPropertyChanged(nameof(GuidedTargetText));
        OnPropertyChanged(nameof(GuidedPrimaryActionText));
        GuidedOpenCommand.RaiseCanExecuteChanged();
        GuidedAdvanceCommand.RaiseCanExecuteChanged();
        GuidedBackCommand.RaiseCanExecuteChanged();
        GuidedPrimaryCommand.RaiseCanExecuteChanged();
        PauseSessionCommand.RaiseCanExecuteChanged();

        var focusKey = BuildGuidanceFocusKey();
        if (IsGuidedWizardVisible &&
            !string.Equals(_guidanceFocusKey, focusKey, StringComparison.Ordinal))
        {
            _guidanceFocusKey = focusKey;
            AssistantFocusRequest++;
        }
    }

    private string BuildGuidanceFocusKey()
    {
        if (_guidedWizard is null)
        {
            return string.Empty;
        }

        var decision = _guidedWizard.NextDecision;
        return string.Join(
            "|",
            _guidedWizard.Current.CurrentStep.Value,
            _guidedWizard.Current.Status,
            decision.TargetStep?.Value,
            decision.BlockCode,
            decision.AccountId,
            decision.ActionId);
    }

    private (string? Account, string? Action) ResolveGuidedTarget()
    {
        if (_guidedWizard is null)
        {
            return (null, null);
        }

        var decision = _guidedWizard.NextDecision;
        var recommendation = _recoverySession?.Dashboard?.Recommendation;
        var accountId = decision.AccountId ?? recommendation?.AccountId;
        var actionId = decision.ActionId ?? recommendation?.ActionId;
        var account = accountId is null
            ? null
            : _accountInventory?.CurrentInventory?.Accounts.SingleOrDefault(
                candidate => candidate.Id == accountId.Value);
        var providerId = account?.ProviderId ?? recommendation?.ProviderId;
        var accountLabel = account?.AccountName ?? providerId;
        if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(providerId))
        {
            return (accountLabel, null);
        }

        var accountHost = Uri.TryCreate(account?.AccountUrl, UriKind.Absolute, out var accountUri)
            ? accountUri.Host
            : null;
        var workflow = RepositoryWorkflowCatalog.Workflows.SingleOrDefault(candidate =>
            string.Equals(candidate.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.ProviderName, providerId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.ProviderId, accountHost, StringComparison.OrdinalIgnoreCase));
        var action = workflow?.Actions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
        return (accountLabel, action is null ? null : _localization.GetString(action.Guidance.TitleKey));
    }

    private void NavigateToGuidedStep(
        RecoveryWizardStepId? step,
        Guid? accountId,
        string? actionId)
    {
        if (step is null)
        {
            return;
        }

        NavigationAccountId = accountId;
        NavigationActionId = actionId;
        NavigateTo(step.Value switch
        {
            "vault-entry" => AppRoute.VaultEntry,
            "incident-intake" or "recovery-plan" => AppRoute.Dashboard,
            "account-inventory" => AppRoute.CsvImport,
            "identity-review" => AppRoute.Accounts,
            "account-recovery" => AppRoute.Workflow,
            "credential-export" => AppRoute.CredentialsExport,
            "completion-preflight" or "final-report" => AppRoute.Completion,
            _ => AppRoute.Dashboard,
        });
    }

    private static string GetWizardStepKey(RecoveryWizardStepId step) => step.Value switch
    {
        "account-inventory" => "Dashboard.WizardStep.AccountInventory",
        "identity-review" => "Dashboard.WizardStep.IdentityReview",
        "recovery-plan" => "Dashboard.WizardStep.RecoveryPlan",
        "account-recovery" => "Dashboard.WizardStep.AccountRecovery",
        "credential-export" => "Dashboard.WizardStep.CredentialExport",
        "completion-preflight" => "Dashboard.WizardStep.CompletionPreflight",
        "final-report" => "Dashboard.WizardStep.FinalReport",
        _ => "Dashboard.WizardStep.Unknown",
    };

    private static string GetGuidanceKey(GuidedRecoveryDecision decision) => decision.BlockCode switch
    {
        GuidedRecoveryBlockCode.AccountsRequired => "Shell.Guided.AccountsRequired",
        GuidedRecoveryBlockCode.RoleConfirmationRequired => "Shell.Guided.RoleConfirmationRequired",
        GuidedRecoveryBlockCode.Paused => "Shell.Guided.Paused",
        GuidedRecoveryBlockCode.Terminal => "Shell.Guided.Terminal",
        GuidedRecoveryBlockCode.UnsupportedStep => "Shell.Guided.OpenCurrent",
        _ => decision.TargetStep?.Value switch
        {
            "identity-review" => "Shell.Guided.NextIdentityReview",
            "recovery-plan" => "Shell.Guided.NextRecoveryPlan",
            "account-recovery" => "Shell.Guided.NextAccountRecovery",
            "credential-export" => "Shell.Guided.NextCredentialExport",
            "completion-preflight" => "Shell.Guided.NextCompletion",
            "final-report" => "Shell.Guided.NextFinalReport",
            _ => "Shell.Guided.OpenCurrent",
        },
    };

    private static string GetGuidedWhyKey(RecoveryWizardStepId step) => step.Value switch
    {
        "account-inventory" => "Shell.Assistant.Why.AccountInventory",
        "identity-review" => "Shell.Assistant.Why.IdentityReview",
        "recovery-plan" => "Shell.Assistant.Why.RecoveryPlan",
        "account-recovery" => "Shell.Assistant.Why.AccountRecovery",
        "credential-export" => "Shell.Assistant.Why.CredentialExport",
        "completion-preflight" or "final-report" => "Shell.Assistant.Why.Completion",
        _ => "Shell.Assistant.Why.General",
    };

    private static string GetGuidedBlockWhyKey(GuidedRecoveryBlockCode blockCode) => blockCode switch
    {
        GuidedRecoveryBlockCode.AccountsRequired => "Shell.Assistant.Why.AccountInventory",
        GuidedRecoveryBlockCode.RoleConfirmationRequired => "Shell.Assistant.Why.IdentityReview",
        GuidedRecoveryBlockCode.Paused => "Shell.Assistant.Why.Paused",
        GuidedRecoveryBlockCode.Terminal => "Shell.Assistant.Why.Completion",
        _ => "Shell.Assistant.Why.General",
    };

    private static string GetPersistenceStatusKey(WorkspacePersistenceSnapshot snapshot) =>
        snapshot.State switch
        {
            WorkspacePersistenceState.Saving => "Shell.Persistence.Saving",
            WorkspacePersistenceState.Retrying => "Shell.Persistence.Retrying",
            WorkspacePersistenceState.Saved => "Shell.Persistence.Saved",
            WorkspacePersistenceState.Canceled => "Shell.Persistence.Canceled",
            WorkspacePersistenceState.SaveFailed => snapshot.FailureCode switch
            {
                WorkspacePersistenceFailureCode.AccessDenied => "Shell.Persistence.Failed.AccessDenied",
                WorkspacePersistenceFailureCode.VersionIncompatible => "Shell.Persistence.Failed.Version",
                WorkspacePersistenceFailureCode.LockedOrConflict => "Shell.Persistence.Failed.Conflict",
                _ => "Shell.Persistence.Failed.Io",
            },
            _ => "Shell.Persistence.Idle",
        };

    private VisualStatusViewModel BuildContextualStatus()
    {
        if (!IsVaultUnlocked)
        {
            return VisualStatusViewModel.Create(
                AppVisualState.Normal,
                _localization,
                "Shell.Status.VaultLocked.Title",
                "Shell.Status.VaultLocked.Message",
                StatusPresentation.GlobalContext);
        }

        var session = _recoverySession?.CurrentSession;
        if (session is null)
        {
            return VisualStatusViewModel.Create(
                AppVisualState.Normal,
                _localization,
                "Shell.Status.VaultUnlocked.Title",
                "Shell.Status.VaultUnlocked.Message",
                StatusPresentation.GlobalContext,
                VaultContextLabel);
        }

        var titleKey = session.Status switch
        {
            RecoveryWorkspaceLifecycleStatus.Active => "Shell.Status.SessionActive.Title",
            RecoveryWorkspaceLifecycleStatus.Paused => "Shell.Status.SessionPaused.Title",
            RecoveryWorkspaceLifecycleStatus.Completed => "Shell.Status.SessionCompleted.Title",
            RecoveryWorkspaceLifecycleStatus.Archived => "Shell.Status.SessionArchived.Title",
            RecoveryWorkspaceLifecycleStatus.FollowUpRequired => "Shell.Status.SessionFollowUp.Title",
            _ => "Shell.Status.SessionActive.Title",
        };
        var state = session.Status == RecoveryWorkspaceLifecycleStatus.FollowUpRequired
            ? AppVisualState.UnresolvedRisk
            : AppVisualState.Normal;
        return VisualStatusViewModel.Create(
            state,
            _localization,
            titleKey,
            "Shell.Status.Session.Message",
            StatusPresentation.GlobalContext,
            VaultContextLabel,
            SessionContextLabel);
    }

    private void LockCommand_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AsyncCommand.LastOutcome) &&
            LockCommand.LastOutcome == AsyncCommandOutcome.Failed)
        {
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Error,
                _localization,
                "Shell.Lock.FailedTitle",
                "Shell.Lock.Error",
                StatusPresentation.GlobalWarning);
        }
    }
}
