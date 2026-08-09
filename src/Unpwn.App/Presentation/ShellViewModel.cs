using System.ComponentModel;
using Unpwn.App.Localization;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly IRecoverySessionService? _recoverySession;
    private readonly IAccountInventoryService? _accountInventory;
    private readonly ILocalizationService _localization;
    private readonly bool _enforceNavigationPrerequisites;
    private IReadOnlyList<NavigationItemViewModel> _navigationItems;
    private readonly LanguageOptionViewModel[] _languageOptions;
    private NavigationItemViewModel _selectedNavigation;
    private LanguageOptionViewModel _selectedLanguage;
    private ScreenViewModel _currentScreen;
    private VisualStatusViewModel _currentStatus;
    private Guid? _navigationAccountId;
    private string? _navigationActionId;

    public ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        ILocalizationService localization)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession: null,
            accountInventory: null,
            localization,
            enforceNavigationPrerequisites: false)
    {
    }

    public ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        ILocalizationService localization)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession ?? throw new ArgumentNullException(nameof(recoverySession)),
            accountInventory ?? throw new ArgumentNullException(nameof(accountInventory)),
            localization,
            enforceNavigationPrerequisites: true)
    {
    }

    private ShellViewModel(
        IScreenFactory screenFactory,
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService? recoverySession,
        IAccountInventoryService? accountInventory,
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
        _localization = localization;
        _enforceNavigationPrerequisites = enforceNavigationPrerequisites;
        _navigationItems = BuildNavigationItems();
        _languageOptions = BuildLanguageOptions();
        _selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        _selectedNavigation = _navigationItems[0];
        _currentScreen = _screenFactory.Create(_selectedNavigation.Route);
        _currentScreen.Activate();
        SubscribeToScreen(_currentScreen);
        _currentStatus = _currentScreen.Status;
        LockCommand = new AsyncCommand(
            LockAsync,
            () => _localization.GetString("Shell.Lock.Error"),
            () => IsVaultUnlocked);
        LockCommand.PropertyChanged += LockCommand_OnPropertyChanged;
        _vaultLifecycle.ContextChanged += ShellContext_OnContextChanged;
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
        _recoverySession?.SessionChanged += RecoverySession_OnSessionChanged;
        _accountInventory?.InventoryChanged += AccountInventory_OnInventoryChanged;

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

    private async Task LockAsync(CancellationToken cancellationToken)
    {
        await _vaultLifecycle.LockAsync(cancellationToken);
        NavigateTo(AppRoute.VaultEntry);
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Success,
            _localization,
            "Shell.Lock.StatusTitle",
            "Shell.Lock.StatusMessage");
    }

    private void NavigateTo(AppRoute route) =>
        SelectedNavigation = NavigationItems.Single(item => item.Route == route);

    private void ShowScreen(AppRoute route)
    {
        var screen = _screenFactory.Create(route);
        if (screen is WorkflowExecutionScreenViewModel workflow)
        {
            workflow.Activate(NavigationAccountId, NavigationActionId);
        }
        else
        {
            screen.Activate();
        }

        CurrentScreen = screen;
        CurrentStatus = CurrentScreen.Status;
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
                : _recoverySession?.CurrentSession is null
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
            AppRoute.Accounts,
            _localization.GetString("Shell.Navigation.Accounts.Label"),
            _localization.GetString("Shell.Navigation.Accounts.Description"),
            "A",
            hasSession),
        new(
            AppRoute.Workflow,
            _localization.GetString("Shell.Navigation.Workflow.Label"),
            _localization.GetString("Shell.Navigation.Workflow.Description"),
            "W",
            hasAccounts),
        new(
            AppRoute.CredentialsExport,
            _localization.GetString("Shell.Navigation.Credentials.Label"),
            _localization.GetString("Shell.Navigation.Credentials.Description"),
            "C",
            hasAccounts),
        new(
            AppRoute.Completion,
            _localization.GetString("Shell.Navigation.Completion.Label"),
            _localization.GetString("Shell.Navigation.Completion.Description"),
            "✓",
            hasAccounts),
        new(
            AppRoute.CsvImport,
            _localization.GetString("Shell.Navigation.Import.Label"),
            _localization.GetString("Shell.Navigation.Import.Description"),
            "I",
            canMutateInventory),
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
        LockCommand.RaiseCanExecuteChanged();
        RefreshNavigationAvailability();

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
                _localization.Format("Vault.Inactivity.Warning.Message", locksAt));
        }
        else if (snapshot.Status == VaultLifecycleStatus.Locked &&
                 snapshot.LastLockReason == VaultLockReason.Inactivity)
        {
            NavigateTo(AppRoute.VaultEntry);
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Warning,
                _localization,
                "Vault.Inactivity.Locked.Title",
                "Vault.Inactivity.Locked.Message");
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

        if (_vaultLifecycle.Snapshot is
            { IsInactivityWarningVisible: true, InactivityLocksAt: { } locksAt })
        {
            CurrentStatus = new VisualStatusViewModel(
                AppVisualState.Warning,
                _localization.GetString("Status.Warning"),
                "!",
                _localization.GetString("Vault.Inactivity.Warning.Title"),
                _localization.Format("Vault.Inactivity.Warning.Message", locksAt));
        }
        else if (LockCommand.LastOutcome == AsyncCommandOutcome.Failed)
        {
            CurrentStatus = VisualStatusViewModel.Create(
                AppVisualState.Error,
                _localization,
                "Shell.Lock.FailedTitle",
                "Shell.Lock.Error");
        }
    }

    private void CurrentScreen_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ScreenViewModel.Status))
        {
            CurrentStatus = CurrentScreen.Status;
        }
    }

    private void RecoverySession_OnSessionChanged(object? sender, EventArgs eventArgs) =>
        RefreshNavigationAvailability();

    private void AccountInventory_OnInventoryChanged(object? sender, EventArgs eventArgs) =>
        RefreshNavigationAvailability();

    private void VaultEntry_OnContinueRequested(object? sender, EventArgs eventArgs) =>
        NavigateTo(AppRoute.Dashboard);

    private void Dashboard_OnNavigationRequested(
        object? sender,
        DashboardNavigationRequest eventArgs)
    {
        NavigationAccountId = eventArgs.AccountId;
        NavigationActionId = eventArgs.ActionId;
        NavigateTo(eventArgs.Route);
    }

    private void Workflow_OnPlanReturnRequested(
        object? sender,
        WorkflowPlanReturnRequest eventArgs)
    {
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
        screen.PropertyChanged += CurrentScreen_OnPropertyChanged;
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
    }

    private void UnsubscribeFromScreen(ScreenViewModel screen)
    {
        screen.PropertyChanged -= CurrentScreen_OnPropertyChanged;
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
                "Shell.Lock.Error");
        }
    }
}
