namespace Unpwn.Core;

public enum RecoveryPathSelectionReasonCode
{
    None = 0,
    ConfirmedAuthenticatedAccess,
    PasswordResetAvailable,
    ManualRecoveryAvailable,
    AuthenticatedAccessLostFallback,
    ProviderFailureFallback,
    NoSafeSupportedPath,
}

public sealed record RecoveryPathSelection(
    RecoveryPath? Path,
    RecoveryPathSelectionReasonCode ReasonCode)
{
    public bool HasSafePath => Path.HasValue;
}

/// <summary>
/// Selects a recovery approach exclusively from canonical user state and a
/// repository workflow definition. Browser observations are deliberately not
/// represented by this API.
/// </summary>
public static class RecoveryPathSelector
{
    public static RecoveryPathSelection Select(
        RecoveryWorkflowDefinition workflow,
        RecoveryAccessState accessState = RecoveryAccessState.Unknown,
        IReadOnlyCollection<RecoveryPath>? excludedPaths = null,
        RecoveryPathSelectionReasonCode? fallbackReason = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (!Enum.IsDefined(accessState))
        {
            throw new ArgumentOutOfRangeException(nameof(accessState));
        }

        var excluded = excludedPaths?.ToHashSet() ?? [];
        if (accessState == RecoveryAccessState.Available &&
            !excluded.Contains(RecoveryPath.AuthenticatedChange) &&
            IsSafeSupportedPath(workflow, RecoveryPath.AuthenticatedChange))
        {
            return Selected(
                RecoveryPath.AuthenticatedChange,
                fallbackReason ?? RecoveryPathSelectionReasonCode.ConfirmedAuthenticatedAccess);
        }

        if (!excluded.Contains(RecoveryPath.PasswordReset) &&
            IsSafeSupportedPath(workflow, RecoveryPath.PasswordReset))
        {
            return Selected(
                RecoveryPath.PasswordReset,
                fallbackReason ?? RecoveryPathSelectionReasonCode.PasswordResetAvailable);
        }

        if (!excluded.Contains(RecoveryPath.ManualRecovery) &&
            IsSafeSupportedPath(workflow, RecoveryPath.ManualRecovery))
        {
            return Selected(
                RecoveryPath.ManualRecovery,
                fallbackReason ?? RecoveryPathSelectionReasonCode.ManualRecoveryAvailable);
        }

        return new RecoveryPathSelection(
            null,
            RecoveryPathSelectionReasonCode.NoSafeSupportedPath);
    }

    private static RecoveryPathSelection Selected(
        RecoveryPath path,
        RecoveryPathSelectionReasonCode reasonCode)
    {
        if (reasonCode is RecoveryPathSelectionReasonCode.None or
            RecoveryPathSelectionReasonCode.NoSafeSupportedPath)
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        return new RecoveryPathSelection(path, reasonCode);
    }

    private static bool IsSafeSupportedPath(
        RecoveryWorkflowDefinition workflow,
        RecoveryPath path)
    {
        var actions = workflow.Actions
            .Where(action => action.SupportsPath(path))
            .ToArray();
        if (actions.Length == 0)
        {
            return false;
        }

        var actionIds = actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        return actions.All(action => action.Prerequisites.All(actionIds.Contains));
    }
}
