using System.Security.Cryptography;
using System.Text.Json;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class RecoveryWizardSessionService
{
    private const string WizardRecordId = "0f654bae-1267-468a-bebf-90ee286e1d86";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
    private static readonly VaultRecordDescriptor WizardDescriptor = new(
        "recovery-session",
        WizardRecordId,
        1);

    public RecoveryWizardSessionService(DateTimeOffset? createdAt = null)
    {
        Current = RecoveryWizardOrchestrator.Start(
            Guid.NewGuid(),
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public event EventHandler? StateChanged;

    public RecoveryWizardState Current { get; private set; }

    public void Reset(DateTimeOffset occurredAt) =>
        SetCurrent(RecoveryWizardOrchestrator.Start(Guid.NewGuid(), occurredAt));

    public void BeginTrustedDeviceCheck(DateTimeOffset occurredAt)
    {
        if (Current.IsTerminal || Current.HasVaultContext)
        {
            Reset(occurredAt);
        }

        if (Current.CurrentStep == RecoveryWizardStepId.Welcome)
        {
            SetCurrent(RecoveryWizardOrchestrator.Continue(
                Current,
                RecoveryWizardStepId.TrustedDeviceCheck,
                occurredAt));
        }
    }

    public void RecordTrustedDeviceDecision(
        TrustedDeviceDecision decision,
        DateTimeOffset occurredAt) =>
        SetCurrent(RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            Current,
            decision,
            occurredAt));

    public void StopAfterTrustedDeviceGuidance(DateTimeOffset occurredAt) =>
        SetCurrent(RecoveryWizardOrchestrator.StopAfterTrustedDeviceGuidance(Current, occurredAt));

    public void ReturnToTrustedDeviceCheck(DateTimeOffset occurredAt) =>
        SetCurrent(RecoveryWizardOrchestrator.GoBack(
            Current,
            RecoveryWizardStepId.TrustedDeviceCheck,
            occurredAt));

    public void AttachNewVault(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        SetCurrent(RecoveryWizardOrchestrator.ConfirmVaultReady(Current, occurredAt));
        Persist(vault);
    }

    public void AttachExistingVault(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var persisted = TryRead(vault);
        if (persisted is null)
        {
            SetCurrent(RecoveryWizardOrchestrator.ConfirmVaultReady(Current, occurredAt));
            Persist(vault);
            return;
        }

        var restored = Restore(persisted, occurredAt);
        SetCurrent(restored);
        Persist(vault);
    }

    public void PrepareForLock(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        if (Current.HasVaultContext &&
            Current.Status is RecoveryWizardLifecycleStatus.Active or RecoveryWizardLifecycleStatus.Paused)
        {
            SetCurrent(RecoveryWizardOrchestrator.Lock(Current, occurredAt));
        }

        if (Current.HasVaultContext)
        {
            Persist(vault);
        }
    }

    public void ResumeAfterUnlock(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var persisted = TryRead(vault);
        if (persisted is null)
        {
            throw new InvalidOperationException("The recovery wizard state is unavailable.");
        }

        SetCurrent(Restore(persisted, occurredAt));
        Persist(vault);
    }

    private static RecoveryWizardState Restore(
        PersistedRecoveryWizardState persisted,
        DateTimeOffset occurredAt)
    {
        var currentStep = RecoveryWizardStepId.Parse(persisted.CurrentStep);
        var resumeStep = RecoveryWizardStepId.Parse(persisted.ResumeStep);
        if (!persisted.HasVaultContext || persisted.Id == Guid.Empty || persisted.Revision < 0)
        {
            throw new InvalidOperationException("The recovery wizard state is invalid.");
        }

        var restored = new RecoveryWizardState(
            persisted.Id,
            currentStep,
            resumeStep,
            persisted.Status,
            TrustedDeviceDecision.Trusted,
            HasVaultContext: true,
            persisted.Revision,
            persisted.UpdatedAt);
        var effectiveTime = occurredAt < restored.UpdatedAt
            ? restored.UpdatedAt
            : occurredAt;

        return restored.Status switch
        {
            RecoveryWizardLifecycleStatus.Locked or RecoveryWizardLifecycleStatus.Paused =>
                RecoveryWizardOrchestrator.Resume(restored, effectiveTime),
            RecoveryWizardLifecycleStatus.Active => restored with
            {
                CurrentStep = restored.ResumeStep,
                Revision = restored.Revision + 1,
                UpdatedAt = effectiveTime,
            },
            _ => restored with
            {
                TrustedDeviceDecision = TrustedDeviceDecision.Trusted,
                UpdatedAt = effectiveTime,
            },
        };
    }

    private static PersistedRecoveryWizardState? TryRead(RecoveryVault vault)
    {
        using var record = vault.ReadRecord(WizardDescriptor.RecordType, WizardDescriptor.RecordId);
        return record is null
            ? null
            : JsonSerializer.Deserialize<PersistedRecoveryWizardState>(
                record.Plaintext.Span,
                SerializerOptions);
    }

    private void Persist(RecoveryVault vault)
    {
        var persisted = new PersistedRecoveryWizardState(
            Current.Id,
            Current.CurrentStep.Value,
            Current.ResumeStep.Value,
            Current.Status,
            Current.HasVaultContext,
            Current.Revision,
            Current.UpdatedAt);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(persisted, SerializerOptions);
        try
        {
            vault.UpsertRecord(WizardDescriptor, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void SetCurrent(RecoveryWizardState state)
    {
        Current = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record PersistedRecoveryWizardState(
        Guid Id,
        string CurrentStep,
        string ResumeStep,
        RecoveryWizardLifecycleStatus Status,
        bool HasVaultContext,
        long Revision,
        DateTimeOffset UpdatedAt);
}
