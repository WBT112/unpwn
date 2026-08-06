namespace Unpwn.Core;

public sealed record RecoveryWizardStepContract(
    RecoveryWizardStepId StepId,
    bool RequiresUnlockedVault,
    bool MayCollectSensitiveData,
    RecoveryWizardStepId SafeResumeStep,
    IReadOnlyList<RecoveryWizardStepId> AllowedNextSteps,
    IReadOnlyList<RecoveryWizardStepId> AllowedPreviousSteps);

public static class RecoveryWizardStepCatalog
{
    private static readonly IReadOnlyList<RecoveryWizardStepContract> Contracts =
    [
        Contract(
            RecoveryWizardStepId.Welcome,
            requiresUnlockedVault: false,
            mayCollectSensitiveData: false,
            RecoveryWizardStepId.Welcome,
            [RecoveryWizardStepId.TrustedDeviceCheck],
            []),
        Contract(
            RecoveryWizardStepId.TrustedDeviceCheck,
            requiresUnlockedVault: false,
            mayCollectSensitiveData: false,
            RecoveryWizardStepId.TrustedDeviceCheck,
            [RecoveryWizardStepId.TrustedDeviceGuidance, RecoveryWizardStepId.VaultEntry],
            [RecoveryWizardStepId.Welcome]),
        Contract(
            RecoveryWizardStepId.TrustedDeviceGuidance,
            requiresUnlockedVault: false,
            mayCollectSensitiveData: false,
            RecoveryWizardStepId.TrustedDeviceGuidance,
            [],
            [RecoveryWizardStepId.TrustedDeviceCheck]),
        Contract(
            RecoveryWizardStepId.VaultEntry,
            requiresUnlockedVault: false,
            mayCollectSensitiveData: false,
            RecoveryWizardStepId.VaultEntry,
            [RecoveryWizardStepId.IncidentIntake],
            [RecoveryWizardStepId.TrustedDeviceCheck]),
        Contract(
            RecoveryWizardStepId.IncidentIntake,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.IncidentIntake,
            [RecoveryWizardStepId.AccountInventory],
            [RecoveryWizardStepId.VaultEntry]),
        Contract(
            RecoveryWizardStepId.AccountInventory,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.AccountInventory,
            [RecoveryWizardStepId.IdentityReview],
            [RecoveryWizardStepId.IncidentIntake]),
        Contract(
            RecoveryWizardStepId.IdentityReview,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.IdentityReview,
            [RecoveryWizardStepId.RecoveryPlan],
            [RecoveryWizardStepId.AccountInventory]),
        Contract(
            RecoveryWizardStepId.RecoveryPlan,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.RecoveryPlan,
            [
                RecoveryWizardStepId.AccountRecovery,
                RecoveryWizardStepId.CredentialExport,
                RecoveryWizardStepId.CompletionPreflight,
            ],
            [RecoveryWizardStepId.IdentityReview, RecoveryWizardStepId.AccountRecovery]),
        Contract(
            RecoveryWizardStepId.AccountRecovery,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.RecoveryPlan,
            [RecoveryWizardStepId.RecoveryPlan],
            [RecoveryWizardStepId.RecoveryPlan]),
        Contract(
            RecoveryWizardStepId.CredentialExport,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.CredentialExport,
            [RecoveryWizardStepId.CompletionPreflight, RecoveryWizardStepId.RecoveryPlan],
            [RecoveryWizardStepId.RecoveryPlan]),
        Contract(
            RecoveryWizardStepId.CompletionPreflight,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.CompletionPreflight,
            [RecoveryWizardStepId.FinalReport, RecoveryWizardStepId.RecoveryPlan, RecoveryWizardStepId.CredentialExport],
            [RecoveryWizardStepId.CredentialExport, RecoveryWizardStepId.RecoveryPlan]),
        Contract(
            RecoveryWizardStepId.FinalReport,
            requiresUnlockedVault: true,
            mayCollectSensitiveData: true,
            RecoveryWizardStepId.CompletionPreflight,
            [],
            [RecoveryWizardStepId.CompletionPreflight]),
    ];

    public static IReadOnlyList<RecoveryWizardStepContract> All => Contracts;

    public static RecoveryWizardStepContract Get(RecoveryWizardStepId stepId)
    {
        ArgumentNullException.ThrowIfNull(stepId);

        return Contracts.Single(contract => contract.StepId == stepId);
    }

    private static RecoveryWizardStepContract Contract(
        RecoveryWizardStepId stepId,
        bool requiresUnlockedVault,
        bool mayCollectSensitiveData,
        RecoveryWizardStepId safeResumeStep,
        IReadOnlyList<RecoveryWizardStepId> allowedNextSteps,
        IReadOnlyList<RecoveryWizardStepId> allowedPreviousSteps) =>
        new(
            stepId,
            requiresUnlockedVault,
            mayCollectSensitiveData,
            safeResumeStep,
            allowedNextSteps,
            allowedPreviousSteps);
}
