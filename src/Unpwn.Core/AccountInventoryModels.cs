namespace Unpwn.Core;

public enum AccountInventoryPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

[Flags]
public enum AccountInventoryRole
{
    None = 0,
    EmailMailbox = 1 << 0,
    PasswordManager = 1 << 1,
    IdentityProvider = 1 << 2,
    RecoveryEmail = 1 << 3,
    TelephoneRecovery = 1 << 4,
    OrganizationManagedSignIn = 1 << 5,
}

public enum AccountRoleDecision
{
    Suggested,
    Confirmed,
    Rejected,
}

public enum AccountDependencyKind
{
    PasswordReset,
    Mfa,
    IdentityProvider,
    RecoveryContact,
    PasswordManager,
    OrganizationManagedSignIn,
}

public enum AccountInventoryPlanStatus
{
    ReadyNow,
    PlannedLater,
    BlockedMissingDependency,
    BlockedCycle,
}

public enum AccountInventoryPlanReasonCode
{
    RecoveryChannelFirst,
    CriticalPriority,
    DependencyRoot,
    WaitingForDependency,
    MissingDependency,
    DependencyCycle,
    UserOverridePresent,
}

public enum AccountInventoryIssueKind
{
    MissingDependency,
    DependencyCycle,
    DependencyOverride,
}

public sealed record AccountRoleState(
    AccountInventoryRole Role,
    AccountRoleDecision Decision)
{
    public void Validate()
    {
        if (Role is AccountInventoryRole.None || !IsSingleRole(Role))
        {
            throw new InvalidOperationException("An account role state must contain exactly one role.");
        }
    }

    private static bool IsSingleRole(AccountInventoryRole role) =>
        ((int)role & ((int)role - 1)) == 0;
}

public sealed record AccountInventoryDependency(
    Guid DependsOnAccountId,
    AccountDependencyKind Kind,
    bool IsOverride,
    string? OverrideReason)
{
    public void Validate(Guid accountId)
    {
        if (DependsOnAccountId == Guid.Empty || DependsOnAccountId == accountId)
        {
            throw new InvalidOperationException("An account dependency must reference another account.");
        }

        if (IsOverride && string.IsNullOrWhiteSpace(OverrideReason))
        {
            throw new InvalidOperationException("An overridden dependency requires a reason.");
        }

        if (!IsOverride && OverrideReason is not null)
        {
            throw new InvalidOperationException("A normal dependency cannot contain an override reason.");
        }
    }
}

