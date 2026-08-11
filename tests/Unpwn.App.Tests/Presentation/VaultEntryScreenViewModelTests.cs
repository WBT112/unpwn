using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application.Diagnostics;
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
    public async Task CreatingVaultValidatesPasswordAndClearsItAfterUse()
    {
        const string password = "UNPWN_TEST_SECRET_long-vault-password";
        var lifecycle = new TestVaultLifecycleService();
        var wizard = new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch);
        var viewModel = CreateViewModel(lifecycle, wizard);
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

    [Fact]
    public async Task DiagnosticExportRequiresVisiblePreviewPathAndExplicitApproval()
    {
        var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
        new SecretSafeDiagnostics(diagnosticStore).ReportFailure(
            DiagnosticOperation.WorkspaceSave,
            new IOException("UNPWN_TEST_SECRET_not-in-preview"));
        var writer = new TestDiagnosticWriter();
        var diagnosticExport = new DiagnosticExportService(
            diagnosticStore,
            new SecretSafeDiagnostics(diagnosticStore),
            writer);
        var viewModel = CreateViewModel(
            new TestVaultLifecycleService(),
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            diagnosticExportService: diagnosticExport);

        Assert.True(viewModel.IsDiagnosticExportAvailable);
        Assert.False(viewModel.ExportDiagnosticsCommand.CanExecute(null));

        viewModel.CreateDiagnosticPreviewCommand.Execute(null);

        Assert.True(viewModel.HasDiagnosticPreview);
        Assert.Contains("UNPWN1007", viewModel.DiagnosticPreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UNPWN_TEST_SECRET_",
            viewModel.DiagnosticPreviewText,
            StringComparison.Ordinal);
        Assert.False(viewModel.ExportDiagnosticsCommand.CanExecute(null));

        viewModel.DiagnosticDestinationPath = "synthetic-diagnostics.json";
        viewModel.DiagnosticPreviewApproved = true;
        var outcome = await viewModel.ExportDiagnosticsCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.NotNull(writer.Content);
        Assert.False(viewModel.HasDiagnosticPreview);
        Assert.Equal(string.Empty, viewModel.DiagnosticDestinationPath);
        Assert.Equal(AppVisualState.Success, viewModel.Status.State);
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
        IDiagnosticExportService? diagnosticExportService = null) =>
        new(
            lifecycle,
            wizard,
            new TestConfirmationDialogService(),
            localization ?? CreateLocalization(),
            passwordRevealDuration,
            passwordRevealDelay,
            diagnosticExportService);

    private sealed class TestDiagnosticWriter : IDiagnosticFileWriter
    {
        public string? Content { get; private set; }

        public Task WriteAtomicallyAsync(
            string destinationPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Content = System.Text.Encoding.UTF8.GetString(content.Span);
            return Task.CompletedTask;
        }
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

        public string? LastPassword { get; private set; }

        public ShellContext Current { get; private set; } = ShellContext.Locked;

        public VaultLifecycleSnapshot Snapshot { get; private set; } = VaultLifecycleSnapshot.Empty;

        public IReadOnlyList<RecentVaultReference> RecentVaults { get; } = [];

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
            CancellationToken cancellationToken) => CompleteVaultOperation("synthetic-vault.db", vaultPassword);

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
            Current = ShellContext.Locked;
            Snapshot = Snapshot with { Status = VaultLifecycleStatus.Locked };
            ContextChanged?.Invoke(this, EventArgs.Empty);
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RemoveRecentReferenceAsync(
            string path,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> DeleteVaultFileAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(NextResult);

        public void RecordUserActivity(DateTimeOffset occurredAt)
        {
        }

        public Task CheckInactivityAsync(
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

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
