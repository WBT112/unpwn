using System.Text;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AtomicWorkspacePersistenceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 6, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionCreationDoesNotPublishSessionOrWizardWhenBatchWriteFails()
    {
        var store = new FailingBatchRecordStore();
        var wizard = new TestWizardCoordinator(CreateIncidentIntakeWizard());
        using var service = new RecoverySessionService(
            store,
            wizard,
            clock: () => StartedAt.AddMinutes(1));
        var originalWizard = wizard.CurrentWizard;

        var result = await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Incident",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoverySessionOperationFailureCode.IoFailure, result.FailureCode);
        Assert.Null(service.CurrentSession);
        Assert.Equal(originalWizard, wizard.CurrentWizard);
        Assert.False(wizard.PreparedTransitionCommitted);
        Assert.Equal(1, store.BatchWriteAttempts);
    }

    [Fact]
    public async Task InventoryMutationDoesNotPublishInventoryOrProjectionWhenBatchWriteFails()
    {
        var store = new FailingBatchRecordStore();
        var session = new TestSessionProjectionService(
            RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Incident",
                RecoveryIncidentIntake.Empty,
                StartedAt));
        using var service = new AccountInventoryService(
            store,
            session,
            clock: () => StartedAt.AddMinutes(1));

        var result = await service.UpsertAsync(
            new AccountInventoryUpsertRequest(
                AccountId: null,
                "example.test",
                "Example",
                "user@example.test",
                "https://example.test/account"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.IoFailure, result.FailureCode);
        Assert.Null(service.CurrentInventory);
        Assert.False(session.PreparedProjectionCommitted);
        Assert.Empty(session.CurrentSession!.Accounts);
        Assert.Equal(1, store.BatchWriteAttempts);
    }

    [Fact]
    public async Task CompletionPublishesSessionAndWizardOnlyAfterAtomicWriteSucceeds()
    {
        var store = new FailingBatchRecordStore { FailBatchWrites = false };
        var wizard = new TestWizardCoordinator(CreateIncidentIntakeWizard());
        using var service = new RecoverySessionService(
            store,
            wizard,
            clock: () => StartedAt.AddMinutes(1));
        var created = await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Incident",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None);
        Assert.True(created.Succeeded);
        var active = service.CurrentSession!;
        var activeWizard = wizard.CurrentWizard;
        var report = new RecoveryCompletionReport(
            active.Id,
            StartedAt.AddMinutes(1),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            []);
        var completion = new RecoveryCompletionRecord(
            RecoveryCompletionOutcome.Completed,
            StartedAt.AddMinutes(1),
            UnresolvedRiskExplicitlyAccepted: false,
            report);

        store.FailBatchWrites = true;
        var failed = await service.CompleteAsync(
            completion,
            active.Revision,
            CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Equal(RecoverySessionOperationFailureCode.IoFailure, failed.FailureCode);
        Assert.Same(active, service.CurrentSession);
        Assert.Equal(activeWizard, wizard.CurrentWizard);

        store.FailBatchWrites = false;
        var succeeded = await service.CompleteAsync(
            completion,
            active.Revision,
            CancellationToken.None);

        Assert.True(succeeded.Succeeded);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Completed, service.CurrentSession!.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Completed, wizard.CurrentWizard.Status);
        Assert.True(service.CurrentSession.IsReadOnly);
    }

    private static RecoveryWizardState CreateIncidentIntakeWizard()
    {
        var state = RecoveryWizardOrchestrator.Start(Guid.NewGuid(), StartedAt);
        state = RecoveryWizardOrchestrator.Continue(
            state,
            RecoveryWizardStepId.TrustedDeviceCheck,
            StartedAt.AddSeconds(1));
        state = RecoveryWizardOrchestrator.RecordTrustedDeviceDecision(
            state,
            TrustedDeviceDecision.Trusted,
            StartedAt.AddSeconds(2));
        return RecoveryWizardOrchestrator.ConfirmVaultReady(
            state,
            StartedAt.AddSeconds(3));
    }

    private sealed class FailingBatchRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => true;

        public int BatchWriteAttempts { get; private set; }

        public bool FailBatchWrites { get; set; } = true;

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken) =>
            throw new IOException("Synthetic single-write failure.");

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            BatchWriteAttempts++;
            return FailBatchWrites
                ? Task.FromException(new IOException("Synthetic batch-write failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class TestWizardCoordinator(RecoveryWizardState initialState) :
        IRecoveryWizardPersistenceCoordinator
    {
        public RecoveryWizardState CurrentWizard { get; private set; } = initialState;

        public bool PreparedTransitionCommitted { get; private set; }

        public void SetSessionDisplayName(string? sessionDisplayName)
        {
        }

        public PreparedRecoveryWizardUpdate PrepareTransition(
            RecoverySessionWizardTransition transition,
            DateTimeOffset occurredAt)
        {
            var next = transition switch
            {
                RecoverySessionWizardTransition.CompleteIncidentIntake =>
                    RecoveryWizardOrchestrator.Continue(
                        CurrentWizard,
                        RecoveryWizardStepId.AccountInventory,
                        occurredAt),
                RecoverySessionWizardTransition.Complete => Finish(
                    RecoveryWizardTerminalOutcome.Completed,
                    occurredAt),
                RecoverySessionWizardTransition.CompleteWithFollowUp => Finish(
                    RecoveryWizardTerminalOutcome.FollowUpRequired,
                    occurredAt),
                RecoverySessionWizardTransition.CompleteAndArchive => Finish(
                    RecoveryWizardTerminalOutcome.Archived,
                    occurredAt),
                _ => throw new NotSupportedException(),
            };
            return new PreparedRecoveryWizardUpdate(
                next,
                new VaultRecordDescriptor(
                    "recovery-session",
                    Guid.NewGuid().ToString("D"),
                    1),
                Encoding.UTF8.GetBytes("wizard"),
                CurrentWizard.Revision);
        }

        private RecoveryWizardState Finish(
            RecoveryWizardTerminalOutcome outcome,
            DateTimeOffset occurredAt)
        {
            var preflight = RecoveryWizardOrchestrator.BeginCompletionReview(
                CurrentWizard,
                occurredAt);
            var report = RecoveryWizardOrchestrator.Continue(
                preflight,
                RecoveryWizardStepId.FinalReport,
                occurredAt);
            return RecoveryWizardOrchestrator.Finish(report, outcome, occurredAt);
        }

        public void CommitPreparedTransition(PreparedRecoveryWizardUpdate update)
        {
            Assert.Equal(CurrentWizard.Revision, update.ExpectedRevision);
            CurrentWizard = update.State;
            PreparedTransitionCommitted = true;
        }
    }

    private sealed class TestSessionProjectionService(RecoverySessionWorkspace currentSession) :
        IRecoverySessionWorkspaceCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } = currentSession;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public bool PreparedProjectionCommitted { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ClearForLock()
        {
        }

        public Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            var current = CurrentSession!;
            var updated = current.ReplaceAccounts(accounts, StartedAt.AddMinutes(1));
            return Task.FromResult(new PreparedRecoverySessionUpdate(
                updated,
                new VaultRecordDescriptor(
                    "recovery-session",
                    Guid.NewGuid().ToString("D"),
                    1),
                Encoding.UTF8.GetBytes("session"),
                current.Revision));
        }

        public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
        {
            Assert.Equal(CurrentSession!.Revision, update.ExpectedRevision);
            CurrentSession = update.State;
            PreparedProjectionCommitted = true;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
