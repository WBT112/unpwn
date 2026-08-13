using System.Text;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AccountInventoryRemovalTests
{
    [Fact]
    public async Task RemovingAccountNeedsNoObsoleteDependencyAcknowledgement()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.UpsertAsync(CreateRequest("Recovery mailbox"), CancellationToken.None)).Succeeded);
        time = time.AddSeconds(1);
        Assert.True((await service.UpsertAsync(CreateRequest("Critical account"), CancellationToken.None)).Succeeded);
        var mailbox = service.CurrentInventory!.Accounts.Single(account =>
            account.ProviderId == "Recovery mailbox");

        time = time.AddSeconds(1);
        var result = await service.RemoveAccountAsync(mailbox.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(service.CurrentInventory!.Accounts, account => account.Id == mailbox.Id);
        Assert.Single(service.CurrentInventory.Accounts);
        Assert.Single(session.LastSummaries);
    }

    private static AccountInventoryUpsertRequest CreateRequest(string provider) =>
        new(
            null,
            provider,
            provider,
            $"{provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            null);

    private sealed class TestEncryptedRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => true;

        public byte[]? StoredRecord { get; private set; }

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Validate();
            return Task.FromResult(StoredRecord?.ToArray());
        }

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Validate();
            StoredRecord = plaintext.ToArray();
            return Task.CompletedTask;
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inventory = writes.Single(write => write.Descriptor.RecordType == "account-state");
            StoredRecord = inventory.Plaintext.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class TestRecoverySessionService : IRecoverySessionWorkspaceCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } =
            RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Removal session",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch);

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public RecoveryAccountDashboardEntry[] LastSummaries { get; private set; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) => Conflict();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) => Conflict();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) => Conflict();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) => Conflict();

        public Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = CurrentSession ?? throw new InvalidOperationException();
            var updated = current.ReplaceAccounts(accounts, current.UpdatedAt.AddSeconds(1));
            return Task.FromResult(new PreparedRecoverySessionUpdate(
                updated,
                new VaultRecordDescriptor(
                    "recovery-session",
                    "8cf13bd9-2ccc-4b71-958a-439fefc90ac6",
                    1),
                Encoding.UTF8.GetBytes("session"),
                current.Revision));
        }

        public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
        {
            LastSummaries = update.State.Accounts;
            CurrentSession = update.State;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearForLock()
        {
        }

        private static Task<RecoverySessionOperationResult> Conflict() =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));
    }
}
