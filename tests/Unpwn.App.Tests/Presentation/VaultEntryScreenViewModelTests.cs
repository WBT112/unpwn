using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class VaultEntryScreenViewModelTests
{
    [Theory]
    [InlineData(TrustedDeviceDecision.NotTrusted)]
    [InlineData(TrustedDeviceDecision.Unsure)]
    public void UnsafeDeviceChoicesStopBeforeVaultAccess(TrustedDeviceDecision decision)
    {
        var lifecycle = new TestVaultLifecycleService();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard);

        viewModel.BeginCommand.Execute(null);
        GetDecisionCommand(viewModel, decision).Execute(null);

        Assert.True(viewModel.IsTrustedDeviceGuidanceVisible);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
        Assert.False(wizard.Current.HasVaultContext);

        viewModel.EndForDeviceSafetyCommand.Execute(null);

        Assert.True(viewModel.IsSafetyStoppedVisible);
        Assert.True(wizard.Current.IsTerminal);
        Assert.Equal(RecoveryWizardLifecycleStatus.StoppedForDeviceSafety, wizard.Current.Status);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
    }

    [Fact]
    public async Task ReturningUserGetsNewestExistingVaultAsPrimaryAction()
    {
        var older = Path.GetFullPath("older-vault.db");
        var newest = Path.GetFullPath("newest-vault.db");
        var lifecycle = new TestVaultLifecycleService
        {
            RecentVaults =
            [
                new RecentVaultReference(newest, "Newest recovery", DateTimeOffset.UnixEpoch.AddHours(2)),
                new RecentVaultReference(older, "Older recovery", DateTimeOffset.UnixEpoch.AddHours(1)),
            ],
        };
        var pathProvider = new TestVaultPathProvider(existingPaths: [older, newest]);
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            vaultPathProvider: pathProvider);
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);

        Assert.True(viewModel.IsVaultChoiceVisible);
        Assert.True(viewModel.HasPrimaryRecentVault);
        Assert.Equal("Newest recovery", viewModel.PrimaryVaultDisplayName);
        Assert.Equal("Open last vault", viewModel.PrimaryVaultActionText);
        Assert.DoesNotContain(Path.GetDirectoryName(newest)!, viewModel.PrimaryVaultPathContext, StringComparison.Ordinal);

        var outcome = await viewModel.PrimaryVaultActionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.True(viewModel.IsOpenVaultVisible);
        Assert.Equal(newest, viewModel.OpenPath);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
    }

    [Fact]
    public async Task FirstRunPrimaryActionOpensCreateWithDefaultPath()
    {
        var defaultPath = Path.GetFullPath("safe-default-vault.db");
        var lifecycle = new TestVaultLifecycleService();
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            vaultPathProvider: new TestVaultPathProvider(defaultPath));
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);

        Assert.False(viewModel.HasPrimaryRecentVault);
        Assert.Equal("Create a new vault", viewModel.PrimaryVaultActionText);

        var outcome = await viewModel.PrimaryVaultActionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.True(viewModel.IsCreateVaultVisible);
        Assert.Equal(defaultPath, viewModel.CreatePath);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
    }

    [Fact]
    public async Task StaleRecentVaultIsNotOfferedAndNeverOpenedOrRecreatedAtStalePath()
    {
        var stalePath = Path.GetFullPath("deleted-vault.db");
        var defaultPath = Path.GetFullPath("new-default-vault.db");
        var lifecycle = new TestVaultLifecycleService
        {
            RecentVaults =
            [
                new RecentVaultReference(stalePath, "Deleted recovery", DateTimeOffset.UnixEpoch),
            ],
        };
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            vaultPathProvider: new TestVaultPathProvider(defaultPath));
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);

        Assert.False(viewModel.HasPrimaryRecentVault);
        Assert.Empty(viewModel.RecentVaults);

        await viewModel.PrimaryVaultActionCommand.ExecuteAsync();

        Assert.True(viewModel.IsCreateVaultVisible);
        Assert.Equal(defaultPath, viewModel.CreatePath);
        Assert.NotEqual(stalePath, viewModel.CreatePath);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
    }

    [Fact]
    public async Task CreatingVaultClearsPasswordAndRequestsOverviewNavigation()
    {
        const string password = "UNPWN_TEST_SECRET_long-vault-password";
        var lifecycle = new TestVaultLifecycleService();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard);
        var continueRequested = false;
        viewModel.ContinueRequested += (_, _) => continueRequested = true;
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);
        viewModel.ShowCreateVaultCommand.Execute(null);
        viewModel.CreatePath = "synthetic-vault.db";
        viewModel.CreatePassword = "short";
        viewModel.ConfirmCreatePassword = "short";
        viewModel.AcknowledgesNonRecoverability = true;

        var validationOutcome = await viewModel.CreateVaultCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Skipped, validationOutcome);
        Assert.Equal(0, lifecycle.VaultOperationCalls);
        Assert.False(viewModel.CreateVaultCommand.CanExecute(null));

        viewModel.CreatePassword = password;
        viewModel.ConfirmCreatePassword = password;
        Assert.True(viewModel.CreateVaultCommand.CanExecute(null));
        var outcome = await viewModel.CreateVaultCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(1, lifecycle.VaultOperationCalls);
        Assert.Equal(password, lifecycle.LastPassword);
        Assert.Equal(string.Empty, viewModel.CreatePassword);
        Assert.Equal(string.Empty, viewModel.ConfirmCreatePassword);
        Assert.True(viewModel.IsUnlockedVaultVisible);
        Assert.True(continueRequested);
    }

    [Fact]
    public async Task OpeningVaultClearsPasswordAndRequestsOverviewNavigation()
    {
        const string password = "UNPWN_TEST_SECRET_existing-vault-password";
        var vaultPath = Path.GetFullPath("existing-vault.db");
        var lifecycle = new TestVaultLifecycleService();
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            vaultPathProvider: new TestVaultPathProvider(existingPaths: [vaultPath]));
        var continueRequested = false;
        viewModel.ContinueRequested += (_, _) => continueRequested = true;
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);
        viewModel.ShowOpenVaultCommand.Execute(null);
        viewModel.OpenPath = vaultPath;
        viewModel.OpenPassword = password;

        var outcome = await viewModel.OpenVaultCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(string.Empty, viewModel.OpenPassword);
        Assert.True(continueRequested);
    }

    [Fact]
    public async Task UnlockingCurrentVaultClearsPasswordAndRequestsOverviewNavigation()
    {
        const string password = "UNPWN_TEST_SECRET_locked-vault-password";
        var vaultPath = Path.GetFullPath("locked-vault.db");
        var lifecycle = new TestVaultLifecycleService();
        lifecycle.SetLocked(vaultPath, "Locked recovery");
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch));
        var continueRequested = false;
        viewModel.ContinueRequested += (_, _) => continueRequested = true;
        viewModel.OpenPassword = password;

        var outcome = await viewModel.UnlockCurrentVaultCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(string.Empty, viewModel.OpenPassword);
        Assert.True(continueRequested);
    }

    [Fact]
    public async Task TrustedDeviceReassessmentLocksVaultBeforeRequestingNewDecision()
    {
        var vaultPath = Path.GetFullPath("open-vault.db");
        var lifecycle = new TestVaultLifecycleService();
        lifecycle.SetUnlocked(vaultPath, "Open recovery");
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard);

        var outcome = await viewModel.ReassessTrustedDeviceCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(1, lifecycle.LockCalls);
        Assert.True(viewModel.IsTrustedDeviceCheckVisible);
        Assert.Equal(TrustedDeviceDecision.NotAnswered, wizard.Current.TrustedDeviceDecision);

        viewModel.TrustedDeviceYesCommand.Execute(null);

        Assert.True(viewModel.IsLockedVaultVisible);
        Assert.Equal(TrustedDeviceDecision.Trusted, wizard.Current.TrustedDeviceDecision);
    }

    [Fact]
    public async Task NavigatingAwayFromPasswordChangeClearsSensitiveInputs()
    {
        const string vaultPassword = "UNPWN_TEST_SECRET_long-vault-password";
        var lifecycle = new TestVaultLifecycleService();
        var viewModel = CreateViewModel(
            lifecycle,
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch));
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);
        viewModel.ShowCreateVaultCommand.Execute(null);
        viewModel.CreatePath = "synthetic-vault.db";
        viewModel.CreatePassword = vaultPassword;
        viewModel.ConfirmCreatePassword = vaultPassword;
        viewModel.AcknowledgesNonRecoverability = true;
        await viewModel.CreateVaultCommand.ExecuteAsync();
        viewModel.ShowChangePasswordCommand.Execute(null);
        viewModel.CurrentPassword = "UNPWN_TEST_SECRET_current-password";
        viewModel.NewPassword = "UNPWN_TEST_SECRET_new-vault-password";
        viewModel.ConfirmNewPassword = "UNPWN_TEST_SECRET_new-vault-password";
        viewModel.IsChangePasswordRevealed = true;

        viewModel.Deactivate();

        Assert.Equal(string.Empty, viewModel.CurrentPassword);
        Assert.Equal(string.Empty, viewModel.NewPassword);
        Assert.Equal(string.Empty, viewModel.ConfirmNewPassword);
        Assert.False(viewModel.IsChangePasswordRevealed);
        Assert.True(viewModel.IsUnlockedVaultVisible);
        Assert.False(viewModel.IsChangePasswordVisible);
    }

    [Fact]
    public async Task FailedUnlockUsesLocalizedSafeMessageAndClearsPassword()
    {
        const string password = "UNPWN_TEST_SECRET_wrong-password";
        var lifecycle = new TestVaultLifecycleService
        {
            NextResult = VaultOperationResult.Failure(
                VaultOperationFailureCode.AuthenticationOrIntegrity),
        };
        var localization = CreateLocalization();
        localization.SetLanguage("de");
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard, localization);
        viewModel.BeginCommand.Execute(null);
        viewModel.TrustedDeviceYesCommand.Execute(null);
        viewModel.ShowOpenVaultCommand.Execute(null);
        viewModel.OpenPath = "synthetic-vault.db";
        viewModel.OpenPassword = password;

        var outcome = await viewModel.OpenVaultCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(string.Empty, viewModel.OpenPassword);
        Assert.Contains("Passwort ist möglicherweise falsch", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(password, viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.Equal(AppVisualState.Error, viewModel.Status.State);
    }

    [Fact]
    public async Task PasswordRevealAutomaticallyEnds()
    {
        var delay = new TestPresentationDelay();
        var viewModel = CreateViewModel(
            new TestVaultLifecycleService(),
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            passwordRevealDuration: TimeSpan.FromMilliseconds(20),
            passwordRevealDelay: delay);
        var passwordHidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.IsCreatePasswordRevealed) &&
                !viewModel.IsCreatePasswordRevealed)
            {
                passwordHidden.TrySetResult();
            }
        };

        viewModel.IsCreatePasswordRevealed = true;
        Assert.True(viewModel.IsCreatePasswordRevealed);

        await delay.Started;
        Assert.Equal(TimeSpan.FromMilliseconds(20), delay.RequestedDelay);
        delay.Complete();
        await passwordHidden.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.IsCreatePasswordRevealed);
    }

    [Fact]
    public void ReturningToWelcomeKeepsTrustedDeviceGateUnanswered()
    {
        var lifecycle = new TestVaultLifecycleService();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard);
        viewModel.BeginCommand.Execute(null);

        viewModel.BackToWelcomeCommand.Execute(null);
        viewModel.BeginCommand.Execute(null);

        Assert.True(viewModel.IsTrustedDeviceCheckVisible);
        Assert.Equal(TrustedDeviceDecision.NotAnswered, wizard.Current.TrustedDeviceDecision);
        Assert.Equal(RecoveryWizardStepId.TrustedDeviceCheck, wizard.Current.CurrentStep);
    }

    private static RelayCommand GetDecisionCommand(
        VaultEntryScreenViewModel viewModel,
        TrustedDeviceDecision decision)
    {
        return decision switch
        {
            TrustedDeviceDecision.NotTrusted => viewModel.TrustedDeviceNoCommand,
            TrustedDeviceDecision.Unsure => viewModel.TrustedDeviceUnsureCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
    }

    private static ResourceLocalizationService CreateLocalization() =>
        new(CultureInfo.GetCultureInfo("en"));

    private static VaultEntryScreenViewModel CreateViewModel(
        TestVaultLifecycleService lifecycle,
        RecoveryWizardSessionService wizard,
        ResourceLocalizationService? localization = null,
        TimeSpan? passwordRevealDuration = null,
        IPresentationDelay? passwordRevealDelay = null,
        IVaultPathProvider? vaultPathProvider = null) =>
        new(
            lifecycle,
            wizard,
            new TestConfirmationDialogService(),
            localization ?? CreateLocalization(),
            passwordRevealDuration,
            passwordRevealDelay,
            vaultPathProvider ?? new TestVaultPathProvider());

    private sealed class TestVaultPathProvider : IVaultPathProvider
    {
        private readonly HashSet<string> _existingPaths;

        public TestVaultPathProvider(
            string? defaultPath = null,
            IEnumerable<string>? existingPaths = null)
        {
            DefaultPath = defaultPath ?? Path.GetFullPath("synthetic-default-vault.db");
            _existingPaths = new HashSet<string>(
                existingPaths ?? [],
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }

        public string DefaultPath { get; }

        public string GetNextDefaultVaultPath() => DefaultPath;

        public bool IsExistingVaultPath(string path) => _existingPaths.Contains(path);
    }

    private sealed class TestPresentationDelay : IPresentationDelay
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public TimeSpan? RequestedDelay { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            RequestedDelay = delay;
            _started.TrySetResult();
            return _completed.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completed.TrySetResult();
    }

    private sealed class TestConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class TestVaultLifecycleService : IVaultLifecycleService
    {
        public event EventHandler? ContextChanged;

        public event EventHandler? VaultStateChanged;

        public VaultOperationResult NextResult { get; set; } = VaultOperationResult.Success;

        public int VaultOperationCalls { get; private set; }

        public int LockCalls { get; private set; }

        public string? LastPassword { get; private set; }

        public ShellContext Current { get; private set; } = ShellContext.Locked;

        public VaultLifecycleSnapshot Snapshot { get; private set; } = VaultLifecycleSnapshot.Empty;

        public IReadOnlyList<RecentVaultReference> RecentVaults { get; set; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> CreateAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => CompleteVaultOperation(path, vaultPassword);

        public Task<VaultOperationResult> OpenAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => CompleteVaultOperation(path, vaultPassword);

        public Task<VaultOperationResult> UnlockCurrentAsync(
            string vaultPassword,
            CancellationToken cancellationToken) => CompleteVaultOperation(
                Snapshot.CurrentPath ?? "synthetic-vault.db",
                vaultPassword);

        public Task<VaultOperationResult> ChangePasswordAsync(
            string currentVaultPassword,
            string newVaultPassword,
            CancellationToken cancellationToken)
        {
            VaultOperationCalls++;
            LastPassword = newVaultPassword;
            return Task.FromResult(NextResult);
        }

        public Task LockAsync(CancellationToken cancellationToken)
        {
            LockCalls++;
            Current = ShellContext.Locked;
            Snapshot = Snapshot with
            {
                Status = VaultLifecycleStatus.Locked,
                LastLockReason = VaultLockReason.User,
            };
            ContextChanged?.Invoke(this, EventArgs.Empty);
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RemoveRecentReferenceAsync(
            string path,
            CancellationToken cancellationToken)
        {
            RecentVaults = RecentVaults
                .Where(reference => !string.Equals(
                    reference.Path,
                    path,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                .ToArray();
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<VaultOperationResult> DeleteVaultFileAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(NextResult);

        public void RecordUserActivity(DateTimeOffset occurredAt)
        {
        }

        public Task CheckInactivityAsync(
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void SetLocked(string path, string displayName)
        {
            Snapshot = new VaultLifecycleSnapshot(
                VaultLifecycleStatus.Locked,
                path,
                displayName,
                VaultLockReason.User,
                IsInactivityWarningVisible: false,
                InactivityLocksAt: null);
            Current = ShellContext.Locked;
        }

        public void SetUnlocked(string path, string displayName)
        {
            Snapshot = new VaultLifecycleSnapshot(
                VaultLifecycleStatus.Unlocked,
                path,
                displayName,
                VaultLockReason.None,
                IsInactivityWarningVisible: false,
                InactivityLocksAt: null);
            Current = ShellContext.Unlocked(displayName);
        }

        public void Dispose()
        {
        }

        private Task<VaultOperationResult> CompleteVaultOperation(
            string path,
            string vaultPassword)
        {
            VaultOperationCalls++;
            LastPassword = vaultPassword;
            if (NextResult.Succeeded)
            {
                var displayName = Path.GetFileNameWithoutExtension(path);
                Snapshot = new VaultLifecycleSnapshot(
                    VaultLifecycleStatus.Unlocked,
                    path,
                    displayName,
                    VaultLockReason.None,
                    IsInactivityWarningVisible: false,
                    InactivityLocksAt: null);
                Current = ShellContext.Unlocked(displayName);
                ContextChanged?.Invoke(this, EventArgs.Empty);
                VaultStateChanged?.Invoke(this, EventArgs.Empty);
            }

            return Task.FromResult(NextResult);
        }
    }
}
