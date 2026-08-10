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

public sealed class CsvImportScreenViewModel(
    IAccountInventoryService inventory,
    ILocalizationService localization)
    : LocalizedScreenViewModel(
        AppRoute.CsvImport,
        localization,
        "Screen.Import.Title",
        "Screen.Import.Description",
        AppVisualState.Warning,
        "Screen.Import.StatusTitle",
        "Screen.Import.StatusMessage")
{
    private readonly IAccountInventoryService _inventory =
        inventory ?? throw new ArgumentNullException(nameof(inventory));

    public CsvImportScreenViewModel(ILocalizationService localization)
        : this(new UnavailableAccountInventoryService(), localization)
    {
    }

    public IReadOnlyList<ExistingAccountReference> ExistingAccounts =>
        _inventory.GetExistingAccountReferences();

    public Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (duplicateResolution == ImportDuplicateResolution.SkipDuplicates &&
            candidates.Count > 0 &&
            candidates.All(candidate => candidate.DuplicateKind != CsvDuplicateKind.None))
        {
            return Task.FromResult(AccountInventoryOperationResult.Success(affectedAccounts: 0));
        }

        return _inventory.ImportAsync(candidates, duplicateResolution, cancellationToken);
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
            AccountInventoryFailureCode.RequiresOverrideReason => "Accounts.Error.RequiresOverrideReason",
            AccountInventoryFailureCode.Corrupted => "Accounts.Error.Corrupted",
            AccountInventoryFailureCode.IoFailure => "Accounts.Error.IoFailure",
            _ => "Import.Result.Failure",
        };
    }
}
