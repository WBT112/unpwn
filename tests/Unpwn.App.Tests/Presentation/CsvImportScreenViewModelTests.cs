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
    public async Task SkipDuplicatesWithOnlyDuplicateCandidatesCompletesWithoutPersisting()
    {
        var inventory = new RecordingInventoryService();
        var viewModel = new CsvImportScreenViewModel(inventory, CreateLocalization());
        var candidates = CreateDuplicateCandidates();

        var result = await viewModel.ImportAsync(
            candidates,
            ImportDuplicateResolution.SkipDuplicates,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.AffectedAccounts);
        Assert.Equal(0, inventory.ImportCalls);
        Assert.Equal("Import.Result.NoChanges", CsvImportScreenViewModel.GetImportResultResourceKey(result));
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
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory => null;

        public AccountInventoryPlan? CurrentPlan => null;

        public int ImportCalls { get; private set; }

        public ImportDuplicateResolution? LastDuplicateResolution { get; private set; }

        public AccountInventoryOperationResult ImportResult { get; set; } =
            AccountInventoryOperationResult.Success();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> DecideRoleAsync(
            Guid accountId,
            AccountInventoryRole role,
            AccountRoleDecision decision,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> AddDependencyAsync(
            AccountDependencyRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
            Guid accountId,
            Guid dependsOnAccountId,
            AccountDependencyKind kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            bool dependencyImpactAcknowledged,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportCalls++;
            LastDuplicateResolution = duplicateResolution;
            return Task.FromResult(ImportResult);
        }

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }
    }
}
