using Unpwn.Core.Recovery.Workflows;

namespace Unpwn.Providers.Workflows;

public static class RepositoryWorkflowCatalog
{
    public static IReadOnlyList<RecoveryWorkflowDefinition> Workflows { get; } =
    [
        new(
            "github.com/consumer-account-recovery",
            "github.com",
            "GitHub",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 5),
            [
                new(
                    "settings",
                    new Uri("https://github.com/settings/security"),
                    ["https://github.com"]),
                new(
                    "password-reset",
                    new Uri("https://github.com/password_reset"),
                    ["https://github.com"])
            ],
            [
                new(
                    "identify-account",
                    RecoveryActionType.IdentifyAccount,
                    RecoveryPath.AuthenticatedChange,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.None,
                    [],
                    ["The user has identified the affected synthetic or real account outside test artifacts."]),
                new(
                    "change-password",
                    RecoveryActionType.ChangePassword,
                    RecoveryPath.AuthenticatedChange,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.Navigation,
                    ["identify-account"],
                    ["The account password has been changed or reset through an official GitHub recovery path."]),
                new(
                    "review-mfa",
                    RecoveryActionType.ReviewMfa,
                    RecoveryPath.AuthenticatedChange,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.Navigation,
                    ["change-password"],
                    ["MFA methods and recovery codes have been reviewed, rotated, or documented as unavailable with unresolved risk."]),
                new(
                    "invalidate-sessions",
                    RecoveryActionType.InvalidateSessions,
                    RecoveryPath.AuthenticatedChange,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Important,
                    AutomationSupport.Navigation,
                    ["change-password"],
                    ["Active sessions, trusted devices, and tokens have been reviewed and revoked where appropriate."]),
                new(
                    "review-recovery-options",
                    RecoveryActionType.ReviewRecoveryOptions,
                    RecoveryPath.AuthenticatedChange,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Important,
                    AutomationSupport.Navigation,
                    ["change-password"],
                    ["Recovery email addresses, phone numbers, SSH keys, and connected applications have been reviewed."]),
                new(
                    "document-completion",
                    RecoveryActionType.DocumentCompletion,
                    RecoveryPath.ManualRecovery,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Routine,
                    AutomationSupport.None,
                    ["review-mfa", "invalidate-sessions", "review-recovery-options"],
                    ["Completion notes record remaining unresolved risks and the account is not represented as fully secured when risks remain."])
            ])
    ];

    public static WorkflowValidationResult ValidateAll()
    {
        RecoveryWorkflowValidator validator = new();
        List<WorkflowValidationDiagnostic> diagnostics = [];

        foreach (RecoveryWorkflowDefinition workflow in Workflows)
        {
            diagnostics.AddRange(validator.Validate(workflow).Diagnostics);
        }

        return diagnostics.Count == 0 ? WorkflowValidationResult.Valid : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }
}
