using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryCompletionServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CleanPersistedStateCanCompleteWithoutRiskAcceptance()
    {
        var accountId = Guid.NewGuid();
        var session = Session(Account(
            accountId,
            AccountCriticality.Critical,
            AccountRecoveryStatus.FullyReviewed,
            completed: 2,
            total: 2));
        var sessionService = new TestSessionService(session);
        var service = new RecoveryCompletionService(
            sessionService,
            new TestInventoryService(Inventory(session.Id, accountId), new AccountRecoveryOrder([])),
            new TestCredentialRepository([]),
            () => StartedAt.AddMinutes(5));

        var review = await service.ReviewAsync(CancellationToken.None);
        var result = await service.CompleteAsync(
            review.Preflight!,
            unresolvedRiskExplicitlyAccepted: false,
            archive: false,
            CancellationToken.None);

        Assert.True(review.Succeeded);
        Assert.True(review.Preflight!.IsClean);
        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryCompletionOutcome.Completed, result.Completion!.Outcome);
        Assert.Equal(session.Revision, sessionService.ExpectedRevision);
        Assert.DoesNotContain(result.Completion.Report.Issues,
            issue => issue.Kind == RecoveryCompletionIssueKind.CredentialRetainedInVault);
    }

    [Fact]
    public async Task PreflightDistinguishesIncompleteBlockedLostAndCredentialStates()
    {
        var accountId = Guid.NewGuid();
        var deferredAccount = Account(
            accountId,
            AccountCriticality.Critical,
            AccountRecoveryStatus.NotFullySecured,
            completed: 0,
            total: 3,
            blocked: 1,
            failed: 1,
            risks: 2,
            accessLost: true) with
        {
            DeferralCount = 1,
            DeferredAt = StartedAt,
        };
        var session = Session(deferredAccount);
        var inventory = Inventory(session.Id, accountId);
        var queue = new AccountRecoveryOrder([]);
        var notExported = GeneratedCredentialMetadata.Create(
            Guid.NewGuid(), accountId, Guid.NewGuid(), StartedAt);
        var exported = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(), accountId, Guid.NewGuid(), StartedAt)
            .MarkExported(Guid.NewGuid(), StartedAt.AddMinutes(1));
        var deleted = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(), accountId, Guid.NewGuid(), StartedAt)
            .Delete(Guid.NewGuid(), StartedAt.AddMinutes(1));
        var service = new RecoveryCompletionService(
            new TestSessionService(session),
            new TestInventoryService(inventory, queue),
            new TestCredentialRepository([notExported, exported, deleted]),
            () => StartedAt.AddMinutes(5));

        var review = await service.ReviewAsync(CancellationToken.None);

        Assert.True(review.Succeeded);
        RecoveryCompletionIssueKind[] expectedKinds =
        [
            RecoveryCompletionIssueKind.DeferredAccount,
            RecoveryCompletionIssueKind.CriticalAccountNotFullyReviewed,
            RecoveryCompletionIssueKind.RequiredActionIncomplete,
            RecoveryCompletionIssueKind.RequiredActionBlocked,
            RecoveryCompletionIssueKind.RequiredActionFailed,
            RecoveryCompletionIssueKind.LostAccountAccess,
            RecoveryCompletionIssueKind.UnresolvedRisk,
            RecoveryCompletionIssueKind.CredentialNotExported,
            RecoveryCompletionIssueKind.PasswordManagerImportUnconfirmed,
            RecoveryCompletionIssueKind.CredentialRetainedInVault,
            RecoveryCompletionIssueKind.PlaintextExportCleanupPending,
        ];
        Assert.All(expectedKinds, kind => Assert.Contains(review.Preflight!.Issues, issue => issue.Kind == kind));
        Assert.True(review.Preflight!.RequiresExplicitRiskAcceptance);
        Assert.Equal(1, review.Report!.CredentialsNotExported);
        Assert.Equal(1, review.Report.PasswordManagerImportsUnconfirmed);
        Assert.Equal(2, review.Report.RetainedCredentials);
        Assert.Equal(1, review.Report.DeletedCredentials);
        Assert.Equal(1, review.Report.PlaintextCleanupPending);
    }

    [Fact]
    public async Task UnresolvedCompletionRequiresAcknowledgementAndBecomesFollowUpRequired()
    {
        var accountId = Guid.NewGuid();
        var session = Session(Account(
            accountId,
            AccountCriticality.Critical,
            AccountRecoveryStatus.Open,
            completed: 0,
            total: 1));
        var sessionService = new TestSessionService(session);
        var service = new RecoveryCompletionService(
            sessionService,
            new TestInventoryService(Inventory(session.Id, accountId), new AccountRecoveryOrder([])),
            new TestCredentialRepository([]),
            () => StartedAt.AddMinutes(5));
        var review = await service.ReviewAsync(CancellationToken.None);

        var rejected = await service.CompleteAsync(
            review.Preflight!, false, false, CancellationToken.None);

        Assert.False(rejected.Succeeded);
        Assert.Equal(RecoveryCompletionFailureCode.RiskAcceptanceRequired, rejected.FailureCode);
        Assert.Equal(0, sessionService.CompleteCalls);

        var accepted = await service.CompleteAsync(
            review.Preflight!, true, false, CancellationToken.None);

        Assert.True(accepted.Succeeded);
        Assert.Equal(RecoveryCompletionOutcome.FollowUpRequired, accepted.Completion!.Outcome);
        Assert.True(accepted.Completion.UnresolvedRiskExplicitlyAccepted);
        Assert.Equal(1, sessionService.CompleteCalls);
    }

    [Fact]
    public async Task RetainedEncryptedCredentialIsAVisibleWarningButNotAnUnresolvedRisk()
    {
        var accountId = Guid.NewGuid();
        var session = Session(Account(
            accountId,
            AccountCriticality.Routine,
            AccountRecoveryStatus.FullyReviewed,
            completed: 1,
            total: 1));
        var credential = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(), accountId, Guid.NewGuid(), StartedAt)
            .MarkExported(Guid.NewGuid(), StartedAt.AddMinutes(1))
            .ConfirmPasswordManagerImport(Guid.NewGuid(), StartedAt.AddMinutes(2))
            .ConfirmPlaintextExportCleanup(Guid.NewGuid(), StartedAt.AddMinutes(3));
        var service = new RecoveryCompletionService(
            new TestSessionService(session),
            new TestInventoryService(Inventory(session.Id, accountId), new AccountRecoveryOrder([])),
            new TestCredentialRepository([credential]),
            () => StartedAt.AddMinutes(5));

        var review = await service.ReviewAsync(CancellationToken.None);
        var result = await service.CompleteAsync(
            review.Preflight!, false, false, CancellationToken.None);

        Assert.False(review.Preflight!.IsClean);
        Assert.True(review.Preflight.HasWarnings);
        Assert.False(review.Preflight.RequiresExplicitRiskAcceptance);
        Assert.Single(review.Preflight.Issues,
            issue => issue.Kind == RecoveryCompletionIssueKind.CredentialRetainedInVault);
        Assert.True(result.Succeeded);
        Assert.Equal(RecoveryCompletionOutcome.Completed, result.Completion!.Outcome);
    }

    [Fact]
    public async Task ChangedPersistedStateInvalidatesReviewedPreflight()
    {
        var accountId = Guid.NewGuid();
        var original = Session(Account(
            accountId,
            AccountCriticality.Routine,
            AccountRecoveryStatus.FullyReviewed,
            completed: 1,
            total: 1));
        var sessionService = new TestSessionService(original);
        var service = new RecoveryCompletionService(
            sessionService,
            new TestInventoryService(Inventory(original.Id, accountId), new AccountRecoveryOrder([])),
            new TestCredentialRepository([]),
            () => StartedAt.AddMinutes(5));
        var review = await service.ReviewAsync(CancellationToken.None);
        sessionService.CurrentSession = original.ReplaceAccounts(
            [Account(accountId, AccountCriticality.Routine, AccountRecoveryStatus.Open, 0, 1)],
            StartedAt.AddMinutes(1));

        var result = await service.CompleteAsync(
            review.Preflight!, false, false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RecoveryCompletionFailureCode.StateChanged, result.FailureCode);
        Assert.Equal(0, sessionService.CompleteCalls);
    }

    [Fact]
    public async Task ExistingCompletionReopensAsReadOnlyReport()
    {
        var accountId = Guid.NewGuid();
        var active = Session(Account(
            accountId,
            AccountCriticality.Routine,
            AccountRecoveryStatus.FullyReviewed,
            completed: 1,
            total: 1));
        var report = new RecoveryCompletionReport(
            active.Id, StartedAt, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        var record = new RecoveryCompletionRecord(
            RecoveryCompletionOutcome.Completed,
            StartedAt,
            false,
            report);
        var completed = active.Complete(record, StartedAt);
        var service = new RecoveryCompletionService(
            new TestSessionService(completed),
            new TestInventoryService(Inventory(active.Id, accountId), new AccountRecoveryOrder([])),
            new TestCredentialRepository([]),
            () => StartedAt.AddMinutes(5));

        var review = await service.ReviewAsync(CancellationToken.None);

        Assert.True(review.Succeeded);
        Assert.Same(record, review.ExistingCompletion);
        Assert.Same(report, review.Report);
        Assert.True(completed.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => completed.ReplaceAccounts([], StartedAt.AddMinutes(1)));
    }

    [Fact]
    public async Task ReportWriterNeverOverwritesAndSerializedShapeHasNoSensitiveFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"unpwn-completion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "report.json");
            var sessionId = Guid.NewGuid();
            var report = new RecoveryCompletionReport(
                sessionId, StartedAt, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
            var writer = new JsonRecoveryCompletionReportWriter();

            var first = await writer.WriteAsync(report, path, CancellationToken.None);
            var original = await File.ReadAllTextAsync(path);
            var second = await writer.WriteAsync(
                report with { AccountsTotal = 1 }, path, CancellationToken.None);

            Assert.True(first.Succeeded);
            Assert.False(second.Succeeded);
            Assert.Equal(RecoveryCompletionReportWriteFailureCode.AlreadyExists, second.FailureCode);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.DoesNotContain("PasswordSecret", original, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LoginIdentifier", original, StringComparison.Ordinal);
            Assert.DoesNotContain("AccountName", original, StringComparison.Ordinal);
            Assert.DoesNotContain("UserNote", original, StringComparison.Ordinal);
            Assert.DoesNotContain("CredentialId", original, StringComparison.Ordinal);
            Assert.DoesNotContain("Incident", original, StringComparison.Ordinal);
            Assert.DoesNotContain("Description", original, StringComparison.Ordinal);

            var failed = await writer.WriteAsync(
                report,
                Path.Combine(directory, "missing", "report.json"),
                CancellationToken.None);
            Assert.False(failed.Succeeded);
            Assert.Equal(RecoveryCompletionReportWriteFailureCode.InvalidPath, failed.FailureCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledReportWriteLeavesNoFinalOrTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"unpwn-completion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "report.json");
            var report = new RecoveryCompletionReport(
                Guid.NewGuid(), StartedAt, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new JsonRecoveryCompletionReportWriter().WriteAsync(
                    report,
                    path,
                    cancellation.Token));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RecoverySessionWorkspace Session(params RecoveryAccountDashboardEntry[] accounts) =>
        RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Synthetic recovery",
                RecoveryIncidentIntake.Empty,
                StartedAt)
            .ReplaceAccounts(accounts, StartedAt);

    private static RecoveryAccountDashboardEntry Account(
        Guid accountId,
        AccountCriticality criticality,
        AccountRecoveryStatus status,
        int completed,
        int total,
        int blocked = 0,
        int failed = 0,
        int risks = 0,
        bool accessLost = false) =>
        new(
            accountId,
            "synthetic.example",
            criticality,
            status,
            completed,
            total,
            completed,
            total,
            blocked,
            failed,
            risks,
            accessLost,
            0,
            0,
            "review-action")
        {
            Category = criticality == AccountCriticality.Critical
                ? AccountRecoveryCategory.Critical
                : AccountRecoveryCategory.NonCritical,
        };

    private static AccountInventoryState Inventory(
        Guid sessionId,
        Guid accountId) =>
        new(
            sessionId,
            1,
            StartedAt,
            [
                new AccountInventoryEntry(
                    accountId,
                    "synthetic.example",
                    "Synthetic account",
                    null,
                    null,
                    AccountRecoveryCategory.Unknown,
                    RepositoryAccountClassificationCatalog.CurrentVersion,
                    ConfirmedCategory: null,
                    CategoryConfirmedRevision: null,
                    StartedAt),
            ]);

    private sealed class TestSessionService(RecoverySessionWorkspace session) : IRecoverySessionService
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; set; } = session;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public int CompleteCalls { get; private set; }

        public long? ExpectedRevision { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CompleteAsync(
            RecoveryCompletionRecord completion,
            long expectedSessionRevision,
            CancellationToken cancellationToken)
        {
            CompleteCalls++;
            ExpectedRevision = expectedSessionRevision;
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) => Unsupported();

        public void ClearForLock() => SessionChanged?.Invoke(this, EventArgs.Empty);

        private static Task<RecoverySessionOperationResult> Unsupported() =>
            Task.FromResult(RecoverySessionOperationResult.Failure(
                RecoverySessionOperationFailureCode.Conflict));
    }

    private sealed class TestInventoryService(
        AccountInventoryState inventory,
        AccountRecoveryOrder queue) : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory => inventory;

        public AccountRecoveryOrder? CurrentRecoveryOrder => queue;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(AccountInventoryUpsertRequest request, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> CategorizeAsync(Guid accountId, AccountRecoveryCategory category, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(Guid accountId, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> ImportAsync(IReadOnlyCollection<ImportAccountCandidate> candidates, ImportDuplicateResolution? duplicateResolution, CancellationToken cancellationToken) => Unsupported();

        public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock() => InventoryChanged?.Invoke(this, EventArgs.Empty);

        private static Task<AccountInventoryOperationResult> Unsupported() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Conflict));
    }

    private sealed class TestCredentialRepository(IReadOnlyList<GeneratedCredentialMetadata> credentials)
        : IGeneratedCredentialRepository
    {
        public bool IsUnlocked => true;

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(credentials);

        public Task<GeneratedCredentialCreationResult> GenerateAsync(Guid accountId, CredentialGenerationPolicy policy, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(GeneratedCredentialReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CredentialSecretLease?> ReadSecretAsync(GeneratedCredentialReference reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> MarkUsedAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> ConfirmAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(IReadOnlyCollection<GeneratedCredentialReference> references, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GeneratedCredentialOperationResult> DeleteAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
