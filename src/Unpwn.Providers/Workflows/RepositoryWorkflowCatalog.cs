using Unpwn.Core;
using Unpwn.Core.Recovery.Workflows;

namespace Unpwn.Providers.Workflows;

public static class RepositoryWorkflowCatalog
{
    private static readonly RecoveryPath[] AuthenticatedPath = [RecoveryPath.AuthenticatedChange];
    private static readonly RecoveryPath[] PasswordResetPath = [RecoveryPath.PasswordReset];
    private static readonly RecoveryPath[] ManualPath = [RecoveryPath.ManualRecovery];

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
                Required("identify-account-auth", RecoveryActionType.IdentifyAccount, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.None, [], "The affected account has been identified."),
                Required("change-password", RecoveryActionType.ChangePassword, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-auth"], "The account password has been changed through the official authenticated flow."),
                Required("review-mfa-auth", RecoveryActionType.ReviewMfa, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["change-password"], "MFA methods and recovery codes have been reviewed or an unresolved risk has been recorded."),
                Required("invalidate-sessions-auth", RecoveryActionType.InvalidateSessions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "Active sessions and trusted devices have been reviewed and revoked where appropriate."),
                Required("review-recovery-options-auth", RecoveryActionType.ReviewRecoveryOptions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "Recovery methods have been reviewed and corrected where possible."),
                Required("review-connected-apps-auth", RecoveryActionType.ReviewConnectedApplications, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "Connected applications have been reviewed and unauthorized access has been revoked."),
                Required("review-api-tokens-auth", RecoveryActionType.ReviewApiTokens, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "Personal access tokens and SSH or signing keys have been reviewed and rotated or revoked where appropriate."),
                Required("document-completion-auth", RecoveryActionType.DocumentCompletion, AuthenticatedPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-mfa-auth", "invalidate-sessions-auth", "review-recovery-options-auth", "review-connected-apps-auth", "review-api-tokens-auth"], "Completion records remaining unresolved risks without claiming unsupported security guarantees."),

                Required("identify-account-reset", RecoveryActionType.IdentifyAccount, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, [], "The affected account and its secured reset channel have been identified."),
                Required("reset-password", RecoveryActionType.ResetPassword, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-reset"], "The account password has been reset through the official provider flow."),
                Required("review-mfa-reset", RecoveryActionType.ReviewMfa, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["reset-password"], "MFA methods and recovery codes have been reviewed or an unresolved risk has been recorded."),
                Required("invalidate-sessions-reset", RecoveryActionType.InvalidateSessions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "Active sessions and trusted devices have been reviewed and revoked where appropriate."),
                Required("review-recovery-options-reset", RecoveryActionType.ReviewRecoveryOptions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "Recovery methods have been reviewed and corrected where possible."),
                Required("review-connected-apps-reset", RecoveryActionType.ReviewConnectedApplications, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "Connected applications have been reviewed and unauthorized access has been revoked."),
                Required("review-api-tokens-reset", RecoveryActionType.ReviewApiTokens, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "Personal access tokens and SSH or signing keys have been reviewed and rotated or revoked where appropriate."),
                Required("document-completion-reset", RecoveryActionType.DocumentCompletion, PasswordResetPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-mfa-reset", "invalidate-sessions-reset", "review-recovery-options-reset", "review-connected-apps-reset", "review-api-tokens-reset"], "Completion records remaining unresolved risks without claiming unsupported security guarantees."),

                Required("identify-account-manual", RecoveryActionType.IdentifyAccount, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, [], "The affected account and available recovery evidence have been identified."),
                Required("manual-recovery", RecoveryActionType.ManualRecovery, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["identify-account-manual"], "The provider's manual recovery outcome has been recorded."),
                Required("document-completion-manual", RecoveryActionType.DocumentCompletion, ManualPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["manual-recovery"], "The manual recovery result and all unresolved risks have been recorded.")
            ])
    ];

    public static IReadOnlyList<ProviderContractScenario> ContractScenarios { get; } =
    [
        new(
            "github-authenticated-change-available",
            "github.com/consumer-account-recovery",
            "Authenticated password change is available after the user confirms account access.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account-auth", "change-password", "review-mfa-auth", "invalidate-sessions-auth", "review-recovery-options-auth", "review-connected-apps-auth", "review-api-tokens-auth", "document-completion-auth"],
            Expectations(
                Expected("identify-account-auth", RecoveryActionImportance.Critical, []),
                Expected("change-password", RecoveryActionImportance.Critical, ["identify-account-auth"]),
                Expected("review-mfa-auth", RecoveryActionImportance.Critical, ["change-password"]),
                Expected("invalidate-sessions-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("review-recovery-options-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("review-connected-apps-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("review-api-tokens-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("document-completion-auth", RecoveryActionImportance.Routine, ["review-mfa-auth", "invalidate-sessions-auth", "review-recovery-options-auth", "review-connected-apps-auth", "review-api-tokens-auth"])),
            AccountContractOutcome.CanBeFullySecured),
        new(
            "github-password-reset-through-secured-email",
            "github.com/consumer-account-recovery",
            "Password reset can proceed after the primary email dependency has already been secured.",
            RecoveryPath.PasswordReset,
            ["identify-account-reset", "reset-password", "review-mfa-reset", "invalidate-sessions-reset", "review-recovery-options-reset", "review-connected-apps-reset", "review-api-tokens-reset", "document-completion-reset"],
            Expectations(
                Expected("identify-account-reset", RecoveryActionImportance.Critical, []),
                Expected("reset-password", RecoveryActionImportance.Critical, ["identify-account-reset"]),
                Expected("review-mfa-reset", RecoveryActionImportance.Critical, ["reset-password"]),
                Expected("invalidate-sessions-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("review-recovery-options-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("review-connected-apps-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("review-api-tokens-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("document-completion-reset", RecoveryActionImportance.Routine, ["review-mfa-reset", "invalidate-sessions-reset", "review-recovery-options-reset", "review-connected-apps-reset", "review-api-tokens-reset"])),
            AccountContractOutcome.CanBeFullySecured),
        new(
            "github-password-reset-blocked-by-email",
            "github.com/consumer-account-recovery",
            "Password reset remains blocked until the dependent primary email account is secured.",
            RecoveryPath.PasswordReset,
            ["identify-account-reset", "reset-password"],
            Expectations(
                Expected("identify-account-reset", RecoveryActionImportance.Critical, []),
                Expected("reset-password", RecoveryActionImportance.Critical, ["identify-account-reset"], blocked: true)),
            AccountContractOutcome.BlockedByDependency),
        new(
            "github-mfa-device-unavailable",
            "github.com/consumer-account-recovery",
            "MFA review pauses for user action when a recovery factor is unavailable.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account-auth", "change-password", "review-mfa-auth"],
            Expectations(
                Expected("identify-account-auth", RecoveryActionImportance.Critical, []),
                Expected("change-password", RecoveryActionImportance.Critical, ["identify-account-auth"]),
                Expected("review-mfa-auth", RecoveryActionImportance.Critical, ["change-password"], blocked: true)),
            AccountContractOutcome.ManualRecoveryRequired),
        new(
            "github-reset-link-expired",
            "github.com/consumer-account-recovery",
            "An expired reset link blocks password reset until the user requests a fresh link.",
            RecoveryPath.PasswordReset,
            ["identify-account-reset", "reset-password"],
            Expectations(
                Expected("identify-account-reset", RecoveryActionImportance.Critical, []),
                Expected("reset-password", RecoveryActionImportance.Critical, ["identify-account-reset"], blocked: true)),
            AccountContractOutcome.BlockedByDependency),
        new(
            "github-required-action-fails",
            "github.com/consumer-account-recovery",
            "A required password-change failure prevents the account from being represented as fully secured.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account-auth", "change-password"],
            Expectations(
                Expected("identify-account-auth", RecoveryActionImportance.Critical, []),
                Expected("change-password", RecoveryActionImportance.Critical, ["identify-account-auth"], risk: true)),
            AccountContractOutcome.NotFullySecuredWithAcceptedRisk),
        new(
            "github-access-cannot-be-restored",
            "github.com/consumer-account-recovery",
            "Manual recovery records that account access cannot currently be restored.",
            RecoveryPath.ManualRecovery,
            ["identify-account-manual", "manual-recovery", "document-completion-manual"],
            Expectations(
                Expected("identify-account-manual", RecoveryActionImportance.Critical, []),
                Expected("manual-recovery", RecoveryActionImportance.Critical, ["identify-account-manual"], risk: true),
                Expected("document-completion-manual", RecoveryActionImportance.Routine, ["manual-recovery"], risk: true)),
            AccountContractOutcome.AccessCannotBeRestored),
        new(
            "github-manual-recovery-with-unresolved-risk",
            "github.com/consumer-account-recovery",
            "Manual recovery documents unsupported or unavailable controls as visible unresolved risk.",
            RecoveryPath.ManualRecovery,
            ["identify-account-manual", "manual-recovery", "document-completion-manual"],
            Expectations(
                Expected("identify-account-manual", RecoveryActionImportance.Critical, []),
                Expected("manual-recovery", RecoveryActionImportance.Critical, ["identify-account-manual"], risk: true),
                Expected("document-completion-manual", RecoveryActionImportance.Routine, ["manual-recovery"], risk: true)),
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

    public static WorkflowValidationResult ValidateAll(DateOnly? currentDate = null)
    {
        List<WorkflowValidationDiagnostic> diagnostics = [];

        foreach (RecoveryWorkflowDefinition workflow in Workflows)
        {
            diagnostics.AddRange(RecoveryWorkflowValidator.Validate(workflow, currentDate).Diagnostics);
        }

        return diagnostics.Count == 0 ? WorkflowValidationResult.Valid : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }

    private static RecoveryActionDefinition Required(
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> paths,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites,
        string completionCriterion) =>
        new(
            id,
            type,
            paths,
            RecoveryActionRequirement.Required,
            importance,
            automationSupport,
            prerequisites,
            [completionCriterion]);

    private static ContractExpectationEntry Expected(
        string id,
        RecoveryActionImportance importance,
        string[] prerequisites,
        bool blocked = false,
        bool risk = false) =>
        new(id, importance, prerequisites, blocked, risk);

    private static Dictionary<string, ContractActionExpectation> Expectations(
        params ContractExpectationEntry[] actions) =>
        actions.ToDictionary(
            action => action.Id,
            action => new ContractActionExpectation(
                RecoveryActionRequirement.Required,
                action.Importance,
                action.Prerequisites,
                action.Blocked,
                action.Risk),
            StringComparer.Ordinal);

    private sealed record ContractExpectationEntry(
        string Id,
        RecoveryActionImportance Importance,
        string[] Prerequisites,
        bool Blocked,
        bool Risk);
}
