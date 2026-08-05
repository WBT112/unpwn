namespace Unpwn.Core.Recovery.Workflows;

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
    RecoveryPath RecoveryPath,
    RecoveryActionRequirement Requirement,
    RecoveryActionImportance Importance,
    AutomationSupport AutomationSupport,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> CompletionCriteria);

public enum RecoveryActionType
{
    IdentifyAccount,
    ChangePassword,
    InvalidateSessions,
    ReviewMfa,
    ReviewRecoveryOptions,
    ReviewConnectedApplications,
    ManualRecovery,
    DocumentCompletion
}

public enum RecoveryPath
{
    AuthenticatedChange,
    PasswordReset,
    ManualRecovery
}

public enum RecoveryActionRequirement
{
    Required,
    Optional
}

public enum RecoveryActionImportance
{
    Critical,
    Important,
    Routine
}

public enum AutomationSupport
{
    None,
    Navigation,
    Assisted,
    Automated
}
