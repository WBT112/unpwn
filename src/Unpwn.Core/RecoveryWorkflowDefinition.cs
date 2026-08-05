namespace Unpwn.Core;

public sealed record RecoveryWorkflowDefinition(
    string WorkflowId,
    string ProviderId,
    string ProviderName,
    string SupportedAccountType,
    string WorkflowVersion,
    DateOnly VerifiedAt,
    IReadOnlyList<RecoveryLocationDefinition> RecoveryLocations,
    IReadOnlyList<RecoveryActionDefinition> Actions);

public sealed record RecoveryLocationDefinition(
    string Id,
    Uri Url,
    IReadOnlyList<string> ExpectedOrigins);

public sealed record RecoveryActionDefinition(
    string Id,
    RecoveryActionType Type,
    IReadOnlyList<RecoveryPath> RecoveryPaths,
    RecoveryActionRequirement Requirement,
    RecoveryActionImportance Importance,
    AutomationSupport AutomationSupport,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> CompletionCriteria)
{
    public bool IsRequired => Requirement == RecoveryActionRequirement.Required;

    public bool SupportsPath(RecoveryPath path) => RecoveryPaths.Contains(path);
}

public enum RecoveryActionType
{
    IdentifyAccount,
    ConfirmAccess,
    ChangePassword,
    ResetPassword,
    InvalidateSessions,
    ReviewTrustedDevices,
    ReviewMfa,
    ReviewRecoveryOptions,
    ReviewConnectedApplications,
    RevokeApplicationAccess,
    ReviewApiTokens,
    ManualRecovery,
    RecordUnresolvedRisk,
    DocumentCompletion,
}

public enum RecoveryPath
{
    AuthenticatedChange,
    PasswordReset,
    ManualRecovery,
}

public enum RecoveryActionRequirement
{
    Optional,
    Required,
}

public enum RecoveryActionImportance
{
    Routine = 1,
    Important = 3,
    Critical = 5,
}

public enum AutomationSupport
{
    None,
    Navigation,
    Assisted,
    Automated,
}