public sealed record AccountInventoryEntry(
    Guid Id,
    string ProviderId,
    string? AccountName,
    string? LoginIdentifier,
    string? AccountUrl,
    AccountInventoryPriority Priority,
    AccountRoleState[] Roles,
    AccountInventoryDependency[] Dependencies,
    DateTimeOffset UpdatedAt)
{
    public AccountCriticality DashboardCriticality => Priority switch
    {
        AccountInventoryPriority.Critical => AccountCriticality.Critical,
        AccountInventoryPriority.High => AccountCriticality.Important,
        AccountInventoryPriority.Normal or AccountInventoryPriority.Low => AccountCriticality.Routine,
        _ => throw new ArgumentOutOfRangeException(nameof(Priority)),
    };

    public bool HasConfirmedRole(AccountInventoryRole role) => Roles.Any(candidate =>
        candidate.Role == role && candidate.Decision == AccountRoleDecision.Confirmed);

    public bool HasConfirmedRecoveryRole =>
        HasConfirmedRole(AccountInventoryRole.EmailMailbox) ||
        HasConfirmedRole(AccountInventoryRole.PasswordManager) ||
        HasConfirmedRole(AccountInventoryRole.IdentityProvider) ||
        HasConfirmedRole(AccountInventoryRole.RecoveryEmail) ||
        HasConfirmedRole(AccountInventoryRole.TelephoneRecovery);

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("An inventory account requires a non-empty identifier.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentNullException.ThrowIfNull(Roles);
        ArgumentNullException.ThrowIfNull(Dependencies);
        if (ProviderId.Trim().Length > 160 || AccountName?.Trim().Length > 200 ||
            LoginIdentifier?.Trim().Length > 320 || AccountUrl?.Trim().Length > 2048)
        {
            throw new InvalidOperationException("An inventory account contains an overlong field.");
        }

        if (string.IsNullOrWhiteSpace(AccountName) && string.IsNullOrWhiteSpace(LoginIdentifier))
        {
            throw new InvalidOperationException("An inventory account requires a name or login identifier.");
        }

        if (!string.IsNullOrWhiteSpace(AccountUrl) &&
            (!Uri.TryCreate(AccountUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("https" or "http")))
        {
            throw new InvalidOperationException("An account URL must be an absolute HTTP or HTTPS URL.");
        }

        foreach (var role in Roles)
        {
            role.Validate();
        }

        if (Roles.Select(role => role.Role).Distinct().Count() != Roles.Length)
        {
            throw new InvalidOperationException("An account cannot contain duplicate role decisions.");
        }

        foreach (var dependency in Dependencies)
        {
            dependency.Validate(Id);
        }

        if (Dependencies
            .Select(dependency => (dependency.DependsOnAccountId, dependency.Kind))
            .Distinct()
            .Count() != Dependencies.Length)
        {
            throw new InvalidOperationException("An account cannot contain duplicate dependencies.");
        }
    }

    public AccountInventoryEntry NormalizeAndInfer(DateTimeOffset occurredAt)
    {
        var normalized = this with
        {
            ProviderId = ProviderId.Trim(),
            AccountName = Normalize(AccountName),
            LoginIdentifier = Normalize(LoginIdentifier),
            AccountUrl = Normalize(AccountUrl),
            Roles = AccountRoleInference.MergeSuggestions(this),
            UpdatedAt = occurredAt,
        };
        normalized.Validate();
        return normalized;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AccountInventoryIssue(
    AccountInventoryIssueKind Kind,
    Guid AccountId,
    Guid? RelatedAccountId,
    string StableCode);

public sealed record AccountInventoryPlanItem(
    Guid AccountId,
    string ProviderId,
    AccountInventoryPlanStatus Status,
    AccountInventoryPlanReasonCode ReasonCode,
    int Order,
    int DependencyDepth,
    Guid[] WaitingForAccountIds,
    bool HasDependencyOverride);

public sealed record AccountInventoryPlan(
    AccountInventoryPlanItem[] Items,
    AccountInventoryIssue[] Issues)
{
    public AccountInventoryPlanItem? Recommended => Items
        .Where(item => item.Status == AccountInventoryPlanStatus.ReadyNow)
        .OrderBy(item => item.Order)
        .FirstOrDefault();
}

public sealed record AccountInventoryState(
    Guid SessionId,
    long Revision,
    DateTimeOffset UpdatedAt,
    AccountInventoryEntry[] Accounts)
{
    public static AccountInventoryState Empty(Guid sessionId, DateTimeOffset occurredAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("An account inventory requires a recovery session.", nameof(sessionId));
        }

        return new AccountInventoryState(sessionId, 0, occurredAt, []);
    }

    public AccountInventoryState ReplaceAccounts(
        IEnumerable<AccountInventoryEntry> accounts,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (occurredAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt));
        }

        var materialized = accounts.Select(account => account.NormalizeAndInfer(occurredAt)).ToArray();
        if (materialized.Select(account => account.Id).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("Inventory account identifiers must be unique.");
        }

        return this with
        {
            Revision = Revision + 1,
            UpdatedAt = occurredAt,
            Accounts = materialized,
        };
    }

    public AccountInventoryPlan CreatePlan(IncidentIndicator incidentIndicators)
    {
        Validate();
        return AccountInventoryPlanner.Create(Accounts, incidentIndicators);
    }

    public void Validate()
    {
        if (SessionId == Guid.Empty || Revision < 0)
        {
            throw new InvalidOperationException("The persisted account inventory is invalid.");
        }

        ArgumentNullException.ThrowIfNull(Accounts);
        foreach (var account in Accounts)
        {
            account.Validate();
        }

        if (Accounts.Select(account => account.Id).Distinct().Count() != Accounts.Length)
        {
            throw new InvalidOperationException("The persisted account inventory contains duplicate accounts.");
        }
    }
}

public static class AccountRoleInference
{
    private static readonly (AccountInventoryRole Role, string[] Terms)[] Rules =
    [
        (AccountInventoryRole.PasswordManager,
            ["1password", "bitwarden", "dashlane", "keepass", "lastpass", "proton pass", "password manager"]),
        (AccountInventoryRole.EmailMailbox,
            ["gmail", "google mail", "outlook", "hotmail", "mail", "protonmail", "proton mail", "yahoo", "icloud"]),
        (AccountInventoryRole.IdentityProvider,
            ["google", "microsoft", "apple", "github", "okta", "onelogin", "auth0", "entra"]),
        (AccountInventoryRole.TelephoneRecovery,
            ["telephone", "phone", "mobile", "sms"]),
        (AccountInventoryRole.OrganizationManagedSignIn,
            ["okta", "onelogin", "entra", "workspace", "school", "university", "company sso", "corporate sso"]),
    ];

