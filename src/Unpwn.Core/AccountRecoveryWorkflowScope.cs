namespace Unpwn.Core;

/// <summary>
/// Derives the actions that are in scope for the current account category without
/// mutating or duplicating repository provider workflows. Non-critical accounts
/// intentionally use only a single password change/reset action for each safe
/// recovery path; all other categories keep the complete reviewed workflow.
/// </summary>
public static class AccountRecoveryWorkflowScope
{
    public static RecoveryWorkflowDefinition Project(
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        if (category != AccountRecoveryCategory.NonCritical)
        {
            return workflow;
        }

        var projected = new List<RecoveryActionDefinition>(2);
        AddSinglePasswordAction(
            projected,
            workflow,
            RecoveryPath.AuthenticatedChange,
            RecoveryActionType.ChangePassword);
        AddSinglePasswordAction(
            projected,
            workflow,
            RecoveryPath.PasswordReset,
            RecoveryActionType.ResetPassword);

        return workflow with { Actions = projected };
    }

    public static bool SupportsPath(
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category,
        RecoveryPath path) =>
        Project(workflow, category).Actions.Any(action => action.SupportsPath(path));

    public static bool IsActionInScope(
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category,
        RecoveryPath path,
        string actionDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionDefinitionId);
        return Project(workflow, category).Actions.Any(action =>
            action.SupportsPath(path) &&
            string.Equals(action.Id, actionDefinitionId, StringComparison.Ordinal));
    }

    public static AccountRecoveryStatus GetRecoveryStatus(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workflow);
        if (category != AccountRecoveryCategory.NonCritical)
        {
            return state.RecoveryStatus;
        }

        if (state.AccessState == RecoveryAccessState.Lost)
        {
            return AccountRecoveryStatus.AccessNotRestored;
        }

        var required = ActiveActions(state, workflow, category)
            .Where(action => action.IsRequired)
            .Where(action => action.Status != RecoveryActionStatus.NotApplicable ||
                action.NotApplicableDisposition != NotApplicableDisposition.TrulyNotApplicable)
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

    public static RecoveryAccountDashboardEntry CreateDashboardProjection(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workflow);
        if (category != AccountRecoveryCategory.NonCritical)
        {
            return state.CreateDashboardProjection(category);
        }

        var allRequired = ActiveActions(state, workflow, category)
            .Where(action => action.IsRequired)
            .ToArray();
        var required = allRequired
            .Where(action => action.Status != RecoveryActionStatus.NotApplicable ||
                action.NotApplicableDisposition != NotApplicableDisposition.TrulyNotApplicable)
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
        var active = ActiveActions(state, workflow, category);
        var recommended = active
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
            state.AccountId,
            state.ProviderId,
            AccountCriticality.Routine,
            GetRecoveryStatus(state, workflow, category),
            completed,
            requiredTotal,
            completedWeight,
            totalWeight,
            blocked,
            failed,
            unresolved,
            state.AccessState == RecoveryAccessState.Lost,
            CredentialsAwaitingExport: active.Count(action =>
                action.CredentialReference is not null && action.Status == RecoveryActionStatus.Completed),
            CredentialsAwaitingDeletion: 0,
            recommended)
        {
            Category = category,
            RequiredActionsOpen = allRequired.Count(action => action.Status == RecoveryActionStatus.Open),
            RequiredActionsInProgress = allRequired.Count(action => action.Status == RecoveryActionStatus.InProgress),
            RequiredActionsAwaitingUser = allRequired.Count(action => action.Status == RecoveryActionStatus.NeedsUserAction),
            RequiredActionsNotApplicable = allRequired.Count(action => action.Status == RecoveryActionStatus.NotApplicable),
            AcceptedRiskActions = allRequired.Count(action => action.HasUnresolvedRisk),
        };
    }

    /// <summary>
    /// Path transitions executed against a reduced projection materialize only
    /// projected actions. Rehydrate the selected path from the full repository
    /// workflow before persistence so a later category upgrade can restore the
    /// complete checklist without a migration.
    /// </summary>
    public static AccountRecoveryExecutionState RehydrateFullPathActions(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workflow);
        var definitions = workflow.Actions
            .Where(action => action.SupportsPath(state.SelectedPath))
            .ToArray();
        var stateIds = state.Actions.Select(action => action.DefinitionId).ToHashSet(StringComparer.Ordinal);
        if (state.Actions.Length == definitions.Length &&
            definitions.All(definition => stateIds.Contains(definition.Id)))
        {
            return state;
        }

        if (definitions.Length == 0)
        {
            throw new InvalidOperationException("The selected full recovery path contains no actions.");
        }

        return state with
        {
            Actions = [.. definitions.Select(RecoveryActionExecutionState.Create)],
        };
    }

    private static RecoveryActionExecutionState[] ActiveActions(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryCategory category)
    {
        var ids = Project(workflow, category).Actions
            .Where(action => action.SupportsPath(state.SelectedPath))
            .Select(action => action.Id)
            .ToHashSet(StringComparer.Ordinal);
        return state.Actions
            .Where(action => ids.Contains(action.DefinitionId))
            .ToArray();
    }

    private static void AddSinglePasswordAction(
        ICollection<RecoveryActionDefinition> destination,
        RecoveryWorkflowDefinition workflow,
        RecoveryPath path,
        RecoveryActionType type)
    {
        var candidates = workflow.Actions
            .Where(action => action.SupportsPath(path) && action.Type == type)
            .ToArray();
        if (candidates.Length != 1)
        {
            return;
        }

        destination.Add(candidates[0] with
        {
            RecoveryPaths = [path],
            Prerequisites = [],
        });
    }
}
