using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class AccountInventoryScreenViewModelTests
{
    [Fact]
    public void ActivationRefreshesAccountsWhenAnEarlierNotificationWasMissed()
    {
        var service = new TestAccountInventoryService([]);
        var viewModel = CreateViewModel(service);
        var imported = CreateAccount("Imported Mail");
        service.ReplaceWithoutNotification([imported]);

        Assert.Empty(viewModel.Accounts);
        viewModel.Activate();

        Assert.Equal(imported.Id, Assert.Single(viewModel.Accounts).Id);
    }

    [Fact]
    public void NewAccountEnablesEditorBeforePersistedAccountOnlyFeatures()
    {
        var viewModel = CreateViewModel(new TestAccountInventoryService([]));

        viewModel.NewAccountCommand.Execute(null);

        Assert.True(viewModel.IsEditingAccount);
        Assert.False(viewModel.HasPersistedAccount);
        Assert.False(viewModel.SaveCategoryCommand.CanExecute(null));
        viewModel.ProviderId = "Mail";
        viewModel.AccountName = "Primary mailbox";
        Assert.True(viewModel.SaveAccountCommand.CanExecute(null));
    }

    [Fact]
    public void AccountUrlWithEmbeddedCredentialsCannotBeSaved()
    {
        var viewModel = CreateViewModel(new TestAccountInventoryService([]));
        viewModel.NewAccountCommand.Execute(null);
        viewModel.ProviderId = "Mail";
        viewModel.AccountName = "Primary mailbox";
        viewModel.AccountUrl = "https://user:old-password@example.test/account";

        Assert.False(viewModel.SaveAccountCommand.CanExecute(null));
    }

    [Fact]
    public void CategoryFiltersUseCanonicalValuesInsteadOfDisplayText()
    {
        var email = CreateAccount("Gmail", confirmed: AccountRecoveryCategory.Email);
        var unknown = CreateAccount("Synthetic");
        var viewModel = CreateViewModel(new TestAccountInventoryService([unknown, email]));

        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.Email);

        Assert.Equal(email.Id, Assert.Single(viewModel.Accounts).Id);
        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.Unreviewed);
        Assert.Equal(unknown.Id, Assert.Single(viewModel.Accounts).Id);
    }

    [Fact]
    public void RuntimeLanguageChangeRelocalizesLabelsWithoutChangingCanonicalState()
    {
        var account = CreateAccount("Banking", confirmed: AccountRecoveryCategory.Critical);
        var service = new TestAccountInventoryService([account]);
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var viewModel = CreateViewModel(service, localization);

        localization.SetLanguage("de");

        Assert.Equal(account.Id, Assert.Single(viewModel.Accounts).Id);
        Assert.Equal("Kritisch", Assert.Single(viewModel.Accounts).CategoryText);
        Assert.Equal(AccountRecoveryCategory.Critical, Assert.Single(service.CurrentInventory!.Accounts).ConfirmedCategory);
    }

    [Fact]
    public async Task SavingCategoryPersistsStableEnumAndSelectsNextUnreviewedAccount()
    {
        var first = CreateAccount("Gmail");
        var second = CreateAccount("Banking");
        var service = new TestAccountInventoryService([first, second]);
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAccount = viewModel.Accounts.Single(item => item.Id == first.Id);
        viewModel.SelectedCategory = viewModel.Categories.Single(option =>
            option.Value == AccountRecoveryCategory.Email);

        var outcome = await viewModel.SaveCategoryCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal((first.Id, AccountRecoveryCategory.Email), service.LastCategoryDecision);
        Assert.Equal(second.Id, viewModel.SelectedAccount?.Id);
        Assert.True(viewModel.CanContinueRecovery);
        Assert.Equal(1, viewModel.RemainingCategoryCount);
    }

    [Fact]
    public void ResumeShowsRemainingReviewAndAllowsCompletionWithoutAnEmailAccount()
    {
        var reviewed = CreateAccount("Banking", confirmed: AccountRecoveryCategory.Critical);
        var remaining = CreateAccount("Unknown service");
        var service = new TestAccountInventoryService([reviewed, remaining]);
        var viewModel = CreateViewModel(service);

        Assert.True(viewModel.CanContinueRecovery);
        Assert.True(viewModel.HasRemainingCategoryReview);
        Assert.False(viewModel.IsCategoryReviewComplete);
        Assert.Equal(1, viewModel.RemainingCategoryCount);
        Assert.Contains("1", viewModel.TriageProgress, StringComparison.Ordinal);

        service.ReplaceWithoutNotification(
            [reviewed, remaining with { ConfirmedCategory = AccountRecoveryCategory.Unknown, CategoryConfirmedRevision = 2 }]);
        viewModel.Activate();

        Assert.True(viewModel.CanContinueRecovery);
        Assert.True(viewModel.IsCategoryReviewComplete);
        Assert.False(viewModel.HasConfirmedEmailCategory);
        Assert.Contains("without an email", viewModel.ContinuationGuidance, StringComparison.OrdinalIgnoreCase);
    }

    private static AccountInventoryScreenViewModel CreateViewModel(
        TestAccountInventoryService service,
        ResourceLocalizationService? localization = null) =>
        new(
            service,
            new TestConfirmationDialogService(),
            localization ?? new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")));

    private static AccountInventoryEntry CreateAccount(
        string provider,
        AccountRecoveryCategory? confirmed = null) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            $"{provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            null,
            AccountRecoveryCategory.Unknown,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            confirmed,
            confirmed.HasValue ? 1 : null,
            DateTimeOffset.UnixEpoch);

    private sealed class TestConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class TestAccountInventoryService(AccountInventoryEntry[] accounts)
        : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; private set; } =
            AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
                .ReplaceAccounts(accounts, DateTimeOffset.UnixEpoch.AddSeconds(1));

        public AccountRecoveryOrder? CurrentRecoveryOrder => CurrentInventory?.CreateRecoveryOrder();

        public (Guid AccountId, AccountRecoveryCategory Category)? LastCategoryDecision { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = CurrentInventory!.Accounts.ToList();
            var id = request.AccountId ?? Guid.NewGuid();
            var existing = entries.SingleOrDefault(account => account.Id == id);
            var replacement = new AccountInventoryEntry(
                id,
                request.ProviderId,
                request.AccountName,
                request.LoginIdentifier,
                request.AccountUrl,
                existing?.SuggestedCategory ?? AccountRecoveryCategory.Unknown,
                existing?.ClassificationCatalogVersion ?? RepositoryAccountClassificationCatalog.CurrentVersion,
                existing?.ConfirmedCategory,
                existing?.CategoryConfirmedRevision,
                DateTimeOffset.UnixEpoch.AddSeconds(CurrentInventory.Revision + 1));
            entries.RemoveAll(account => account.Id == id);
            entries.Add(replacement);
            Replace(entries);
            return Task.FromResult(AccountInventoryOperationResult.Success());
        }

        public Task<AccountInventoryOperationResult> CategorizeAsync(
            Guid accountId,
            AccountRecoveryCategory category,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCategoryDecision = (accountId, category);
            Replace(CurrentInventory!.Accounts.Select(account => account.Id == accountId
                ? account with
                {
                    ConfirmedCategory = category,
                    CategoryConfirmedRevision = CurrentInventory.Revision + 1,
                }
                : account));
            return Task.FromResult(AccountInventoryOperationResult.Success());
        }

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.NotFound));

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }

        public void ReplaceWithoutNotification(AccountInventoryEntry[] replacements)
        {
            CurrentInventory = CurrentInventory!.ReplaceAccounts(
                replacements,
                CurrentInventory.UpdatedAt.AddSeconds(1));
        }

        private void Replace(IEnumerable<AccountInventoryEntry> replacements)
        {
            CurrentInventory = CurrentInventory!.ReplaceAccounts(
                replacements,
                CurrentInventory.UpdatedAt.AddSeconds(1));
            InventoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
