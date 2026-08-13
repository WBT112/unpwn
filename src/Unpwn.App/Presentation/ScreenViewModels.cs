using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application;
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
        protected set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsTransientStatusVisible));
            }
        }
    }

    public bool IsTransientStatusVisible => Status.IsTransientResult;

    public virtual void Activate()
    {
    }

    public virtual void Deactivate()
    {
    }
}

public abstract class LocalizedScreenViewModel : ScreenViewModel
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private AppVisualState _statusState;
    private string _statusTitleKey;
    private string _statusMessageKey;
    private StatusPresentation _statusPresentation;

    protected LocalizedScreenViewModel(
        AppRoute route,
        ILocalizationService localization,
        string titleKey,
        string descriptionKey,
        AppVisualState statusState,
        string statusTitleKey,
        string statusMessageKey,
        StatusPresentation statusPresentation = StatusPresentation.ScreenInstruction)
        : base(
            route,
            Get(localization, titleKey),
            Get(localization, descriptionKey),
            VisualStatusViewModel.Create(
                statusState,
                Require(localization),
                statusTitleKey,
                statusMessageKey,
                statusPresentation))
    {
        Localization = localization;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _statusState = statusState;
        _statusTitleKey = statusTitleKey;
        _statusMessageKey = statusMessageKey;
        _statusPresentation = statusPresentation;
        Localization.CultureChanged += Localization_OnCultureChanged;
    }

    public ILocalizationService Localization { get; }

    protected void SetLocalizedStatus(
        AppVisualState state,
        string titleKey,
        string messageKey,
        StatusPresentation presentation = StatusPresentation.ScreenInstruction)
    {
        _statusState = state;
        _statusTitleKey = titleKey;
        _statusMessageKey = messageKey;
        _statusPresentation = presentation;
        Status = VisualStatusViewModel.Create(
            state,
            Localization,
            titleKey,
            messageKey,
            presentation);
    }

    protected virtual void RefreshLocalization()
    {
        Title = Localization.GetString(_titleKey);
        Description = Localization.GetString(_descriptionKey);
        Status = VisualStatusViewModel.Create(
            _statusState,
            Localization,
            _statusTitleKey,
            _statusMessageKey,
            _statusPresentation);
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
    private readonly IRecoveryFlowService? _recoveryFlow;
    private int _importActive;

    public CsvImportScreenViewModel(
        IAccountInventoryService inventory,
        ILocalizationService localization,
        IRecoveryFlowService? recoveryFlow = null)
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
        _recoveryFlow = recoveryFlow;
        ContinueToAccountReviewCommand = new RelayCommand(
            () => ContinueRequested?.Invoke(this, EventArgs.Empty),
            () => IsAccountReviewContinuationVisible);
        _inventory.InventoryChanged += Inventory_OnInventoryChanged;
        _recoveryFlow?.NextTaskChanged += RecoveryFlow_OnNextTaskChanged;
    }

    public CsvImportScreenViewModel(ILocalizationService localization)
        : this(new UnavailableAccountInventoryService(), localization)
    {
    }

    public event EventHandler? ContinueRequested;

    public RelayCommand ContinueToAccountReviewCommand { get; }

    public bool HasImportedAccounts =>
        _inventory.CurrentInventory?.Accounts.Length > 0;

    public bool IsAccountReviewContinuationVisible =>
        HasImportedAccounts &&
        (_recoveryFlow is null ||
         _recoveryFlow.NextTask.Target == NextUserTaskTarget.AccountTriage);

    public IReadOnlyList<ExistingAccountReference> ExistingAccounts =>
        _inventory.GetExistingAccountReferences();

    public async Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (Interlocked.CompareExchange(ref _importActive, 1, 0) != 0)
        {
            return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Conflict);
        }

        try
        {
            if (duplicateResolution == ImportDuplicateResolution.SkipDuplicates &&
                candidates.Count > 0 &&
                candidates.All(candidate => candidate.DuplicateKind != CsvDuplicateKind.None))
            {
                return AccountInventoryOperationResult.Success(affectedAccounts: 0);
            }

            var result = await _inventory.ImportAsync(candidates, duplicateResolution, cancellationToken);
            RefreshContinuationState();
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _importActive, 0);
        }
    }

    public override void Activate() => RefreshContinuationState();

    private void Inventory_OnInventoryChanged(object? sender, EventArgs eventArgs) =>
        RefreshContinuationState();

    private void RecoveryFlow_OnNextTaskChanged(object? sender, EventArgs eventArgs) =>
        RefreshContinuationState();

    private void RefreshContinuationState()
    {
        OnPropertyChanged(nameof(HasImportedAccounts));
        OnPropertyChanged(nameof(IsAccountReviewContinuationVisible));
        ContinueToAccountReviewCommand.RaiseCanExecuteChanged();
    }

    public static string GetImportResultResourceKey(AccountInventoryOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            return result.AffectedAccounts == 0
                ? "Import.Result.NoChanges"
                : "Import.Result.Success";
        }

        return result.FailureCode switch
        {
            AccountInventoryFailureCode.Locked => "Accounts.Error.Locked",
            AccountInventoryFailureCode.InvalidInput => "Accounts.Error.InvalidInput",
            AccountInventoryFailureCode.NotFound => "Accounts.Error.NotFound",
            AccountInventoryFailureCode.Conflict => "Accounts.Error.Conflict",
            AccountInventoryFailureCode.RequiresConfirmation => "Accounts.Error.RequiresConfirmation",
            AccountInventoryFailureCode.Corrupted => "Accounts.Error.Corrupted",
            AccountInventoryFailureCode.IoFailure => "Accounts.Error.IoFailure",
            _ => "Import.Result.Failure",
        };
    }
}
