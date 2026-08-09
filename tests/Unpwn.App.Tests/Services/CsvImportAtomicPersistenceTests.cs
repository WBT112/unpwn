using System.Text;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class CsvImportAtomicPersistenceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportDuplicatesAsSeparateAccountsSucceedsThroughAtomicProjectionPath()
    {
        var time = StartedAt;
        var store = new SuccessfulBatchRecordStore();
        var session = new TestSessionProjectionService(
            RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic CSV import",
                RecoveryIncidentIntake.Empty,
                StartedAt),
            () => time);
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        var candidates = CreateDuplicateCandidates();

        var result = await service.ImportAsync(
            candidates,
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, service.CurrentInventory?.Accounts.Length);
        Assert.Equal(2, session.CurrentSession?.Accounts.Length);
        Assert.True(session.PreparedProjectionCommitted);
        Assert.Equal(2, store.LastBatchWrites.Count);
        Assert.Contains(store.LastBatchWrites, write => write.Descriptor.RecordType == "account-state");
        Assert.Contains(store.LastBatchWrites, write => write.Descriptor.RecordType == "recovery-session");
    }

    private static IReadOnlyList<ImportAccountCandidate> CreateDuplicateCandidates()
    {
        const string csv = "service,login\nMail,person@example.invalid\nMail,person@example.invalid\n";
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv));
        return CsvAccountImportService.CreatePreview(
                new StringReader(csv),
                analysis.SuggestedMapping)
            .Candidates;
    }

    private sealed class SuccessfulBatchRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => true;

        public IReadOnlyList<VaultRecordWrite> LastBatchWrites { get; private set; } = [];

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(null);
        }

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastBatchWrites = [.. writes];
            return Task.CompletedTask;
        }
    }

    private sealed class TestSessionProjectionService(
        RecoverySessionWorkspace currentSession,
        Func<DateTimeOffset> clock) :
        IRecoverySessionService,
        IRecoverySessionProjectionCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } = currentSession;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public bool PreparedProjectionCommitted { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void ClearForLock()
        {
        }

        public Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = CurrentSession ?? throw new InvalidOperationException();
            var updated = current.ReplaceAccounts(accounts, clock());
            return Task.FromResult(new PreparedRecoverySessionUpdate(
                updated,
                new VaultRecordDescriptor(
                    "recovery-session",
                    "0128a0f1-43ac-4701-a03d-a564306c2210",
                    1),
                Encoding.UTF8.GetBytes("session"),
                current.Revision));
        }

        public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
        {
            Assert.Equal(CurrentSession?.Revision, update.ExpectedRevision);
            CurrentSession = update.State;
            PreparedProjectionCommitted = true;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
