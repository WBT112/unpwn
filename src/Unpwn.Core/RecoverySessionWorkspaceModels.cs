namespace Unpwn.Core;

[Flags]
public enum IncidentIndicator
{
    None = 0,
    LostAccess = 1 << 0,
    UnexpectedPasswordChange = 1 << 1,
    UnexpectedMfaChange = 1 << 2,
    UnknownActiveSessions = 1 << 3,
    CompromisedRecoveryChannel = 1 << 4,
    PotentiallyUntrustedDevice = 1 << 5,
}

public enum RecoveryWorkspaceLifecycleStatus
{
    Active,
    Paused,
    Archived,
}

public enum RecoveryDashboardRecommendationCode
{
    ImportAccounts,
    SecureRecoveryChannel,
    RestoreCriticalAccess,
    ResolveCriticalBlocker,
    AddressUnresolvedRisk,
    ExportGeneratedCredentials,
    ReviewCriticalAccount,
    ReviewNextAccount,
    ResumeSession,
    ArchivedSession,
}

public enum RecoveryDashboardAlertKind
{
    BlockedAction,
    FailedAction,
    UnresolvedRisk,
    LostAccess,
    CredentialExport,
    CredentialDeletion,
}

public sealed record RecoveryIncidentIntake(
    IncidentIndicator Indicators,
    string? Description)
{
    public bool Has(IncidentIndicator indicator) => (Indicators & indicator) == indicator;

    public bool RequiresEmergencyAttention =>
        Has(IncidentIndicator.CompromisedRecoveryChannel) ||
        (Has(IncidentIndicator.LostAccess) && Has(IncidentIndicator.UnexpectedMfaChange));

    public static RecoveryIncidentIntake Empty { get; } = new(IncidentIndicator.None, null);
}

