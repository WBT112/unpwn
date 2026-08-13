using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoverySessionServiceTests
{
    [Fact]
    public async Task EmptyVaultCreatesAndReloadsEncryptedSessionWithCurrentIntakeSchema()
    {
        var currentTime = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(currentTime);
        using var service = new RecoverySessionService(store, coordinator, () => currentTime);

        await service.InitializeAsync(CancellationToken.None);
        var result = await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Recovery session",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None);

        Assert.Equal(RecoverySessionLoadState.Loaded, service.LoadState);
        Assert.True(result.Succeeded);
        Assert.NotNull(store.StoredRecord);
        Assert.DoesNotContain(
            "Description",
            Encoding.UTF8.GetString(store.StoredRecord),
            StringComparison.Ordinal);
        Assert.Equal("Recovery session", service.CurrentSession?.Name);
        Assert.Equal(IncidentIndicator.None, service.CurrentSession?.Incident.Indicators);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, coordinator.CurrentWizard.CurrentStep);
        Assert.Equal("Recovery session", coordinator.SessionDisplayName);

        using var reloaded = new RecoverySessionService(store, coordinator, () => currentTime);
        await reloaded.InitializeAsync(CancellationToken.None);

        Assert.Equal(RecoverySessionLoadState.Loaded, reloaded.LoadState);
        var expected = Assert.IsType<RecoverySessionWorkspace>(service.CurrentSession);
        var actual = Assert.IsType<RecoverySessionWorkspace>(reloaded.CurrentSession);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Incident, actual.Incident);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Accounts, actual.Accounts);
    }

    [Fact]
    public async Task AccountDeferralIsRevisionBoundEncryptedAndReloadable()
    {
        var currentTime = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(currentTime);
        using var service = new RecoverySessionService(store, coordinator, () => currentTime);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Deferred work",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        var accountId = Guid.NewGuid();
        var account = new RecoveryAccountDashboardEntry(
            accountId,
            "synthetic.example",
            AccountCriticality.Important,
            AccountRecoveryStatus.Open,
            0, 1, 0, 1, 0, 0, 0, false, 0, 0, "reset-password")
        {
            Category = AccountRecoveryCategory.Email,
        };
        currentTime = currentTime.AddMinutes(1);
        Assert.True((await service.ReplaceAccountSummariesAsync(
            [account],
            CancellationToken.None)).Succeeded);
        var expectedRevision = service.CurrentSession!.Revision;
        currentTime = currentTime.AddMinutes(1);

        var stale = await service.DeferAccountAsync(
            accountId,
            expectedRevision - 1,
            CancellationToken.None);
        var deferred = await service.DeferAccountAsync(
            accountId,
            expectedRevision,
            CancellationToken.None);

        Assert.Equal(RecoverySessionOperationFailureCode.Conflict, stale.FailureCode);
        Assert.True(deferred.Succeeded);
        Assert.Equal(1, service.CurrentSession!.Accounts.Single().DeferralCount);
        Assert.Equal(currentTime, service.CurrentSession.Accounts.Single().DeferredAt);

        using var reloaded = new RecoverySessionService(store, coordinator, () => currentTime);
        await reloaded.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, reloaded.CurrentSession!.Accounts.Single().DeferralCount);
        Assert.Equal(currentTime, reloaded.CurrentSession.Accounts.Single().DeferredAt);
        Assert.Equal(AccountRecoveryStatus.Open, reloaded.CurrentSession.Accounts.Single().RecoveryStatus);
    }

    [Fact]
    public async Task CorruptedRecordIsReportedAndNeverOverwrittenByCreate()
    {
        byte[] original = [0x01, 0x02, 0x03, 0x04];
        var store = new TestEncryptedRecordStore
        {
            StoredRecord = [.. original],
        };
        var coordinator = new TestWizardCoordinator(DateTimeOffset.UnixEpoch);
        using var service = new RecoverySessionService(store, coordinator, () => DateTimeOffset.UnixEpoch);

        await service.InitializeAsync(CancellationToken.None);
        var result = await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Replacement",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None);

        Assert.Equal(RecoverySessionLoadState.Corrupted, service.LoadState);
        Assert.False(result.Succeeded);
        Assert.Equal(RecoverySessionOperationFailureCode.Corrupted, result.FailureCode);
        Assert.Equal(original, store.StoredRecord);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task RemovedLegacyDescriptionFieldFailsClosedOnReload()
    {
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(DateTimeOffset.UnixEpoch);
        using var service = new RecoverySessionService(store, coordinator, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Current schema",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        var currentJson = Encoding.UTF8.GetString(Assert.IsType<byte[]>(store.StoredRecord));
        store.StoredRecord = Encoding.UTF8.GetBytes(currentJson.Replace(
            "\"Indicators\":0",
            "\"Indicators\":0,\"Description\":\"legacy\"",
            StringComparison.Ordinal));

        using var reloaded = new RecoverySessionService(store, coordinator, () => DateTimeOffset.UnixEpoch);
        await reloaded.InitializeAsync(CancellationToken.None);

        Assert.Equal(RecoverySessionLoadState.Corrupted, reloaded.LoadState);
        Assert.Null(reloaded.CurrentSession);
    }

    [Fact]
    public async Task DashboardEntryWithoutCurrentCategoryFailsClosedOnReload()
    {
        var currentTime = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(currentTime);
        using var service = new RecoverySessionService(store, coordinator, () => currentTime);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Current category schema",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        var account = new RecoveryAccountDashboardEntry(
            Guid.NewGuid(),
            "synthetic.example",
            AccountCriticality.Important,
            AccountRecoveryStatus.Open,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            0,
            0,
            null)
        {
            Category = AccountRecoveryCategory.Email,
        };
        currentTime = currentTime.AddMinutes(1);
        var updated = service.CurrentSession!.ReplaceAccounts([account], currentTime);
        var json = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(updated))!.AsObject();
        json[nameof(RecoverySessionWorkspace.Accounts)]![0]!.AsObject()
            .Remove(nameof(RecoveryAccountDashboardEntry.Category));
        store.StoredRecord = Encoding.UTF8.GetBytes(json.ToJsonString());

        using var reloaded = new RecoverySessionService(store, coordinator, () => DateTimeOffset.UnixEpoch);
        await reloaded.InitializeAsync(CancellationToken.None);

        Assert.Equal(RecoverySessionLoadState.Corrupted, reloaded.LoadState);
        Assert.Null(reloaded.CurrentSession);
    }

    [Fact]
    public async Task PauseResumeAndArchivePersistBothWorkspaceAndWizardLifecycle()
    {
        var currentTime = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(currentTime);
        using var service = new RecoverySessionService(store, coordinator, () => currentTime);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Lifecycle",
                IncidentIndicator.CompromisedRecoveryChannel,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);

        currentTime = currentTime.AddMinutes(1);
        Assert.True((await service.PauseAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Paused, service.CurrentSession?.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Paused, coordinator.CurrentWizard.Status);

        currentTime = currentTime.AddMinutes(1);
        Assert.True((await service.ResumeAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Active, service.CurrentSession?.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Active, coordinator.CurrentWizard.Status);

        currentTime = currentTime.AddMinutes(1);
        Assert.True((await service.ArchiveAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Archived, service.CurrentSession?.Status);
        Assert.Equal(RecoveryWizardLifecycleStatus.Archived, coordinator.CurrentWizard.Status);
        Assert.True(store.WriteCount >= 4);
    }

    [Fact]
    public async Task ClearingForLockRemovesDecryptedSessionFromMemory()
    {
        var store = new TestEncryptedRecordStore();
        var coordinator = new TestWizardCoordinator(DateTimeOffset.UnixEpoch);
        using var service = new RecoverySessionService(store, coordinator, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.CreateAsync(
            new RecoverySessionCreateRequest(
                "Memory boundary",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);

        service.ClearForLock();

        Assert.Equal(RecoverySessionLoadState.Locked, service.LoadState);
        Assert.Null(service.CurrentSession);
        Assert.Null(service.Dashboard);
        Assert.Null(coordinator.SessionDisplayName);
        Assert.NotNull(store.StoredRecord);
    }

    private sealed class TestEncryptedRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked { get; set; } = true;

        public byte[]? StoredRecord { get; set; }

        public int WriteCount { get; private set; }

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
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var write = Assert.Single(writes);
            write.Validate();
            StoredRecord = write.Plaintext.ToArray();
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestWizardCoordinator : IRecoveryWizardVaultCoordinator
    {
        private DateTimeOffset _lastTransitionTime;

        public TestWizardCoordinator(DateTimeOffset createdAt)
        {
            _lastTransitionTime = createdAt;
            var state = RecoveryWizardState.Create(Guid.NewGuid(), createdAt);
            state = RecoveryWizardStateMachine.Advance(
                state,
                RecoveryWizardStepId.TrustedDeviceCheck,
                createdAt);
            state = RecoveryWizardStateMachine.RecordTrustedDeviceDecision(
                state,
                TrustedDeviceDecision.Trusted,
                createdAt);
            CurrentWizard = RecoveryWizardStateMachine.ConfirmVaultReady(state, createdAt);
        }

        public RecoveryWizardState CurrentWizard { get; private set; }

        public string? SessionDisplayName { get; private set; }

        public Task ApplyWizardTransitionAsync(
            RecoverySessionWizardTransition transition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastTransitionTime = _lastTransitionTime.AddSeconds(1);
            CurrentWizard = transition switch
            {
                RecoverySessionWizardTransition.CompleteIncidentIntake =>
                    RecoveryWizardStateMachine.Advance(
                        CurrentWizard,
                        RecoveryWizardStepId.AccountInventory,
                        _lastTransitionTime),
                RecoverySessionWizardTransition.Pause =>
                    RecoveryWizardStateMachine.Pause(CurrentWizard, _lastTransitionTime),
                RecoverySessionWizardTransition.Resume =>
                    RecoveryWizardStateMachine.Resume(CurrentWizard, _lastTransitionTime),
                RecoverySessionWizardTransition.Archive =>
                    RecoveryWizardStateMachine.Archive(CurrentWizard, _lastTransitionTime),
                _ => throw new ArgumentOutOfRangeException(nameof(transition)),
            };
            return Task.CompletedTask;
        }

        public void SetSessionDisplayName(string? sessionDisplayName) =>
            SessionDisplayName = sessionDisplayName;
    }
}
