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
                    "reset-password",
                    RecoveryActionType.ChangePassword,
                    RecoveryPath.PasswordReset,
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.Navigation,
                    ["identify-account"],
                    ["The account password has been reset through an official GitHub recovery path after dependent recovery channels are secured."]),
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

    public static IReadOnlyList<ProviderContractScenario> ContractScenarios { get; } =
    [
        new(
            "github-authenticated-change-available",
            "github.com/consumer-account-recovery",
            "Authenticated password change is available after the user confirms account access.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account", "change-password", "review-mfa", "invalidate-sessions", "review-recovery-options", "document-completion"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["change-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"]),
                ["review-mfa"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["change-password"]),
                ["invalidate-sessions"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Important, ["change-password"]),
                ["review-recovery-options"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Important, ["change-password"]),
                ["document-completion"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Routine, ["review-mfa", "invalidate-sessions", "review-recovery-options"]),
            },
            AccountContractOutcome.CanBeFullySecured),
        new(
            "github-password-reset-through-secured-email",
            "github.com/consumer-account-recovery",
            "Password reset can proceed after the primary email dependency has already been secured.",
            RecoveryPath.PasswordReset,
            ["identify-account", "reset-password"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["reset-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"]),
            },
            AccountContractOutcome.CanBeFullySecured),
        new(
            "github-password-reset-blocked-by-email",
            "github.com/consumer-account-recovery",
            "Password reset remains blocked until the dependent primary email account is secured.",
            RecoveryPath.PasswordReset,
            ["identify-account", "reset-password"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["reset-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"], IsInitiallyBlocked: true),
            },
            AccountContractOutcome.BlockedByDependency),
        new(
            "github-mfa-device-unavailable",
            "github.com/consumer-account-recovery",
            "MFA review pauses for user action when a recovery factor is unavailable.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account", "change-password", "review-mfa"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["change-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"]),
                ["review-mfa"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["change-password"], IsInitiallyBlocked: true),
            },
            AccountContractOutcome.ManualRecoveryRequired),
        new(
            "github-reset-link-expired",
            "github.com/consumer-account-recovery",
            "An expired reset link blocks password reset until the user requests a fresh link.",
            RecoveryPath.PasswordReset,
            ["identify-account", "reset-password"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["reset-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"], IsInitiallyBlocked: true),
            },
            AccountContractOutcome.BlockedByDependency),
        new(
            "github-required-action-fails",
            "github.com/consumer-account-recovery",
            "A required password-change failure prevents the account from being represented as fully secured.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account", "change-password"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["change-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account"], CreatesUnresolvedRisk: true),
            },
            AccountContractOutcome.NotFullySecuredWithAcceptedRisk),
        new(
            "github-access-cannot-be-restored",
            "github.com/consumer-account-recovery",
            "Manual recovery records that account access cannot currently be restored.",
            RecoveryPath.ManualRecovery,
            ["identify-account", "document-completion"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["document-completion"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Routine, ["review-mfa", "invalidate-sessions", "review-recovery-options"], CreatesUnresolvedRisk: true),
            },
            AccountContractOutcome.AccessCannotBeRestored),
        new(
            "github-manual-recovery-with-unresolved-risk",
            "github.com/consumer-account-recovery",
            "Manual recovery documents unsupported or unavailable controls as visible unresolved risk.",
            RecoveryPath.ManualRecovery,
            ["identify-account", "document-completion"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["identify-account"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
                ["document-completion"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Routine, ["review-mfa", "invalidate-sessions", "review-recovery-options"], CreatesUnresolvedRisk: true),
            },
            AccountContractOutcome.NotFullySecuredWithAcceptedRisk),
    ];

    public static ProviderContractValidationResult ValidateContractScenarios()
    {
        List<ProviderContractValidationDiagnostic> diagnostics = [];

        foreach (RecoveryWorkflowDefinition workflow in Workflows)
        {
            var scenarios = ContractScenarios
                .Where(scenario => string.Equals(scenario.WorkflowId, workflow.WorkflowId, StringComparison.Ordinal))
                .ToArray();
            diagnostics.AddRange(ProviderContractValidator.Validate(workflow, scenarios).Diagnostics);
        }

        return diagnostics.Count == 0 ? ProviderContractValidationResult.Valid : new ProviderContractValidationResult(diagnostics);
    }

    public static WorkflowValidationResult ValidateAll()
    {
        List<WorkflowValidationDiagnostic> diagnostics = [];

        foreach (RecoveryWorkflowDefinition workflow in Workflows)
        {
            diagnostics.AddRange(RecoveryWorkflowValidator.Validate(workflow).Diagnostics);
        }

        return diagnostics.Count == 0 ? WorkflowValidationResult.Valid : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }
}
