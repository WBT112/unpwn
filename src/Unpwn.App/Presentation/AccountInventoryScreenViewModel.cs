using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public enum AccountInventoryFilter
{
    All,
    Critical,
    RecoveryChannels,
    NeedsRoleConfirmation,
    Blocked,
}

public enum AccountInventorySort
{
    RecoveryOrder,
    Priority,
    Provider,
    Updated,
}

public sealed record AccountInventoryOption<T>(T Value, string Label);

public sealed record AccountInventoryListItem(
    Guid Id,
    string DisplayName,
    string ProviderId,
    string PriorityText,
    string RoleText,
    string PlanText,
    AccountInventoryEntry Account);

public sealed record AccountInventoryDependencyItem(
    Guid DependsOnAccountId,
    AccountDependencyKind Kind,
    string DisplayText,
    bool IsOverride,
    string? OverrideReason);

public sealed record AccountInventoryPlanDisplayItem(
    int Order,
    Guid AccountId,
    string DisplayText,
    AccountInventoryPlanStatus Status);

public sealed class AccountInventoryScreenViewModel : LocalizedScreenViewModel
{
    private static readonly AccountInventoryRole[] IndividualRoles =
    [
        AccountInventoryRole.EmailMailbox,
        AccountInventoryRole.PasswordManager,
        AccountInventoryRole.IdentityProvider,
        AccountInventoryRole.RecoveryEmail,
        AccountInventoryRole.TelephoneRecovery,
        AccountInventoryRole.OrganizationManagedSignIn,
    ];

    private readonly IAccountInventoryService _inventory;
    private readonly IConfirmationDialogService _confirmationDialog;
    private IReadOnlyList<AccountInventoryListItem> _accounts = [];
    private IReadOnlyList<AccountInventoryDependencyItem> _dependencies = [];
    private IReadOnlyList<AccountInventoryPlanDisplayItem> _planItems = [];
    private IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> _suggestedRoles = [];
    private IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> _confirmedRoles = [];
    private IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> _availableRoles = [];
    private IReadOnlyList<AccountInventoryOption<Guid>> _dependencyTargets = [];
    private string _searchText = string.Empty;
    private AccountInventoryListItem? _selectedAccount;
    private AccountInventoryOption<AccountInventoryPriority>? _selectedPriority;
    private AccountInventoryOption<AccountInventoryFilter>? _selectedFilter;
    private AccountInventoryOption<AccountInventorySort>? _selectedSort;
    private AccountInventoryOption<AccountInventoryRole>? _selectedSuggestedRole;
    private AccountInventoryOption<AccountInventoryRole>? _selectedConfirmedRole;
    private AccountInventoryOption<AccountInventoryRole>? _selectedRoleToAdd;
    private AccountInventoryOption<Guid>? _selectedDependencyTarget;
    private AccountInventoryOption<AccountDependencyKind>? _selectedDependencyKind;
    private AccountInventoryDependencyItem? _selectedDependency;
    private Guid? _editingAccountId;
    private string _providerId = string.Empty;
    private string _accountName = string.Empty;
    private string _loginIdentifier = string.Empty;
    private string _accountUrl = string.Empty;
    private string _overrideReason = string.Empty;
    private string? _validationMessage;
    private string _inventorySummary = string.Empty;
    private string _planSummary = string.Empty;

