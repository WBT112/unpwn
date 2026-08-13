namespace Unpwn.Core;

public enum TrustedDeviceDecision
{
    NotAnswered,
    Trusted,
    NotTrusted,
    Unsure,
}

public enum RecoveryWizardLifecycleStatus
{
    Active,
    Paused,
    Locked,
    StoppedForDeviceSafety,
    Cancelled,
    Completed,
    Archived,
    FollowUpRequired,
}

public enum RecoveryWizardTerminalOutcome
{
    Completed,
    Archived,
    FollowUpRequired,
}

public sealed record RecoveryWizardStepId
{
    private RecoveryWizardStepId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static RecoveryWizardStepId Welcome { get; } = new("welcome");

    public static RecoveryWizardStepId TrustedDeviceCheck { get; } = new("trusted-device-check");

    public static RecoveryWizardStepId TrustedDeviceGuidance { get; } = new("trusted-device-guidance");

    public static RecoveryWizardStepId VaultEntry { get; } = new("vault-entry");

    public static RecoveryWizardStepId IncidentIntake { get; } = new("incident-intake");

    public static RecoveryWizardStepId AccountInventory { get; } = new("account-inventory");

    public static RecoveryWizardStepId AccountTriage { get; } = new("account-triage");

    public static RecoveryWizardStepId RecoveryOverview { get; } = new("recovery-overview");

    public static RecoveryWizardStepId CredentialExport { get; } = new("credential-export");

    public static RecoveryWizardStepId CompletionPreflight { get; } = new("completion-preflight");

    public static RecoveryWizardStepId FinalReport { get; } = new("final-report");

    public static IReadOnlyList<RecoveryWizardStepId> All { get; } =
    [
        Welcome,
        TrustedDeviceCheck,
        TrustedDeviceGuidance,
        VaultEntry,
        IncidentIntake,
        AccountInventory,
        AccountTriage,
        RecoveryOverview,
        CredentialExport,
        CompletionPreflight,
        FinalReport,
    ];

    public static RecoveryWizardStepId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return All.SingleOrDefault(step => string.Equals(step.Value, value, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown recovery wizard step identifier.");
    }

    public override string ToString() => Value;
}

public sealed record RecoveryWizardState(
    Guid Id,
    RecoveryWizardStepId CurrentStep,
    RecoveryWizardStepId ResumeStep,
    RecoveryWizardLifecycleStatus Status,
    TrustedDeviceDecision TrustedDeviceDecision,
    bool HasVaultContext,
    long Revision,
    DateTimeOffset UpdatedAt)
{
    public bool IsTerminal => Status is
        RecoveryWizardLifecycleStatus.StoppedForDeviceSafety or
        RecoveryWizardLifecycleStatus.Cancelled or
        RecoveryWizardLifecycleStatus.Completed or
        RecoveryWizardLifecycleStatus.Archived or
        RecoveryWizardLifecycleStatus.FollowUpRequired;

    public static RecoveryWizardState Create(Guid id, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A recovery wizard requires a non-empty identifier.", nameof(id));
        }

        return new RecoveryWizardState(
            id,
            RecoveryWizardStepId.Welcome,
            RecoveryWizardStepId.Welcome,
            RecoveryWizardLifecycleStatus.Active,
            TrustedDeviceDecision.NotAnswered,
            HasVaultContext: false,
            Revision: 0,
            createdAt);
    }
}
