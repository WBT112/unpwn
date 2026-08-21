using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class CsvImportScreenViewModelTests
{
    [Fact]
    public async Task SkipDuplicatesKeepsFirstWithinImportCandidate()
    {
        var inventory = new RecordingInventoryService
        {
            ImportResult = AccountInventoryOperationResult.Success(1),
        };
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var candidates = CreateDuplicateCandidates();

        var result = await viewModel.ImportAsync(
            candidates,
            ImportDuplicateResolution.SkipDuplicates,
            CancellationToken.None);

        Assert.Equal(CsvDuplicateKind.None, candidates[0].DuplicateKind);
        Assert.Equal(CsvDuplicateKind.WithinImport, candidates[1].DuplicateKind);
        Assert.True(result.Succeeded);
        Assert.Equal(1, inventory.ImportCalls);
        Assert.Equal(ImportDuplicateResolution.SkipDuplicates, inventory.LastDuplicateResolution);
        Assert.Equal("Import.Result.Success", CsvImportScreenViewModel.GetImportResultResourceKey(result));
    }

    [Fact]
    public async Task ImportDuplicatesAsSeparateAccountsStillDelegatesToInventoryService()
    {
        var inventory = new RecordingInventoryService
        {
            ImportResult = AccountInventoryOperationResult.Success(2),
        };
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var candidates = CreateDuplicateCandidates();

        var result = await viewModel.ImportAsync(
            candidates,
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, inventory.ImportCalls);
        Assert.Equal(ImportDuplicateResolution.ImportAsSeparateAccounts, inventory.LastDuplicateResolution);
        Assert.Equal("Import.Result.Success", CsvImportScreenViewModel.GetImportResultResourceKey(result));
    }

    [Fact]
    public async Task SuccessfulImportWithPersistedAccountsRequestsCategoryReview()
    {
        var sessionId = Guid.NewGuid();
        var inventory = new RecordingInventoryService
        {
            ImportResult = AccountInventoryOperationResult.Success(1),
            CurrentInventory = AccountInventoryState.Empty(sessionId, DateTimeOffset.UnixEpoch)
                .ReplaceAccounts(
                [
                    new AccountInventoryEntry(
                        Guid.NewGuid(),
                        "example.test",
                        "Synthetic account",
                        "person@example.invalid",
                        null,
                        AccountRecoveryCategory.Unknown,
                        RepositoryAccountClassificationCatalog.CurrentVersion,
                        ConfirmedCategory: null,
                        CategoryConfirmedRevision: null,
                        DateTimeOffset.UnixEpoch),
                ],
                DateTimeOffset.UnixEpoch),
        };
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var reviewRequested = false;
        viewModel.AccountReviewRequested += (_, _) => reviewRequested = true;

        var result = await viewModel.ImportAsync(
            CreateDuplicateCandidates(),
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(reviewRequested);
    }

    [Fact]
    public async Task FailedImportDoesNotRequestCategoryReview()
    {
        var inventory = new RecordingInventoryService
        {
            ImportResult = AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.IoFailure),
        };
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var reviewRequested = false;
        viewModel.AccountReviewRequested += (_, _) => reviewRequested = true;

        var result = await viewModel.ImportAsync(
            CreateDuplicateCandidates(),
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(reviewRequested);
    }

    [Fact]
    public async Task CancelledImportDoesNotRequestCategoryReview()
    {
        var inventory = new RecordingInventoryService();
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var reviewRequested = false;
        viewModel.AccountReviewRequested += (_, _) => reviewRequested = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => viewModel.ImportAsync(
            CreateDuplicateCandidates(),
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            cancellation.Token));

        Assert.False(reviewRequested);
    }

    [Fact]
    public async Task ConcurrentSubmissionDoesNotStartASecondImport()
    {
        var inventory = new RecordingInventoryService
        {
            ImportCompletion = new TaskCompletionSource<AccountInventoryOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var candidates = CreateDuplicateCandidates();

        var firstImport = viewModel.ImportAsync(
            candidates,
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);
        await inventory.ImportStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var repeatedImport = await viewModel.ImportAsync(
            candidates,
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);
        inventory.ImportCompletion.SetResult(AccountInventoryOperationResult.Success(2));

        Assert.True((await firstImport).Succeeded);
        Assert.False(repeatedImport.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.Conflict, repeatedImport.FailureCode);
        Assert.Equal(1, inventory.ImportCalls);
    }

    [Theory]
    [InlineData(AccountInventoryFailureCode.Locked, "Accounts.Error.Locked")]
    [InlineData(AccountInventoryFailureCode.InvalidInput, "Accounts.Error.InvalidInput")]
    [InlineData(AccountInventoryFailureCode.Conflict, "Accounts.Error.Conflict")]
    [InlineData(AccountInventoryFailureCode.RequiresConfirmation, "Accounts.Error.RequiresConfirmation")]
    [InlineData(AccountInventoryFailureCode.Corrupted, "Accounts.Error.Corrupted")]
    [InlineData(AccountInventoryFailureCode.IoFailure, "Accounts.Error.IoFailure")]
    public void ImportFailureUsesSpecificSafeResourceKey(
        AccountInventoryFailureCode failureCode,
        string expectedResourceKey)
    {
        var result = AccountInventoryOperationResult.Failure(failureCode);

        Assert.Equal(expectedResourceKey, CsvImportScreenViewModel.GetImportResultResourceKey(result));
    }

    private static IReadOnlyList<ImportAccountCandidate> CreateDuplicateCandidates()
    {
        const string csv = "service,login\nMail,person@example.invalid\nMail,person@example.invalid\n";
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv));
        return CsvAccountImportService.CreatePreview(
                new StringReader(csv),
                analysis.SuggestedMapping)
            .Candidates;
    }

    private static ResourceLocalizationService CreateLocalization() =>
        new(CultureInfo.GetCultureInfo("en"));

    private sealed class RecordingInventoryService : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged
        {
            add { }
            remove { }
        }

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; init; }

        public AccountRecoveryOrder? CurrentRecoveryOrder => null;

        public int ImportCalls { get; private set; }

        public ImportDuplicateResolution? LastDuplicateResolution { get; private set; }

        public AccountInventoryOperationResult ImportResult { get; set; } =
            AccountInventoryOperationResult.Success();

        public TaskCompletionSource<AccountInventoryOperationResult>? ImportCompletion { get; init; }

        public TaskCompletionSource ImportStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> CategorizeAsync(
            Guid accountId,
            AccountRecoveryCategory category,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportCalls++;
            LastDuplicateResolution = duplicateResolution;
            ImportStarted.TrySetResult();
            return ImportCompletion?.Task ?? Task.FromResult(ImportResult);
        }

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }
    }
}
