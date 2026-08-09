using System.Globalization;
using System.Text;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class CredentialExportScreenViewModelTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RevealIsDeliberateAndLanguageChangeClearsSecretPresentation()
    {
        var context = CreateContext();
        await context.ViewModel.RefreshCommand.ExecuteAsync();

        Assert.False(context.ViewModel.IsSecretRevealed);
        Assert.DoesNotContain("temporary", context.ViewModel.SelectedCredentialStage, StringComparison.Ordinal);

        await context.ViewModel.RevealCommand.ExecuteAsync();
        Assert.True(context.ViewModel.IsSecretRevealed);
        Assert.Equal("UNPWN_TEST_SECRET_temporary-generated-value", context.ViewModel.RevealedSecret);

        context.Localization.SetLanguage("de");

        Assert.False(context.ViewModel.IsSecretRevealed);
        Assert.Empty(context.ViewModel.RevealedSecret);
    }

    [Fact]
    public async Task GenerationUsesSelectedAccountAndKeepsValueConcealed()
    {
        var context = CreateContext();
        await context.ViewModel.RefreshCommand.ExecuteAsync();

        await context.ViewModel.GenerateCommand.ExecuteAsync();

        Assert.Equal(1, context.Repository.GenerateCalls);
        Assert.Equal(context.ViewModel.SelectedAccount?.AccountId, context.Repository.Metadata.AccountId);
        Assert.False(context.ViewModel.IsSecretRevealed);
        Assert.Empty(context.ViewModel.RevealedSecret);
    }

    [Fact]
    public async Task CopyShowsCountdownAndNavigationClearsOnlyOwnedClipboard()
    {
        var context = CreateContext();
        await context.ViewModel.RefreshCommand.ExecuteAsync();

        await context.ViewModel.CopyCommand.ExecuteAsync();

        Assert.Equal(30, context.ViewModel.ClipboardSecondsRemaining);
        Assert.True(context.ViewModel.IsClipboardCountdownVisible);
        Assert.Equal(1, context.Clipboard.CopyCalls);

        context.ViewModel.Deactivate();
        await context.Clipboard.Cleared.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, context.ViewModel.ClipboardSecondsRemaining);
        Assert.Equal(1, context.Clipboard.ClearCalls);
    }

    [Fact]
    public async Task ExportUsesOnlyExplicitlySelectedCredentialsAndRequiresRiskAcknowledgement()
    {
        var context = CreateContext();
        context.Repository.SetConfirmed();
        await context.ViewModel.RefreshCommand.ExecuteAsync();
        context.ViewModel.Credentials[0].IsSelectedForExport = true;
        context.ViewModel.DestinationPath = Path.Combine(Path.GetTempPath(), "selected-export.csv");

        Assert.False(context.ViewModel.ExportCommand.CanExecute(null));
        context.ViewModel.PlaintextRiskAcknowledged = true;
        Assert.True(context.ViewModel.ExportCommand.CanExecute(null));

        await context.ViewModel.ExportCommand.ExecuteAsync();

        var request = Assert.IsType<CredentialExportRequest>(context.Export.LastRequest);
        Assert.Single(request.Selections);
        Assert.Equal(context.Repository.Metadata.Reference, request.Selections[0].Reference);
        Assert.True(request.PlaintextRiskAcknowledged);
    }

    [Fact]
    public async Task HandoffCanBePostponedConfirmedCorrectedAndCleanedUpFromUi()
    {
        var context = CreateContext();
        context.Repository.SetExported();
        await context.ViewModel.RefreshCommand.ExecuteAsync();

        await context.ViewModel.PostponeImportConfirmationCommand.ExecuteAsync();
        Assert.True(context.ViewModel.IsImportConfirmationPostponed);

        await context.ViewModel.ConfirmImportCommand.ExecuteAsync();
        Assert.True(context.ViewModel.IsImportConfirmed);

        await context.ViewModel.RevokeImportConfirmationCommand.ExecuteAsync();
        Assert.False(context.ViewModel.IsImportConfirmed);

        await context.ViewModel.ConfirmCleanupCommand.ExecuteAsync();
        Assert.False(context.ViewModel.IsCleanupPending);
    }

    [Fact]
    public async Task VaultLockClearsRevealClipboardAndCredentialList()
    {
        var context = CreateContext();
        await context.ViewModel.RefreshCommand.ExecuteAsync();
        await context.ViewModel.RevealCommand.ExecuteAsync();
        await context.ViewModel.CopyCommand.ExecuteAsync();

        await context.Shell.LockAsync(CancellationToken.None);
        await context.Clipboard.Cleared.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(context.ViewModel.IsSecretRevealed);
        Assert.Empty(context.ViewModel.RevealedSecret);
        Assert.Empty(context.ViewModel.Credentials);
        Assert.False(context.ViewModel.CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmedDeletionRemovesRevealableCredentialState()
    {
        var context = CreateContext();
        await context.ViewModel.RefreshCommand.ExecuteAsync();
        await context.ViewModel.RevealCommand.ExecuteAsync();

        await context.ViewModel.DeleteCommand.ExecuteAsync();

        Assert.True(context.Repository.Metadata.IsDeleted);
        Assert.True(context.ViewModel.SelectedCredential?.IsDeleted);
        Assert.False(context.ViewModel.IsSecretRevealed);
        Assert.False(context.ViewModel.RevealCommand.CanExecute(null));
    }

    private static TestContext CreateContext()
    {
        var accountId = Guid.NewGuid();
        var metadata = GeneratedCredentialMetadata.Create(
            Guid.NewGuid(), accountId, Guid.NewGuid(), StartedAt);
        var repository = new TestCredentialRepository(metadata);
        var inventory = new TestInventoryService(new AccountInventoryEntry(
            accountId,
            "example",
            "Example account",
            "person@example.invalid",
            "https://example.invalid/account",
            AccountInventoryPriority.Normal,
            [],
            [],
            StartedAt));
        var shell = new TestShellContext();
        var clipboard = new TestClipboard();
        var export = new TestExportService();
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var viewModel = new CredentialExportScreenViewModel(
            repository,
            export,
            inventory,
            shell,
            clipboard,
            new TestConfirmation(),
            localization);
        return new TestContext(viewModel, repository, export, clipboard, shell, localization);
    }

    private sealed record TestContext(
        CredentialExportScreenViewModel ViewModel,
        TestCredentialRepository Repository,
        TestExportService Export,
        TestClipboard Clipboard,
        TestShellContext Shell,
        ResourceLocalizationService Localization);

    private sealed class TestCredentialRepository(GeneratedCredentialMetadata metadata)
        : IGeneratedCredentialRepository
    {
        private readonly byte[] _secret = Encoding.UTF8.GetBytes(
            "UNPWN_TEST_SECRET_temporary-generated-value");
        private DateTimeOffset _current = StartedAt;

        public GeneratedCredentialMetadata Metadata { get; private set; } = metadata;

        public int GenerateCalls { get; private set; }

        public bool IsUnlocked { get; set; } = true;

        public Task<GeneratedCredentialCreationResult> GenerateAsync(
            Guid accountId, CredentialGenerationPolicy policy, Guid operationId,
            CancellationToken cancellationToken)
        {
            GenerateCalls++;
            Metadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(), accountId, operationId, StartedAt);
            return Task.FromResult(GeneratedCredentialCreationResult.Success(
                Metadata,
                new CredentialSecretLease(_secret.ToArray())));
        }

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeneratedCredentialMetadata>>([Metadata]);

        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(
            GeneratedCredentialReference reference, CancellationToken cancellationToken) =>
            Task.FromResult<GeneratedCredentialMetadata?>(Metadata);

        public Task<CredentialSecretLease?> ReadSecretAsync(
            GeneratedCredentialReference reference, CancellationToken cancellationToken) =>
            Task.FromResult<CredentialSecretLease?>(new(_secret.ToArray()));

        public Task<GeneratedCredentialOperationResult> MarkUsedAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.MarkUsed(operationId, NextTime()));

        public Task<GeneratedCredentialOperationResult> ConfirmAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.Confirm(operationId, NextTime()));

        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(
            IReadOnlyCollection<GeneratedCredentialReference> references,
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GeneratedCredentialBatchResult.Success([Metadata]));

        public Task<GeneratedCredentialOperationResult> DeleteAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.Delete(operationId, NextTime()));

        public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.ConfirmPasswordManagerImport(operationId, NextTime()));

        public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.RevokePasswordManagerImportConfirmation(operationId, NextTime()));

        public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.PostponePasswordManagerImportConfirmation(operationId, NextTime()));

        public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(
            GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
            Mutate(item => item.ConfirmPlaintextExportCleanup(operationId, NextTime()));

        public void SetExported() => Metadata = Metadata.MarkExported(Guid.NewGuid(), NextTime());

        public void SetConfirmed()
        {
            Metadata = Metadata.MarkUsed(Guid.NewGuid(), NextTime());
            Metadata = Metadata.Confirm(Guid.NewGuid(), NextTime());
        }

        private Task<GeneratedCredentialOperationResult> Mutate(
            Func<GeneratedCredentialMetadata, GeneratedCredentialMetadata> mutation)
        {
            Metadata = mutation(Metadata);
            return Task.FromResult(GeneratedCredentialOperationResult.Success(Metadata));
        }

        private DateTimeOffset NextTime() => _current = _current.AddMinutes(1);
    }

    private sealed class TestExportService : IGeneratedCredentialExportService
    {
        public CredentialExportRequest? LastRequest { get; private set; }

        public Task<CredentialExportResult> ExportAsync(
            CredentialExportRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(CredentialExportResult.Success(request.DestinationPath, request.Selections.Count));
        }
    }

    private sealed class TestClipboard : ICredentialClipboardService
    {
        public int CopyCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public TaskCompletionSource Cleared { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> CopyAsync(ReadOnlyMemory<byte> secretUtf8, CancellationToken cancellationToken)
        {
            CopyCalls++;
            return Task.FromResult(true);
        }

        public Task ClearOwnedAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            Cleared.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TestShellContext : IShellContextService
    {
        public event EventHandler? ContextChanged;

        public ShellContext Current { get; private set; } = ShellContext.Unlocked("Synthetic vault");

        public Task LockAsync(CancellationToken cancellationToken)
        {
            Current = ShellContext.Locked;
            ContextChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConfirmation : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class TestInventoryService(AccountInventoryEntry account) : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged
        {
            add { }
            remove { }
        }

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; } = new(
            Guid.NewGuid(), 0, StartedAt, [account]);

        public AccountInventoryPlan? CurrentPlan => null;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> DecideRoleAsync(
            Guid accountId, AccountInventoryRole role, AccountRoleDecision decision,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> AddDependencyAsync(
            AccountDependencyRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
            Guid accountId, Guid dependsOnAccountId, AccountDependencyKind kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId, bool dependencyImpactAcknowledged, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }
    }
}
