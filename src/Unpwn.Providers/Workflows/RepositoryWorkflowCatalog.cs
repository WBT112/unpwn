using Unpwn.Core;
using Unpwn.Core.Recovery.Workflows;

namespace Unpwn.Providers.Workflows;

public static class RepositoryWorkflowCatalog
{
    private const string GuidancePrefix = "Workflow.GitHub.Action";
    private const string GoogleGuidancePrefix = "Workflow.Google.Action";
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
                    ["https://github.com"]),
            ],
            [
                Required("identify-account-auth", RecoveryActionType.IdentifyAccount, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("change-password", RecoveryActionType.ChangePassword, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-auth"], "settings"),
                Required("review-mfa-auth", RecoveryActionType.ReviewMfa, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["change-password"], "settings"),
                Required("invalidate-sessions-auth", RecoveryActionType.InvalidateSessions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "settings"),
                Required("review-recovery-options-auth", RecoveryActionType.ReviewRecoveryOptions, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "settings"),
                Required("review-connected-apps-auth", RecoveryActionType.ReviewConnectedApplications, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "settings"),
                Required("review-api-tokens-auth", RecoveryActionType.ReviewApiTokens, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "settings"),
                Required("document-completion-auth", RecoveryActionType.DocumentCompletion, AuthenticatedPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-mfa-auth", "invalidate-sessions-auth", "review-recovery-options-auth", "review-connected-apps-auth", "review-api-tokens-auth"]),

                Required("identify-account-reset", RecoveryActionType.IdentifyAccount, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("reset-password", RecoveryActionType.ResetPassword, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-reset"], "password-reset"),
                Required("review-mfa-reset", RecoveryActionType.ReviewMfa, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["reset-password"], "settings"),
                Required("invalidate-sessions-reset", RecoveryActionType.InvalidateSessions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "settings"),
                Required("review-recovery-options-reset", RecoveryActionType.ReviewRecoveryOptions, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "settings"),
                Required("review-connected-apps-reset", RecoveryActionType.ReviewConnectedApplications, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "settings"),
                Required("review-api-tokens-reset", RecoveryActionType.ReviewApiTokens, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "settings"),
                Required("document-completion-reset", RecoveryActionType.DocumentCompletion, PasswordResetPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-mfa-reset", "invalidate-sessions-reset", "review-recovery-options-reset", "review-connected-apps-reset", "review-api-tokens-reset"]),

                Required("identify-account-manual", RecoveryActionType.IdentifyAccount, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                Required("manual-recovery", RecoveryActionType.ManualRecovery, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, ["identify-account-manual"]),
                Required("document-completion-manual", RecoveryActionType.DocumentCompletion, ManualPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["manual-recovery"]),
            ]),
        new(
            "google.com/consumer-account-recovery",
            "google.com",
            "Google",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 10),
            [
                new(
                    "security",
                    new Uri("https://myaccount.google.com/security"),
                    ["https://myaccount.google.com"]),
                new(
                    "account-recovery",
                    new Uri("https://accounts.google.com/signin/recovery"),
                    ["https://accounts.google.com"]),
            ],
            [
                GoogleRequired("identify-account-auth", RecoveryActionType.IdentifyAccount, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                GoogleRequired("change-password", RecoveryActionType.ChangePassword, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-auth"], "security"),
                GoogleRequired("review-devices-auth", RecoveryActionType.ReviewTrustedDevices, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "security"),
                GoogleRequired("review-mfa-auth", RecoveryActionType.ReviewMfa, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["change-password"], "security"),
                GoogleRequired("review-recovery-options-auth", RecoveryActionType.ReviewRecoveryOptions, AuthenticatedPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["change-password"], "security"),
                GoogleRequired("review-connected-apps-auth", RecoveryActionType.ReviewConnectedApplications, AuthenticatedPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["change-password"], "security"),
                GoogleRequired("document-completion-auth", RecoveryActionType.DocumentCompletion, AuthenticatedPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-devices-auth", "review-mfa-auth", "review-recovery-options-auth", "review-connected-apps-auth"]),

                GoogleRequired("identify-account-reset", RecoveryActionType.IdentifyAccount, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                GoogleRequired("reset-password", RecoveryActionType.ResetPassword, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-reset"], "account-recovery"),
                GoogleRequired("review-devices-reset", RecoveryActionType.ReviewTrustedDevices, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "security"),
                GoogleRequired("review-mfa-reset", RecoveryActionType.ReviewMfa, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["reset-password"], "security"),
                GoogleRequired("review-recovery-options-reset", RecoveryActionType.ReviewRecoveryOptions, PasswordResetPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["reset-password"], "security"),
                GoogleRequired("review-connected-apps-reset", RecoveryActionType.ReviewConnectedApplications, PasswordResetPath, RecoveryActionImportance.Important, AutomationSupport.Navigation, ["reset-password"], "security"),
                GoogleRequired("document-completion-reset", RecoveryActionType.DocumentCompletion, PasswordResetPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["review-devices-reset", "review-mfa-reset", "review-recovery-options-reset", "review-connected-apps-reset"]),

                GoogleRequired("identify-account-manual", RecoveryActionType.IdentifyAccount, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.None, []),
                GoogleRequired("manual-recovery", RecoveryActionType.ManualRecovery, ManualPath, RecoveryActionImportance.Critical, AutomationSupport.Navigation, ["identify-account-manual"], "account-recovery"),
                GoogleRequired("document-completion-manual", RecoveryActionType.DocumentCompletion, ManualPath, RecoveryActionImportance.Routine, AutomationSupport.None, ["manual-recovery"]),
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
        new(
            "google-authenticated-change-available",
            "google.com/consumer-account-recovery",
            "Authenticated password change is followed by explicit device, session, MFA, passkey, recovery-channel, and connected-application review.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account-auth", "change-password", "review-devices-auth", "review-mfa-auth", "review-recovery-options-auth", "review-connected-apps-auth", "document-completion-auth"],
            Expectations(
                Expected("identify-account-auth", RecoveryActionImportance.Critical, []),
                Expected("change-password", RecoveryActionImportance.Critical, ["identify-account-auth"]),
                Expected("review-devices-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("review-mfa-auth", RecoveryActionImportance.Critical, ["change-password"]),
                Expected("review-recovery-options-auth", RecoveryActionImportance.Critical, ["change-password"]),
                Expected("review-connected-apps-auth", RecoveryActionImportance.Important, ["change-password"]),
                Expected("document-completion-auth", RecoveryActionImportance.Routine, ["review-devices-auth", "review-mfa-auth", "review-recovery-options-auth", "review-connected-apps-auth"])),
            AccountContractOutcome.CanBeFullySecured),
        new(
            "google-password-reset-through-secured-recovery-channel",
            "google.com/consumer-account-recovery",
            "Password reset proceeds only after the recovery email account or other recovery channel has already been secured.",
            RecoveryPath.PasswordReset,
            ["identify-account-reset", "reset-password", "review-devices-reset", "review-mfa-reset", "review-recovery-options-reset", "review-connected-apps-reset", "document-completion-reset"],
            Expectations(
                Expected("identify-account-reset", RecoveryActionImportance.Critical, []),
                Expected("reset-password", RecoveryActionImportance.Critical, ["identify-account-reset"]),
                Expected("review-devices-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("review-mfa-reset", RecoveryActionImportance.Critical, ["reset-password"]),
                Expected("review-recovery-options-reset", RecoveryActionImportance.Critical, ["reset-password"]),
                Expected("review-connected-apps-reset", RecoveryActionImportance.Important, ["reset-password"]),
                Expected("document-completion-reset", RecoveryActionImportance.Routine, ["review-devices-reset", "review-mfa-reset", "review-recovery-options-reset", "review-connected-apps-reset"])),
            AccountContractOutcome.CanBeFullySecured),
        new(
            "google-password-reset-blocked-by-recovery-email",
            "google.com/consumer-account-recovery",
            "Password reset remains blocked while the dependent recovery email account is not yet secured.",
            RecoveryPath.PasswordReset,
            ["identify-account-reset", "reset-password"],
            Expectations(
                Expected("identify-account-reset", RecoveryActionImportance.Critical, []),
                Expected("reset-password", RecoveryActionImportance.Critical, ["identify-account-reset"], blocked: true)),
            AccountContractOutcome.BlockedByDependency),
        new(
            "google-unrecognized-device-remains-risk",
            "google.com/consumer-account-recovery",
            "An unrecognized device or session remains visible as unresolved risk when it cannot be signed out.",
            RecoveryPath.AuthenticatedChange,
            ["identify-account-auth", "change-password", "review-devices-auth"],
            Expectations(
                Expected("identify-account-auth", RecoveryActionImportance.Critical, []),
                Expected("change-password", RecoveryActionImportance.Critical, ["identify-account-auth"]),
                Expected("review-devices-auth", RecoveryActionImportance.Important, ["change-password"], risk: true)),
            AccountContractOutcome.NotFullySecuredWithAcceptedRisk),
        new(
            "google-access-cannot-be-restored",
            "google.com/consumer-account-recovery",
            "The official account-recovery flow may require provider checks or waiting and can end without restored access.",
            RecoveryPath.ManualRecovery,
            ["identify-account-manual", "manual-recovery", "document-completion-manual"],
            Expectations(
                Expected("identify-account-manual", RecoveryActionImportance.Critical, []),
                Expected("manual-recovery", RecoveryActionImportance.Critical, ["identify-account-manual"], risk: true),
                Expected("document-completion-manual", RecoveryActionImportance.Routine, ["manual-recovery"], risk: true)),
            AccountContractOutcome.AccessCannotBeRestored),
    ];

    public static ProviderContractValidationResult ValidateContractScenarios()
    {
        List<ProviderContractValidationDiagnostic> diagnostics = [];

        foreach (var workflow in Workflows)
        {
            var scenarios = ContractScenarios
                .Where(scenario => string.Equals(scenario.WorkflowId, workflow.WorkflowId, StringComparison.Ordinal))
                .ToArray();
            diagnostics.AddRange(ProviderContractValidator.Validate(workflow, scenarios).Diagnostics);
        }

        return diagnostics.Count == 0
            ? ProviderContractValidationResult.Valid
            : new ProviderContractValidationResult(diagnostics);
    }

    public static WorkflowValidationResult ValidateAll(DateOnly? currentDate = null)
    {
        List<WorkflowValidationDiagnostic> diagnostics = [];

        foreach (var workflow in Workflows)
        {
            diagnostics.AddRange(RecoveryWorkflowValidator.Validate(workflow, currentDate).Diagnostics);
        }

        return diagnostics.Count == 0
            ? WorkflowValidationResult.Valid
            : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }

    private static RecoveryActionDefinition Required(
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> paths,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites,
        string? recoveryLocationId = null) =>
        Required(
            GuidancePrefix,
            id,
            type,
            paths,
            importance,
            automationSupport,
            prerequisites,
            recoveryLocationId);

    private static RecoveryActionDefinition GoogleRequired(
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> paths,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites,
        string? recoveryLocationId = null) =>
        Required(
            GoogleGuidancePrefix,
            id,
            type,
            paths,
            importance,
            automationSupport,
            prerequisites,
            recoveryLocationId);

    private static RecoveryActionDefinition Required(
        string guidancePrefix,
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> paths,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites,
        string? recoveryLocationId)
    {
        var prefix = $"{guidancePrefix}.{id}";
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
                criteria))
        {
            RecoveryLocationId = recoveryLocationId,
        };
    }

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
