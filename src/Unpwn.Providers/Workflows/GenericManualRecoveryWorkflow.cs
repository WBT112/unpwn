using Unpwn.Core;

namespace Unpwn.Providers.Workflows;

public static class GenericManualRecoveryWorkflow
{
    public const string WorkflowId = "generic/manual-account-recovery";
    public const string WorkflowVersion = "1.0.0";

    private const string GuidancePrefix = "Workflow.Generic.Action";
    private static readonly RecoveryPath[] AuthenticatedPath = [RecoveryPath.AuthenticatedChange];
    private static readonly RecoveryPath[] PasswordResetPath = [RecoveryPath.PasswordReset];
    private static readonly RecoveryPath[] ManualPath = [RecoveryPath.ManualRecovery];

    public static RecoveryWorkflowDefinition Create(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return new RecoveryWorkflowDefinition(
            WorkflowId,
            providerId,
            providerId,
            "unspecified",
            WorkflowVersion,
            new DateOnly(2026, 8, 12),
            [],
            [
                Required("identify-account-auth", RecoveryActionType.IdentifyAccount, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("change-password", RecoveryActionType.ChangePassword, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-auth"]),
                Required("review-sessions-auth", RecoveryActionType.InvalidateSessions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.None, ["change-password"]),
                Required("review-sign-in-methods-auth", RecoveryActionType.ReviewMfa, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["change-password"]),
                Required("review-recovery-options-auth", RecoveryActionType.ReviewRecoveryOptions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.None, ["change-password"]),
                Required("review-connected-access-auth", RecoveryActionType.ReviewConnectedApplications, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.None, ["change-password"]),
                Required("document-completion-auth", RecoveryActionType.DocumentCompletion, AuthenticatedPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-sessions-auth", "review-sign-in-methods-auth", "review-recovery-options-auth", "review-connected-access-auth"]),

                Required("identify-account-reset", RecoveryActionType.IdentifyAccount, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("reset-password", RecoveryActionType.ResetPassword, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["identify-account-reset"]),
                Required("review-sessions-reset", RecoveryActionType.InvalidateSessions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.None, ["reset-password"]),
                Required("review-sign-in-methods-reset", RecoveryActionType.ReviewMfa, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["reset-password"]),
                Required("review-recovery-options-reset", RecoveryActionType.ReviewRecoveryOptions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.None, ["reset-password"]),
                Required("review-connected-access-reset", RecoveryActionType.ReviewConnectedApplications, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.None, ["reset-password"]),
                Required("document-completion-reset", RecoveryActionType.DocumentCompletion, PasswordResetPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-sessions-reset", "review-sign-in-methods-reset", "review-recovery-options-reset", "review-connected-access-reset"]),

                Required("identify-account-manual", RecoveryActionType.IdentifyAccount, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("manual-recovery", RecoveryActionType.ManualRecovery, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["identify-account-manual"]),
                Required("review-sessions-manual", RecoveryActionType.InvalidateSessions, ManualPath, RecoveryActionImportance.Important, AutomationSupport.None, ["manual-recovery"]),
                Required("review-sign-in-methods-manual", RecoveryActionType.ReviewMfa, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["manual-recovery"]),
                Required("review-recovery-options-manual", RecoveryActionType.ReviewRecoveryOptions, ManualPath, RecoveryActionImportance.Important, AutomationSupport.None, ["manual-recovery"]),
                Required("review-connected-access-manual", RecoveryActionType.ReviewConnectedApplications, ManualPath, RecoveryActionImportance.Important, AutomationSupport.None, ["manual-recovery"]),
                Required("document-completion-manual", RecoveryActionType.DocumentCompletion, ManualPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-sessions-manual", "review-sign-in-methods-manual", "review-recovery-options-manual", "review-connected-access-manual"]),
            ])
        {
            TrustLevel = RecoveryWorkflowTrustLevel.GeneralManualGuidance,
            AllowsAccountOriginDiscovery = true,
        };
    }

    private static RecoveryActionDefinition Required(
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> paths,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites)
    {
        var guidanceId = type switch
        {
            RecoveryActionType.IdentifyAccount => "identify-account",
            RecoveryActionType.InvalidateSessions => "review-sessions",
            RecoveryActionType.ReviewMfa => "review-sign-in-methods",
            RecoveryActionType.ReviewRecoveryOptions => "review-recovery-options",
            RecoveryActionType.ReviewConnectedApplications => "review-connected-access",
            RecoveryActionType.DocumentCompletion => "document-completion",
            _ => id,
        };
        var prefix = $"{GuidancePrefix}.{guidanceId}";
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            type,
            paths,
            RecoveryActionRequirement.Required,
            importance,
            automationSupport,
            prerequisites,
            criteria,
            new RecoveryActionGuidanceKeys(
                $"{prefix}.Title",
                $"{prefix}.Instruction",
                $"{prefix}.Warning",
                $"{prefix}.Completion",
                criteria));
    }
}
