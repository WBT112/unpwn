namespace Unpwn.Core;

public enum RecoveryAccessState
{
    Unknown,
    Available,
    Lost,
    WaitingForProviderReview,
}

public enum RecoveryActionReasonCode
{
    None,
    WaitingForPrerequisite,
    UserActionRequired,
    UserBlocked,
    ProviderFailure,
    TrulyNotApplicable,
    UnresolvedRiskAccepted,
    AccessLost,
}

public sealed record RecoveryActionExecutionState(
    string DefinitionId,
    bool IsRequired,
    RecoveryActionImportance Importance,
    RecoveryActionStatus Status,
    RecoveryActionReasonCode ReasonCode,
    string[] ReasonArguments,
    string? UserReason,
    string? UserNotes,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt,
    bool HasUnresolvedRisk,
    NotApplicableDisposition? NotApplicableDisposition,
    GeneratedCredentialReference? CredentialReference)
{
    public string[] AcknowledgedCompletionCriteria { get; init; } = [];

    public static RecoveryActionExecutionState Create(RecoveryActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new RecoveryActionExecutionState(
            definition.Id,
            definition.IsRequired,
            definition.Importance,
            RecoveryActionStatus.Open,
            RecoveryActionReasonCode.None,
            [],
            UserReason: null,
            UserNotes: null,
            StartedAt: null,
            CompletedAt: null,
            UpdatedAt: null,
            HasUnresolvedRisk: false,
            NotApplicableDisposition: null,
            CredentialReference: null);
    }

    public void Validate(
        RecoveryActionDefinition definition,
        Guid accountId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionId);
        ArgumentNullException.ThrowIfNull(ReasonArguments);
        ArgumentNullException.ThrowIfNull(AcknowledgedCompletionCriteria);
        if (!string.Equals(DefinitionId, definition.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A recovery action execution must match its definition identifier.");
        }

        if (IsRequired != definition.IsRequired || Importance != definition.Importance)
        {
            throw new InvalidOperationException("A recovery action execution must preserve requirement and importance from its definition.");
        }

        ValidateOptionalText(UserReason, 1000, nameof(UserReason));
        ValidateOptionalText(UserNotes, 4000, nameof(UserNotes));
        ValidateTimestamp(StartedAt, createdAt, nameof(StartedAt));
        ValidateTimestamp(CompletedAt, createdAt, nameof(CompletedAt));
        ValidateTimestamp(UpdatedAt, createdAt, nameof(UpdatedAt));
        if (CompletedAt is not null && StartedAt is null)
        {
            throw new InvalidOperationException("A completed recovery action requires a start time.");
        }

        if (StartedAt is not null && CompletedAt < StartedAt)
        {
            throw new InvalidOperationException("A recovery action completion cannot predate its start.");
        }

        if (UpdatedAt is not null &&
            ((StartedAt is not null && UpdatedAt < StartedAt) ||
             (CompletedAt is not null && UpdatedAt < CompletedAt)))
        {
            throw new InvalidOperationException("A recovery action update time cannot predate its lifecycle state.");
        }

        if (RequiresReason(Status) && ReasonCode == RecoveryActionReasonCode.None)
        {
            throw new InvalidOperationException("The recovery action state requires a structured reason code.");
        }

        if (!RequiresReason(Status) && ReasonCode != RecoveryActionReasonCode.None)
        {
            throw new InvalidOperationException("The recovery action state contains an unexpected reason code.");
        }

        if (ReasonCode == RecoveryActionReasonCode.WaitingForPrerequisite && ReasonArguments.Length == 0)
        {
            throw new InvalidOperationException("A prerequisite reason requires stable prerequisite action identifiers.");
        }

        if (ReasonCode != RecoveryActionReasonCode.WaitingForPrerequisite && ReasonArguments.Length != 0)
        {
            throw new InvalidOperationException("Only prerequisite reasons may contain structured action identifiers.");
        }

        if (Status == RecoveryActionStatus.NotApplicable && NotApplicableDisposition is null)
        {
            throw new InvalidOperationException("A not-applicable recovery action requires an explicit disposition.");
        }

        if (Status != RecoveryActionStatus.NotApplicable && NotApplicableDisposition is not null)
        {
            throw new InvalidOperationException("Only a not-applicable recovery action may contain a disposition.");
        }

        if (HasUnresolvedRisk && !definition.IsRequired)
        {
            throw new InvalidOperationException("Only required recovery actions may carry unresolved risk.");
        }

        if (HasUnresolvedRisk && ReasonCode != RecoveryActionReasonCode.UnresolvedRiskAccepted)
        {
            throw new InvalidOperationException("Unresolved risk requires its stable structured reason code.");
        }

        if (CredentialReference is not null)
        {
            CredentialReference.Validate();
            if (CredentialReference.AccountId != accountId)
            {
                throw new InvalidOperationException("A credential reference must belong to the same account execution.");
            }
        }

        var definedCriteria = definition.Guidance.CompletionCriteriaKeys.ToHashSet(StringComparer.Ordinal);
        if (AcknowledgedCompletionCriteria.Distinct(StringComparer.Ordinal).Count() !=
                AcknowledgedCompletionCriteria.Length ||
            AcknowledgedCompletionCriteria.Any(criterion => !definedCriteria.Contains(criterion)))
        {
            throw new InvalidOperationException(
                "Completion acknowledgements must reference distinct repository-controlled criteria.");
        }
    }

    private static bool RequiresReason(RecoveryActionStatus status) => status is
        RecoveryActionStatus.Blocked or
        RecoveryActionStatus.Failed or
        RecoveryActionStatus.NotApplicable or
        RecoveryActionStatus.NeedsUserAction;

    private static void ValidateOptionalText(string? value, int maximumLength, string propertyName)
    {
        if (value?.Length > maximumLength)
        {
            throw new InvalidOperationException($"{propertyName} exceeds the encrypted user-content limit.");
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset? value,
        DateTimeOffset createdAt,
        string propertyName)
    {
        if (value is { } timestamp && timestamp < createdAt)
        {
            throw new InvalidOperationException($"{propertyName} predates the account execution.");
        }
    }
}

public sealed record AccountRecoveryExecutionState(
    Guid AccountId,
    string ProviderId,
    string WorkflowId,
    string WorkflowVersion,
    RecoveryPath SelectedPath,
    RecoveryAccessState AccessState,
    string? AccessReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    RecoveryActionExecutionState[] Actions)
{
    public AccountRecoveryStatus RecoveryStatus
    {
        get
        {
            if (AccessState == RecoveryAccessState.Lost)
            {
                return AccountRecoveryStatus.AccessNotRestored;
            }

            var required = Actions
                .Where(action => action.IsRequired)
                .Where(action => action.Status != RecoveryActionStatus.NotApplicable ||
                    action.NotApplicableDisposition != global::Unpwn.Core.NotApplicableDisposition.TrulyNotApplicable)
                .ToArray();
            if (required.Any(action => action.HasUnresolvedRisk ||
                action.Status is RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed))
            {
                return AccountRecoveryStatus.NotFullySecured;
            }

            if (required.Length > 0 &&
                required.All(action => action.Status == RecoveryActionStatus.Completed))
            {
                return AccountRecoveryStatus.FullyReviewed;
            }

            return required.Any(action => action.Status is
                    RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction)
                ? AccountRecoveryStatus.InProgress
                : AccountRecoveryStatus.Open;
        }
    }

    public static AccountRecoveryExecutionState Create(
        Guid accountId,
        RecoveryWorkflowDefinition workflow,
        RecoveryPath selectedPath,
        DateTimeOffset occurredAt)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account recovery execution requires an account identifier.", nameof(accountId));
        }

        ArgumentNullException.ThrowIfNull(workflow);
        var definitions = workflow.Actions
            .Where(action => action.SupportsPath(selectedPath))
            .ToArray();
        if (definitions.Length == 0)
        {
            throw new InvalidOperationException("The selected recovery path contains no actions.");
        }

        var state = new AccountRecoveryExecutionState(
            accountId,
            workflow.ProviderId,
            workflow.WorkflowId,
            workflow.WorkflowVersion,
            selectedPath,
            RecoveryAccessState.Unknown,
            AccessReason: null,
            occurredAt,
            occurredAt,
            Revision: 0,
            Actions: [.. definitions.Select(RecoveryActionExecutionState.Create)]);
        state.Validate(workflow);
        return state;
    }

    public RecoveryActionExecutionState GetAction(string definitionId) =>
        Actions.Single(action => string.Equals(action.DefinitionId, definitionId, StringComparison.Ordinal));

    public AccountRecoveryExecutionState ChangePath(
        RecoveryWorkflowDefinition workflow,
        RecoveryPath selectedPath,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ValidateWorkflowIdentity(workflow);
        ValidateTimestamp(occurredAt);
        if (!Enum.IsDefined(selectedPath))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedPath));
        }

        if (selectedPath == SelectedPath)
        {
            throw new InvalidOperationException("The requested recovery path is already selected.");
        }

        var hasMaterialActionState = Actions.Any(action =>
            action.Status != RecoveryActionStatus.Open ||
            action.StartedAt is not null ||
            action.CompletedAt is not null ||
            action.UserReason is not null ||
            action.UserNotes is not null ||
            action.AcknowledgedCompletionCriteria.Length > 0 ||
            action.CredentialReference is not null);
        if (hasMaterialActionState)
        {
            throw new InvalidOperationException(
                "A recovery path cannot change after action progress has been recorded.");
        }

        var definitions = workflow.Actions
            .Where(action => action.SupportsPath(selectedPath))
            .ToArray();
        if (definitions.Length == 0)
        {
            throw new InvalidOperationException("The selected recovery path contains no actions.");
        }

        var changed = this with
        {
            SelectedPath = selectedPath,
            Actions = [.. definitions.Select(RecoveryActionExecutionState.Create)],
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
        changed.Validate(workflow);
        return changed;
    }

    public AccountRecoveryExecutionState SetAccessState(
        RecoveryAccessState accessState,
        string? userReason,
        DateTimeOffset occurredAt)
    {
        ValidateTimestamp(occurredAt);
        if (!Enum.IsDefined(accessState))
        {
            throw new ArgumentOutOfRangeException(nameof(accessState));
        }

        var accessReason = accessState is RecoveryAccessState.Lost or RecoveryAccessState.WaitingForProviderReview
            ? RequireUserText(userReason)
            : null;
        var actions = Actions.ToArray();
        if (accessState == RecoveryAccessState.Lost)
        {
            var activeIndex = Array.FindIndex(actions, action => action.Status is
                RecoveryActionStatus.InProgress or RecoveryActionStatus.NeedsUserAction);
            if (activeIndex >= 0)
            {
                actions[activeIndex] = actions[activeIndex] with
                {
                    Status = RecoveryActionStatus.Failed,
                    ReasonCode = RecoveryActionReasonCode.AccessLost,
                    ReasonArguments = [],
                    UserReason = accessReason,
                    UpdatedAt = occurredAt,
                    HasUnresolvedRisk = false,
                    NotApplicableDisposition = null,
                };
            }
        }

        return this with
        {
            AccessState = accessState,
            AccessReason = accessReason,
            Actions = actions,
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
    }

    public AccountRecoveryExecutionState StartAction(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        DateTimeOffset occurredAt)
    {
        var definition = GetDefinition(workflow, definitionId);
        var action = GetAction(definitionId);
        EnsureTransitionAllowed(action.Status, RecoveryActionStatus.InProgress);
        ValidateTimestamp(occurredAt);
        var incomplete = definition.Prerequisites
            .Select(GetAction)
            .Where(prerequisite => !IsPrerequisiteSatisfied(prerequisite))
            .Select(prerequisite => prerequisite.DefinitionId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (incomplete.Length > 0)
        {
            return ReplaceAction(action with
            {
                Status = RecoveryActionStatus.Blocked,
                ReasonCode = RecoveryActionReasonCode.WaitingForPrerequisite,
                ReasonArguments = incomplete,
                UserReason = null,
                UpdatedAt = occurredAt,
            }, occurredAt);
        }

        return ReplaceAction(action with
        {
            Status = RecoveryActionStatus.InProgress,
            ReasonCode = RecoveryActionReasonCode.None,
            ReasonArguments = [],
            UserReason = null,
            StartedAt = action.StartedAt ?? occurredAt,
            CompletedAt = null,
            UpdatedAt = occurredAt,
            HasUnresolvedRisk = false,
            NotApplicableDisposition = null,
            AcknowledgedCompletionCriteria = [],
        }, occurredAt);
    }

    public AccountRecoveryExecutionState SetCompletionCriteriaAcknowledgements(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        IReadOnlyCollection<string> acknowledgedCriteria,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedCriteria);
        var definition = GetDefinition(workflow, definitionId);
        var action = GetAction(definitionId);
        if (action.Status != RecoveryActionStatus.InProgress)
        {
            throw new InvalidOperationException(
                "Completion criteria may be acknowledged only while the action is in progress.");
        }

        ValidateTimestamp(occurredAt);
        var definedCriteria = definition.Guidance.CompletionCriteriaKeys.ToHashSet(StringComparer.Ordinal);
        var normalized = acknowledgedCriteria
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(criterion => !definedCriteria.Contains(criterion)))
        {
            throw new InvalidOperationException(
                "Completion acknowledgements must reference repository-controlled criteria.");
        }

        return ReplaceAction(action with
        {
            AcknowledgedCompletionCriteria = normalized,
            UpdatedAt = occurredAt,
        }, occurredAt);
    }

    public AccountRecoveryExecutionState CompleteAction(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        bool completionCriteriaAcknowledged,
        DateTimeOffset occurredAt)
    {
        _ = GetDefinition(workflow, definitionId);
        var action = GetAction(definitionId);
        if (!completionCriteriaAcknowledged)
        {
            throw new InvalidOperationException("Completion requires explicit acknowledgement of the structured completion criteria.");
        }

        EnsureTransitionAllowed(action.Status, RecoveryActionStatus.Completed);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            Status = RecoveryActionStatus.Completed,
            ReasonCode = RecoveryActionReasonCode.None,
            ReasonArguments = [],
            UserReason = null,
            StartedAt = action.StartedAt ?? occurredAt,
            CompletedAt = occurredAt,
            UpdatedAt = occurredAt,
            HasUnresolvedRisk = false,
            NotApplicableDisposition = null,
            AcknowledgedCompletionCriteria =
                [.. GetDefinition(workflow, definitionId).Guidance.CompletionCriteriaKeys],
        }, occurredAt);
    }

    public AccountRecoveryExecutionState RequireUserAction(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        string userReason,
        DateTimeOffset occurredAt) =>
        TransitionWithReason(
            workflow,
            definitionId,
            RecoveryActionStatus.NeedsUserAction,
            RecoveryActionReasonCode.UserActionRequired,
            userReason,
            unresolvedRisk: false,
            occurredAt);

    public AccountRecoveryExecutionState BlockAction(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        string userReason,
        DateTimeOffset occurredAt) =>
        TransitionWithReason(
            workflow,
            definitionId,
            RecoveryActionStatus.Blocked,
            RecoveryActionReasonCode.UserBlocked,
            userReason,
            unresolvedRisk: false,
            occurredAt);

    public AccountRecoveryExecutionState FailAction(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        string userReason,
        DateTimeOffset occurredAt) =>
        TransitionWithReason(
            workflow,
            definitionId,
            RecoveryActionStatus.Failed,
            RecoveryActionReasonCode.ProviderFailure,
            userReason,
            unresolvedRisk: false,
            occurredAt);

    public AccountRecoveryExecutionState MarkNotApplicable(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        string userReason,
        NotApplicableDisposition disposition,
        DateTimeOffset occurredAt)
    {
        var definition = GetDefinition(workflow, definitionId);
        if (disposition == global::Unpwn.Core.NotApplicableDisposition.UnresolvedRisk && !definition.IsRequired)
        {
            throw new InvalidOperationException("Only required actions may create unresolved risk.");
        }

        var action = GetAction(definitionId);
        EnsureTransitionAllowed(action.Status, RecoveryActionStatus.NotApplicable);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            Status = RecoveryActionStatus.NotApplicable,
            ReasonCode = disposition == global::Unpwn.Core.NotApplicableDisposition.TrulyNotApplicable
                ? RecoveryActionReasonCode.TrulyNotApplicable
                : RecoveryActionReasonCode.UnresolvedRiskAccepted,
            ReasonArguments = [],
            UserReason = RequireUserText(userReason),
            UpdatedAt = occurredAt,
            HasUnresolvedRisk = disposition == global::Unpwn.Core.NotApplicableDisposition.UnresolvedRisk,
            NotApplicableDisposition = disposition,
        }, occurredAt);
    }

    public AccountRecoveryExecutionState AcceptUnresolvedRisk(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        string userReason,
        DateTimeOffset occurredAt)
    {
        var definition = GetDefinition(workflow, definitionId);
        if (!definition.IsRequired)
        {
            throw new InvalidOperationException("Only required actions may create unresolved risk.");
        }

        var action = GetAction(definitionId);
        EnsureTransitionAllowed(action.Status, RecoveryActionStatus.Failed);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            Status = RecoveryActionStatus.Failed,
            ReasonCode = RecoveryActionReasonCode.UnresolvedRiskAccepted,
            ReasonArguments = [],
            UserReason = RequireUserText(userReason),
            UpdatedAt = occurredAt,
            HasUnresolvedRisk = true,
            NotApplicableDisposition = null,
        }, occurredAt);
    }

    public AccountRecoveryExecutionState SetUserNotes(
        string definitionId,
        string? userNotes,
        DateTimeOffset occurredAt)
    {
        var action = GetAction(definitionId);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            UserNotes = NormalizeUserText(userNotes),
            UpdatedAt = occurredAt,
        }, occurredAt);
    }

    public AccountRecoveryExecutionState AttachCredentialReference(
        string definitionId,
        GeneratedCredentialReference reference,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        if (reference.AccountId != AccountId)
        {
            throw new InvalidOperationException("A generated credential reference belongs to another account.");
        }

        var action = GetAction(definitionId);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            CredentialReference = reference,
            UpdatedAt = occurredAt,
        }, occurredAt);
    }

    public RecoveryAccountDashboardEntry CreateDashboardProjection(
        AccountCriticality criticality,
        int dependencyDepth,
        Guid[] waitingForAccountIds,
        int inventoryBlockedIssues = 0,
        int inventoryUnresolvedRisks = 0)
    {
        ArgumentNullException.ThrowIfNull(waitingForAccountIds);
        ArgumentOutOfRangeException.ThrowIfNegative(inventoryBlockedIssues);
        ArgumentOutOfRangeException.ThrowIfNegative(inventoryUnresolvedRisks);
        var allRequired = Actions.Where(action => action.IsRequired).ToArray();
        var required = allRequired
            .Where(action => action.Status != RecoveryActionStatus.NotApplicable ||
                action.NotApplicableDisposition != global::Unpwn.Core.NotApplicableDisposition.TrulyNotApplicable)
            .ToArray();
        var requiredTotal = required.Length;
        var completed = required.Count(action => action.Status == RecoveryActionStatus.Completed);
        var blocked = required.Count(action => action.Status == RecoveryActionStatus.Blocked);
        var failed = required.Count(action => action.Status == RecoveryActionStatus.Failed);
        var unresolved = required.Count(action => action.HasUnresolvedRisk);
        var totalWeight = required.Sum(action => (int)action.Importance);
        var completedWeight = required
            .Where(action => action.Status == RecoveryActionStatus.Completed)
            .Sum(action => (int)action.Importance);
        var recommended = Actions
            .OrderBy(action => action.Status switch
            {
                RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed => 0,
                RecoveryActionStatus.NeedsUserAction => 1,
                RecoveryActionStatus.InProgress => 2,
                RecoveryActionStatus.Open => 3,
                _ => 4,
            })
            .FirstOrDefault(action => action.Status is not
                (RecoveryActionStatus.Completed or RecoveryActionStatus.NotApplicable))
            ?.DefinitionId;
        return new RecoveryAccountDashboardEntry(
            AccountId,
            ProviderId,
            criticality,
            RecoveryStatus,
            completed,
            requiredTotal,
            completedWeight,
            totalWeight,
            blocked + inventoryBlockedIssues,
            failed,
            unresolved + inventoryUnresolvedRisks,
            AccessState == RecoveryAccessState.Lost,
            CredentialsAwaitingExport: Actions.Count(action =>
                action.CredentialReference is not null && action.Status == RecoveryActionStatus.Completed),
            CredentialsAwaitingDeletion: 0,
            recommended,
            dependencyDepth,
            waitingForAccountIds)
        {
            InventoryBlockedIssues = inventoryBlockedIssues,
            InventoryUnresolvedRisks = inventoryUnresolvedRisks,
            RequiredActionsOpen = allRequired.Count(action => action.Status == RecoveryActionStatus.Open),
            RequiredActionsInProgress = allRequired.Count(action => action.Status == RecoveryActionStatus.InProgress),
            RequiredActionsAwaitingUser = allRequired.Count(action => action.Status == RecoveryActionStatus.NeedsUserAction),
            RequiredActionsNotApplicable = allRequired.Count(action => action.Status == RecoveryActionStatus.NotApplicable),
            AcceptedRiskActions = allRequired.Count(action => action.HasUnresolvedRisk),
        };
    }

    public void Validate(RecoveryWorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (AccountId == Guid.Empty || Revision < 0 || UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException("The persisted account recovery execution is invalid.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowVersion);
        ArgumentNullException.ThrowIfNull(Actions);
        if (!string.Equals(ProviderId, workflow.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(WorkflowId, workflow.WorkflowId, StringComparison.Ordinal) ||
            !string.Equals(WorkflowVersion, workflow.WorkflowVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The account execution workflow identity does not match the repository definition.");
        }

        ValidateAccessReason();
        var definitions = workflow.Actions
            .Where(action => action.SupportsPath(SelectedPath))
            .ToDictionary(action => action.Id, StringComparer.Ordinal);
        if (Actions.Length != definitions.Count ||
            Actions.Select(action => action.DefinitionId).Distinct(StringComparer.Ordinal).Count() != Actions.Length)
        {
            throw new InvalidOperationException("The account execution actions do not match the selected workflow path.");
        }

        foreach (var action in Actions)
        {
            if (!definitions.TryGetValue(action.DefinitionId, out var definition))
            {
                throw new InvalidOperationException("The account execution contains an action outside its selected workflow path.");
            }

            action.Validate(definition, AccountId, CreatedAt);
        }
    }

    private AccountRecoveryExecutionState TransitionWithReason(
        RecoveryWorkflowDefinition workflow,
        string definitionId,
        RecoveryActionStatus status,
        RecoveryActionReasonCode reasonCode,
        string userReason,
        bool unresolvedRisk,
        DateTimeOffset occurredAt)
    {
        var definition = GetDefinition(workflow, definitionId);
        if (unresolvedRisk && !definition.IsRequired)
        {
            throw new InvalidOperationException("Only required actions may create unresolved risk.");
        }

        var action = GetAction(definitionId);
        EnsureTransitionAllowed(action.Status, status);
        ValidateTimestamp(occurredAt);
        return ReplaceAction(action with
        {
            Status = status,
            ReasonCode = reasonCode,
            ReasonArguments = [],
            UserReason = RequireUserText(userReason),
            StartedAt = action.StartedAt ?? occurredAt,
            UpdatedAt = occurredAt,
            HasUnresolvedRisk = unresolvedRisk,
            NotApplicableDisposition = null,
        }, occurredAt);
    }

    private RecoveryActionDefinition GetDefinition(
        RecoveryWorkflowDefinition workflow,
        string definitionId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ValidateWorkflowIdentity(workflow);
        var definition = workflow.Actions.SingleOrDefault(action =>
            action.SupportsPath(SelectedPath) &&
            string.Equals(action.Id, definitionId, StringComparison.Ordinal));
        return definition ?? throw new KeyNotFoundException("The recovery action definition is unavailable for the selected path.");
    }

    private void ValidateWorkflowIdentity(RecoveryWorkflowDefinition workflow)
    {
        if (!string.Equals(WorkflowId, workflow.WorkflowId, StringComparison.Ordinal) ||
            !string.Equals(WorkflowVersion, workflow.WorkflowVersion, StringComparison.Ordinal) ||
            !string.Equals(ProviderId, workflow.ProviderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The repository workflow does not match the account execution.");
        }
    }

    private void ValidateAccessReason()
    {
        if (AccessReason?.Length > 1000)
        {
            throw new InvalidOperationException("AccessReason exceeds the encrypted user-content limit.");
        }

        var requiresReason = AccessState is RecoveryAccessState.Lost or RecoveryAccessState.WaitingForProviderReview;
        if (requiresReason == string.IsNullOrWhiteSpace(AccessReason))
        {
            throw new InvalidOperationException("The recovery access state and its user-authored reason are inconsistent.");
        }
    }

    private AccountRecoveryExecutionState ReplaceAction(
        RecoveryActionExecutionState updatedAction,
        DateTimeOffset occurredAt)
    {
        var actions = Actions.ToArray();
        var index = Array.FindIndex(actions, action =>
            string.Equals(action.DefinitionId, updatedAction.DefinitionId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new KeyNotFoundException("The recovery action execution is unavailable.");
        }

        actions[index] = updatedAction;
        return this with
        {
            Actions = actions,
            UpdatedAt = occurredAt,
            Revision = Revision + 1,
        };
    }

    private void ValidateTimestamp(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt), "Recovery execution timestamps must be monotonic.");
        }
    }

    private static bool IsPrerequisiteSatisfied(RecoveryActionExecutionState action) =>
        action.Status == RecoveryActionStatus.Completed ||
        action is
        {
            Status: RecoveryActionStatus.NotApplicable,
            NotApplicableDisposition: global::Unpwn.Core.NotApplicableDisposition.TrulyNotApplicable,
        };

    private static void EnsureTransitionAllowed(
        RecoveryActionStatus current,
        RecoveryActionStatus next)
    {
        var allowed = current switch
        {
            RecoveryActionStatus.Open => next is
                RecoveryActionStatus.InProgress or
                RecoveryActionStatus.Blocked or
                RecoveryActionStatus.NeedsUserAction or
                RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.InProgress => next is
                RecoveryActionStatus.Completed or
                RecoveryActionStatus.Blocked or
                RecoveryActionStatus.NeedsUserAction or
                RecoveryActionStatus.Failed or
                RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.Blocked => next is
                RecoveryActionStatus.InProgress or
                RecoveryActionStatus.NeedsUserAction or
                RecoveryActionStatus.Failed or
                RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.NeedsUserAction => next is
                RecoveryActionStatus.InProgress or
                RecoveryActionStatus.Blocked or
                RecoveryActionStatus.Failed or
                RecoveryActionStatus.NotApplicable,
            RecoveryActionStatus.Failed => next is
                RecoveryActionStatus.InProgress or
                RecoveryActionStatus.Blocked or
                RecoveryActionStatus.NotApplicable,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException("The requested recovery action transition is not allowed.");
        }
    }

    private static string RequireUserText(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string? NormalizeUserText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
