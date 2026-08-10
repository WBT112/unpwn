using System.ComponentModel;
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
    private readonly IGuidedRecoveryWizardService? _guidedWizard;
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
            guidedWizard: null,
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
        IGuidedRecoveryWizardService? guidedWizard = null)
        : this(
            screenFactory,
            vaultLifecycle,
            recoverySession ?? throw new ArgumentNullException(nameof(recoverySession)),
            accountInventory ?? throw new ArgumentNullException(nameof(accountInventory)),
            guidedWizard,
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
        GuidedOpenCommand = new RelayCommand(OpenGuidedStep, () => IsGuidedWizardVisible);
        GuidedAdvanceCommand = new AsyncCommand(
            AdvanceGuidedStepAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedWizardVisible && !_guidedWizard!.Current.IsTerminal);
        GuidedBackCommand = new AsyncCommand(
            GoBackGuidedStepAsync,
            () => _localization.GetString("Shell.Guided.Error"),
            () => IsGuidedWizardVisible && _guidedWizard!.PreviousDecision.CanMove);
        _vaultLifecycle.ContextChanged += ShellContext_OnContextChanged;
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
        _recoverySession?.SessionChanged += RecoverySession_OnSessionChanged;
        _accountInventory?.InventoryChanged += AccountInventory_OnInventoryChanged;
        if (_guidedWizard is not null)
        {
            _guidedWizard.GuidanceChanged += GuidedWizard_OnGuidanceChanged;
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

    public bool IsGuidedWizardVisible =>
        _guidedWizard is not null && IsVaultUnlocked && _recoverySession?.CurrentSession is not null;

    public string GuidedStepText => _guidedWizard is null
        ? string.Empty
        : _localization.GetString(GetWizardStepKey(_guidedWizard.Current.CurrentStep));

    public string GuidedRecommendationText => _guidedWizard is null
        ? string.Empty
        : _localization.GetString(GetGuidanceKey(_guidedWizard.NextDecision));

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
                GetGuidanceKey(result.Decision));
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
        var isTerminal = _enforceNavigationPrerequisites &&
            _recoverySession?.CurrentSession?.IsReadOnly == true;
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
            hasSession && !isTerminal),
        new(
            AppRoute.Workflow,
            _localization.GetString("Shell.Navigation.Workflow.Label"),
            _localization.GetString("Shell.Navigation.Workflow.Description"),
            "W",
            hasAccounts && !isTerminal),
        new(
            AppRoute.CredentialsExport,
            _localization.GetString("Shell.Navigation.Credentials.Label"),
            _localization.GetString("Shell.Navigation.Credentials.Description"),
            "C",
            hasAccounts && !isTerminal),
        new(
            AppRoute.Completion,
            _localization.GetString("Shell.Navigation.Completion.Label"),
            _localization.GetString("Shell.Navigation.Completion.Description"),
            "✓",
            hasAccounts || isTerminal),
        new(
            AppRoute.CsvImport,
            _localization.GetString("Shell.Navigation.Import.Label"),
            _localization.GetString("Shell.Navigation.Import.Description"),
            "I",
            canMutateInventory && !isTerminal),
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
        RefreshGuidance();
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
        RefreshGuidance();

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

    private void RecoverySession_OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        RefreshNavigationAvailability();
        RefreshGuidance();
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
                    "Shell.Guided.Error");
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


        if (screen is CompletionScreenViewModel completion)
        {
            completion.NavigationRequested += Completion_OnNavigationRequested;
            completion.CompletionReviewSucceeded += Completion_OnReviewSucceeded;
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
                "Shell.Guided.Error");
        }
    }

    private void GuidedWizard_OnGuidanceChanged(object? sender, EventArgs eventArgs) =>
        RefreshGuidance();

    private void RefreshGuidance()
    {
        OnPropertyChanged(nameof(IsGuidedWizardVisible));
        OnPropertyChanged(nameof(GuidedStepText));
        OnPropertyChanged(nameof(GuidedRecommendationText));
        GuidedOpenCommand.RaiseCanExecuteChanged();
        GuidedAdvanceCommand.RaiseCanExecuteChanged();
        GuidedBackCommand.RaiseCanExecuteChanged();
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
            "account-inventory" or "identity-review" => AppRoute.Accounts,
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
