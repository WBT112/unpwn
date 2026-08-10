using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class GuidedRecoveryWizardServiceTests
{
    private const string TestPassword = "UNPWN_TEST_SECRET_guided-wizard-vault-password";

    [Fact]
    public async Task GuidedStepIsEncryptedPersistedAndResumesAfterLock()
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
                null,
                IncidentIndicator.None,
                SecurityWarningAcknowledged: true),
            CancellationToken.None)).Succeeded);
        await inventory.InitializeAsync(CancellationToken.None);
        using var guided = new GuidedRecoveryWizardService(
            vault,
            wizard,
            session,
            inventory,
            mutations,
            () => time);

        var blocked = await guided.AdvanceAsync(CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal(GuidedRecoveryBlockCode.AccountsRequired, blocked.Decision.BlockCode);
        Assert.Equal(RecoveryWizardStepId.AccountInventory, wizard.Current.CurrentStep);

        Assert.True((await inventory.UpsertAsync(
            new AccountInventoryUpsertRequest(
                null,
                "synthetic-provider",
                "Synthetic account",
                null,
                null,
                AccountInventoryPriority.Normal),
            CancellationToken.None)).Succeeded);
        time = time.AddMinutes(1);
        var advanced = await guided.AdvanceAsync(CancellationToken.None);

        Assert.True(advanced.Succeeded);
        Assert.Equal(RecoveryWizardStepId.IdentityReview, wizard.Current.CurrentStep);

        time = time.AddMinutes(1);
        await vault.LockAsync(CancellationToken.None);
        time = time.AddMinutes(1);
        Assert.True((await vault.UnlockCurrentAsync(
            TestPassword,
            CancellationToken.None)).Succeeded);

        Assert.Equal(RecoveryWizardStepId.IdentityReview, wizard.Current.CurrentStep);
        Assert.Equal(RecoveryWizardLifecycleStatus.Active, wizard.Current.Status);
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
