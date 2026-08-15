namespace Unpwn.Core;

/// <summary>
/// Derives the active recovery checklist for an account category without
/// duplicating repository provider workflows or discarding full recovery state.
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

        var fullWorkflow = Expand(workflow);
        if (category != AccountRecoveryCategory.NonCritical)
        {
            return fullWorkflow;
        }

        var projected = new List<RecoveryActionDefinition>(2);
        AddSinglePasswordAction(
            projected,
            fullWorkflow,
            RecoveryPath.AuthenticatedChange,
            RecoveryActionType.ChangePassword);
        AddSinglePasswordAction(
            projected,
            fullWorkflow,
            RecoveryPath.PasswordReset,
            RecoveryActionType.ResetPassword);

        return fullWorkflow with
        {
            Actions = projected,
            UnscopedActions = fullWorkflow.Actions,
        };
    }

    public static RecoveryWorkflowDefinition Expand(RecoveryWorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return workflow.UnscopedActions is { } fullActions
            ? workflow with { Actions = fullActions, UnscopedActions = null }
            : workflow;
    }

    public static AccountRecoveryExecutionState ProjectStateForView(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition scopedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scopedWorkflow);
        var activeIds = scopedWorkflow.Actions
            .Where(action => action.SupportsPath(state.SelectedPath))
            .Select(action => action.Id)
            .ToHashSet(StringComparer.Ordinal);
        var projectedActions = state.Actions
            .Where(action => activeIds.Contains(action.DefinitionId))
            .ToArray();
        return projectedActions.Length == state.Actions.Length
            ? state
            : state with { Actions = projectedActions };
    }

    /// <summary>
    /// Path transitions executed against a reduced workflow materialize only
    /// projected actions. Rehydrate the selected path from the complete
    /// repository workflow before encrypted persistence.
    /// </summary>
    public static AccountRecoveryExecutionState RehydrateFullPathActions(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(state);
        var fullWorkflow = Expand(workflow);
        var definitions = fullWorkflow.Actions
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
