using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public enum AccountInventoryFilter
{
    All,
    NeedsReview,
    Email,
    Critical,
    Unknown,
}

public enum AccountInventorySort
{
    ReviewPriority,
    RecoveryOrder,
    Provider,
    Updated,
}

public sealed record AccountInventoryOption<T>(T Value, string Label);

public sealed record AccountInventoryListItem(
    Guid Id,
    string DisplayName,
    string ProviderId,
    string CategoryText,
    string ReviewText,
    AccountInventoryEntry Account);

public sealed class AccountInventoryScreenViewModel : LocalizedScreenViewModel
{
    private readonly IAccountInventoryService _inventory;
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IRecoveryFlowService? _recoveryFlow;
    private AccountInventoryLoadState _loadState;
    private AccountInventoryState? _currentInventory;
    private AccountRecoveryOrder? _currentRecoveryOrder;
    private IReadOnlyList<AccountInventoryListItem> _accounts = [];
    private AccountInventoryListItem? _selectedAccount;
    private AccountInventoryOption<AccountRecoveryCategory>? _selectedCategory;
    private AccountInventoryOption<AccountInventoryFilter>? _selectedFilter;
    private AccountInventoryOption<AccountInventorySort>? _selectedSort;
    private Guid? _editingAccountId;
    private string _providerId = string.Empty;
    private string _accountName = string.Empty;
    private string _loginIdentifier = string.Empty;
    private string _accountUrl = string.Empty;
    private string? _validationMessage;
    private string _inventorySummary = string.Empty;
    private string _triageProgress = string.Empty;
    private string _continuationGuidance = string.Empty;