public sealed record RecoveryAccountDashboardEntry(
    Guid AccountId,
    string ProviderId,
    AccountCriticality Criticality,
    AccountRecoveryStatus RecoveryStatus,
    int RequiredActionsCompleted,
    int RequiredActionsTotal,
    int CompletedRequiredWeight,
    int TotalRequiredWeight,
    int BlockedRequiredActions,
    int FailedRequiredActions,
    int UnresolvedRisks,
    bool AccessLost,
    int CredentialsAwaitingExport,
    int CredentialsAwaitingDeletion,
    string? RecommendedActionId,
    int DependencyDepth,
    Guid[] WaitingForAccountIds)
{
    public int InventoryBlockedIssues { get; init; }

    public int InventoryUnresolvedRisks { get; init; }

    public bool IsFullyReviewed => RecoveryStatus == AccountRecoveryStatus.FullyReviewed;

    public bool IsCriticalReady =>
        Criticality == AccountCriticality.Critical &&
        IsFullyReviewed &&
        !AccessLost &&
        BlockedRequiredActions == 0 &&
        FailedRequiredActions == 0 &&
        UnresolvedRisks == 0;

    public void Validate()
    {
        if (AccountId == Guid.Empty)
        {
            throw new ArgumentException("A dashboard account requires a non-empty identifier.", nameof(AccountId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentNullException.ThrowIfNull(WaitingForAccountIds);
        ValidateNonNegative(RequiredActionsCompleted, nameof(RequiredActionsCompleted));
        ValidateNonNegative(RequiredActionsTotal, nameof(RequiredActionsTotal));
        ValidateNonNegative(CompletedRequiredWeight, nameof(CompletedRequiredWeight));
        ValidateNonNegative(TotalRequiredWeight, nameof(TotalRequiredWeight));
        ValidateNonNegative(BlockedRequiredActions, nameof(BlockedRequiredActions));
        ValidateNonNegative(FailedRequiredActions, nameof(FailedRequiredActions));
        ValidateNonNegative(UnresolvedRisks, nameof(UnresolvedRisks));
        ValidateNonNegative(CredentialsAwaitingExport, nameof(CredentialsAwaitingExport));
        ValidateNonNegative(CredentialsAwaitingDeletion, nameof(CredentialsAwaitingDeletion));
        ValidateNonNegative(DependencyDepth, nameof(DependencyDepth));
        ValidateNonNegative(InventoryBlockedIssues, nameof(InventoryBlockedIssues));
        ValidateNonNegative(InventoryUnresolvedRisks, nameof(InventoryUnresolvedRisks));

        if (RequiredActionsCompleted > RequiredActionsTotal)
        {
            throw new ArgumentException("Completed required actions cannot exceed the required total.");
        }

        if (CompletedRequiredWeight > TotalRequiredWeight)
        {
            throw new ArgumentException("Completed required weight cannot exceed the required total weight.");
        }

        if (InventoryBlockedIssues > BlockedRequiredActions ||
            InventoryUnresolvedRisks > UnresolvedRisks)
        {
            throw new ArgumentException(
                "Inventory issue counters cannot exceed the combined dashboard counters.");
        }
    }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Dashboard counters cannot be negative.");
        }
    }
}

public sealed record RecoveryDashboardAlert(
    RecoveryDashboardAlertKind Kind,
    Guid? AccountId,
    string? ProviderId,
    string? ActionId,
    int Count);

public sealed record RecoveryDashboardRecommendation(
    RecoveryDashboardRecommendationCode Code,
    Guid? AccountId,
    string? ProviderId,
    string? ActionId);

public sealed record RecoveryDashboardSnapshot(
    int CriticalAccountsReady,
    int CriticalAccountsTotal,
    int AccountsFullyReviewed,
    int AccountsTotal,
    double WeightedRequiredActionProgress,
    int BlockedRequiredActions,
    int FailedRequiredActions,
    int UnresolvedRisks,
    int AccountsWithLostAccess,
    int CredentialsAwaitingExport,
    int CredentialsAwaitingDeletion,
    IReadOnlyList<RecoveryDashboardAlert> Alerts,
    RecoveryDashboardRecommendation Recommendation)
{
    public bool HasCriticalAccounts => CriticalAccountsTotal > 0;

    public bool AreAllCriticalAccountsReady =>
        CriticalAccountsTotal > 0 && CriticalAccountsReady == CriticalAccountsTotal;
}

public sealed record RecoverySessionWorkspace(
    Guid Id,
    string Name,
    RecoveryIncidentIntake Incident,
    RecoveryWorkspaceLifecycleStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    RecoveryAccountDashboardEntry[] Accounts)
{
    public static RecoverySessionWorkspace Create(
        Guid id,
        string name,
        RecoveryIncidentIntake incident,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A recovery session requires a non-empty identifier.", nameof(id));
        }

        ValidateName(name);
        ArgumentNullException.ThrowIfNull(incident);
        IncidentDescriptionSafety.Validate(incident.Description);

        return new RecoverySessionWorkspace(
            id,
            name.Trim(),
            incident with { Description = NormalizeDescription(incident.Description) },
            RecoveryWorkspaceLifecycleStatus.Active,
            createdAt,
            createdAt,
            Revision: 0,
            Accounts: []);
    }

    public RecoverySessionWorkspace Pause(DateTimeOffset occurredAt) =>
        TransitionTo(RecoveryWorkspaceLifecycleStatus.Paused, occurredAt);

    public RecoverySessionWorkspace Resume(DateTimeOffset occurredAt) =>
        TransitionTo(RecoveryWorkspaceLifecycleStatus.Active, occurredAt);

    public RecoverySessionWorkspace Archive(DateTimeOffset occurredAt) =>
        TransitionTo(RecoveryWorkspaceLifecycleStatus.Archived, occurredAt);

    public RecoverySessionWorkspace ReplaceAccounts(
        IEnumerable<RecoveryAccountDashboardEntry> accounts,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ValidateTimestamp(occurredAt);
        var materialized = accounts.ToArray();
        foreach (var account in materialized)
        {
            account.Validate();
        }

        if (materialized.Select(account => account.AccountId).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Dashboard accounts must have unique identifiers.", nameof(accounts));
        }

        return this with
        {
            Accounts = materialized,
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
    }

    public RecoveryDashboardSnapshot CreateDashboardSnapshot()
    {
        foreach (var account in Accounts)
        {
            account.Validate();
        }

        var criticalAccounts = Accounts
            .Where(account => account.Criticality == AccountCriticality.Critical)
            .ToArray();
        var totalWeight = Accounts.Sum(account => account.TotalRequiredWeight);
        var completedWeight = Accounts.Sum(account => account.CompletedRequiredWeight);
        var alerts = BuildAlerts();

        return new RecoveryDashboardSnapshot(
            criticalAccounts.Count(account => account.IsCriticalReady),
            criticalAccounts.Length,
            Accounts.Count(account => account.IsFullyReviewed),
            Accounts.Length,
            totalWeight == 0 ? 0 : completedWeight / (double)totalWeight,
            Accounts.Sum(account => account.BlockedRequiredActions),
            Accounts.Sum(account => account.FailedRequiredActions),
            Accounts.Sum(account => account.UnresolvedRisks),
            Accounts.Count(account => account.AccessLost),
            Accounts.Sum(account => account.CredentialsAwaitingExport),
            Accounts.Sum(account => account.CredentialsAwaitingDeletion),
            alerts,
            BuildRecommendation());
    }

    public void Validate()
    {
        if (Id == Guid.Empty || Revision < 0)
        {
            throw new InvalidOperationException("The persisted recovery session is invalid.");
        }

        ValidateName(Name);
        ArgumentNullException.ThrowIfNull(Incident);
        ArgumentNullException.ThrowIfNull(Accounts);
        IncidentDescriptionSafety.Validate(Incident.Description);
        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("The recovery session update time predates its creation time.");
        }

        foreach (var account in Accounts)
        {
            account.Validate();
        }
    }

    private List<RecoveryDashboardAlert> BuildAlerts()
    {
        var alerts = new List<RecoveryDashboardAlert>();
        foreach (var account in Accounts)
        {
            AddAlert(alerts, RecoveryDashboardAlertKind.BlockedAction, account, account.BlockedRequiredActions);
            AddAlert(alerts, RecoveryDashboardAlertKind.FailedAction, account, account.FailedRequiredActions);
            AddAlert(alerts, RecoveryDashboardAlertKind.UnresolvedRisk, account, account.UnresolvedRisks);
            AddAlert(alerts, RecoveryDashboardAlertKind.LostAccess, account, account.AccessLost ? 1 : 0);
            AddAlert(alerts, RecoveryDashboardAlertKind.CredentialExport, account, account.CredentialsAwaitingExport);
            AddAlert(alerts, RecoveryDashboardAlertKind.CredentialDeletion, account, account.CredentialsAwaitingDeletion);
        }

        return alerts;
    }

    private RecoveryDashboardRecommendation BuildRecommendation()
    {
        if (Status == RecoveryWorkspaceLifecycleStatus.Archived)
        {
            return new RecoveryDashboardRecommendation(
                RecoveryDashboardRecommendationCode.ArchivedSession,
                null,
                null,
                null);
        }

        if (Status == RecoveryWorkspaceLifecycleStatus.Paused)
        {
            return new RecoveryDashboardRecommendation(
                RecoveryDashboardRecommendationCode.ResumeSession,
                null,
                null,
                null);
        }

        if (Incident.RequiresEmergencyAttention)
        {
            return new RecoveryDashboardRecommendation(
                RecoveryDashboardRecommendationCode.SecureRecoveryChannel,
                null,
                null,
                null);
        }

        if (Accounts.Length == 0)
        {
            return new RecoveryDashboardRecommendation(
                RecoveryDashboardRecommendationCode.ImportAccounts,
                null,
                null,
                null);
        }

        var candidates = Accounts
            .Where(account => !account.IsFullyReviewed || account.AccessLost || account.UnresolvedRisks > 0)
            .OrderBy(account => account.WaitingForAccountIds.Length > 0)
            .ThenByDescending(account => account.Criticality)
            .ThenByDescending(account => account.AccessLost)
            .ThenByDescending(account => account.BlockedRequiredActions + account.FailedRequiredActions)
            .ThenByDescending(account => account.UnresolvedRisks)
            .ThenBy(account => account.DependencyDepth)
            .ThenBy(account => account.ProviderId, StringComparer.Ordinal)
            .ToArray();
        var next = candidates.FirstOrDefault();
        if (next is null)
        {
            var exportAccount = Accounts.FirstOrDefault(account =>
                account.CredentialsAwaitingExport > 0 || account.CredentialsAwaitingDeletion > 0);
            return exportAccount is null
                ? new RecoveryDashboardRecommendation(
                    RecoveryDashboardRecommendationCode.ReviewNextAccount,
                    null,
                    null,
                    null)
                : CreateRecommendation(
                    RecoveryDashboardRecommendationCode.ExportGeneratedCredentials,
                    exportAccount);
        }

        var code = next.AccessLost && next.Criticality == AccountCriticality.Critical
            ? RecoveryDashboardRecommendationCode.RestoreCriticalAccess
            : next.Criticality == AccountCriticality.Critical &&
              next.BlockedRequiredActions + next.FailedRequiredActions > 0
                ? RecoveryDashboardRecommendationCode.ResolveCriticalBlocker
                : next.UnresolvedRisks > 0
                    ? RecoveryDashboardRecommendationCode.AddressUnresolvedRisk
                    : next.Criticality == AccountCriticality.Critical
                        ? RecoveryDashboardRecommendationCode.ReviewCriticalAccount
                        : RecoveryDashboardRecommendationCode.ReviewNextAccount;
        return CreateRecommendation(code, next);
    }

    private RecoverySessionWorkspace TransitionTo(
        RecoveryWorkspaceLifecycleStatus next,
        DateTimeOffset occurredAt)
    {
        ValidateTimestamp(occurredAt);
        var allowed = (Status, next) switch
        {
            (RecoveryWorkspaceLifecycleStatus.Active, RecoveryWorkspaceLifecycleStatus.Paused) => true,
            (RecoveryWorkspaceLifecycleStatus.Paused, RecoveryWorkspaceLifecycleStatus.Active) => true,
            (RecoveryWorkspaceLifecycleStatus.Active, RecoveryWorkspaceLifecycleStatus.Archived) => true,
            (RecoveryWorkspaceLifecycleStatus.Paused, RecoveryWorkspaceLifecycleStatus.Archived) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot transition a recovery session from {Status} to {next}.");
        }

        return this with
        {
            Status = next,
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
    }

    private void ValidateTimestamp(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "Recovery session transitions cannot move backwards in time.");
        }
    }

    private static RecoveryDashboardRecommendation CreateRecommendation(
        RecoveryDashboardRecommendationCode code,
        RecoveryAccountDashboardEntry account) =>
        new(code, account.AccountId, account.ProviderId, account.RecommendedActionId);

    private static void AddAlert(
        List<RecoveryDashboardAlert> alerts,
        RecoveryDashboardAlertKind kind,
        RecoveryAccountDashboardEntry account,
        int count)
    {
        if (count > 0)
        {
            alerts.Add(new RecoveryDashboardAlert(
                kind,
                account.AccountId,
                account.ProviderId,
                account.RecommendedActionId,
                count));
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Recovery session names are limited to 120 characters.");
        }
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

public static class IncidentDescriptionSafety
{
    private static readonly string[] ForbiddenFragments =
    [
        "password:",
        "passwort:",
        "passwd:",
        "pwd:",
        "token:",
        "cookie:",
        "recovery code:",
        "recovery-code:",
        "wiederherstellungscode:",
        "mfa secret:",
        "2fa secret:",
        "reset link:",
        "reset-link:",
        "http://",
        "https://",
    ];

    public static void Validate(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        if (description.Length > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description),
                "Incident descriptions are limited to 500 characters.");
        }

        var normalized = description.ToLowerInvariant();
        if (ForbiddenFragments.Any(normalized.Contains) || ContainsLongSecretLikeToken(description))
        {
            throw new ArgumentException(
                "Incident descriptions must not contain credentials, tokens, recovery codes, cookies, or links.",
                nameof(description));
        }
    }

    private static bool ContainsLongSecretLikeToken(string value)
    {
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim('"', '\'', ',', '.', ';', '(', ')', '[', ']', '{', '}');
            if (trimmed.Length >= 32 && trimmed.All(IsSecretLikeCharacter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSecretLikeCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '-' or '_' or '+' or '/' or '=';
}
