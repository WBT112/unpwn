using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly IRecoverySessionService? _recoverySession;
    private readonly IAccountInventoryService? _accountInventory;
    private readonly ILocalizationService _localization;
    private readonly IRecoveryFlowService? _recoveryFlow;
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
    private bool _navigationStartsRecovery;
    private bool _hasStartupRecoveryWarning;
    private bool _isWorkspaceNavigationExpanded;
    private bool _hadRecoverySession;
    private bool _wasVaultUnlocked;
    private bool _resumeWorkspaceAfterUnlock;

    public ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        ILocalizationService localization)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession: null,
            accountInventory: null,
            recoveryFlow: null,
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
        IRecoveryFlowService? recoveryFlow = null,
        IWorkspacePersistenceStatus? persistenceStatus = null,
        ApplicationRunState? runState = null,
        IRecoveryBrowserSessionLifecycle? browserSessions = null)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession ?? throw new ArgumentNullException(nameof(recoverySession)),
            accountInventory ?? throw new ArgumentNullException(nameof(accountInventory)),
            recoveryFlow,
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
        IRecoveryFlowService? recoveryFlow,
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
        _recoveryFlow = recoveryFlow;
        _persistenceStatus = persistenceStatus;
        _browserSessions = browserSessions;
        _hasStartupRecoveryWarning = runState?.PreviousExitWasAbnormal == true;
        _localization = localization;
        _enforceNavigationPrerequisites = enforceNavigationPrerequisites;
        _isWorkspaceNavigationExpanded = recoverySession?.CurrentSession is null;
        _hadRecoverySession = recoverySession?.CurrentSession is not null;
        _wasVaultUnlocked = IsVaultUnlocked;
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
        _recoveryFlow?.NextTaskChanged += RecoveryFlow_OnNextTaskChanged;
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

    private void ShowRecoveryFlowFailure() =>
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Error,
            _localization,
            "Shell.Flow.Error.Title",
            "Shell.Flow.Error",
            StatusPresentation.GlobalWarning);

    private void ToggleWorkspaceNavigation() =>
        IsWorkspaceNavigationExpanded = !IsWorkspaceNavigationExpanded;

    private void NavigateTo(AppRoute route)
    {
        var previousNavigation = _selectedNavigation;
        try
        {
            SelectedNavigation = NavigationItems.Single(item => item.Route == route);
        }
        catch
        {
            if (!ReferenceEquals(_selectedNavigation, previousNavigation))
            {
                _selectedNavigation = previousNavigation;
                OnPropertyChanged(nameof(SelectedNavigation));
            }

            throw;
        }
    }

    private void ShowScreen(AppRoute route)
    {
        var screen = _screenFactory.Create(route);
        CurrentScreen = screen;
        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            var startRecovery = _navigationStartsRecovery;
            _navigationStartsRecovery = false;
            workflow.Activate(NavigationAccountId, NavigationActionId, startRecovery);
        }
        else if (screen is AccountInventoryScreenViewModel accounts)
        {
            accounts.Activate(NavigationAccountId);
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
        var wasVaultUnlocked = _wasVaultUnlocked;
        _wasVaultUnlocked = IsVaultUnlocked;
        if (!_wasVaultUnlocked)
        {
            _resumeWorkspaceAfterUnlock = false;
        }
        else if (!wasVaultUnlocked && _recoveryFlow is not null)
        {
            _resumeWorkspaceAfterUnlock = true;
        }

        OnPropertyChanged(nameof(IsVaultUnlocked));
        OnPropertyChanged(nameof(VaultContextLabel));
        OnPropertyChanged(nameof(SessionContextLabel));
        OnPropertyChanged(nameof(PersistenceStatusText));
        OnPropertyChanged(nameof(BrowserSessionCleanupText));
        OnPropertyChanged(nameof(WorkspaceNavigationToggleText));
        LockCommand.RaiseCanExecuteChanged();
        RefreshNavigationAvailability();
        CurrentStatus = BuildContextualStatus();

        if (!IsVaultUnlocked && CurrentScreen.Route != AppRoute.VaultEntry)
        {
            NavigationAccountId = null;
            NavigationActionId = null;
            NavigateTo(AppRoute.VaultEntry);
        }

        TryResumeWorkspaceAfterUnlock();
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
        CurrentStatus = BuildContextualStatus();
        TryResumeWorkspaceAfterUnlock();
    }

    private void AccountInventory_OnInventoryChanged(object? sender, EventArgs eventArgs)
    {
        RefreshNavigationAvailability();
        TryResumeWorkspaceAfterUnlock();
    }

    private void VaultEntry_OnContinueRequested(object? sender, EventArgs eventArgs)
    {
        if (_resumeWorkspaceAfterUnlock)
        {
            TryResumeWorkspaceAfterUnlock();
            return;
        }

        NavigateTo(AppRoute.Dashboard);
    }

    private async void Dashboard_OnNavigationRequested(
        object? sender,
        DashboardNavigationRequest eventArgs)
    {
        if (eventArgs.Route == AppRoute.Completion && _recoveryFlow is not null)
        {
            var result = await _recoveryFlow.BeginCompletionReviewAsync(CancellationToken.None);
            if (!result.Succeeded)
            {
                ShowRecoveryFlowFailure();
                return;
            }
        }
        else if (eventArgs.Route == AppRoute.CredentialsExport &&
                 _recoveryFlow?.NextTask is
                 { Target: NextUserTaskTarget.CredentialHandoff, RequiresTransition: true })
        {
            var result = await _recoveryFlow.AdvanceAsync(CancellationToken.None);
            if (!result.Succeeded)
            {
                ShowRecoveryFlowFailure();
                return;
            }
        }

        NavigationAccountId = eventArgs.AccountId;
        NavigationActionId = eventArgs.ActionId;
        _navigationStartsRecovery = eventArgs.StartRecovery;
        NavigateTo(eventArgs.Route);
    }

    private void Workflow_OnOverviewReturnRequested(
        object? sender,
        WorkflowOverviewReturnRequest eventArgs)
    {
        NavigationAccountId = null;
        NavigationActionId = null;
        NavigateTo(AppRoute.Dashboard);
        if (CurrentScreen is DashboardScreenViewModel dashboard)
        {
            dashboard.ShowRecoveryQueueFeedback(eventArgs.FeedbackResourceKey);
        }
    }

    private void Workflow_OnAccountReviewRequested(
        object? sender,
        WorkflowAccountReviewRequest eventArgs)
    {
        NavigationAccountId = eventArgs.AccountId;
        NavigationActionId = null;
        NavigateTo(AppRoute.Accounts);
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

        if (screen is CsvImportScreenViewModel csvImport)
        {
            csvImport.AccountReviewRequested += CsvImport_OnAccountReviewRequested;
        }

        if (screen is AccountInventoryScreenViewModel accounts)
        {
            accounts.ContinueToRecoveryRequested += Accounts_OnContinueToRecoveryRequested;
        }

        if (screen is CredentialExportScreenViewModel credentials)
        {
            credentials.ContinueToCompletionRequested += Credentials_OnContinueToCompletionRequested;
        }

        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.OverviewReturnRequested += Workflow_OnOverviewReturnRequested;
            workflow.AccountReviewRequested += Workflow_OnAccountReviewRequested;
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

        if (screen is CsvImportScreenViewModel csvImport)
        {
            csvImport.AccountReviewRequested -= CsvImport_OnAccountReviewRequested;
        }

        if (screen is AccountInventoryScreenViewModel accounts)
        {
            accounts.ContinueToRecoveryRequested -= Accounts_OnContinueToRecoveryRequested;
        }

        if (screen is CredentialExportScreenViewModel credentials)
        {
            credentials.ContinueToCompletionRequested -= Credentials_OnContinueToCompletionRequested;
        }

        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.OverviewReturnRequested -= Workflow_OnOverviewReturnRequested;
            workflow.AccountReviewRequested -= Workflow_OnAccountReviewRequested;
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
        if (_recoveryFlow is null)
        {
            return;
        }

        var result = await _recoveryFlow.MarkCompletionReviewReadyAsync(CancellationToken.None);
        if (!result.Succeeded)
        {
            ShowRecoveryFlowFailure();
        }
    }

    private void RecoveryFlow_OnNextTaskChanged(object? sender, EventArgs eventArgs)
    {
        RefreshNavigationAvailability();
        TryResumeWorkspaceAfterUnlock();
    }

    private void TryResumeWorkspaceAfterUnlock()
    {
        if (!_resumeWorkspaceAfterUnlock ||
            !IsVaultUnlocked ||
            _recoveryFlow is null ||
            !WorkspaceStateHasFinishedLoading())
        {
            return;
        }

        var task = _recoveryFlow.NextTask;
        var route = HasWorkspaceLoadFailure()
            ? AppRoute.Dashboard
            : RouteFor(task.Target);
        var navigation = NavigationItems.Single(item => item.Route == route);
        if (!navigation.IsEnabled)
        {
            route = AppRoute.Dashboard;
        }

        _resumeWorkspaceAfterUnlock = false;
        NavigationAccountId = task.AccountId;
        NavigationActionId = task.ActionId;
        NavigateTo(route);
    }

    private bool WorkspaceStateHasFinishedLoading() =>
        _recoverySession is not null &&
        _accountInventory is not null &&
        _recoverySession.LoadState is not RecoverySessionLoadState.Locked and
            not RecoverySessionLoadState.Loading &&
        _accountInventory.LoadState is not AccountInventoryLoadState.Locked and
            not AccountInventoryLoadState.Loading;

    private bool HasWorkspaceLoadFailure() =>
        _recoverySession?.LoadState is RecoverySessionLoadState.Corrupted or
            RecoverySessionLoadState.LoadFailed ||
        _accountInventory?.LoadState is AccountInventoryLoadState.Corrupted or
            AccountInventoryLoadState.LoadFailed;

    private static AppRoute RouteFor(NextUserTaskTarget target) => target switch
    {
        NextUserTaskTarget.TrustedDeviceCheck or
        NextUserTaskTarget.TrustedDeviceGuidance or
        NextUserTaskTarget.VaultEntry => AppRoute.VaultEntry,
        NextUserTaskTarget.RecoveryOverview => AppRoute.Dashboard,
        NextUserTaskTarget.CsvImport => AppRoute.CsvImport,
        NextUserTaskTarget.AccountTriage => AppRoute.Accounts,
        NextUserTaskTarget.AccountRecovery => AppRoute.Workflow,
        NextUserTaskTarget.CredentialHandoff => AppRoute.CredentialsExport,
        NextUserTaskTarget.CompletionReview => AppRoute.Completion,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown recovery task target."),
    };

    private async void CsvImport_OnAccountReviewRequested(object? sender, EventArgs eventArgs)
    {
        await ContinueWorkspaceAsync(NextUserTaskTarget.AccountTriage, AppRoute.Accounts);
    }

    private async void Accounts_OnContinueToRecoveryRequested(object? sender, EventArgs eventArgs)
    {
        await ContinueWorkspaceAsync(NextUserTaskTarget.RecoveryOverview, AppRoute.Dashboard);
    }

    private async void Credentials_OnContinueToCompletionRequested(object? sender, EventArgs eventArgs)
    {
        await ContinueWorkspaceAsync(NextUserTaskTarget.CompletionReview, AppRoute.Completion);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the Avalonia async-event boundary. Recovery-flow and screen-activation failures must remain secret-safe UI state instead of escaping to the platform dispatcher and terminating the desktop process.")]
    private async Task ContinueWorkspaceAsync(
        NextUserTaskTarget expectedTarget,
        AppRoute destination)
    {
        try
        {
            if (await AdvanceWorkspaceTaskAsync(expectedTarget))
            {
                NavigateTo(destination);
            }
        }
        catch (Exception)
        {
            ShowRecoveryFlowFailure();
        }
    }

    private async Task<bool> AdvanceWorkspaceTaskAsync(NextUserTaskTarget expectedTarget)
    {
        if (_recoveryFlow is null)
        {
            return true;
        }

        var task = _recoveryFlow.NextTask;
        if (task.Target != expectedTarget)
        {
            ShowRecoveryFlowFailure();
            return false;
        }

        if (!task.RequiresTransition)
        {
            return true;
        }

        var result = await _recoveryFlow.AdvanceAsync(CancellationToken.None);
        if (result.Succeeded)
        {
            return true;
        }

        ShowRecoveryFlowFailure();
        return false;
    }

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