    public AccountInventoryScreenViewModel(
        IAccountInventoryService inventory,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        IRecoveryFlowService? recoveryFlow = null)
        : base(
            AppRoute.Accounts,
            localization,
            "Screen.Accounts.Title",
            "Screen.Accounts.Description",
            AppVisualState.Normal,
            "Screen.Accounts.StatusTitle",
            "Screen.Accounts.StatusMessage")
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _recoveryFlow = recoveryFlow;
        NewAccountCommand = new RelayCommand(BeginNewAccount, () => CanMutate);
        SaveAccountCommand = new AsyncCommand(
            SaveAccountAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            CanSaveAccount);
        SaveCategoryCommand = new AsyncCommand(
            SaveCategoryAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => HasPersistedAccount &&
                SelectedCategory is { } selected &&
                AccountRecoveryCategoryRules.IsUserSelectable(selected.Value));
        ClearCategoryOverrideCommand = new AsyncCommand(
            ClearCategoryOverrideAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => HasCategoryOverride);
        DeleteAccountCommand = new AsyncCommand(
            DeleteAccountAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedAccount is not null);
        ContinueRecoveryCommand = new AsyncCommand(
            ContinueRecoveryAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanContinueRecovery);
        _inventory.InventoryChanged += Inventory_OnInventoryChanged;
        _recoveryFlow?.NextTaskChanged += RecoveryFlow_OnNextTaskChanged;
        BuildStaticOptions();
        RefreshFromService();
    }

    public RelayCommand NewAccountCommand { get; }

    public AsyncCommand SaveAccountCommand { get; }

    public AsyncCommand SaveCategoryCommand { get; }

    public AsyncCommand ClearCategoryOverrideCommand { get; }

    public AsyncCommand DeleteAccountCommand { get; }

    public AsyncCommand ContinueRecoveryCommand { get; }

    public event EventHandler? ContinueToRecoveryRequested;

    public IReadOnlyList<AccountInventoryListItem> Accounts
    {
        get => _accounts;
        private set => SetProperty(ref _accounts, value);
    }

    public IReadOnlyList<AccountInventoryOption<AccountRecoveryCategory>> Categories { get; private set; } = [];

    public IReadOnlyList<AccountInventoryOption<AccountInventoryFilter>> Filters { get; private set; } = [];

    public IReadOnlyList<AccountInventoryOption<AccountInventorySort>> Sorts { get; private set; } = [];

    public AccountInventoryListItem? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                LoadSelectedAccount();
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryOption<AccountRecoveryCategory>? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                SaveCategoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AccountInventoryOption<AccountInventoryFilter>? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                RefreshAccountList();
            }
        }
    }

    public AccountInventoryOption<AccountInventorySort>? SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                RefreshAccountList();
            }
        }
    }

    public string ProviderId
    {
        get => _providerId;
        set => SetEditorValue(ref _providerId, value, nameof(ProviderId));
    }

    public string AccountName
    {
        get => _accountName;
        set => SetEditorValue(ref _accountName, value, nameof(AccountName));
    }

    public string LoginIdentifier
    {
        get => _loginIdentifier;
        set => SetEditorValue(ref _loginIdentifier, value, nameof(LoginIdentifier));
    }

    public string AccountUrl
    {
        get => _accountUrl;
        set => SetEditorValue(ref _accountUrl, value, nameof(AccountUrl));
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public string InventorySummary
    {
        get => _inventorySummary;
        private set => SetProperty(ref _inventorySummary, value);
    }

    public string TriageProgress
    {
        get => _triageProgress;
        private set => SetProperty(ref _triageProgress, value);
    }

    public string ContinuationGuidance
    {
        get => _continuationGuidance;
        private set => SetProperty(ref _continuationGuidance, value);
    }

    public bool HasValidationMessage => ValidationMessage is not null;

    public bool IsLocked => _loadState == AccountInventoryLoadState.Locked;

    public bool IsCorrupted => _loadState == AccountInventoryLoadState.Corrupted;

    public bool CanMutate => _loadState is AccountInventoryLoadState.Empty or AccountInventoryLoadState.Loaded;

    public bool IsEditingAccount => _editingAccountId is not null;

    public bool HasPersistedAccount => CanMutate &&
        _editingAccountId is { } accountId &&
        _currentInventory?.Accounts.Any(account => account.Id == accountId) == true;

    public bool HasCategoryOverride => HasPersistedAccount &&
        _editingAccountId is { } accountId &&
        _currentInventory?.Accounts.Single(account => account.Id == accountId).ConfirmedCategory.HasValue == true;

    public bool CanUseAutomaticCategory => HasCategoryOverride &&
        SelectedAccount?.Account is { } account &&
        AccountRecoveryCategoryRules.IsUserSelectable(account.SuggestedCategory);

    public bool HasEmailCategory =>
        _currentInventory?.Accounts.Any(account =>
            account.EffectiveCategory == AccountRecoveryCategory.Email) == true;

    public int RemainingCategoryCount =>
        _currentInventory?.Accounts.Count(account => account.RequiresCategoryReview) ?? 0;

    public bool CanContinueRecovery =>
        _currentInventory?.Accounts.Length > 0 &&
        (_recoveryFlow is null ||
         _recoveryFlow.NextTask.Target is
             NextUserTaskTarget.AccountTriage or NextUserTaskTarget.RecoveryOverview);

    public bool HasRemainingCategoryReview => CanContinueRecovery && RemainingCategoryCount > 0;

    public bool IsCategoryReviewComplete => CanContinueRecovery && RemainingCategoryCount == 0;

    public override void Activate() => RefreshFromService();

    protected override void RefreshLocalization()
    {
        var category = SelectedCategory?.Value;
        var filter = SelectedFilter?.Value ?? AccountInventoryFilter.All;
        var sort = SelectedSort?.Value ?? AccountInventorySort.ReviewPriority;
        base.RefreshLocalization();
        BuildStaticOptions();
        SelectedCategory = category is { } selected && AccountRecoveryCategoryRules.IsUserSelectable(selected)
            ? Categories.Single(option => option.Value == selected)
            : null;
        SelectedFilter = Filters.Single(option => option.Value == filter);
        SelectedSort = Sorts.Single(option => option.Value == sort);
        RefreshFromService();
    }

    private void BuildStaticOptions()
    {
        Categories =
        [
            .. Enum.GetValues<AccountRecoveryCategory>()
                .Where(AccountRecoveryCategoryRules.IsUserSelectable)
                .Select(value => new AccountInventoryOption<AccountRecoveryCategory>(
                    value,
                    Localization.GetString($"Accounts.Category.{value}"))),
        ];
        Filters =
        [
            .. Enum.GetValues<AccountInventoryFilter>().Select(value =>
                new AccountInventoryOption<AccountInventoryFilter>(
                    value,
                    Localization.GetString($"Accounts.Filter.{value}"))),
        ];
        Sorts =
        [
            .. Enum.GetValues<AccountInventorySort>().Select(value =>
                new AccountInventoryOption<AccountInventorySort>(
                    value,
                    Localization.GetString($"Accounts.Sort.{value}"))),
        ];
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(Sorts));
        SelectedFilter ??= Filters[0];
        SelectedSort ??= Sorts[0];
    }

    private void RefreshFromService()
    {
        CaptureInventoryProjection();
        var selectedId = _editingAccountId;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsCorrupted));
        OnPropertyChanged(nameof(CanMutate));
        var inventory = _currentInventory;
        InventorySummary = inventory is null
            ? Localization.GetString(_loadState switch
            {
                AccountInventoryLoadState.Locked => "Accounts.State.Locked",
                AccountInventoryLoadState.Loading => "Accounts.State.Loading",
                AccountInventoryLoadState.Corrupted => "Accounts.State.Corrupted",
                _ => "Accounts.State.Empty",
            })
            : Localization.FormatPlural("Accounts.Summary.Count", inventory.Accounts.Length, inventory.Accounts.Length);
        var remaining = RemainingCategoryCount;
        var total = inventory?.Accounts.Length ?? 0;
        TriageProgress = Localization.Format("Accounts.Triage.Progress", remaining, total);
        ContinuationGuidance = Localization.GetString(HasEmailCategory
            ? "Accounts.Triage.EmailReady"
            : remaining > 0
                ? "Accounts.Triage.EmailRecommended"
                : "Accounts.Triage.NoEmailReviewed");
        var needsAttention = remaining > 0 || (total > 0 && !HasEmailCategory);
        SetLocalizedStatus(
            needsAttention ? AppVisualState.Warning : AppVisualState.Normal,
            needsAttention ? "Accounts.Triage.Status.Title" : "Screen.Accounts.StatusTitle",
            needsAttention ? "Accounts.Triage.Status.Message" : "Screen.Accounts.StatusMessage");
        RefreshAccountList();
        var first = Accounts.Count == 0 ? null : Accounts[0];
        var requiredReview = Accounts.FirstOrDefault(item => item.Account.RequiresCategoryReview);
        var previous = selectedId is null
            ? null
            : Accounts.FirstOrDefault(item => item.Id == selectedId);
        SelectedAccount = previous?.Account.RequiresCategoryReview == true
            ? previous
            : requiredReview ?? previous ?? first;
        NotifyState();
        RaiseCommandStates();
    }

    private void RefreshAccountList()
    {
        IEnumerable<AccountInventoryEntry> accounts = _currentInventory?.Accounts ?? [];
        accounts = (SelectedFilter?.Value ?? AccountInventoryFilter.All) switch
        {
            AccountInventoryFilter.NeedsReview => accounts.Where(account => account.RequiresCategoryReview),
            AccountInventoryFilter.Email => accounts.Where(account => account.EffectiveCategory == AccountRecoveryCategory.Email),
            AccountInventoryFilter.Critical => accounts.Where(account => account.EffectiveCategory == AccountRecoveryCategory.Critical),
            AccountInventoryFilter.Unknown => accounts.Where(account => account.EffectiveCategory == AccountRecoveryCategory.Unknown),
            _ => accounts,
        };
        var order = _currentRecoveryOrder?.Items.ToDictionary(item => item.AccountId, item => item.Order) ?? [];
        accounts = (SelectedSort?.Value ?? AccountInventorySort.ReviewPriority) switch
        {
            AccountInventorySort.ReviewPriority => accounts
                .OrderBy(account => account.RequiresCategoryReview ? 0 : 1)
                .ThenBy(account => order.GetValueOrDefault(account.Id, int.MaxValue)),
            AccountInventorySort.RecoveryOrder => accounts.OrderBy(account => order.GetValueOrDefault(account.Id, int.MaxValue)),
            AccountInventorySort.Provider => accounts.OrderBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase),
            AccountInventorySort.Updated => accounts.OrderByDescending(account => account.UpdatedAt),
            _ => accounts,
        };
        Accounts =
        [
            .. accounts.Select(account => new AccountInventoryListItem(
                account.Id,
                account.AccountName ?? account.LoginIdentifier ?? account.ProviderId,
                account.ProviderId,
                Localization.GetString(account.RequiresCategoryReview
                    ? "Accounts.Triage.NeedsReview"
                    : $"Accounts.Category.{account.EffectiveCategory}"),
                Localization.GetString(account.RequiresCategoryReview
                    ? "Accounts.Triage.NotAutomaticallyRecognized"
                    : account.ConfirmedCategory.HasValue
                        ? "Accounts.Triage.ChangedByYou"
                        : "Accounts.Triage.AutomaticallyCategorized"),
                account)),
        ];
    }

    private void CaptureInventoryProjection()
    {
        var loadState = _inventory.LoadState;
        var inventory = _inventory.CurrentInventory;
        try
        {
            if (loadState == AccountInventoryLoadState.Loaded && inventory is null)
            {
                throw new InvalidOperationException(
                    "A loaded account inventory requires current inventory state.");
            }

            if (inventory is not null && loadState is not
                (AccountInventoryLoadState.Empty or AccountInventoryLoadState.Loaded))
            {
                throw new InvalidOperationException(
                    "Account inventory data cannot be exposed outside a readable load state.");
            }

            inventory?.Validate();
            _currentRecoveryOrder = inventory?.CreateRecoveryOrder();
            _currentInventory = inventory;
            _loadState = loadState;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _currentRecoveryOrder = null;
            _currentInventory = null;
            _loadState = AccountInventoryLoadState.Corrupted;
        }
    }

    private void LoadSelectedAccount()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var account = SelectedAccount.Account;
        _editingAccountId = account.Id;
        ProviderId = account.ProviderId;
        AccountName = account.AccountName ?? string.Empty;
        LoginIdentifier = account.LoginIdentifier ?? string.Empty;
        AccountUrl = account.AccountUrl ?? string.Empty;
        SelectedCategory = AccountRecoveryCategoryRules.IsUserSelectable(account.EffectiveCategory)
            ? Categories.Single(option => option.Value == account.EffectiveCategory)
            : null;
        ValidationMessage = null;
        NotifyState();
    }

    private void BeginNewAccount()
    {
        _editingAccountId = Guid.NewGuid();
        _selectedAccount = null;
        OnPropertyChanged(nameof(SelectedAccount));
        ProviderId = string.Empty;
        AccountName = string.Empty;
        LoginIdentifier = string.Empty;
        AccountUrl = string.Empty;
        SelectedCategory = null;
        ValidationMessage = null;
        NotifyState();
        RaiseCommandStates();
    }

    private async Task SaveAccountAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null)
        {
            return;
        }

        var result = await _inventory.UpsertAsync(
            new AccountInventoryUpsertRequest(
                _editingAccountId,
                ProviderId,
                AccountName,
                LoginIdentifier,
                AccountUrl),
            cancellationToken);
        ApplyResult(result);
    }

    private async Task SaveCategoryAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null ||
            SelectedCategory is not { } selected ||
            !AccountRecoveryCategoryRules.IsUserSelectable(selected.Value))
        {
            return;
        }

        var result = await _inventory.CategorizeAsync(
            _editingAccountId.Value,
            selected.Value,
            cancellationToken);
        ApplyResult(result);
    }

    private async Task ClearCategoryOverrideAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null || !HasCategoryOverride)
        {
            return;
        }

        var result = await _inventory.ClearCategoryOverrideAsync(
            _editingAccountId.Value,
            cancellationToken);
        ApplyResult(result);
    }

    private async Task ContinueRecoveryAsync(CancellationToken cancellationToken)
    {
        if (_recoveryFlow is not null &&
            _recoveryFlow.NextTask.Target == NextUserTaskTarget.AccountTriage)
        {
            var transition = await _recoveryFlow.AdvanceAsync(cancellationToken);
            if (!transition.Succeeded)
            {
                ValidationMessage = Localization.GetString("Accounts.Error.Flow");
                return;
            }
        }

        if (_recoveryFlow is not null &&
            _recoveryFlow.NextTask.Target != NextUserTaskTarget.RecoveryOverview)
        {
            ValidationMessage = Localization.GetString("Accounts.Error.Flow");
            return;
        }

        ContinueToRecoveryRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteAccountAsync(CancellationToken cancellationToken)
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Accounts.Delete.Action"),
                SelectedAccount.DisplayName,
                Localization.GetString("Accounts.Triage.DeleteConsequence"),
                Localization.GetString("Accounts.Delete.Confirm"),
                Localization.GetString("Confirmation.Risk.Destructive"),
                isDestructive: true),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        var result = await _inventory.RemoveAccountAsync(SelectedAccount.Id, cancellationToken);
        if (result.Succeeded)
        {
            _editingAccountId = null;
        }

        ApplyResult(result);
    }

    private void ApplyResult(AccountInventoryOperationResult result)
    {
        ValidationMessage = Localization.GetString(result.Succeeded
            ? "Accounts.Operation.Saved"
            : $"Accounts.Error.{result.FailureCode}");
        if (result.Succeeded)
        {
            RefreshFromService();
        }
    }

    private bool CanSaveAccount()
    {
        if (!CanMutate || _editingAccountId is null || string.IsNullOrWhiteSpace(ProviderId) ||
            (string.IsNullOrWhiteSpace(AccountName) && string.IsNullOrWhiteSpace(LoginIdentifier)))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(AccountUrl) ||
            (Uri.TryCreate(AccountUrl, UriKind.Absolute, out var uri) &&
             uri.Scheme is "https" or "http" &&
             !string.IsNullOrWhiteSpace(uri.Host) &&
             string.IsNullOrEmpty(uri.UserInfo));
    }

    private void SetEditorValue(ref string field, string? value, string propertyName)
    {
        if (SetProperty(ref field, value ?? string.Empty, propertyName))
        {
            SaveAccountCommand.RaiseCanExecuteChanged();
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEditingAccount));
        OnPropertyChanged(nameof(HasPersistedAccount));
        OnPropertyChanged(nameof(HasCategoryOverride));
        OnPropertyChanged(nameof(CanUseAutomaticCategory));
        OnPropertyChanged(nameof(HasEmailCategory));
        OnPropertyChanged(nameof(RemainingCategoryCount));
        OnPropertyChanged(nameof(CanContinueRecovery));
        OnPropertyChanged(nameof(HasRemainingCategoryReview));
        OnPropertyChanged(nameof(IsCategoryReviewComplete));
    }

    private void Inventory_OnInventoryChanged(object? sender, EventArgs eventArgs) => RefreshFromService();

    private void RecoveryFlow_OnNextTaskChanged(object? sender, EventArgs eventArgs) => RefreshFromService();

    private void RaiseCommandStates()
    {
        NewAccountCommand.RaiseCanExecuteChanged();
        SaveAccountCommand.RaiseCanExecuteChanged();
        SaveCategoryCommand.RaiseCanExecuteChanged();
        ClearCategoryOverrideCommand.RaiseCanExecuteChanged();
        DeleteAccountCommand.RaiseCanExecuteChanged();
        ContinueRecoveryCommand.RaiseCanExecuteChanged();
    }
}
