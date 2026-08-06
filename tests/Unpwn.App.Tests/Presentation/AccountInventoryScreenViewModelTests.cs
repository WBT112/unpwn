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
    public void FiltersSearchesAndSortsPersistedInventoryWithoutUsingDisplayTextForSemantics()
    {
        var critical = CreateAccount(
            "Primary Mail",
            AccountInventoryPriority.Critical,
            AccountInventoryRole.EmailMailbox,
            AccountRoleDecision.Confirmed);
        var normal = CreateAccount(
            "Forum",
            AccountInventoryPriority.Normal,
            AccountInventoryRole.IdentityProvider,
            AccountRoleDecision.Suggested);
        var service = new TestAccountInventoryService([normal, critical]);
        var viewModel = CreateViewModel(service);

        Assert.Equal(2, viewModel.Accounts.Count);
        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.RecoveryChannels);
        Assert.Single(viewModel.Accounts);
        Assert.Equal(critical.Id, viewModel.Accounts[0].Id);

        viewModel.SelectedFilter = viewModel.Filters.Single(option =>
            option.Value == AccountInventoryFilter.All);
        viewModel.SearchText = "forum";
        Assert.Single(viewModel.Accounts);
        Assert.Equal(normal.Id, viewModel.Accounts[0].Id);
    }

    [Fact]
    public void RuntimeLanguageChangeRelocalizesLabelsWithoutChangingInventoryState()
    {
        var account = CreateAccount(
            "Primary Mail",
            AccountInventoryPriority.Critical,
            AccountInventoryRole.EmailMailbox,
            AccountRoleDecision.Confirmed);
        var service = new TestAccountInventoryService([account]);
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var viewModel = CreateViewModel(service, localization);
        var accountId = viewModel.Accounts.Single().Id;

        localization.SetLanguage("de");

        Assert.Equal(accountId, viewModel.Accounts.Single().Id);
        Assert.Equal("Kritisch", viewModel.Accounts.Single().PriorityText);
        Assert.Contains("E-Mail-Postfach", viewModel.Accounts.Single().RoleText, StringComparison.Ordinal);
        Assert.Equal("Konten", viewModel.Title);
    }

    [Fact]
    public async Task ExplicitRoleDecisionIsForwardedAsStableEnum()
    {
        var account = CreateAccount(
            "Google Mail",
            AccountInventoryPriority.High,
            AccountInventoryRole.EmailMailbox,
            AccountRoleDecision.Suggested);
        var service = new TestAccountInventoryService([account]);
        var viewModel = CreateViewModel(service);
        viewModel.SelectedAccount = viewModel.Accounts.Single();
        viewModel.SelectedSuggestedRole = viewModel.SuggestedRoles.Single();

        var outcome = await viewModel.AcceptSuggestedRoleCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(
            (account.Id, AccountInventoryRole.EmailMailbox, AccountRoleDecision.Confirmed),
            service.LastRoleDecision);
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
        AccountInventoryPriority priority,
        AccountInventoryRole role,
        AccountRoleDecision decision) =>
        new(
            Guid.NewGuid(),
            provider,
            provider,
            $"{provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            null,
            priority,
            [new AccountRoleState(role, decision)],
            [],
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

    private sealed class TestAccountInventoryService : IAccountInventoryService
    {
        public TestAccountInventoryService(AccountInventoryEntry[] accounts)
        {
            CurrentInventory = AccountInventoryState.Empty(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
                .ReplaceAccounts(accounts, DateTimeOffset.UnixEpoch.AddSeconds(1));
        }

        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; private set; }

        public AccountInventoryPlan? CurrentPlan => CurrentInventory?.CreatePlan(IncidentIndicator.None);

        public (Guid AccountId, AccountInventoryRole Role, AccountRoleDecision Decision)? LastRoleDecision { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(
            AccountInventoryUpsertRequest request,
            CancellationToken cancellationToken) => Failure();

        public Task<AccountInventoryOperationResult> DecideRoleAsync(
            Guid accountId,
            AccountInventoryRole role,
            AccountRoleDecision decision,
            CancellationToken cancellationToken)
        {
            LastRoleDecision = (accountId, role, decision);
            var accounts = CurrentInventory!.Accounts.ToArray();
            var index = Array.FindIndex(accounts, account => account.Id == accountId);
            var account = accounts[index];
            accounts[index] = account with
            {
                Roles = account.Roles
                    .Where(candidate => candidate.Role != role)
                    .Append(new AccountRoleState(role, decision))
                    .ToArray(),
            };
            CurrentInventory = CurrentInventory.ReplaceAccounts(
                accounts,
                CurrentInventory.UpdatedAt.AddSeconds(1));
            InventoryChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(AccountInventoryOperationResult.Success());
        }

        public Task<AccountInventoryOperationResult> AddDependencyAsync(
            AccountDependencyRequest request,
            CancellationToken cancellationToken) => Failure();

        public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
            Guid accountId,
            Guid dependsOnAccountId,
            AccountDependencyKind kind,
            CancellationToken cancellationToken) => Failure();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(
            Guid accountId,
            bool dependencyImpactAcknowledged,
            CancellationToken cancellationToken) => Failure();

        public Task<AccountInventoryOperationResult> ImportAsync(
            IReadOnlyCollection<ImportAccountCandidate> candidates,
            ImportDuplicateResolution? duplicateResolution,
            CancellationToken cancellationToken) => Failure();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
        }

        private static Task<AccountInventoryOperationResult> Failure() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.Conflict));
    }
}