    public static AccountRoleState[] MergeSuggestions(AccountInventoryEntry account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var existing = account.Roles.ToDictionary(role => role.Role);
        var searchable = string.Join(
            ' ',
            new[] { account.ProviderId, account.AccountName, account.LoginIdentifier, account.AccountUrl }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        foreach (var (role, terms) in Rules)
        {
            if (!existing.ContainsKey(role) && terms.Any(searchable.Contains))
            {
                existing.Add(role, new AccountRoleState(role, AccountRoleDecision.Suggested));
            }
        }

        return existing.Values.OrderBy(role => role.Role).ToArray();
    }
}

public static class AccountInventoryPlanner
{
    public static AccountInventoryPlan Create(
        IReadOnlyCollection<AccountInventoryEntry> accounts,
        IncidentIndicator incidentIndicators)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var byId = accounts.ToDictionary(account => account.Id);
        var issues = new List<AccountInventoryIssue>();
        var effectiveDependencies = new Dictionary<Guid, List<Guid>>();

        foreach (var account in accounts)
        {
            var dependencies = new List<Guid>();
            foreach (var dependency in account.Dependencies)
            {
                if (!byId.ContainsKey(dependency.DependsOnAccountId))
                {
                    issues.Add(new AccountInventoryIssue(
                        AccountInventoryIssueKind.MissingDependency,
                        account.Id,
                        dependency.DependsOnAccountId,
                        "inventory.dependency.missing"));
                    continue;
                }

                if (dependency.IsOverride)
                {
                    issues.Add(new AccountInventoryIssue(
                        AccountInventoryIssueKind.DependencyOverride,
                        account.Id,
                        dependency.DependsOnAccountId,
                        "inventory.dependency.override"));
                    continue;
                }

                dependencies.Add(dependency.DependsOnAccountId);
            }

            effectiveDependencies[account.Id] = dependencies;
        }

        var cycleAccounts = FindCycleAccounts(accounts.Select(account => account.Id), effectiveDependencies);
        foreach (var accountId in cycleAccounts.Order())
        {
            issues.Add(new AccountInventoryIssue(
                AccountInventoryIssueKind.DependencyCycle,
                accountId,
                null,
                "inventory.dependency.cycle"));
        }

        var missingAccounts = issues
            .Where(issue => issue.Kind == AccountInventoryIssueKind.MissingDependency)
            .Select(issue => issue.AccountId)
            .ToHashSet();
        var ordered = TopologicalOrder(
            accounts.Where(account => !missingAccounts.Contains(account.Id) && !cycleAccounts.Contains(account.Id)),
            effectiveDependencies,
            incidentIndicators);
        var orderLookup = ordered
            .Select((account, index) => (account.Id, Order: index + 1))
            .ToDictionary(item => item.Id, item => item.Order);
        var depthLookup = CalculateDepths(ordered, effectiveDependencies);
        var items = new List<AccountInventoryPlanItem>();

        foreach (var account in ordered)
        {
            var waitingFor = effectiveDependencies[account.Id].ToArray();
            var hasOverride = account.Dependencies.Any(dependency => dependency.IsOverride);
            var reason = hasOverride
                ? AccountInventoryPlanReasonCode.UserOverridePresent
                : waitingFor.Length > 0
                    ? AccountInventoryPlanReasonCode.WaitingForDependency
                    : IsRecoveryChannelPriority(account, incidentIndicators)
                        ? AccountInventoryPlanReasonCode.RecoveryChannelFirst
                        : account.Priority == AccountInventoryPriority.Critical
                            ? AccountInventoryPlanReasonCode.CriticalPriority
                            : AccountInventoryPlanReasonCode.DependencyRoot;
            items.Add(new AccountInventoryPlanItem(
                account.Id,
                account.ProviderId,
                waitingFor.Length == 0
                    ? AccountInventoryPlanStatus.ReadyNow
                    : AccountInventoryPlanStatus.PlannedLater,
                reason,
                orderLookup[account.Id],
                depthLookup.GetValueOrDefault(account.Id),
                waitingFor,
                hasOverride));
        }

