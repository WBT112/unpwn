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

public enum RecoveryActionType
{
    ConfirmAccess,
    ChangePassword,
    ResetPassword,
    InvalidateSessions,
    ReviewTrustedDevices,
    ReviewMfa,
    ReviewRecoveryOptions,
    RevokeApplicationAccess,
    ReviewApiTokens,
    RecordUnresolvedRisk,
}

public enum RecoveryPath
{
    AuthenticatedChange,
    PasswordReset,
    ManualRecovery,
}

public enum RecoveryActionImportance
{
    Routine = 1,
    Important = 2,
    Critical = 3,
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

public enum AutomationSupport
{
    None,
    Navigation,
    Assisted,
    Automated,
}

public sealed record RecoveryActionDefinition(
    string Id,
    RecoveryActionType Type,
    RecoveryPath Path,
    RecoveryActionImportance Importance,
    bool IsRequired,
    AutomationSupport AutomationSupport,
    string CompletionCriteria,
    IReadOnlyCollection<string>? PrerequisiteActionIds = null)
{
    public IReadOnlyCollection<string> PrerequisiteActionIds { get; } = PrerequisiteActionIds ?? [];
}

public sealed record RecoveryWorkflowDefinition(
    string Id,
    string ProviderId,
    string Version,
    DateOnly VerifiedAt,
    IReadOnlyCollection<RecoveryActionDefinition> Actions);

public sealed record AccountDependency(Guid AccountId, Guid DependsOnAccountId, string Reason);

public sealed record AuditEvent(DateTimeOffset OccurredAt, string EventType, string Message)
{
    public static AuditEvent Create(string eventType, string message, DateTimeOffset? occurredAt = null)
    {
        if (ContainsSyntheticSecret(message))
        {
            throw new ArgumentException("Audit event messages must not contain secret values.", nameof(message));
        }

        return new AuditEvent(occurredAt ?? DateTimeOffset.UtcNow, eventType, message);
    }

    private static bool ContainsSyntheticSecret(string value) =>
        value.Contains("UNPWN_TEST_SECRET_", StringComparison.Ordinal);
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

    public static RecoveryActionInstance Create(RecoveryActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.IsRequired && string.IsNullOrWhiteSpace(definition.CompletionCriteria))
        {
            throw new ArgumentException("Required recovery actions must define completion criteria.", nameof(definition));
        }

        return new RecoveryActionInstance(definition);
    }

    public void Start() => TransitionTo(RecoveryActionStatus.InProgress);

    public void RequireUserAction(string reason) => TransitionTo(RecoveryActionStatus.NeedsUserAction, reason);

    public void Block(string reason) => TransitionTo(RecoveryActionStatus.Blocked, reason);

    public void Complete() => TransitionTo(RecoveryActionStatus.Completed);

    public void Fail(string reason) => TransitionTo(RecoveryActionStatus.Failed, reason);

    public void MarkNotApplicable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A not-applicable recovery action requires a recorded reason.", nameof(reason));
        }

        TransitionTo(RecoveryActionStatus.NotApplicable, reason);
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

        HasUnresolvedRisk = true;
        TransitionTo(RecoveryActionStatus.Failed, reason);
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
        var incompletePrerequisites = action.Definition.PrerequisiteActionIds
            .Select(GetAction)
            .Where(prerequisite => prerequisite.Status != RecoveryActionStatus.Completed)
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

            if (required.Any(action => action.Status is RecoveryActionStatus.Open or RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction))
            {
                return _actions.Any(action => action.Status is RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction)
                    ? AccountRecoveryStatus.InProgress
                    : AccountRecoveryStatus.Open;
            }

            return AccountRecoveryStatus.FullyReviewed;
        }
    }
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
    int UnresolvedRisks)
{
    public double CriticalAccountReadinessRatio => CriticalAccountsTotal == 0 ? 1 : CriticalAccountsSecured / (double)CriticalAccountsTotal;

    public double AccountReviewRatio => AccountsTotal == 0 ? 1 : AccountsFullyReviewed / (double)AccountsTotal;
}

public sealed class RecoverySession(Guid id, DateTimeOffset createdAt)
{
    private readonly List<Account> _accounts = [];
    private readonly List<AccountDependency> _dependencies = [];
    private readonly List<AuditEvent> _auditEvents = [];

    public Guid Id { get; } = id;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public IReadOnlyList<Account> Accounts => _accounts;

    public IReadOnlyList<AccountDependency> Dependencies => _dependencies;

    public IReadOnlyList<AuditEvent> AuditEvents => _auditEvents;

    public void AddAccount(Account account) => _accounts.Add(account);

    public void AddDependency(AccountDependency dependency) => _dependencies.Add(dependency);

    public void RecordAuditEvent(AuditEvent auditEvent) => _auditEvents.Add(auditEvent);

    public IReadOnlyList<CriticalAccountReadiness> CalculateCriticalAccountReadiness() =>
        _accounts
            .Where(account => account.Criticality == AccountCriticality.Critical)
            .Select(CreateCriticalAccountReadiness)
            .ToArray();

    public RecoveryProgress CalculateProgress()
    {
        var requiredActions = _accounts.SelectMany(account => account.Actions).Where(action => action.Definition.IsRequired).ToArray();
        var completedWeight = requiredActions
            .Where(action => IsCompletedForProgress(action) && !action.HasUnresolvedRisk)
            .Sum(action => (int)action.Definition.Importance);
        var totalWeight = requiredActions.Sum(action => (int)action.Definition.Importance);
        var criticalAccountReadiness = CalculateCriticalAccountReadiness();

        return new RecoveryProgress(
            criticalAccountReadiness.Count(readiness => readiness.IsReady),
            criticalAccountReadiness.Count,
            _accounts.Count(account => account.Status == AccountRecoveryStatus.FullyReviewed),
            _accounts.Count,
            totalWeight == 0 ? 1 : completedWeight / (double)totalWeight,
            requiredActions.Count(action => action.Status == RecoveryActionStatus.Blocked),
            requiredActions.Count(action => action.HasUnresolvedRisk));
    }

    private static CriticalAccountReadiness CreateCriticalAccountReadiness(Account account)
    {
        var requiredActions = account.Actions.Where(action => action.Definition.IsRequired).ToArray();
        var blockedRequiredActions = requiredActions.Count(action => action.Status == RecoveryActionStatus.Blocked);
        var unresolvedRisks = requiredActions.Count(action => action.HasUnresolvedRisk);
        var requiredActionsCompleted = requiredActions.Count(IsCompletedForProgress);
        var isReady = requiredActions.Length == requiredActionsCompleted
            && blockedRequiredActions == 0
            && unresolvedRisks == 0
            && account.Status == AccountRecoveryStatus.FullyReviewed;

        return new CriticalAccountReadiness(
            account.Id,
            account.ProviderId,
            isReady ? CriticalAccountReadinessStatus.Ready : CriticalAccountReadinessStatus.NotReady,
            requiredActionsCompleted,
            requiredActions.Length,
            blockedRequiredActions,
            unresolvedRisks);
    }

    private static bool IsCompletedForProgress(RecoveryActionInstance action) =>
        action.Status is RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable;
}
