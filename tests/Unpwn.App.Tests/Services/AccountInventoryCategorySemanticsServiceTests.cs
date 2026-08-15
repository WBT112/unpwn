using System.Text;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AccountInventoryCategorySemanticsServiceTests
{
    [Fact]
    public async Task ServiceRejectsUnknownAsAnExplicitUserCategoryWithoutMutation()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestRecordStore();
        var session = new TestRecoverySession();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        Assert.True((await service.UpsertAsync(
            CreateUnknownAccountRequest(),
            CancellationToken.None)).Succeeded);
        var before = service.CurrentInventory!;
        var account = Assert.Single(before.Accounts);
        Assert.Equal(AccountRecoveryCategory.Unknown, account.SuggestedCategory);

        var result = await service.CategorizeAsync(
            account.Id,
            AccountRecoveryCategory.Unknown,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.InvalidInput, result.FailureCode);
        Assert.Equal(before.Revision, service.CurrentInventory!.Revision);
        var unchanged = Assert.Single(service.CurrentInventory.Accounts);
        Assert.Null(unchanged.ConfirmedCategory);
        Assert.True(unchanged.RequiresCategoryReview);
    }

    [Fact]
    public async Task ClearingAValidOverrideRestoresUnknownSuggestionAndNeedsReviewState()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestRecordStore();
        var session = new TestRecoverySession();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        Assert.True((await service.UpsertAsync(
            CreateUnknownAccountRequest(),
            CancellationToken.None)).Succeeded);
        var account = Assert.Single(service.CurrentInventory!.Accounts);
        time = time.AddMinutes(1);
        Assert.True((await service.CategorizeAsync(
            account.Id,
            AccountRecoveryCategory.Critical,
            CancellationToken.None)).Succeeded);
        var overridden = Assert.Single(service.CurrentInventory.Accounts);
        Assert.Equal(AccountRecoveryCategory.Critical, overridden.EffectiveCategory);
        Assert.NotNull(overridden.CategoryConfirmedRevision);

        time = time.AddMinutes(1);
        var cleared = await service.ClearCategoryOverrideAsync(
            account.Id,
            CancellationToken.None);

        Assert.True(cleared.Succeeded);
        var restored = Assert.Single(service.CurrentInventory!.Accounts);
        Assert.Equal(AccountRecoveryCategory.Unknown, restored.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.Unknown, restored.EffectiveCategory);
        Assert.Null(restored.ConfirmedCategory);
        Assert.Null(restored.CategoryConfirmedRevision);
        Assert.True(restored.RequiresCategoryReview);
        Assert.Equal(
            AccountRecoveryOrderReasonCode.UnknownCategory,
            service.CurrentRecoveryOrder?.Recommended?.ReasonCode);

        using var reloaded = new AccountInventoryService(store, session, () => time);
        await reloaded.InitializeAsync(CancellationToken.None);
        var persisted = Assert.Single(reloaded.CurrentInventory!.Accounts);
        Assert.Equal(AccountRecoveryCategory.Unknown, persisted.EffectiveCategory);
        Assert.True(persisted.RequiresCategoryReview);
    }

    [Fact]
    public async Task ClearingAnAccountWithoutAnOverrideFailsWithoutChangingRevision()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestRecordStore();
        var session = new TestRecoverySession();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        Assert.True((await service.UpsertAsync(
            CreateUnknownAccountRequest(),
            CancellationToken.None)).Succeeded);
        var before = service.CurrentInventory!;
        var account = Assert.Single(before.Accounts);

        var result = await service.ClearCategoryOverrideAsync(
            account.Id,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.Conflict, result.FailureCode);
        Assert.Equal(before.Revision, service.CurrentInventory!.Revision);
    }

    private static AccountInventoryUpsertRequest CreateUnknownAccountRequest() => new(
        null,
        "synthetic-unclassified.example",
        "Unclassified account",
        "user@example.invalid",
        null);

    private sealed class TestRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => true;

        public byte[]? StoredInventory { get; private set; }

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                descriptor.RecordType == "account-state"
                    ? StoredInventory?.ToArray()
                    : null);
        }

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (descriptor.RecordType == "account-state")
            {
                StoredInventory = plaintext.ToArray();
            }
            return Task.CompletedTask;
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inventory = writes.FirstOrDefault(write => write.Descriptor.RecordType == "account-state");
            if (inventory is not null)
            {
                StoredInventory = inventory.Plaintext.ToArray();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class TestRecoverySession : IRecoverySessionWorkspaceCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } =
            RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic category semantics",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch);

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

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
                    "16200000-0000-0000-0000-000000000000",
                    1),
                Encoding.UTF8.GetBytes("session"),
                current.Revision));
        }

        public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
        {
            CurrentSession = update.State;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearForLock()
        {
        }
    }
}
