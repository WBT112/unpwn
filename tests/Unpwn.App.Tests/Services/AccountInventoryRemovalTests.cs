using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AccountInventoryRemovalTests
{
    [Fact]
    public async Task AccountWithDependentsRequiresAcknowledgementAndLeavesMissingDependencyVisible()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.UpsertAsync(
            CreateRequest("Recovery mailbox"),
            CancellationToken.None)).Succeeded);
        time = time.AddSeconds(1);
        Assert.True((await service.UpsertAsync(
            CreateRequest("Critical account"),
            CancellationToken.None)).Succeeded);
        var mailbox = service.CurrentInventory!.Accounts.Single(account =>
            account.ProviderId == "Recovery mailbox");
        var critical = service.CurrentInventory.Accounts.Single(account =>
            account.ProviderId == "Critical account");
        time = time.AddSeconds(1);
        Assert.True((await service.AddDependencyAsync(
            new AccountDependencyRequest(
                critical.Id,
                mailbox.Id,
                AccountDependencyKind.PasswordReset,
                null),
            CancellationToken.None)).Succeeded);

        time = time.AddSeconds(1);
        var unacknowledged = await service.RemoveAccountAsync(
            mailbox.Id,
            dependencyImpactAcknowledged: false,
            CancellationToken.None);
        var acknowledged = await service.RemoveAccountAsync(
            mailbox.Id,
            dependencyImpactAcknowledged: true,
            CancellationToken.None);

        Assert.False(unacknowledged.Succeeded);
        Assert.Equal(
            AccountInventoryFailureCode.RequiresConfirmation,
            unacknowledged.FailureCode);
        Assert.True(acknowledged.Succeeded);
        Assert.DoesNotContain(
            service.CurrentInventory!.Accounts,
            account => account.Id == mailbox.Id);
        Assert.Contains(
            service.CurrentPlan!.Issues,
            issue => issue.Kind == AccountInventoryIssueKind.MissingDependency &&
                     issue.AccountId == critical.Id &&
                     issue.RelatedAccountId == mailbox.Id);
        Assert.Contains(
            session.LastSummaries,
            summary => summary.AccountId == critical.Id &&
                       summary.BlockedRequiredActions == 1);
    }

    private static AccountInventoryUpsertRequest CreateRequest(string provider) =>
        new(
            null,
            provider,
            provider,
            $"{provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            null,
            provider == "Critical account"
                ? AccountInventoryPriority.Critical
                : AccountInventoryPriority.High);

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
    }

    private sealed class TestRecoverySessionService : IRecoverySessionService
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

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSummaries = [.. accounts];
            CurrentSession = CurrentSession!.ReplaceAccounts(
                accounts,
                CurrentSession.UpdatedAt.AddSeconds(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public void ClearForLock()
        {
        }

        private static Task<RecoverySessionOperationResult> Conflict() =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));
    }
}
