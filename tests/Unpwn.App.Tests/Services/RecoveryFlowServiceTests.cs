using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryFlowServiceTests
{
    private const string TestPassword = "UNPWN_TEST_SECRET_guided-wizard-vault-password";

    [Fact]
    public async Task WorkspaceTransitionIsEncryptedPersistedAndResumesAfterLock()
    {
        using var directory = new TemporaryDirectory();
        var time = DateTimeOffset.UnixEpoch;
        var wizard = new RecoveryWizardSessionService(time);
        wizard.BeginTrustedDeviceCheck(time);
        wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, time);
        using var mutations = new WorkspaceMutationCoordinator();
        using var vault = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            clock: () => time);
        Assert.True((await vault.CreateAsync(
            Path.Combine(directory.Path, "recovery.db"),
            TestPassword,
            CancellationToken.None)).Succeeded);

        using var session = new RecoverySessionService(
            vault,
            vault,
            () => time,
            mutations);
        using var inventory = new AccountInventoryService(
            vault,
            session,
            () => time,
            mutations);
        await session.InitializeAsync(CancellationToken.None);
        Assert.True((await session.CreateAsync(
            new RecoverySessionCreateRequest(
                "Synthetic recovery",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        await inventory.InitializeAsync(CancellationToken.None);
        using var flow = new RecoveryFlowService(
            vault,
            wizard,
            session,
            inventory,
            mutations,
            () => time);

        var blocked = await flow.AdvanceAsync(CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal(NextUserTaskTarget.CsvImport, blocked.Task.Target);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, wizard.Current.CurrentStep);

        Assert.True((await inventory.UpsertAsync(
            new AccountInventoryUpsertRequest(
                null,
                "synthetic-provider",
                "Synthetic account",
                null,
                null),
            CancellationToken.None)).Succeeded);
        time = time.AddMinutes(1);
        var advanced = await flow.AdvanceAsync(CancellationToken.None);

        Assert.True(advanced.Succeeded);
        Assert.Equal(RecoveryWizardStepId.AccountTriage, wizard.Current.CurrentStep);

        time = time.AddMinutes(1);
        await vault.LockAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        Assert.True((await vault.UnlockCurrentAsync(
            TestPassword,
            CancellationToken.None)).Succeeded);

        Assert.Equal(RecoveryWizardStepId.AccountTriage, wizard.Current.CurrentStep);
        Assert.Equal(RecoveryWizardLifecycleStatus.Active, wizard.Current.Status);
    }

    [Fact]
    public async Task PlatformSpecificPersistenceFailureReturnsControlledFailure()
    {
        using var directory = new TemporaryDirectory();
        var time = DateTimeOffset.UnixEpoch;
        var wizard = new RecoveryWizardSessionService(time);
        wizard.BeginTrustedDeviceCheck(time);
        wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, time);
        using var mutations = new WorkspaceMutationCoordinator();
        using var vault = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            clock: () => time);
        Assert.True((await vault.CreateAsync(
            Path.Combine(directory.Path, "recovery.db"),
            TestPassword,
            CancellationToken.None)).Succeeded);
        using var session = new RecoverySessionService(vault, vault, () => time, mutations);
        using var inventory = new AccountInventoryService(vault, session, () => time, mutations);
        await session.InitializeAsync(CancellationToken.None);
        Assert.True((await session.CreateAsync(
            new RecoverySessionCreateRequest(
                "Synthetic recovery",
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        await inventory.InitializeAsync(CancellationToken.None);
        Assert.True((await inventory.UpsertAsync(
            new AccountInventoryUpsertRequest(
                null,
                "synthetic-provider",
                "Synthetic account",
                null,
                null),
            CancellationToken.None)).Succeeded);
        using var flow = new RecoveryFlowService(
            new ThrowingSingleWriteStore(vault),
            wizard,
            session,
            inventory,
            mutations,
            () => time);

        var result = await flow.AdvanceAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryFlowMoveFailureCode.PersistenceFailure, result.FailureCode);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, wizard.Current.CurrentStep);
    }

    private sealed class ThrowingSingleWriteStore(IEncryptedVaultRecordStore inner)
        : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => inner.IsVaultUnlocked;

        public Task<byte[]?> ReadEncryptedRecordAsync(
            Unpwn.Vault.Cryptography.VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken) =>
            inner.ReadEncryptedRecordAsync(descriptor, cancellationToken);

        public Task WriteEncryptedRecordAsync(
            Unpwn.Vault.Cryptography.VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken) =>
            Task.FromException(new UnauthorizedAccessException(
                "Synthetic platform persistence failure."));

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<Unpwn.Vault.Storage.VaultRecordWrite> writes,
            CancellationToken cancellationToken) =>
            inner.WriteEncryptedRecordsAtomicallyAsync(writes, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unpwn-guided-wizard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