    public AccountInventoryScreenViewModel(
        IAccountInventoryService inventory,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization)
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
        NewAccountCommand = new RelayCommand(BeginNewAccount, () => CanMutate);
        RefreshCommand = new RelayCommand(RefreshFromService);
        SaveAccountCommand = new AsyncCommand(
            SaveAccountAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && _editingAccountId is not null);
        DeleteAccountCommand = new AsyncCommand(
            DeleteAccountAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedAccount is not null);
        AcceptSuggestedRoleCommand = new AsyncCommand(
            cancellationToken => DecideSelectedRoleAsync(AccountRoleDecision.Confirmed, cancellationToken),
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedSuggestedRole is not null && _editingAccountId is not null);
        RejectSuggestedRoleCommand = new AsyncCommand(
            cancellationToken => DecideSelectedRoleAsync(AccountRoleDecision.Rejected, cancellationToken),
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedSuggestedRole is not null && _editingAccountId is not null);
        AddRoleCommand = new AsyncCommand(
            cancellationToken => DecideAddedRoleAsync(AccountRoleDecision.Confirmed, cancellationToken),
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedRoleToAdd is not null && _editingAccountId is not null);
        RemoveRoleCommand = new AsyncCommand(
            cancellationToken => DecideConfirmedRoleAsync(AccountRoleDecision.Rejected, cancellationToken),
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && SelectedConfirmedRole is not null && _editingAccountId is not null);
        AddDependencyCommand = new AsyncCommand(
            AddDependencyAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && _editingAccountId is not null &&
                  SelectedDependencyTarget is not null && SelectedDependencyKind is not null);
        RemoveDependencyCommand = new AsyncCommand(
            RemoveDependencyAsync,
            () => Localization.GetString("Accounts.Error.Command"),
            () => CanMutate && _editingAccountId is not null && SelectedDependency is not null);
        _inventory.InventoryChanged += Inventory_OnInventoryChanged;
        BuildStaticOptions();
        RefreshFromService();
    }

    public RelayCommand NewAccountCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public AsyncCommand SaveAccountCommand { get; }

    public AsyncCommand DeleteAccountCommand { get; }

    public AsyncCommand AcceptSuggestedRoleCommand { get; }

    public AsyncCommand RejectSuggestedRoleCommand { get; }

    public AsyncCommand AddRoleCommand { get; }

    public AsyncCommand RemoveRoleCommand { get; }

    public AsyncCommand AddDependencyCommand { get; }

    public AsyncCommand RemoveDependencyCommand { get; }

    public IReadOnlyList<AccountInventoryListItem> Accounts
    {
        get => _accounts;
        private set => SetProperty(ref _accounts, value);
    }

    public IReadOnlyList<AccountInventoryDependencyItem> Dependencies
    {
        get => _dependencies;
        private set => SetProperty(ref _dependencies, value);
    }

    public IReadOnlyList<AccountInventoryPlanDisplayItem> PlanItems
    {
        get => _planItems;
        private set => SetProperty(ref _planItems, value);
    }

    public IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> SuggestedRoles
    {
        get => _suggestedRoles;
        private set => SetProperty(ref _suggestedRoles, value);
    }

    public IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> ConfirmedRoles
    {
        get => _confirmedRoles;
        private set => SetProperty(ref _confirmedRoles, value);
    }

    public IReadOnlyList<AccountInventoryOption<AccountInventoryRole>> AvailableRoles
    {
        get => _availableRoles;
        private set => SetProperty(ref _availableRoles, value);
    }

    public IReadOnlyList<AccountInventoryOption<Guid>> DependencyTargets
    {
        get => _dependencyTargets;
        private set => SetProperty(ref _dependencyTargets, value);
    }

    public IReadOnlyList<AccountInventoryOption<AccountInventoryPriority>> Priorities { get; private set; } = [];

    public IReadOnlyList<AccountInventoryOption<AccountInventoryFilter>> Filters { get; private set; } = [];

    public IReadOnlyList<AccountInventoryOption<AccountInventorySort>> Sorts { get; private set; } = [];

    public IReadOnlyList<AccountInventoryOption<AccountDependencyKind>> DependencyKinds { get; private set; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshAccountList();
            }
        }
    }

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

    public AccountInventoryOption<AccountInventoryPriority>? SelectedPriority
    {
        get => _selectedPriority;
        set => SetProperty(ref _selectedPriority, value);
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

    public AccountInventoryOption<AccountInventoryRole>? SelectedSuggestedRole
    {
        get => _selectedSuggestedRole;
        set
        {
            if (SetProperty(ref _selectedSuggestedRole, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryOption<AccountInventoryRole>? SelectedConfirmedRole
    {
        get => _selectedConfirmedRole;
        set
        {
            if (SetProperty(ref _selectedConfirmedRole, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryOption<AccountInventoryRole>? SelectedRoleToAdd
    {
        get => _selectedRoleToAdd;
        set
        {
            if (SetProperty(ref _selectedRoleToAdd, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryOption<Guid>? SelectedDependencyTarget
    {
        get => _selectedDependencyTarget;
        set
        {
            if (SetProperty(ref _selectedDependencyTarget, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryOption<AccountDependencyKind>? SelectedDependencyKind
    {
        get => _selectedDependencyKind;
        set
        {
            if (SetProperty(ref _selectedDependencyKind, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AccountInventoryDependencyItem? SelectedDependency
    {
        get => _selectedDependency;
        set
        {
            if (SetProperty(ref _selectedDependency, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ProviderId
    {
        get => _providerId;
        set => SetProperty(ref _providerId, value);
    }

    public string AccountName
    {
        get => _accountName;
        set => SetProperty(ref _accountName, value);
    }

    public string LoginIdentifier
    {
        get => _loginIdentifier;
        set => SetProperty(ref _loginIdentifier, value);
    }

    public string AccountUrl
    {
        get => _accountUrl;
        set => SetProperty(ref _accountUrl, value);
    }

    public string OverrideReason
    {
        get => _overrideReason;
        set => SetProperty(ref _overrideReason, value);
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

    public bool HasValidationMessage => ValidationMessage is not null;

    public string InventorySummary
    {
        get => _inventorySummary;
        private set => SetProperty(ref _inventorySummary, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public bool IsLocked => _inventory.LoadState == AccountInventoryLoadState.Locked;

    public bool IsCorrupted => _inventory.LoadState == AccountInventoryLoadState.Corrupted;

    public bool CanMutate => _inventory.LoadState is AccountInventoryLoadState.Empty or AccountInventoryLoadState.Loaded;

    public bool HasSelectedAccount => SelectedAccount is not null;

    protected override void RefreshLocalization()
    {
        var priority = SelectedPriority?.Value ?? AccountInventoryPriority.Normal;
        var filter = SelectedFilter?.Value ?? AccountInventoryFilter.All;
        var sort = SelectedSort?.Value ?? AccountInventorySort.RecoveryOrder;
        var dependencyKind = SelectedDependencyKind?.Value ?? AccountDependencyKind.PasswordReset;
        base.RefreshLocalization();
        BuildStaticOptions();
        SelectedPriority = Priorities.Single(option => option.Value == priority);
        SelectedFilter = Filters.Single(option => option.Value == filter);
        SelectedSort = Sorts.Single(option => option.Value == sort);
        SelectedDependencyKind = DependencyKinds.Single(option => option.Value == dependencyKind);
        RefreshFromService();
    }

    private void BuildStaticOptions()
    {
        Priorities =
        [
            .. Enum.GetValues<AccountInventoryPriority>()
                .OrderByDescending(value => value)
                .Select(value => new AccountInventoryOption<AccountInventoryPriority>(
                    value,
                    Localization.GetString($"Accounts.Priority.{value}"))),
        ];
        Filters =
        [
            .. Enum.GetValues<AccountInventoryFilter>()
                .Select(value => new AccountInventoryOption<AccountInventoryFilter>(
                    value,
                    Localization.GetString($"Accounts.Filter.{value}"))),
        ];
        Sorts =
        [
            .. Enum.GetValues<AccountInventorySort>()
                .Select(value => new AccountInventoryOption<AccountInventorySort>(
                    value,
                    Localization.GetString($"Accounts.Sort.{value}"))),
        ];
        DependencyKinds =
        [
            .. Enum.GetValues<AccountDependencyKind>()
                .Select(value => new AccountInventoryOption<AccountDependencyKind>(
                    value,
                    Localization.GetString($"Accounts.Dependency.Kind.{value}"))),
        ];
        OnPropertyChanged(nameof(Priorities));
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(Sorts));
        OnPropertyChanged(nameof(DependencyKinds));
        SelectedPriority ??= Priorities.Single(option => option.Value == AccountInventoryPriority.Normal);
        SelectedFilter ??= Filters.Single(option => option.Value == AccountInventoryFilter.All);
        SelectedSort ??= Sorts.Single(option => option.Value == AccountInventorySort.RecoveryOrder);
        SelectedDependencyKind ??= DependencyKinds[0];
    }

    private void RefreshFromService()
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsCorrupted));
        OnPropertyChanged(nameof(CanMutate));
        var inventory = _inventory.CurrentInventory;
        InventorySummary = inventory is null
            ? Localization.GetString(_inventory.LoadState switch
            {
                AccountInventoryLoadState.Locked => "Accounts.State.Locked",
                AccountInventoryLoadState.Loading => "Accounts.State.Loading",
                AccountInventoryLoadState.Corrupted => "Accounts.State.Corrupted",
                _ => "Accounts.State.Empty",
            })
            : Localization.FormatPlural(
                "Accounts.Summary.Count",
                inventory.Accounts.Length,
                inventory.Accounts.Length);
        var issueCount = _inventory.CurrentPlan?.Issues.Length ?? 0;
        PlanSummary = Localization.FormatPlural("Accounts.Plan.Issues", issueCount, issueCount);
        SetLocalizedStatus(
            issueCount > 0 ? AppVisualState.Blocked : AppVisualState.Normal,
            issueCount > 0 ? "Accounts.Status.Blocked.Title" : "Screen.Accounts.StatusTitle",
            issueCount > 0 ? "Accounts.Status.Blocked.Message" : "Screen.Accounts.StatusMessage");
        RefreshAccountList();
        RefreshPlan();
        if (_editingAccountId is { } editingId)
        {
            SelectedAccount = Accounts.FirstOrDefault(account => account.Id == editingId);
        }

        RaiseCommandStates();
    }

    private void RefreshAccountList()
    {
        var inventory = _inventory.CurrentInventory;
        var plan = _inventory.CurrentPlan;
        Dictionary<Guid, AccountInventoryPlanItem> planByAccount =
            plan?.Items.ToDictionary(item => item.AccountId) ?? [];
        IEnumerable<AccountInventoryEntry> accounts = inventory?.Accounts ?? [];
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            accounts = accounts.Where(account =>
                Contains(account.ProviderId, search) ||
                Contains(account.AccountName, search) ||
                Contains(account.LoginIdentifier, search) ||
                Contains(account.AccountUrl, search));
        }

        accounts = (SelectedFilter?.Value ?? AccountInventoryFilter.All) switch
        {
            AccountInventoryFilter.Critical => accounts.Where(account =>
                account.Priority == AccountInventoryPriority.Critical),
            AccountInventoryFilter.RecoveryChannels => accounts.Where(account =>
                account.HasConfirmedRecoveryRole),
            AccountInventoryFilter.NeedsRoleConfirmation => accounts.Where(account =>
                account.Roles.Any(role => role.Decision == AccountRoleDecision.Suggested)),
            AccountInventoryFilter.Blocked => accounts.Where(account =>
                planByAccount.TryGetValue(account.Id, out var item) &&
                item.Status is AccountInventoryPlanStatus.BlockedCycle or
                    AccountInventoryPlanStatus.BlockedMissingDependency),
            _ => accounts,
        };
        accounts = (SelectedSort?.Value ?? AccountInventorySort.RecoveryOrder) switch
        {
            AccountInventorySort.RecoveryOrder => accounts
                .OrderBy(account => planByAccount.GetValueOrDefault(account.Id)?.Order ?? int.MaxValue),
            AccountInventorySort.Priority => accounts
                .OrderByDescending(account => account.Priority)
                .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase),
            AccountInventorySort.Provider => accounts
                .OrderBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase),
            AccountInventorySort.Updated => accounts.OrderByDescending(account => account.UpdatedAt),
            _ => accounts,
        };
        Accounts = [.. accounts.Select(account => CreateListItem(account, planByAccount))];
        OnPropertyChanged(nameof(HasSelectedAccount));
    }

    private AccountInventoryListItem CreateListItem(
        AccountInventoryEntry account,
        Dictionary<Guid, AccountInventoryPlanItem> planByAccount)
    {
        string[] roles =
        [
            .. account.Roles
                .Where(role => role.Decision == AccountRoleDecision.Confirmed)
                .Select(role => Localization.GetString($"Accounts.Role.{role.Role}")),
        ];
        var planText = planByAccount.TryGetValue(account.Id, out var planItem)
            ? Localization.GetString($"Accounts.Plan.Status.{planItem.Status}")
            : Localization.GetString("Accounts.Plan.Status.PlannedLater");
        return new AccountInventoryListItem(
            account.Id,
            account.AccountName ?? account.LoginIdentifier ?? account.ProviderId,
            account.ProviderId,
            Localization.GetString($"Accounts.Priority.{account.Priority}"),
            roles.Length == 0
                ? Localization.GetString("Accounts.Role.NoneConfirmed")
                : string.Join(", ", roles),
            planText,
            account);
    }

    private void RefreshPlan()
    {
        var inventory = _inventory.CurrentInventory;
        Dictionary<Guid, AccountInventoryEntry> byId =
            inventory?.Accounts.ToDictionary(account => account.Id) ?? [];
        PlanItems = _inventory.CurrentPlan is { } plan
            ?
            [
                .. plan.Items
                    .OrderBy(item => item.Order)
                    .Select(item => new AccountInventoryPlanDisplayItem(
                        item.Order,
                        item.AccountId,
                        Localization.Format(
                            "Accounts.Plan.Item",
                            item.Order,
                            byId.GetValueOrDefault(item.AccountId)?.ProviderId ?? item.ProviderId,
                            Localization.GetString($"Accounts.Plan.Status.{item.Status}"),
                            Localization.GetString($"Accounts.Plan.Reason.{item.ReasonCode}")),
                        item.Status)),
            ]
            : [];
    }

    private void LoadSelectedAccount()
    {
        if (SelectedAccount is null)
        {
            OnPropertyChanged(nameof(HasSelectedAccount));
            return;
        }

        var account = SelectedAccount.Account;
        _editingAccountId = account.Id;
        ProviderId = account.ProviderId;
        AccountName = account.AccountName ?? string.Empty;
        LoginIdentifier = account.LoginIdentifier ?? string.Empty;
        AccountUrl = account.AccountUrl ?? string.Empty;
        SelectedPriority = Priorities.Single(option => option.Value == account.Priority);
        RefreshAccountDetails(account);
        ValidationMessage = null;
        OnPropertyChanged(nameof(HasSelectedAccount));
    }

    private void RefreshAccountDetails(AccountInventoryEntry account)
    {
        SuggestedRoles =
        [
            .. account.Roles
                .Where(role => role.Decision == AccountRoleDecision.Suggested)
                .Select(role => RoleOption(role.Role)),
        ];
        ConfirmedRoles =
        [
            .. account.Roles
                .Where(role => role.Decision == AccountRoleDecision.Confirmed)
                .Select(role => RoleOption(role.Role)),
        ];
        AvailableRoles =
        [
            .. IndividualRoles
                .Where(role => account.Roles.All(existing =>
                    existing.Role != role || existing.Decision != AccountRoleDecision.Confirmed))
                .Select(RoleOption),
        ];
        Dictionary<Guid, AccountInventoryEntry> accountsById =
            _inventory.CurrentInventory?.Accounts.ToDictionary(candidate => candidate.Id) ?? [];
        Dependencies =
        [
            .. account.Dependencies.Select(dependency => new AccountInventoryDependencyItem(
                dependency.DependsOnAccountId,
                dependency.Kind,
                Localization.Format(
                    "Accounts.Dependency.Item",
                    accountsById.GetValueOrDefault(dependency.DependsOnAccountId)?.ProviderId ??
                        Localization.GetString("Accounts.Dependency.Missing"),
                    Localization.GetString($"Accounts.Dependency.Kind.{dependency.Kind}"),
                    dependency.IsOverride
                        ? Localization.GetString("Accounts.Dependency.OverrideMarker")
                        : string.Empty),
                dependency.IsOverride,
                dependency.OverrideReason)),
        ];
        DependencyTargets =
        [
            .. (_inventory.CurrentInventory?.Accounts ?? [])
                .Where(candidate => candidate.Id != account.Id)
                .OrderBy(candidate => candidate.ProviderId, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => new AccountInventoryOption<Guid>(
                    candidate.Id,
                    candidate.AccountName ?? candidate.LoginIdentifier ?? candidate.ProviderId)),
        ];
        SelectedSuggestedRole = SuggestedRoles.Count == 0 ? null : SuggestedRoles[0];
        SelectedConfirmedRole = ConfirmedRoles.Count == 0 ? null : ConfirmedRoles[0];
        SelectedRoleToAdd = AvailableRoles.Count == 0 ? null : AvailableRoles[0];
        SelectedDependencyTarget = DependencyTargets.Count == 0 ? null : DependencyTargets[0];
        SelectedDependency = Dependencies.Count == 0 ? null : Dependencies[0];
    }

    private void BeginNewAccount()
    {
        _editingAccountId = Guid.NewGuid();
        SelectedAccount = null;
        ProviderId = string.Empty;
        AccountName = string.Empty;
        LoginIdentifier = string.Empty;
        AccountUrl = string.Empty;
        OverrideReason = string.Empty;
        SelectedPriority = Priorities.Single(option => option.Value == AccountInventoryPriority.Normal);
        SuggestedRoles = [];
        ConfirmedRoles = [];
        AvailableRoles = [.. IndividualRoles.Select(RoleOption)];
        Dependencies = [];
        DependencyTargets =
        [
            .. (_inventory.CurrentInventory?.Accounts ?? [])
                .OrderBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
                .Select(account => new AccountInventoryOption<Guid>(
                    account.Id,
                    account.AccountName ?? account.LoginIdentifier ?? account.ProviderId)),
        ];
        SelectedRoleToAdd = AvailableRoles.Count == 0 ? null : AvailableRoles[0];
        SelectedDependencyTarget = DependencyTargets.Count == 0 ? null : DependencyTargets[0];
        ValidationMessage = null;
        OnPropertyChanged(nameof(HasSelectedAccount));
        RaiseCommandStates();
    }

    private async Task SaveAccountAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null || SelectedPriority is null)
        {
            return;
        }

        var result = await _inventory.UpsertAsync(
            new AccountInventoryUpsertRequest(
                _editingAccountId,
                ProviderId,
                AccountName,
                LoginIdentifier,
                AccountUrl,
                SelectedPriority.Value),
            cancellationToken);
        ApplyResult(result);
    }

    private async Task DeleteAccountAsync(CancellationToken cancellationToken)
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var dependentCount = _inventory.CurrentInventory?.Accounts.Count(account =>
            account.Dependencies.Any(dependency => dependency.DependsOnAccountId == SelectedAccount.Id)) ?? 0;
        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Accounts.Delete.Action"),
                SelectedAccount.DisplayName,
                Localization.Format("Accounts.Delete.Consequence", dependentCount),
                Localization.GetString("Accounts.Delete.Confirm"),
                Localization.GetString("Confirmation.Risk.Destructive"),
                isDestructive: true),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        var result = await _inventory.RemoveAccountAsync(
            SelectedAccount.Id,
            dependencyImpactAcknowledged: true,
            cancellationToken);
        if (result.Succeeded)
        {
            _editingAccountId = null;
            SelectedAccount = null;
        }

        ApplyResult(result);
    }

    private Task DecideSelectedRoleAsync(
        AccountRoleDecision decision,
        CancellationToken cancellationToken) =>
        DecideRoleAsync(SelectedSuggestedRole?.Value, decision, cancellationToken);

    private Task DecideAddedRoleAsync(
        AccountRoleDecision decision,
        CancellationToken cancellationToken) =>
        DecideRoleAsync(SelectedRoleToAdd?.Value, decision, cancellationToken);

    private Task DecideConfirmedRoleAsync(
        AccountRoleDecision decision,
        CancellationToken cancellationToken) =>
        DecideRoleAsync(SelectedConfirmedRole?.Value, decision, cancellationToken);

    private async Task DecideRoleAsync(
        AccountInventoryRole? role,
        AccountRoleDecision decision,
        CancellationToken cancellationToken)
    {
        if (_editingAccountId is null || role is null)
        {
            return;
        }

        var result = await _inventory.DecideRoleAsync(
            _editingAccountId.Value,
            role.Value,
            decision,
            cancellationToken);
        ApplyResult(result);
    }

    private async Task AddDependencyAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null || SelectedDependencyTarget is null || SelectedDependencyKind is null)
        {
            return;
        }

        var result = await _inventory.AddDependencyAsync(
            new AccountDependencyRequest(
                _editingAccountId.Value,
                SelectedDependencyTarget.Value,
                SelectedDependencyKind.Value,
                OverrideReason),
            cancellationToken);
        ApplyResult(result);
        if (result.Succeeded)
        {
            OverrideReason = string.Empty;
        }
    }

    private async Task RemoveDependencyAsync(CancellationToken cancellationToken)
    {
        if (_editingAccountId is null || SelectedDependency is null)
        {
            return;
        }

        var result = await _inventory.RemoveDependencyAsync(
            _editingAccountId.Value,
            SelectedDependency.DependsOnAccountId,
            SelectedDependency.Kind,
            cancellationToken);
        ApplyResult(result);
    }

    private void ApplyResult(AccountInventoryOperationResult result)
    {
        ValidationMessage = result.Succeeded
            ? Localization.GetString("Accounts.Operation.Saved")
            : Localization.GetString($"Accounts.Error.{result.FailureCode}");
        if (result.Succeeded)
        {
            RefreshFromService();
        }
    }

    private AccountInventoryOption<AccountInventoryRole> RoleOption(AccountInventoryRole role) =>
        new(role, Localization.GetString($"Accounts.Role.{role}"));

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private void Inventory_OnInventoryChanged(object? sender, EventArgs eventArgs) => RefreshFromService();

    private void RaiseCommandStates()
    {
        NewAccountCommand.RaiseCanExecuteChanged();
        SaveAccountCommand.RaiseCanExecuteChanged();
        DeleteAccountCommand.RaiseCanExecuteChanged();
        AcceptSuggestedRoleCommand.RaiseCanExecuteChanged();
        RejectSuggestedRoleCommand.RaiseCanExecuteChanged();
        AddRoleCommand.RaiseCanExecuteChanged();
        RemoveRoleCommand.RaiseCanExecuteChanged();
        AddDependencyCommand.RaiseCanExecuteChanged();
        RemoveDependencyCommand.RaiseCanExecuteChanged();
    }
}
