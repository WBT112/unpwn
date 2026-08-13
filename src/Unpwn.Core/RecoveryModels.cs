namespace Unpwn.Core;

public enum AccountCriticality
{
    Routine = 0,
    Important = 1,
    Critical = 2,
}

public enum AccountRecoveryStatus
{
    Open,
    InProgress,
    FullyReviewed,
    NotFullySecured,
    AccessNotRestored,
}

public enum RecoveryActionStatus
{
    Open,
    InProgress,
    Blocked,
    NeedsUserAction,
    Completed,
    Failed,
    NotApplicable,
}

public enum NotApplicableDisposition
{
    TrulyNotApplicable,
    UnresolvedRisk,
}

public enum AuditEventType
{
    AccountImported,
    AccountCategoryChanged,
    RecoveryActionStarted,
    RecoveryActionCompleted,
    RecoveryActionBlocked,
    RecoveryActionFailed,
    UnresolvedRiskAccepted,
    CredentialGenerated,
    CredentialExported,
    CredentialDeleted,
    VaultLocked,
    SessionCompleted,
}

public sealed record AuditEvent(
    DateTimeOffset OccurredAt,
    AuditEventType EventType,
    Guid? AccountId,
    RecoveryActionType? ActionType)
{
    public static AuditEvent Create(
        AuditEventType eventType,
        Guid? accountId = null,
        RecoveryActionType? actionType = null,
        DateTimeOffset? occurredAt = null) =>
        new(occurredAt ?? DateTimeOffset.UtcNow, eventType, accountId, actionType);
}

public sealed class RecoveryActionInstance
{
    private RecoveryActionInstance(RecoveryActionDefinition definition)
    {
        Definition = definition;
        Status = RecoveryActionStatus.Open;
    }

    public RecoveryActionDefinition Definition { get; }

    public RecoveryActionStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public bool HasUnresolvedRisk { get; private set; }

    public NotApplicableDisposition? NotApplicableDisposition { get; private set; }

    public bool IsExcludedFromRequiredProgress =>
        Status == RecoveryActionStatus.NotApplicable &&
        NotApplicableDisposition == global::Unpwn.Core.NotApplicableDisposition.TrulyNotApplicable;

    public static RecoveryActionInstance Create(RecoveryActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.IsRequired &&
            (definition.CompletionCriteria.Count == 0 || definition.CompletionCriteria.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("Required recovery actions must define non-empty completion criteria.", nameof(definition));
        }

        return new RecoveryActionInstance(definition);
    }

    public void Start()
    {
        TransitionTo(RecoveryActionStatus.InProgress);
        HasUnresolvedRisk = false;
        NotApplicableDisposition = null;
    }

    public void RequireUserAction(string reason) => TransitionTo(RecoveryActionStatus.NeedsUserAction, reason);

    public void Block(string reason) => TransitionTo(RecoveryActionStatus.Blocked, reason);

    public void Complete() => TransitionTo(RecoveryActionStatus.Completed);

    public void Fail(string reason) => TransitionTo(RecoveryActionStatus.Failed, reason);

