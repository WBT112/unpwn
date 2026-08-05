using System.ComponentModel;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class ShellViewModel : ObservableObject
{
    private readonly IScreenFactory _screenFactory;
    private readonly IShellContextService _shellContext;
    private NavigationItemViewModel _selectedNavigation;
    private ScreenViewModel _currentScreen;
    private VisualStatusViewModel _currentStatus;

    public ShellViewModel(IScreenFactory screenFactory, IShellContextService shellContext)
    {
        ArgumentNullException.ThrowIfNull(screenFactory);
        ArgumentNullException.ThrowIfNull(shellContext);

        _screenFactory = screenFactory;
        _shellContext = shellContext;
        NavigationItems =
        [
            new(AppRoute.VaultEntry, "Vault", "Open or create workspace", "V"),
            new(AppRoute.Dashboard, "Dashboard", "Recovery overview", "D"),
            new(AppRoute.Accounts, "Accounts", "Inventory and priorities", "A"),
            new(AppRoute.Workflow, "Workflow", "Recovery actions", "W"),
            new(AppRoute.CredentialsExport, "Credentials", "New credentials and export", "C"),
            new(AppRoute.Completion, "Completion", "Final review", "✓"),
            new(AppRoute.CsvImport, "CSV import", "Import account inventory", "I"),
        ];

        _selectedNavigation = NavigationItems[0];
        _currentScreen = _screenFactory.Create(_selectedNavigation.Route);
        _currentScreen.PropertyChanged += CurrentScreen_OnPropertyChanged;
        _currentStatus = _currentScreen.Status;
        LockCommand = new AsyncCommand(
            LockAsync,
            "The recovery vault could not be locked.",
            () => IsVaultUnlocked);
        LockCommand.PropertyChanged += LockCommand_OnPropertyChanged;
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

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
        : "No vault unlocked";

    public string SessionContextLabel => _shellContext.Current.IsVaultUnlocked
        ? _shellContext.Current.SessionDisplayName
        : "No recovery session";

    public AsyncCommand LockCommand { get; }

    private async Task LockAsync(CancellationToken cancellationToken)
    {
        await _shellContext.LockAsync(cancellationToken);
        SelectedNavigation = NavigationItems[0];
        CurrentStatus = VisualStatusViewModel.Create(
            AppVisualState.Success,
            "Vault locked",
            "Recovery data is no longer available in the application shell.");
    }

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsVaultUnlocked));
        OnPropertyChanged(nameof(VaultContextLabel));
        OnPropertyChanged(nameof(SessionContextLabel));
        LockCommand.RaiseCanExecuteChanged();
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
                "Vault lock failed",
                LockCommand.LastErrorMessage ?? "The recovery vault could not be locked.");
        }
    }
}
