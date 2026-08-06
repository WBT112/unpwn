using System.ComponentModel;
using Unpwn.App.Localization;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IShellContextService _shellContext;
    private readonly ILocalizationService _localization;
    private IReadOnlyList<NavigationItemViewModel> _navigationItems;
    private IReadOnlyList<LanguageOptionViewModel> _languageOptions;
    private NavigationItemViewModel _selectedNavigation;
    private LanguageOptionViewModel _selectedLanguage;
    private ScreenViewModel _currentScreen;
    private VisualStatusViewModel _currentStatus;

    public ShellViewModel(
        IScreenFactory screenFactory,
        IShellContextService shellContext,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(screenFactory);
        ArgumentNullException.ThrowIfNull(shellContext);
        ArgumentNullException.ThrowIfNull(localization);

        _screenFactory = screenFactory;
        _shellContext = shellContext;
        _localization = localization;
        _navigationItems = BuildNavigationItems();
        _languageOptions = BuildLanguageOptions();
        _selectedLanguage = _languageOptions.Single(option =>
            option.Code == _localization.CurrentLanguageCode);
        _selectedNavigation = _navigationItems[0];
        _currentScreen = _screenFactory.Create(_selectedNavigation.Route);
        _currentScreen.PropertyChanged += CurrentScreen_OnPropertyChanged;
        _currentStatus = _currentScreen.Status;
        LockCommand = new AsyncCommand(
            LockAsync,
            () => _localization.GetString("Shell.Lock.Error"),
            () => IsVaultUnlocked);
        LockCommand.PropertyChanged += LockCommand_OnPropertyChanged;
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
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

            _currentScreen.PropertyChanged -= CurrentScreen_OnPropertyChanged;
            _currentScreen = value;
            _currentScreen.PropertyChanged += CurrentScreen_OnPropertyChanged;
            OnPropertyChanged();
        }
    }

    public VisualStatusViewModel CurrentStatus
    {
        get => _currentStatus;
        private set => SetProperty(ref _currentStatus, value);
    }

    public bool IsVaultUnlocked => _shellContext.Current.IsVaultUnlocked;

    public string VaultContextLabel => _shellContext.Current.IsVaultUnlocked
        ? _shellContext.Current.VaultDisplayName
        : _localization.GetString("Shell.Context.NoVault");

    public string SessionContextLabel => _shellContext.Current.IsVaultUnlocked
        ? _shellContext.Current.SessionDisplayName
        : _localization.GetString("Shell.Context.NoSession");

    public AsyncCommand LockCommand { get; }

    private async Task LockAsync(CancellationToken cancellationToken)
    {
        await _shellContext.LockAsync(cancellationToken);
        SelectedNavigation = NavigationItems[0];
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Success,
            _localization,
            "Shell.Lock.StatusTitle",
            "Shell.Lock.StatusMessage");
    }

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

    private IReadOnlyList<LanguageOptionViewModel> BuildLanguageOptions() =>
        _localization.SupportedLanguages
            .Select(language => new LanguageOptionViewModel(
                language.Code,
                _localization.GetString(language.DisplayNameKey)))
            .ToArray();

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsVaultUnlocked));
        OnPropertyChanged(nameof(VaultContextLabel));
        OnPropertyChanged(nameof(SessionContextLabel));
        LockCommand.RaiseCanExecuteChanged();
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

        if (LockCommand.LastOutcome == AsyncCommandOutcome.Failed)
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
