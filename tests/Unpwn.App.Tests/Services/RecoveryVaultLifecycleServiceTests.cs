using Unpwn.App.Services;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryVaultLifecycleServiceTests
{
    private const string OriginalPassword = "UNPWN_TEST_SECRET_original-vault-password";
    private const string NewPassword = "UNPWN_TEST_SECRET_replacement-vault-password";

    [Fact]
    public async Task VaultCreationCannotBypassTrustedDeviceGate()
    {
        using var directory = new TemporaryDirectory();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        using var lifecycle = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            clock: () => DateTimeOffset.UnixEpoch);
        var vaultPath = Path.Combine(directory.Path, "recovery.db");

        var result = await lifecycle.CreateAsync(
            vaultPath,
            OriginalPassword,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VaultOperationFailureCode.InvalidInput, result.FailureCode);
        Assert.False(File.Exists(vaultPath));
        Assert.False(wizard.Current.HasVaultContext);
    }

    [Fact]
    public async Task InactivityLocksVaultAndCorrectPasswordResumesAtSafeStep()
    {
        using var directory = new TemporaryDirectory();
        var currentTime = DateTimeOffset.UnixEpoch;
        var wizard = PrepareWizard(currentTime);
        using var lifecycle = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            new VaultInactivityPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)),
            () => currentTime);
        var vaultPath = Path.Combine(directory.Path, "recovery.db");

        var created = await lifecycle.CreateAsync(
            vaultPath,
            OriginalPassword,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.True(lifecycle.Snapshot.IsUnlocked);
        Assert.True(wizard.Current.HasVaultContext);
        Assert.Equal(RecoveryWizardStepId.IncidentIntake, wizard.Current.CurrentStep);

        currentTime = currentTime.AddMinutes(1).AddSeconds(1);
        await lifecycle.CheckInactivityAsync(currentTime, CancellationToken.None);

        Assert.True(lifecycle.Snapshot.IsInactivityWarningVisible);
        Assert.NotNull(lifecycle.Snapshot.InactivityLocksAt);

        currentTime = currentTime.AddMinutes(1);
        await lifecycle.CheckInactivityAsync(currentTime, CancellationToken.None);

        Assert.Equal(VaultLifecycleStatus.Locked, lifecycle.Snapshot.Status);
        Assert.Equal(VaultLockReason.Inactivity, lifecycle.Snapshot.LastLockReason);
        Assert.Equal(RecoveryWizardLifecycleStatus.Locked, wizard.Current.Status);
        Assert.False(lifecycle.Current.IsVaultUnlocked);

        var wrongPassword = await lifecycle.UnlockCurrentAsync(
            NewPassword,
            CancellationToken.None);

        Assert.False(wrongPassword.Succeeded);
        Assert.Equal(
            VaultOperationFailureCode.AuthenticationOrIntegrity,
            wrongPassword.FailureCode);
        Assert.Equal(VaultLifecycleStatus.Locked, lifecycle.Snapshot.Status);

        var unlocked = await lifecycle.UnlockCurrentAsync(
            OriginalPassword,
            CancellationToken.None);

        Assert.True(unlocked.Succeeded);
        Assert.True(lifecycle.Snapshot.IsUnlocked);
        Assert.Equal(RecoveryWizardLifecycleStatus.Active, wizard.Current.Status);
        Assert.Equal(RecoveryWizardStepId.IncidentIntake, wizard.Current.CurrentStep);
    }

    [Fact]
    public async Task PasswordChangeRewrapsVaultAndRecentStoreContainsNoPassword()
    {
        using var directory = new TemporaryDirectory();
        var currentTime = DateTimeOffset.UnixEpoch;
        var recentPath = Path.Combine(directory.Path, "recent.json");
        var recentStore = new JsonRecentVaultStore(recentPath);
        var wizard = PrepareWizard(currentTime);
        using var lifecycle = new RecoveryVaultLifecycleService(
            recentStore,
            wizard,
            clock: () => currentTime);
        var vaultPath = Path.Combine(directory.Path, "recovery.db");
        Assert.True((await lifecycle.CreateAsync(
            vaultPath,
            OriginalPassword,
            CancellationToken.None)).Succeeded);

        var wrongCurrentPassword = await lifecycle.ChangePasswordAsync(
            NewPassword,
            NewPassword,
            CancellationToken.None);

        Assert.False(wrongCurrentPassword.Succeeded);
        Assert.Equal(
            VaultOperationFailureCode.AuthenticationOrIntegrity,
            wrongCurrentPassword.FailureCode);

        var changed = await lifecycle.ChangePasswordAsync(
            OriginalPassword,
            NewPassword,
            CancellationToken.None);

        Assert.True(changed.Succeeded);
        await lifecycle.LockAsync(CancellationToken.None);
        Assert.False((await lifecycle.UnlockCurrentAsync(
            OriginalPassword,
            CancellationToken.None)).Succeeded);
        Assert.True((await lifecycle.UnlockCurrentAsync(
            NewPassword,
            CancellationToken.None)).Succeeded);

        var recentJson = await File.ReadAllTextAsync(recentPath);
        Assert.DoesNotContain(OriginalPassword, recentJson, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, recentJson, StringComparison.Ordinal);
        var storedReference = Assert.Single(await recentStore.LoadAsync(CancellationToken.None));
        Assert.Equal(Path.GetFullPath(vaultPath), storedReference.Path);
    }

    [Fact]
    public async Task RemovingRecentReferenceDoesNotDeleteVaultFile()
    {
        using var directory = new TemporaryDirectory();
        var currentTime = DateTimeOffset.UnixEpoch;
        var wizard = PrepareWizard(currentTime);
        using var lifecycle = new RecoveryVaultLifecycleService(
            new JsonRecentVaultStore(Path.Combine(directory.Path, "recent.json")),
            wizard,
            clock: () => currentTime);
        var vaultPath = Path.Combine(directory.Path, "recovery.db");
        Assert.True((await lifecycle.CreateAsync(
            vaultPath,
            OriginalPassword,
            CancellationToken.None)).Succeeded);

        await lifecycle.RemoveRecentReferenceAsync(vaultPath, CancellationToken.None);

        Assert.Empty(lifecycle.RecentVaults);
        Assert.True(File.Exists(vaultPath));
    }

    private static RecoveryWizardSessionService PrepareWizard(DateTimeOffset occurredAt)
    {
        var wizard = new RecoveryWizardSessionService(occurredAt);
        wizard.BeginTrustedDeviceCheck(occurredAt);
        wizard.RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted, occurredAt);
        return wizard;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"unpwn-vault-lifecycle-{Guid.NewGuid():N}");
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
