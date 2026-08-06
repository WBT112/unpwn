using System.Text;
using Unpwn.App.Services;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Unpwn.Vault.Cryptography;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AccountInventoryServiceTests
{
    [Fact]
    public async Task InventoryPersistsReloadsAndSynchronizesDashboard()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);

        time = time.AddMinutes(1);
        var result = await service.UpsertAsync(
            new AccountInventoryUpsertRequest(
                null,
                "Google Mail",
                "Primary mailbox",
                "person@example.invalid",
                "https://accounts.example.invalid",
                AccountInventoryPriority.Critical),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(store.StoredRecord);
        Assert.Single(service.CurrentInventory?.Accounts ?? []);
        Assert.Contains(
            service.CurrentInventory!.Accounts[0].Roles,
            role => role.Role == AccountInventoryRole.EmailMailbox &&
                    role.Decision == AccountRoleDecision.Suggested);
        Assert.Single(session.LastSummaries);
        Assert.Equal(AccountCriticality.Critical, session.LastSummaries[0].Criticality);

        using var reloaded = new AccountInventoryService(store, session, () => time);
        await reloaded.InitializeAsync(CancellationToken.None);

        Assert.Equal(AccountInventoryLoadState.Loaded, reloaded.LoadState);
        Assert.Equal("Primary mailbox", reloaded.CurrentInventory?.Accounts.Single().AccountName);
    }

    [Fact]
    public async Task ImportedPasswordColumnNeverReachesPersistedInventory()
    {
        const string secret = "UNPWN_TEST_SECRET_old-password";
        var csv = $"service,login,password\nMail,person@example.invalid,{secret}\n";
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv));
        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping with
            {
                ExcludedPasswordColumns = analysis.DetectedPasswordColumns,
            });
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);

        var result = await service.ImportAsync(
            preview.Candidates,
            ImportDuplicateResolution.SkipDuplicates,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(store.StoredRecord);
        var persistedText = Encoding.UTF8.GetString(store.StoredRecord);
        Assert.DoesNotContain("old-password", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("password", persistedText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("person@example.invalid", service.CurrentInventory?.Accounts.Single().LoginIdentifier);
    }

    [Fact]
    public async Task DuplicateImportRequiresExplicitResolution()
    {
        const string csv = "service,login\nMail,person@example.invalid\nMail,person@example.invalid\n";
        var analysis = CsvAccountImportService.Analyze(new StringReader(csv));
        var preview = CsvAccountImportService.CreatePreview(
            new StringReader(csv),
            analysis.SuggestedMapping);
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);

        var unresolved = await service.ImportAsync(
            preview.Candidates,
            duplicateResolution: null,
            CancellationToken.None);
        var resolved = await service.ImportAsync(
            preview.Candidates,
            ImportDuplicateResolution.ImportAsSeparateAccounts,
            CancellationToken.None);

        Assert.False(unresolved.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.RequiresConfirmation, unresolved.FailureCode);
        Assert.True(resolved.Succeeded);
        Assert.Equal(2, service.CurrentInventory?.Accounts.Length);
    }

    [Fact]
    public async Task SuggestedRoleOnlyAffectsPlanAfterConfirmation()
    {
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService(
            IncidentIndicator.CompromisedRecoveryChannel);
        using var service = new AccountInventoryService(store, session, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);
        Assert.True((await service.UpsertAsync(
            new AccountInventoryUpsertRequest(
                null,
                "Google Mail",
                "Mailbox",
                "person@example.invalid",
                null,
                AccountInventoryPriority.High),
            CancellationToken.None)).Succeeded);
        var account = service.CurrentInventory!.Accounts.Single();

        Assert.False(account.HasConfirmedRecoveryRole);
        Assert.True((await service.DecideRoleAsync(
            account.Id,
            AccountInventoryRole.EmailMailbox,
            AccountRoleDecision.Confirmed,
            CancellationToken.None)).Succeeded);

        Assert.True(service.CurrentInventory!.Accounts.Single().HasConfirmedRecoveryRole);
        Assert.Equal(
            AccountInventoryPlanReasonCode.RecoveryChannelFirst,
            service.CurrentPlan?.Recommended?.ReasonCode);
    }

    [Fact]
    public async Task DependencyCycleRequiresReasonAndRemainsVisibleAsRisk()
    {
        var time = DateTimeOffset.UnixEpoch;
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => time);
        await service.InitializeAsync(CancellationToken.None);
        await service.UpsertAsync(CreateRequest("First"), CancellationToken.None);
        time = time.AddSeconds(1);
        await service.UpsertAsync(CreateRequest("Second"), CancellationToken.None);
        var first = service.CurrentInventory!.Accounts.Single(account => account.ProviderId == "First");
        var second = service.CurrentInventory.Accounts.Single(account => account.ProviderId == "Second");
        time = time.AddSeconds(1);
        Assert.True((await service.AddDependencyAsync(
            new AccountDependencyRequest(
                second.Id,
                first.Id,
                AccountDependencyKind.PasswordReset,
                null),
            CancellationToken.None)).Succeeded);

        time = time.AddSeconds(1);
        var withoutReason = await service.AddDependencyAsync(
            new AccountDependencyRequest(
                first.Id,
                second.Id,
                AccountDependencyKind.IdentityProvider,
                null),
            CancellationToken.None);
        time = time.AddSeconds(1);
        var withReason = await service.AddDependencyAsync(
            new AccountDependencyRequest(
                first.Id,
                second.Id,
                AccountDependencyKind.IdentityProvider,
                "The normal recovery channel is unavailable."),
            CancellationToken.None);

        Assert.False(withoutReason.Succeeded);
        Assert.Equal(AccountInventoryFailureCode.RequiresOverrideReason, withoutReason.FailureCode);
        Assert.True(withReason.Succeeded);
        Assert.Contains(service.CurrentPlan!.Issues, issue =>
            issue.Kind == AccountInventoryIssueKind.DependencyOverride &&
            issue.AccountId == first.Id);
        Assert.Contains(session.LastSummaries, summary =>
            summary.AccountId == first.Id && summary.UnresolvedRisks == 1);
    }

    [Fact]
    public async Task LockClearsMaterializedInventoryButKeepsPersistedRecord()
    {
        var store = new TestEncryptedRecordStore();
        var session = new TestRecoverySessionService();
        using var service = new AccountInventoryService(store, session, () => DateTimeOffset.UnixEpoch);
        await service.InitializeAsync(CancellationToken.None);
        await service.UpsertAsync(CreateRequest("Mail"), CancellationToken.None);

        service.ClearForLock();

        Assert.Equal(AccountInventoryLoadState.Locked, service.LoadState);
        Assert.Null(service.CurrentInventory);
        Assert.NotNull(store.StoredRecord);
    }

    private static AccountInventoryUpsertRequest CreateRequest(string provider) =>
        new(
            null,
            provider,
            provider,
            $"{provider.ToLowerInvariant()}@example.invalid",
            null,
            AccountInventoryPriority.Normal);

    private sealed class TestEncryptedRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked { get; set; } = true;

        public byte[]? StoredRecord { get; private set; }

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Validate();
            return Task.FromResult(StoredRecord?.ToArray());
        }

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptor.Validate();
            StoredRecord = plaintext.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class TestRecoverySessionService : IRecoverySessionService
    {
        public TestRecoverySessionService(IncidentIndicator indicators = IncidentIndicator.None)
        {
            CurrentSession = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic session",
                new RecoveryIncidentIntake(indicators, null),
                DateTimeOffset.UnixEpoch);
        }

        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; }

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public IReadOnlyList<RecoveryAccountDashboardEntry> LastSummaries { get; private set; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSummaries = accounts.ToArray();
            CurrentSession = CurrentSession!.ReplaceAccounts(accounts, CurrentSession.UpdatedAt.AddSeconds(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public void ClearForLock()
        {
        }
    }
}
