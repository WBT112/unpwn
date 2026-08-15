using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class AccountInventoryUnknownCategoryViewModelTests
{
    [Fact]
    public void UnknownIsSystemOnlyAndNeverAppearsInTheUserCategoryChoices()
    {
        var inventory = new TestInventoryService(CreateUnknownInventory());
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var viewModel = CreateViewModel(inventory, localization);

        Assert.Equal(
            [AccountRecoveryCategory.Email, AccountRecoveryCategory.Critical, AccountRecoveryCategory.NonCritical],
            viewModel.Categories.Select(option => option.Value));
        Assert.Null(viewModel.SelectedCategory);
        Assert.False(viewModel.SaveCategoryCommand.CanExecute(null));
        var item = Assert.Single(viewModel.Accounts);
        Assert.Equal("Needs review", item.CategoryText);
        Assert.Contains("Not automatically recognized", item.ReviewText, StringComparison.Ordinal);
        Assert.True(item.Account.RequiresCategoryReview);

        localization.SetLanguage("de");
        item = Assert.Single(viewModel.Accounts);
        Assert.Null(viewModel.SelectedCategory);
        Assert.Equal("Prüfung erforderlich", item.CategoryText);
        Assert.Contains("Nicht automatisch erkannt", item.ReviewText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            viewModel.Categories,
            option => option.Value == AccountRecoveryCategory.Unknown);

        localization.SetLanguage(ResourceLocalizationService.PseudoLanguageCode);
        item = Assert.Single(viewModel.Accounts);
        Assert.Null(viewModel.SelectedCategory);
        Assert.StartsWith("⟦", item.CategoryText, StringComparison.Ordinal);
        Assert.All(viewModel.Categories, option =>
            Assert.True(AccountRecoveryCategoryRules.IsUserSelectable(option.Value)));
        Assert.All(viewModel.Categories, option =>
            Assert.StartsWith("⟦", option.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChoosingARealCategoryResolvesUnknownAndRecordsUserProvenance()
    {
        var inventory = new TestInventoryService(CreateUnknownInventory());
        var viewModel = CreateViewModel(
            inventory,
            new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")));
        viewModel.SelectedCategory = viewModel.Categories.Single(option =>
            option.Value == AccountRecoveryCategory.Critical);

        await viewModel.SaveCategoryCommand.ExecuteAsync();

        var account = Assert.Single(inventory.CurrentInventory!.Accounts);
        Assert.Equal(AccountRecoveryCategory.Unknown, account.SuggestedCategory);
        Assert.Equal(AccountRecoveryCategory.Critical, account.ConfirmedCategory);
        Assert.Equal(AccountRecoveryCategory.Critical, account.EffectiveCategory);
        Assert.False(account.RequiresCategoryReview);
        Assert.Equal(inventory.CurrentInventory.Revision, account.CategoryConfirmedRevision);
        Assert.True(viewModel.HasCategoryOverride);
        Assert.Equal("Critical", Assert.Single(viewModel.Accounts).CategoryText);
        Assert.Equal("Changed by you", Assert.Single(viewModel.Accounts).ReviewText);
    }

    [Fact]
    public async Task ClearingAnOverrideReturnsToUnknownNeedsReviewWithoutManufacturingAChoice()
    {
        var state = CreateUnknownInventory();
        var account = Assert.Single(state.Accounts);
        state = state with
        {
            Revision = 2,
            Accounts =
            [
                account with
                {
                    ConfirmedCategory = AccountRecoveryCategory.Critical,
                    CategoryConfirmedRevision = 2,
                },
            ],
        };
        state.Validate();
        var inventory = new TestInventoryService(state);
        var viewModel = CreateViewModel(
            inventory,
            new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")));

        Assert.True(viewModel.HasCategoryOverride);
        Assert.True(viewModel.ClearCategoryOverrideCommand.CanExecute(null));

        await viewModel.ClearCategoryOverrideCommand.ExecuteAsync();

        var restored = Assert.Single(inventory.CurrentInventory!.Accounts);
        Assert.Null(restored.ConfirmedCategory);
        Assert.Null(restored.CategoryConfirmedRevision);
        Assert.Equal(AccountRecoveryCategory.Unknown, restored.EffectiveCategory);
        Assert.True(restored.RequiresCategoryReview);
        Assert.Null(viewModel.SelectedCategory);
        Assert.False(viewModel.HasCategoryOverride);
        Assert.False(viewModel.ClearCategoryOverrideCommand.CanExecute(null));
        Assert.Equal("Needs review", Assert.Single(viewModel.Accounts).CategoryText);
    }

    private static AccountInventoryScreenViewModel CreateViewModel(
        IAccountInventoryService inventory,
        ILocalizationService localization) => new(
            inventory,
            new ConfirmationDialogService(),
            localization);

    private static AccountInventoryState CreateUnknownInventory()
    {
        var account = new AccountInventoryEntry(
            Guid.NewGuid(),
            "synthetic-unclassified.example",
            "Unclassified account",
            "user@example.invalid",
            null,
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            ConfirmedCategory: null,
            CategoryConfirmedRevision: null,
            DateTimeOffset.UnixEpoch.AddSeconds(1));
        var state = new AccountInventoryState(
            Guid.NewGuid(),
            Revision: 1,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            [account]);
        state.Validate();
        return state;
    }

    private sealed class ConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class TestInventoryService(AccountInventoryState initial) : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; private set; } = initial;

        public AccountRecoveryOrder? CurrentRecoveryOrder => CurrentInventory?.CreateRecoveryOrder();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput));

        public Task<AccountInventoryOperationResult> CategorizeAsync(
            Guid accountId,
            AccountRecoveryCategory category,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AccountRecoveryCategoryRules.IsUserSelectable(category) || CurrentInventory is null)
            {
                return Task.FromResult(AccountInventoryOperationResult.Failure(
                    AccountInventoryFailureCode.InvalidInput));
            }

            var account = CurrentInventory.Accounts.Single(candidate => candidate.Id == accountId);
            var revision = CurrentInventory.Revision + 1;
            CurrentInventory = CurrentInventory with
            {
                Revision = revision,
                UpdatedAt = CurrentInventory.UpdatedAt.AddSeconds(1),
                Accounts =
                [
                    account with
                    {
                        ConfirmedCategory = category,
                        CategoryConfirmedRevision = revision,
                        UpdatedAt = CurrentInventory.UpdatedAt.AddSeconds(1),
                    },
                ],
            };
            CurrentInventory.Validate();
            InventoryChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(AccountInventoryOperationResult.Success(1));
        }

        public Task<AccountInventoryOperationResult> ClearCategoryOverrideAsync(
            Guid accountId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentInventory is null)
            {
                return Task.FromResult(AccountInventoryOperationResult.Failure(
                    AccountInventoryFailureCode.Conflict));
            }

            var account = CurrentInventory.Accounts.Single(candidate => candidate.Id == accountId);
            if (!account.ConfirmedCategory.HasValue)
            {
                return Task.FromResult(AccountInventoryOperationResult.Failure(
                    AccountInventoryFailureCode.Conflict));
            }

            var revision = CurrentInventory.Revision + 1;
            CurrentInventory = CurrentInventory with
            {
                Revision = revision,
                UpdatedAt = CurrentInventory.UpdatedAt.AddSeconds(1),
                Accounts =
                [
                    account with
                    {
                        ConfirmedCategory = null,
                        CategoryConfirmedRevision = null,
                        UpdatedAt = CurrentInventory.UpdatedAt.AddSeconds(1),
                    },
                ],
            };
            CurrentInventory.Validate();
            InventoryChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(AccountInventoryOperationResult.Success(1));
        }

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(
                AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput));

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => Task.FromResult(
                AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput));

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }
    }
}
