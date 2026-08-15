using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application;
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
        var imported = CreateAccount("mystery.example");
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
    public void NeedsReviewFilterExcludesKnownAutomaticSuggestions()
    {
        var email = CreateAccount("gmail");
        var unknown = CreateAccount("mystery.example");
        var viewModel = CreateViewModel(new TestAccountInventoryService([unknown, email]));

        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.Email);

        Assert.Equal(email.Id, Assert.Single(viewModel.Accounts).Id);
        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.NeedsReview);
        Assert.Equal(unknown.Id, Assert.Single(viewModel.Accounts).Id);
    }

    [Fact]
    public void UnknownAccountsAreSelectedAndOrderedBeforeKnownSuggestions()
    {
        var email = CreateAccount("gmail");
        var critical = CreateAccount("bitwarden");
        var unknown = CreateAccount("mystery.example");
        var viewModel = CreateViewModel(new TestAccountInventoryService([email, critical, unknown]));

        Assert.Equal(unknown.Id, viewModel.Accounts[0].Id);
        Assert.Equal(unknown.Id, viewModel.SelectedAccount?.Id);
        Assert.Equal("Needs review", viewModel.Accounts[0].CategoryText);
        Assert.Equal(
            "Not automatically recognized — choose a recovery category",
            viewModel.Accounts[0].ReviewText);
        Assert.Equal(1, viewModel.RemainingCategoryCount);
        Assert.Equal(
            "Automatically categorized",
            viewModel.Accounts.Single(item => item.Id == email.Id).ReviewText);
        Assert.Equal(
            "Automatically categorized",
            viewModel.Accounts.Single(item => item.Id == critical.Id).ReviewText);
    }

    [Fact]
    public void AllKnownSuggestionsRequireNoConfirmationAndKeepProvenance()
    {
        var email = CreateAccount("gmail");
        var critical = CreateAccount("bitwarden");
        var routine = CreateAccount("streaming");
        var service = new TestAccountInventoryService([critical, routine, email]);
        var viewModel = CreateViewModel(service);

        Assert.Equal(0, viewModel.RemainingCategoryCount);
        Assert.True(viewModel.IsCategoryReviewComplete);
        Assert.True(viewModel.CanContinueRecovery);
        Assert.True(viewModel.HasEmailCategory);
        Assert.All(viewModel.Accounts, item =>
            Assert.Equal("Automatically categorized", item.ReviewText));
        Assert.All(service.CurrentInventory!.Accounts, account =>
            Assert.Null(account.ConfirmedCategory));
        Assert.Contains("email account was identified", viewModel.ContinuationGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeLanguageChangeRelocalizesReviewStateWithoutChangingCanonicalState()
    {
        var account = CreateAccount("bitwarden");
        var service = new TestAccountInventoryService([account]);
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var viewModel = CreateViewModel(service, localization);

        localization.SetLanguage("de");

        Assert.Equal(account.Id, Assert.Single(viewModel.Accounts).Id);
        Assert.Equal("Kritisch", Assert.Single(viewModel.Accounts).CategoryText);
        Assert.Equal("Automatisch kategorisiert", Assert.Single(viewModel.Accounts).ReviewText);
        Assert.Null(Assert.Single(service.CurrentInventory!.Accounts).ConfirmedCategory);
    }

    [Fact]
    public async Task SavingUnknownCategoryCreatesExplicitOverrideAndMovesToNextRequiredReview()
    {
        var first = CreateAccount("first-unknown.example");
        var second = CreateAccount("second-unknown.example");
        var known = CreateAccount("gmail");
        var service = new TestAccountInventoryService([known, first, second]);
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAccount = viewModel.Accounts.Single(item => item.Id == first.Id);
        viewModel.SelectedCategory = viewModel.Categories.Single(option =>
            option.Value == AccountRecoveryCategory.NonCritical);

        var outcome = await viewModel.SaveCategoryCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal((first.Id, AccountRecoveryCategory.NonCritical), service.LastCategoryDecision);
        Assert.Equal(second.Id, viewModel.SelectedAccount?.Id);
        Assert.Equal(1, viewModel.RemainingCategoryCount);
        Assert.Equal(
            "Changed by you",
            viewModel.Accounts.Single(item => item.Id == first.Id).ReviewText);
        Assert.Null(service.CurrentInventory!.Accounts.Single(account => account.Id == known.Id).ConfirmedCategory);
    }

    [Fact]
    public async Task DirectContinueAdvancesCanonicalTriageWithoutConfirmingAutomaticCategories()
    {
        var email = CreateAccount("gmail");
        var inventory = new TestAccountInventoryService([email]);
        var flow = new TestRecoveryFlowService(StartAtAccountInventory());
        var viewModel = CreateViewModel(inventory, recoveryFlow: flow);
        var continuationRequested = false;
        viewModel.ContinueToRecoveryRequested += (_, _) => continuationRequested = true;

        Assert.True(viewModel.CanContinueRecovery);
        var outcome = await viewModel.ContinueRecoveryCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(1, flow.AdvanceCalls);
        Assert.True(continuationRequested);
        Assert.Equal(NextUserTaskTarget.RecoveryOverview, flow.NextTask.Target);
        Assert.Null(Assert.Single(inventory.CurrentInventory!.Accounts).ConfirmedCategory);
        Assert.Null(inventory.LastCategoryDecision);
    }

    [Fact]
    public void ResumeReturnsToFirstAccountThatActuallyNeedsReview()
    {
        var known = CreateAccount("gmail");
        var firstUnknown = CreateAccount("first-unknown.example");
        var secondUnknown = CreateAccount("second-unknown.example");
        var service = new TestAccountInventoryService([known, firstUnknown, secondUnknown]);
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAccount = viewModel.Accounts.Single(item => item.Id == known.Id);

        viewModel.Activate();

        Assert.Equal(firstUnknown.Id, viewModel.SelectedAccount?.Id);
        Assert.True(viewModel.SelectedAccount?.Account.RequiresCategoryReview);
    }

    [Fact]
    public void ReviewedInventoryWithoutEmailCanStillContinueAfterWarning()
    {
        var critical = CreateAccount("banking", confirmed: AccountRecoveryCategory.Critical);
        var routine = CreateAccount("mystery.example", confirmed: AccountRecoveryCategory.NonCritical);
        var service = new TestAccountInventoryService([critical, routine]);
        var viewModel = CreateViewModel(service);

        Assert.True(viewModel.CanContinueRecovery);
        Assert.True(viewModel.IsCategoryReviewComplete);
        Assert.False(viewModel.HasEmailCategory);
        Assert.Equal(0, viewModel.RemainingCategoryCount);
        Assert.Contains("No email account", viewModel.ContinuationGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.ContinueRecoveryCommand.CanExecute(null));
    }

    [Fact]
    public void InvalidCurrentModelFailsClosedWithoutReplacingServiceState()
    {
        var invalidAccount = CreateAccount("Invalid") with
        {
            SuggestedCategory = (AccountRecoveryCategory)99,
        };
        var invalidState = new AccountInventoryState(
            Guid.NewGuid(),
            1,
            DateTimeOffset.UnixEpoch,
            [invalidAccount]);
        var service = new InvalidCurrentModelInventoryService(invalidState);

        var viewModel = new AccountInventoryScreenViewModel(
            service,
            new TestConfirmationDialogService(),
            new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")));

        Assert.True(viewModel.IsCorrupted);
        Assert.False(viewModel.CanMutate);
        Assert.Empty(viewModel.Accounts);
        Assert.Contains("not replaced", viewModel.InventorySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Same(invalidState, service.CurrentInventory);
    }

    private static AccountInventoryScreenViewModel CreateViewModel(
        TestAccountInventoryService service,
        ResourceLocalizationService? localization = null,
        IRecoveryFlowService? recoveryFlow = null) =>
        new(
            service,
            new TestConfirmationDialogService(),
            localization ?? new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")),
            recoveryFlow);

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

    private static NextUserTask StartAtAccountInventory() =>
        new(
            RecoveryWizardStepId.AccountInventory,
            NextUserTaskState.ActionAvailable,
            NextUserTaskCode.ReviewAccountCategories,
            NextUserTaskTarget.AccountTriage,
            RecoveryWizardStepId.AccountTriage);

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

    private sealed class TestRecoveryFlowService(NextUserTask nextTask) : IRecoveryFlowService
    {
        public event EventHandler? NextTaskChanged;

        public int AdvanceCalls { get; private set; }

        public RecoveryWizardState Current { get; } = RecoveryWizardState.Create(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch);

        public NextUserTask NextTask { get; private set; } = nextTask;

        public Task<RecoveryFlowMoveResult> AdvanceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdvanceCalls++;
            var previous = NextTask;
            if (previous.Target != NextUserTaskTarget.AccountTriage || !previous.RequiresTransition)
            {
                return Task.FromResult(RecoveryFlowMoveResult.Failure(
                    RecoveryFlowMoveFailureCode.Blocked,
                    previous));
            }

            NextUserTask advanced = new(
                RecoveryWizardStepId.AccountTriage,
                NextUserTaskState.ActionAvailable,
                NextUserTaskCode.ContinueToRecovery,
                NextUserTaskTarget.RecoveryOverview,
                RecoveryWizardStepId.RecoveryOverview);
            NextTask = advanced;
            NextTaskChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoveryFlowMoveResult.Success(advanced));
        }

        public Task<RecoveryFlowMoveResult> BeginCompletionReviewAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoveryFlowMoveResult.Failure(
                RecoveryFlowMoveFailureCode.Blocked,
                NextTask));

        public Task<RecoveryFlowMoveResult> MarkCompletionReviewReadyAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoveryFlowMoveResult.Failure(
                RecoveryFlowMoveFailureCode.Blocked,
                NextTask));
    }

    private sealed class InvalidCurrentModelInventoryService(AccountInventoryState invalidState)
        : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; } = invalidState;

        public AccountRecoveryOrder? CurrentRecoveryOrder => CurrentInventory?.CreateRecoveryOrder();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> CategorizeAsync(
            Guid accountId,
            AccountRecoveryCategory category,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => Unsupported();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock() => InventoryChanged?.Invoke(this, EventArgs.Empty);

        private static Task<AccountInventoryOperationResult> Unsupported() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.Corrupted));
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
