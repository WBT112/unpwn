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

public sealed record RecoveryActionGuidanceKeys(
    string TitleKey,
    string InstructionKey,
    string? WarningKey,
    string CompletionKey,
    IReadOnlyList<string> CompletionCriteriaKeys)
{
    public void Validate()
    {
        ValidateKey(TitleKey, nameof(TitleKey));
        ValidateKey(InstructionKey, nameof(InstructionKey));
        if (WarningKey is not null)
        {
            ValidateKey(WarningKey, nameof(WarningKey));
        }

        ValidateKey(CompletionKey, nameof(CompletionKey));
        ArgumentNullException.ThrowIfNull(CompletionCriteriaKeys);
        if (CompletionCriteriaKeys.Count == 0)
        {
            throw new InvalidOperationException("A recovery action requires at least one structured completion criterion key.");
        }

        foreach (var key in CompletionCriteriaKeys)
        {
            ValidateKey(key, nameof(CompletionCriteriaKeys));
        }

        if (CompletionCriteriaKeys.Distinct(StringComparer.Ordinal).Count() != CompletionCriteriaKeys.Count)
        {
            throw new InvalidOperationException("Recovery action completion criterion keys must be unique.");
        }
    }

    public static bool IsResourceKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            value[0] == '.' || value[^1] == '.')
        {
            return false;
        }

        return value.Split('.').All(segment =>
            segment.Length > 0 && segment.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
    }

    private static void ValidateKey(string value, string parameterName)
    {
        if (!IsResourceKey(value))
        {
            throw new InvalidOperationException($"{parameterName} must contain a stable resource key.");
        }
    }
}

public sealed record RecoveryActionDefinition(
    string Id,
    RecoveryActionType Type,
    IReadOnlyList<RecoveryPath> RecoveryPaths,
    RecoveryActionRequirement Requirement,
    RecoveryActionImportance Importance,
    AutomationSupport AutomationSupport,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> CompletionCriteria,
    RecoveryActionGuidanceKeys Guidance)
{
    public string? RecoveryLocationId { get; init; }

    public RecoveryActionDefinition(
        string id,
        RecoveryActionType type,
        IReadOnlyList<RecoveryPath> recoveryPaths,
        RecoveryActionRequirement requirement,
        RecoveryActionImportance importance,
        AutomationSupport automationSupport,
        IReadOnlyList<string> prerequisites,
        IReadOnlyList<string> completionCriteria)
        : this(
            id,
            type,
            recoveryPaths,
            requirement,
            importance,
            automationSupport,
            prerequisites,
            completionCriteria,
            new RecoveryActionGuidanceKeys(
                $"Workflow.Legacy.{id}.Title",
                $"Workflow.Legacy.{id}.Instruction",
                null,
                $"Workflow.Legacy.{id}.Completion",
                completionCriteria))
    {
    }

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
    ReviewSshAndSigningKeys,
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