    public void MarkNotApplicable(string reason, NotApplicableDisposition disposition)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A not-applicable recovery action requires a recorded reason.", nameof(reason));
        }

        if (disposition == global::Unpwn.Core.NotApplicableDisposition.UnresolvedRisk && !Definition.IsRequired)
        {
            throw new InvalidOperationException("Only required actions can create unresolved risks.");
        }

        TransitionTo(RecoveryActionStatus.NotApplicable, reason);
        NotApplicableDisposition = disposition;
        HasUnresolvedRisk = disposition == global::Unpwn.Core.NotApplicableDisposition.UnresolvedRisk;
    }

    public void AcceptUnresolvedRisk(string reason)
    {
        if (!Definition.IsRequired)
        {
            throw new InvalidOperationException("Only required actions can create unresolved risks.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unresolved risk requires a recorded reason.", nameof(reason));
        }

        TransitionTo(RecoveryActionStatus.Failed, reason);
        HasUnresolvedRisk = true;
        NotApplicableDisposition = null;
    }

    private void TransitionTo(RecoveryActionStatus next, string? reason = null)
    {
        if (RequiresReason(next) && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException($"The {next} state requires a recorded reason.", nameof(reason));
        }

        if (!IsAllowedTransition(Status, next))
        {
            throw new InvalidOperationException($"Cannot transition a recovery action from {Status} to {next}.");
        }

        Status = next;
        StatusReason = reason;
    }

    private static bool RequiresReason(RecoveryActionStatus status) =>
        status is RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NotApplicable or RecoveryActionStatus.NeedsUserAction;

    private static bool IsAllowedTransition(RecoveryActionStatus current, RecoveryActionStatus next) =>
        current switch
        {
            RecoveryActionStatus.Open => next is RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction or RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.InProgress => next is RecoveryActionStatus.Completed or RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction or RecoveryActionStatus.Failed or RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.Blocked => next is RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction or RecoveryActionStatus.Failed or RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.NeedsUserAction => next is RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.Failed => next is RecoveryActionStatus.InProgress or RecoveryActionStatus.Blocked or RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable => false,
            _ => false,
        };
}

public sealed class Account(Guid id, string providerId, AccountCriticality criticality, IEnumerable<RecoveryActionInstance> actions)
{
    private readonly List<RecoveryActionInstance> _actions = [.. actions];

    public Guid Id { get; } = id;

    public string ProviderId { get; } = providerId;

    public AccountCriticality Criticality { get; } = criticality;

    public IReadOnlyList<RecoveryActionInstance> Actions => _actions;

    public RecoveryActionInstance GetAction(string actionId) =>
        _actions.Single(action => string.Equals(action.Definition.Id, actionId, StringComparison.Ordinal));

    public void StartAction(string actionId)
    {
        var action = GetAction(actionId);
        var incompletePrerequisites = action.Definition.Prerequisites
            .Select(GetAction)
            .Where(prerequisite => !IsPrerequisiteSatisfied(prerequisite))
            .Select(prerequisite => prerequisite.Definition.Id)
            .ToArray();

        if (incompletePrerequisites.Length > 0)
        {
            action.Block($"Waiting for prerequisite actions: {string.Join(", ", incompletePrerequisites)}.");
            return;
        }

        action.Start();
    }

    public AccountRecoveryStatus Status
    {
        get
        {
            var required = _actions.Where(action => action.Definition.IsRequired).ToArray();
            if (required.Any(action => action.HasUnresolvedRisk || action.Status is RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed))
            {
                return AccountRecoveryStatus.NotFullySecured;
            }

            var applicableRequired = required.Where(action => !action.IsExcludedFromRequiredProgress).ToArray();
            if (applicableRequired.Any(action => action.Status is RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction))
            {
                return applicableRequired.Any(action => action.Status is RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction)
                    ? AccountRecoveryStatus.InProgress
                    : AccountRecoveryStatus.Open;
            }

            return AccountRecoveryStatus.FullyReviewed;
        }
    }

    private static bool IsPrerequisiteSatisfied(RecoveryActionInstance action) =>
        action.Status == RecoveryActionStatus.Completed || action.IsExcludedFromRequiredProgress;
}

public enum CriticalAccountReadinessStatus
{
    Ready,
    NotReady,
}

public sealed record CriticalAccountReadiness(
    Guid AccountId,
    string ProviderId,
    CriticalAccountReadinessStatus Status,
    int RequiredActionsCompleted,
    int RequiredActionsTotal,
    int BlockedRequiredActions,
    int FailedRequiredActions,
    int UnresolvedRisks)
{
    public bool IsReady => Status == CriticalAccountReadinessStatus.Ready;
}

