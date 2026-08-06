using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Import.Csv;

namespace Unpwn.App.Presentation;

public abstract class ScreenViewModel(
    AppRoute route,
    string title,
    string description,
    VisualStatusViewModel status) : ObservableObject
{
    private string _title = title;
    private string _description = description;
    private VisualStatusViewModel _status = status;

    public AppRoute Route { get; } = route;

    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        protected set => SetProperty(ref _description, value);
    }

    public VisualStatusViewModel Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }
}

public abstract class LocalizedScreenViewModel : ScreenViewModel
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private AppVisualState _statusState;
    private string _statusTitleKey;
    private string _statusMessageKey;

    protected LocalizedScreenViewModel(
        AppRoute route,
        ILocalizationService localization,
        string titleKey,
        string descriptionKey,
        AppVisualState statusState,
        string statusTitleKey,
        string statusMessageKey)
        : base(
            route,
            Get(localization, titleKey),
            Get(localization, descriptionKey),
            VisualStatusViewModel.Create(
                statusState,
                Require(localization),
                statusTitleKey,
                statusMessageKey))
    {
        Localization = localization;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _statusState = statusState;
        _statusTitleKey = statusTitleKey;
        _statusMessageKey = statusMessageKey;
        Localization.CultureChanged += Localization_OnCultureChanged;
    }

    public ILocalizationService Localization { get; }

    protected void SetLocalizedStatus(
        AppVisualState state,
        string titleKey,
        string messageKey)
    {
        _statusState = state;
        _statusTitleKey = titleKey;
        _statusMessageKey = messageKey;
        Status = VisualStatusViewModel.Create(state, Localization, titleKey, messageKey);
    }

    protected virtual void RefreshLocalization()
    {
        Title = Localization.GetString(_titleKey);
        Description = Localization.GetString(_descriptionKey);
        Status = VisualStatusViewModel.Create(
            _statusState,
            Localization,
            _statusTitleKey,
            _statusMessageKey);
    }

    private static ILocalizationService Require(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization;
    }

    private static string Get(ILocalizationService localization, string key) =>
        Require(localization).GetString(key);

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs) => RefreshLocalization();
}

public sealed class PlaceholderScreenViewModel(
    AppRoute route,
    ILocalizationService localization,
    string titleKey,
    string descriptionKey,
    AppVisualState statusState,
    string statusTitleKey,
    string statusMessageKey) : LocalizedScreenViewModel(
        route,
        localization,
        titleKey,
        descriptionKey,
        statusState,
        statusTitleKey,
        statusMessageKey);

public sealed class CsvImportScreenViewModel : LocalizedScreenViewModel
{
    private readonly IAccountInventoryService _inventory;

    public CsvImportScreenViewModel(
        IAccountInventoryService inventory,
        ILocalizationService localization)
        : base(
            AppRoute.CsvImport,
            localization,
            "Screen.Import.Title",
            "Screen.Import.Description",
            AppVisualState.Warning,
            "Screen.Import.StatusTitle",
            "Screen.Import.StatusMessage")
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public IReadOnlyList<ExistingAccountReference> ExistingAccounts =>
        _inventory.GetExistingAccountReferences();

    public Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken) =>
        _inventory.ImportAsync(candidates, duplicateResolution, cancellationToken);
}

public sealed class CompletionScreenViewModel : LocalizedScreenViewModel
{
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IShellContextService _shellContext;

    public CompletionScreenViewModel(
        IConfirmationDialogService confirmationDialog,
        IShellContextService shellContext,
        ILocalizationService localization)
        : base(
            AppRoute.Completion,
            localization,
            "Screen.Completion.Title",
            "Screen.Completion.Description",
            AppVisualState.UnresolvedRisk,
            "Screen.Completion.StatusTitle",
            "Screen.Completion.StatusMessage")
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(shellContext);

        _confirmationDialog = confirmationDialog;
        _shellContext = shellContext;
        ReviewCompletionCommand = new AsyncCommand(
            ReviewCompletionAsync,
            () => Localization.GetString("Completion.Command.Error"),
            () => _shellContext.Current.IsVaultUnlocked);
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
    }

    public AsyncCommand ReviewCompletionCommand { get; }

    public bool HasUnlockedVault => _shellContext.Current.IsVaultUnlocked;

    private async Task ReviewCompletionAsync(CancellationToken cancellationToken)
    {
        var context = _shellContext.Current;
        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Completion.Confirmation.Action"),
                context.SessionDisplayName ?? Localization.GetString("Shell.Context.NoSession"),
                Localization.GetString("Completion.Confirmation.Consequence"),
                Localization.GetString("Completion.Confirmation.Confirm"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);

        if (confirmed)
        {
            SetLocalizedStatus(
                AppVisualState.Success,
                "Completion.Confirmed.Title",
                "Completion.Confirmed.Message");
        }
        else
        {
            SetLocalizedStatus(
                AppVisualState.Normal,
                "Completion.Canceled.Title",
                "Completion.Canceled.Message");
        }
    }

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasUnlockedVault));
        ReviewCompletionCommand.RaiseCanExecuteChanged();
    }
}
