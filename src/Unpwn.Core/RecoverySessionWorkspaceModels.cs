namespace Unpwn.Core;

[Flags]
public enum IncidentIndicator
{
    None = 0,
    LostAccess = 1 << 0,
    CompromisedRecoveryChannel = 1 << 1,
}

public enum RecoveryWorkspaceLifecycleStatus
{
    Active,
    Paused,
    Archived,
    Completed,
    FollowUpRequired,
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

public sealed record RecoveryIncidentIntake(IncidentIndicator Indicators)
{
    private const IncidentIndicator SupportedIndicators =
        IncidentIndicator.LostAccess | IncidentIndicator.CompromisedRecoveryChannel;

    public bool Has(IncidentIndicator indicator) => (Indicators & indicator) == indicator;

    public bool RequiresEmergencyAttention =>
        Has(IncidentIndicator.CompromisedRecoveryChannel);

    public void Validate()
    {
        if ((Indicators & ~SupportedIndicators) != IncidentIndicator.None)
        {
            throw new InvalidOperationException("The incident intake contains unsupported indicators.");
        }
    }

    public static RecoveryIncidentIntake Empty { get; } = new(IncidentIndicator.None);
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
    string? RecommendedActionId)
{
    public int RequiredActionsOpen { get; init; }

    public int RequiredActionsInProgress { get; init; }

    public int RequiredActionsAwaitingUser { get; init; }

    public int RequiredActionsNotApplicable { get; init; }

    public int AcceptedRiskActions { get; init; }

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
        ValidateNonNegative(RequiredActionsCompleted, nameof(RequiredActionsCompleted));
        ValidateNonNegative(RequiredActionsTotal, nameof(RequiredActionsTotal));
        ValidateNonNegative(CompletedRequiredWeight, nameof(CompletedRequiredWeight));
        ValidateNonNegative(TotalRequiredWeight, nameof(TotalRequiredWeight));
        ValidateNonNegative(BlockedRequiredActions, nameof(BlockedRequiredActions));
        ValidateNonNegative(FailedRequiredActions, nameof(FailedRequiredActions));
        ValidateNonNegative(UnresolvedRisks, nameof(UnresolvedRisks));
        ValidateNonNegative(CredentialsAwaitingExport, nameof(CredentialsAwaitingExport));
        ValidateNonNegative(CredentialsAwaitingDeletion, nameof(CredentialsAwaitingDeletion));
        ValidateNonNegative(RequiredActionsOpen, nameof(RequiredActionsOpen));
        ValidateNonNegative(RequiredActionsInProgress, nameof(RequiredActionsInProgress));
        ValidateNonNegative(RequiredActionsAwaitingUser, nameof(RequiredActionsAwaitingUser));
        ValidateNonNegative(RequiredActionsNotApplicable, nameof(RequiredActionsNotApplicable));
        ValidateNonNegative(AcceptedRiskActions, nameof(AcceptedRiskActions));

        if (RequiredActionsCompleted > RequiredActionsTotal)
        {
            throw new ArgumentException("Completed required actions cannot exceed the required total.");
        }

        if (CompletedRequiredWeight > TotalRequiredWeight)
        {
            throw new ArgumentException("Completed required weight cannot exceed the required total weight.");
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
    public RecoveryCompletionRecord? Completion { get; init; }

    public bool IsReadOnly => Status is
        RecoveryWorkspaceLifecycleStatus.Archived or
        RecoveryWorkspaceLifecycleStatus.Completed or
        RecoveryWorkspaceLifecycleStatus.FollowUpRequired;

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
        incident.Validate();

        return new RecoverySessionWorkspace(
            id,
            name.Trim(),
            incident,
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

    public RecoverySessionWorkspace Complete(
        RecoveryCompletionRecord completion,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(completion);
        completion.Validate();
        ValidateTimestamp(occurredAt);
        if (Status != RecoveryWorkspaceLifecycleStatus.Active || Completion is not null ||
            completion.Report.SessionId != Id)
        {
            throw new InvalidOperationException("Only an active recovery session can be completed once.");
        }

        var status = completion.Outcome switch
        {
            RecoveryCompletionOutcome.Completed => RecoveryWorkspaceLifecycleStatus.Completed,
            RecoveryCompletionOutcome.Archived => RecoveryWorkspaceLifecycleStatus.Archived,
            RecoveryCompletionOutcome.FollowUpRequired => RecoveryWorkspaceLifecycleStatus.FollowUpRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(completion)),
        };
        return this with
        {
            Status = status,
            Completion = completion,
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
    }

    public RecoverySessionWorkspace ReplaceAccounts(
        IEnumerable<RecoveryAccountDashboardEntry> accounts,
        DateTimeOffset occurredAt)
    {
        EnsureMutable();
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
        Incident.Validate();
        if (UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("The recovery session update time predates its creation time.");
        }

        Completion?.Validate();
        if (Completion is not null && !IsReadOnly)
        {
            throw new InvalidOperationException("A completed recovery session must be read-only.");
        }

        if (Completion is not null && Status != (Completion.Outcome switch
        {
            RecoveryCompletionOutcome.Completed => RecoveryWorkspaceLifecycleStatus.Completed,
            RecoveryCompletionOutcome.Archived => RecoveryWorkspaceLifecycleStatus.Archived,
            RecoveryCompletionOutcome.FollowUpRequired => RecoveryWorkspaceLifecycleStatus.FollowUpRequired,
            _ => throw new InvalidOperationException("The completion outcome is invalid."),
        }))
        {
            throw new InvalidOperationException("The recovery-session lifecycle does not match its completion outcome.");
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
        if (IsReadOnly)
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
            .OrderByDescending(account => account.Criticality)
            .ThenByDescending(account => account.AccessLost)
            .ThenByDescending(account => account.BlockedRequiredActions + account.FailedRequiredActions)
            .ThenByDescending(account => account.UnresolvedRisks)
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

    private void EnsureMutable()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("A terminal recovery session is read-only.");
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

}