public sealed record RecoveryProgress(
    int CriticalAccountsSecured,
    int CriticalAccountsTotal,
    int AccountsFullyReviewed,
    int AccountsTotal,
    double WeightedRequiredActionsCompleted,
    int BlockedRequiredActions,
    int FailedRequiredActions,
    int UnresolvedRisks)
{
    public double CriticalAccountReadinessRatio => CriticalAccountsTotal == 0 ? 1 : CriticalAccountsSecured / (double)CriticalAccountsTotal;

    public double AccountReviewRatio => AccountsTotal == 0 ? 1 : AccountsFullyReviewed / (double)AccountsTotal;
}

public sealed class RecoverySession(Guid id, DateTimeOffset createdAt)
{
    private readonly List<Account> _accounts = [];
    private readonly List<AuditEvent> _auditEvents = [];

    public Guid Id { get; } = id;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public IReadOnlyList<Account> Accounts => _accounts;

    public IReadOnlyList<AuditEvent> AuditEvents => _auditEvents;

    public void AddAccount(Account account) => _accounts.Add(account);

    public void RecordAuditEvent(AuditEvent auditEvent) => _auditEvents.Add(auditEvent);

    public IReadOnlyList<CriticalAccountReadiness> CalculateCriticalAccountReadiness() =>
        [.. _accounts
            .Where(account => account.Criticality == AccountCriticality.Critical)
            .Select(CreateCriticalAccountReadiness)];

    public RecoveryProgress CalculateProgress()
    {
        var requiredActions = _accounts.SelectMany(account => account.Actions).Where(action => action.Definition.IsRequired).ToArray();
        var applicableRequiredActions = requiredActions.Where(action => !action.IsExcludedFromRequiredProgress).ToArray();
        var completedWeight = applicableRequiredActions
            .Where(action => action.Status == RecoveryActionStatus.Completed && !action.HasUnresolvedRisk)
            .Sum(action => (int)action.Definition.Importance);
        var totalWeight = applicableRequiredActions.Sum(action => (int)action.Definition.Importance);
        var criticalAccountReadiness = CalculateCriticalAccountReadiness();

        return new RecoveryProgress(
            criticalAccountReadiness.Count(readiness => readiness.IsReady),
            criticalAccountReadiness.Count,
            _accounts.Count(account => account.Status == AccountRecoveryStatus.FullyReviewed),
            _accounts.Count,
            totalWeight == 0 ? 1 : completedWeight / (double)totalWeight,
            applicableRequiredActions.Count(action => action.Status == RecoveryActionStatus.Blocked),
            applicableRequiredActions.Count(action => action.Status == RecoveryActionStatus.Failed),
            requiredActions.Count(action => action.HasUnresolvedRisk));
    }

    private static CriticalAccountReadiness CreateCriticalAccountReadiness(Account account)
    {
        var requiredActions = account.Actions.Where(action => action.Definition.IsRequired).ToArray();
        var applicableRequiredActions = requiredActions.Where(action => !action.IsExcludedFromRequiredProgress).ToArray();
        var blockedRequiredActions = applicableRequiredActions.Count(action => action.Status == RecoveryActionStatus.Blocked);
        var failedRequiredActions = applicableRequiredActions.Count(action => action.Status == RecoveryActionStatus.Failed);
        var unresolvedRisks = requiredActions.Count(action => action.HasUnresolvedRisk);
        var requiredActionsCompleted = applicableRequiredActions.Count(action => action.Status == RecoveryActionStatus.Completed);
        var isReady = applicableRequiredActions.Length == requiredActionsCompleted
            && blockedRequiredActions == 0
            && failedRequiredActions == 0
            && unresolvedRisks == 0
            && account.Status == AccountRecoveryStatus.FullyReviewed;

        return new CriticalAccountReadiness(
            account.Id,
            account.ProviderId,
            isReady ? CriticalAccountReadinessStatus.Ready : CriticalAccountReadinessStatus.NotReady,
            requiredActionsCompleted,
            applicableRequiredActions.Length,
            blockedRequiredActions,
            failedRequiredActions,
            unresolvedRisks);
    }
}
