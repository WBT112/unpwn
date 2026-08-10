using System.Security.Cryptography;
using System.Text.Json;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class RecoveryWizardSessionService(DateTimeOffset? createdAt = null)
{
    private const string WizardRecordId = "0f654bae-1267-468a-bebf-90ee286e1d86";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
    private static readonly VaultRecordDescriptor WizardDescriptor = new(
        "recovery-session",
        WizardRecordId,
        1);

    public event EventHandler? StateChanged;

    public RecoveryWizardState Current { get; private set; } = RecoveryWizardOrchestrator.Start(
        Guid.NewGuid(),
        createdAt ?? DateTimeOffset.UtcNow);

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
        var next = RecoveryWizardOrchestrator.ConfirmVaultReady(Current, occurredAt);
        PersistState(vault, next);
        SetCurrent(next);
    }

    public void AttachExistingVault(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var persisted = TryRead(vault);
        var next = persisted is null
            ? RecoveryWizardOrchestrator.ConfirmVaultReady(Current, occurredAt)
            : Restore(persisted, occurredAt);
        PersistState(vault, next);
        SetCurrent(next);
    }

    public void CompleteIncidentIntake(RecoveryVault vault, DateTimeOffset occurredAt) =>
        ApplyPreparedTransition(vault, PrepareTransition(
            RecoverySessionWizardTransition.CompleteIncidentIntake,
            occurredAt));

    public void Pause(RecoveryVault vault, DateTimeOffset occurredAt) =>
        ApplyPreparedTransition(vault, PrepareTransition(RecoverySessionWizardTransition.Pause, occurredAt));

    public void Resume(RecoveryVault vault, DateTimeOffset occurredAt) =>
        ApplyPreparedTransition(vault, PrepareTransition(RecoverySessionWizardTransition.Resume, occurredAt));

    public void Archive(RecoveryVault vault, DateTimeOffset occurredAt) =>
        ApplyPreparedTransition(vault, PrepareTransition(RecoverySessionWizardTransition.Archive, occurredAt));

    public PreparedRecoveryWizardUpdate PrepareTransition(
        RecoverySessionWizardTransition transition,
        DateTimeOffset occurredAt)
    {
        var next = transition switch
        {
            RecoverySessionWizardTransition.CompleteIncidentIntake =>
                RecoveryWizardOrchestrator.Continue(
                    Current,
                    RecoveryWizardStepId.AccountInventory,
                    occurredAt),
            RecoverySessionWizardTransition.Pause =>
                RecoveryWizardOrchestrator.Pause(Current, occurredAt),
            RecoverySessionWizardTransition.Resume =>
                RecoveryWizardOrchestrator.Resume(Current, occurredAt),
            RecoverySessionWizardTransition.Archive =>
                RecoveryWizardOrchestrator.Archive(Current, occurredAt),
            RecoverySessionWizardTransition.Complete =>
                PrepareTerminalState(RecoveryWizardTerminalOutcome.Completed, occurredAt),
            RecoverySessionWizardTransition.CompleteWithFollowUp =>
                PrepareTerminalState(RecoveryWizardTerminalOutcome.FollowUpRequired, occurredAt),
            RecoverySessionWizardTransition.CompleteAndArchive =>
                PrepareTerminalState(RecoveryWizardTerminalOutcome.Archived, occurredAt),
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, "Unknown session transition."),
        };
        return PrepareState(next, Current.Revision);
    }

    private RecoveryWizardState PrepareTerminalState(
        RecoveryWizardTerminalOutcome outcome,
        DateTimeOffset occurredAt)
    {
        var preflight = Current.CurrentStep == RecoveryWizardStepId.CompletionPreflight
            ? Current
            : RecoveryWizardOrchestrator.BeginCompletionReview(Current, occurredAt);
        var report = RecoveryWizardOrchestrator.Continue(
            preflight,
            RecoveryWizardStepId.FinalReport,
            occurredAt);
        return RecoveryWizardOrchestrator.Finish(report, outcome, occurredAt);
    }

    public void CommitPreparedTransition(PreparedRecoveryWizardUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (Current.Revision != update.ExpectedRevision)
        {
            throw new InvalidOperationException("The recovery wizard changed before the prepared transition was committed.");
        }

        SetCurrent(update.State);
    }

    public void PrepareForLock(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var next = Current;
        if (Current.HasVaultContext &&
            Current.Status is RecoveryWizardLifecycleStatus.Active or RecoveryWizardLifecycleStatus.Paused)
        {
            next = RecoveryWizardOrchestrator.Lock(Current, occurredAt);
        }

        if (!next.HasVaultContext)
        {
            return;
        }

        PersistState(vault, next);
        if (!ReferenceEquals(next, Current) && next != Current)
        {
            SetCurrent(next);
        }
    }

    public void ResumeAfterUnlock(RecoveryVault vault, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var persisted = TryRead(vault)
            ?? throw new InvalidOperationException("The recovery wizard state is unavailable.");
        var next = Restore(persisted, occurredAt);
        PersistState(vault, next);
        SetCurrent(next);
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

    private void ApplyPreparedTransition(
        RecoveryVault vault,
        PreparedRecoveryWizardUpdate update)
    {
        ArgumentNullException.ThrowIfNull(vault);
        using (update)
        {
            vault.UpsertRecord(update.Descriptor, update.Plaintext.Span);
            CommitPreparedTransition(update);
        }
    }

    private static PreparedRecoveryWizardUpdate PrepareState(
        RecoveryWizardState state,
        long expectedRevision)
    {
        var plaintext = Serialize(state);
        return new PreparedRecoveryWizardUpdate(
            state,
            WizardDescriptor,
            plaintext,
            expectedRevision);
    }

    private static void PersistState(RecoveryVault vault, RecoveryWizardState state)
    {
        var plaintext = Serialize(state);
        try
        {
            vault.UpsertRecord(WizardDescriptor, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Serialize(RecoveryWizardState state)
    {
        var persisted = new PersistedRecoveryWizardState(
            state.Id,
            state.CurrentStep.Value,
            state.ResumeStep.Value,
            state.Status,
            state.HasVaultContext,
            state.Revision,
            state.UpdatedAt);
        return JsonSerializer.SerializeToUtf8Bytes(persisted, SerializerOptions);
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
