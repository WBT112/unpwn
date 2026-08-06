using System.ComponentModel;
using Unpwn.App.Localization;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly ILocalizationService _localization;
    private IReadOnlyList<NavigationItemViewModel> _navigationItems;
    private LanguageOptionViewModel[] _languageOptions;
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
    {
        ArgumentNullException.ThrowIfNull(screenFactory);
        ArgumentNullException.ThrowIfNull(vaultLifecycle);
        ArgumentNullException.ThrowIfNull(localization);

        _screenFactory = screenFactory;
        _vaultLifecycle = vaultLifecycle;
        _localization = localization;
        _navigationItems = BuildNavigationItems();
        _languageOptions = BuildLanguageOptions();
        _selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        _selectedNavigation = _navigationItems[0];
        _currentScreen = _screenFactory.Create(_selectedNavigation.Route);
        SubscribeToScreen(_currentScreen);
        _currentStatus = _currentScreen.Status;
        LockCommand = new AsyncCommand(
            LockAsync,
            () => _localization.GetString("Shell.Lock.Error"),
            () => IsVaultUnlocked);
        LockCommand.PropertyChanged += LockCommand_OnPropertyChanged;
        _vaultLifecycle.ContextChanged += ShellContext_OnContextChanged;
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
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
            if (value.Code == _localization.CurrentLanguageCode)
            {
                return;
            }

            _localization.SetLanguage(value.Code);
        }
    }

    public NavigationItemViewModel SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedNavigation, value))
            {
                return;
            }

            CurrentScreen = _screenFactory.Create(value.Route);
            CurrentStatus = CurrentScreen.Status;
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

    private IReadOnlyList<NavigationItemViewModel> BuildNavigationItems() =>
    [
        new(
            AppRoute.VaultEntry,
            _localization.GetString("Shell.Navigation.Vault.Label"),
            _localization.GetString("Shell.Navigation.Vault.Description"),
            "V"),
        new(
            AppRoute.Dashboard,
            _localization.GetString("Shell.Navigation.Dashboard.Label"),
            _localization.GetString("Shell.Navigation.Dashboard.Description"),
            "D"),
        new(
            AppRoute.Accounts,
            _localization.GetString("Shell.Navigation.Accounts.Label"),
            _localization.GetString("Shell.Navigation.Accounts.Description"),
            "A"),
        new(
            AppRoute.Workflow,
            _localization.GetString("Shell.Navigation.Workflow.Label"),
            _localization.GetString("Shell.Navigation.Workflow.Description"),
            "W"),
        new(
            AppRoute.CredentialsExport,
            _localization.GetString("Shell.Navigation.Credentials.Label"),
            _localization.GetString("Shell.Navigation.Credentials.Description"),
            "C"),
        new(
            AppRoute.Completion,
            _localization.GetString("Shell.Navigation.Completion.Label"),
            _localization.GetString("Shell.Navigation.Completion.Description"),
            "✓"),
        new(
            AppRoute.CsvImport,
            _localization.GetString("Shell.Navigation.Import.Label"),
            _localization.GetString("Shell.Navigation.Import.Description"),
            "I"),
    ];

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
        var selectedRoute = SelectedNavigation.Route;
        _navigationItems = BuildNavigationItems();
        OnPropertyChanged(nameof(NavigationItems));
        _selectedNavigation = _navigationItems.Single(item => item.Route == selectedRoute);
        OnPropertyChanged(nameof(SelectedNavigation));

        _languageOptions = BuildLanguageOptions();
        OnPropertyChanged(nameof(LanguageOptions));
        _selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        OnPropertyChanged(nameof(SelectedLanguage));

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