        foreach (var account in accounts
                     .Where(account => missingAccounts.Contains(account.Id) || cycleAccounts.Contains(account.Id))
                     .OrderByDescending(account => account.Priority)
                     .ThenBy(account => account.ProviderId, StringComparer.Ordinal)
                     .ThenBy(account => account.Id))
        {
            var isMissing = missingAccounts.Contains(account.Id);
            items.Add(new AccountInventoryPlanItem(
                account.Id,
                account.ProviderId,
                isMissing
                    ? AccountInventoryPlanStatus.BlockedMissingDependency
                    : AccountInventoryPlanStatus.BlockedCycle,
                isMissing
                    ? AccountInventoryPlanReasonCode.MissingDependency
                    : AccountInventoryPlanReasonCode.DependencyCycle,
                items.Count + 1,
                0,
                effectiveDependencies.GetValueOrDefault(account.Id)?.ToArray() ?? [],
                account.Dependencies.Any(dependency => dependency.IsOverride)));
        }

        return new AccountInventoryPlan(items.ToArray(), issues.ToArray());
    }

    private static List<AccountInventoryEntry> TopologicalOrder(
        IEnumerable<AccountInventoryEntry> accounts,
        IReadOnlyDictionary<Guid, List<Guid>> dependencies,
        IncidentIndicator incidentIndicators)
    {
        var remaining = accounts.ToDictionary(account => account.Id);
        var completed = new HashSet<Guid>();
        var result = new List<AccountInventoryEntry>();
        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(account => dependencies[account.Id].All(completed.Contains))
                .OrderByDescending(account => IsRecoveryChannelPriority(account, incidentIndicators))
                .ThenByDescending(account => account.Priority)
                .ThenBy(account => account.ProviderId, StringComparer.Ordinal)
                .ThenBy(account => account.Id)
                .FirstOrDefault();
            if (ready is null)
            {
                break;
            }

            result.Add(ready);
            completed.Add(ready.Id);
            remaining.Remove(ready.Id);
        }

        return result;
    }

    private static Dictionary<Guid, int> CalculateDepths(
        IReadOnlyList<AccountInventoryEntry> ordered,
        IReadOnlyDictionary<Guid, List<Guid>> dependencies)
    {
        var depths = new Dictionary<Guid, int>();
        foreach (var account in ordered)
        {
            depths[account.Id] = dependencies[account.Id].Count == 0
                ? 0
                : dependencies[account.Id]
                    .Where(depths.ContainsKey)
                    .Select(dependency => depths[dependency] + 1)
                    .DefaultIfEmpty(0)
                    .Max();
        }

        return depths;
    }

    private static HashSet<Guid> FindCycleAccounts(
        IEnumerable<Guid> accountIds,
        IReadOnlyDictionary<Guid, List<Guid>> dependencies)
    {
        var cycleAccounts = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var stack = new List<Guid>();

        foreach (var accountId in accountIds)
        {
            Visit(accountId, dependencies, visiting, visited, stack, cycleAccounts);
        }

        return cycleAccounts;
    }

    private static void Visit(
        Guid accountId,
        IReadOnlyDictionary<Guid, List<Guid>> dependencies,
        HashSet<Guid> visiting,
        HashSet<Guid> visited,
        List<Guid> stack,
        HashSet<Guid> cycleAccounts)
    {
        if (visited.Contains(accountId))
        {
            return;
        }

        if (visiting.Contains(accountId))
        {
            var start = stack.IndexOf(accountId);
            foreach (var cycleAccount in stack.Skip(start))
            {
                cycleAccounts.Add(cycleAccount);
            }

            return;
        }

        visiting.Add(accountId);
        stack.Add(accountId);
        foreach (var dependency in dependencies.GetValueOrDefault(accountId) ?? [])
        {
            Visit(dependency, dependencies, visiting, visited, stack, cycleAccounts);
        }

        stack.RemoveAt(stack.Count - 1);
        visiting.Remove(accountId);
        visited.Add(accountId);
    }

    private static bool IsRecoveryChannelPriority(
        AccountInventoryEntry account,
        IncidentIndicator incidentIndicators) =>
        account.HasConfirmedRecoveryRole &&
        (incidentIndicators.HasFlag(IncidentIndicator.CompromisedRecoveryChannel) ||
         incidentIndicators.HasFlag(IncidentIndicator.LostAccess) ||
         account.Priority >= AccountInventoryPriority.High);
}
