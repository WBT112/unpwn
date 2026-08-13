using System.Text.Json;
using Unpwn.App.Services;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class AccountRecoveryExecutionServiceTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 6, 16, 0, 0, TimeSpan.Zero);
    private readonly WorkspaceMutationCoordinator _mutations = new();

    [Fact]
    public async Task CreationPersistsExecutionAndDashboardProjectionInOneBatch()
    {
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(1));
        var workflow = CreateWorkflow();
        var accountId = Guid.NewGuid();

        var result = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                accountId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.BatchWrites);
        Assert.Equal(2, store.LastBatchSize);
        Assert.True(session.PreparedProjectionCommitted);
        var dashboardAccount = Assert.Single(session.CurrentSession!.Accounts);
        Assert.Equal(accountId, dashboardAccount.AccountId);
        Assert.Equal(AccountRecoveryStatus.Open, dashboardAccount.RecoveryStatus);
    }

    [Fact]
    public async Task FailedBatchPublishesNeitherExecutionNorProjection()
    {
        var store = new InMemoryAtomicRecordStore { FailBatchWrites = true };
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(1));

        var request = new AccountRecoveryExecutionCreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreateWorkflow(),
            RecoveryPath.AuthenticatedChange,
            ProjectionContext());
        var result = await service.CreateAsync(
            request,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AccountRecoveryExecutionFailureCode.PersistenceFailure, result.FailureCode);
        Assert.False(session.PreparedProjectionCommitted);
        Assert.Empty(session.CurrentSession!.Accounts);
        Assert.Empty(store.Records);

        store.FailBatchWrites = false;
        var retry = await service.CreateAsync(request, CancellationToken.None);
        var repeatedRetry = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(retry.Succeeded);
        Assert.True(repeatedRetry.Succeeded);
        Assert.True(session.PreparedProjectionCommitted);
        Assert.Equal(2, store.BatchWrites);
    }

    [Fact]
    public async Task RepeatedOperationIsIdempotentAndRevisionConflictIsRejected()
    {
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(1));
        var workflow = CreateWorkflow();
        var accountId = Guid.NewGuid();
        var createOperation = Guid.NewGuid();
        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                createOperation,
                accountId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);
        var repeatedCreate = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                createOperation,
                accountId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);
        var transitionOperation = Guid.NewGuid();
        var transition = TransitionRequest(
            transitionOperation,
            accountId,
            created.State!.Revision,
            workflow,
            AccountRecoveryExecutionTransitionKind.StartAction,
            "identify-account");
        var started = await service.ApplyAsync(transition, CancellationToken.None);
        var repeated = await service.ApplyAsync(transition, CancellationToken.None);
        var stale = await service.ApplyAsync(
            TransitionRequest(
                Guid.NewGuid(),
                accountId,
                expectedRevision: 0,
                workflow,
                AccountRecoveryExecutionTransitionKind.CompleteAction,
                "identify-account") with
            {
                CompletionCriteriaAcknowledged = true,
            },
            CancellationToken.None);

        Assert.True(repeatedCreate.Succeeded);
        AssertStateEquivalent(created.State, repeatedCreate.State);
        Assert.True(started.Succeeded);
        Assert.True(repeated.Succeeded);
        AssertStateEquivalent(started.State, repeated.State);
        Assert.False(stale.Succeeded);
        Assert.Equal(AccountRecoveryExecutionFailureCode.Conflict, stale.FailureCode);
    }

    [Fact]
    public async Task PersistedExecutionReloadsAndUsesStructuredPrerequisiteReason()
    {
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(1));
        var workflow = CreateWorkflow();
        var accountId = Guid.NewGuid();
        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                accountId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);
        var blocked = await service.ApplyAsync(
            TransitionRequest(
                Guid.NewGuid(),
                accountId,
                created.State!.Revision,
                workflow,
                AccountRecoveryExecutionTransitionKind.StartAction,
                "change-password"),
            CancellationToken.None);

        var reloaded = await service.LoadAsync(accountId, workflow, CancellationToken.None);

        Assert.True(blocked.Succeeded);
        Assert.True(reloaded.Succeeded);
        var action = reloaded.State!.GetAction("change-password");
        Assert.Equal(RecoveryActionReasonCode.WaitingForPrerequisite, action.ReasonCode);
        Assert.Equal(["identify-account"], action.ReasonArguments);
        Assert.Null(action.UserReason);
    }

    [Fact]
    public async Task ChecklistAcknowledgementCommitsBeforeItIsReloadedAsRecorded()
    {
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var workflow = CreateWorkflow();
        var accountId = Guid.NewGuid();
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(store.BatchWrites + 1));
        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                accountId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);
        var started = await service.ApplyAsync(
            TransitionRequest(
                Guid.NewGuid(),
                accountId,
                created.State!.Revision,
                workflow,
                AccountRecoveryExecutionTransitionKind.StartAction,
                "identify-account"),
            CancellationToken.None);
        var criterion = workflow.Actions[0].Guidance.CompletionCriteriaKeys.Single();

        store.FailBatchWrites = true;
        var failed = await service.ApplyAsync(
            TransitionRequest(
                Guid.NewGuid(),
                accountId,
                started.State!.Revision,
                workflow,
                AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements,
                "identify-account") with
            {
                AcknowledgedCompletionCriteria = [criterion],
            },
            CancellationToken.None);
        store.FailBatchWrites = false;
        var afterFailure = await service.LoadAsync(accountId, workflow, CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Empty(afterFailure.State!.GetAction("identify-account").AcknowledgedCompletionCriteria);

        var saved = await service.ApplyAsync(
            TransitionRequest(
                Guid.NewGuid(),
                accountId,
                afterFailure.State.Revision,
                workflow,
                AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements,
                "identify-account") with
            {
                AcknowledgedCompletionCriteria = [criterion],
            },
            CancellationToken.None);
        var reloaded = await service.LoadAsync(accountId, workflow, CancellationToken.None);

        Assert.True(saved.Succeeded);
        Assert.Equal([criterion], reloaded.State!.GetAction("identify-account").AcknowledgedCompletionCriteria);
        Assert.Equal(RecoveryActionStatus.InProgress, reloaded.State.GetAction("identify-account").Status);
    }

    [Fact]
    public async Task CompletingAnAccountPreservesOtherDashboardEntriesInAtomicProjection()
    {
        var dependencyId = Guid.NewGuid();
        var dependentId = Guid.NewGuid();
        var initial = CreateSession().ReplaceAccounts(
            [DashboardEntry(dependentId)],
            StartedAt.AddMinutes(1));
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(initial);
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            _mutations,
            () => StartedAt.AddMinutes(2));
        var workflow = CreateWorkflow();
        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                dependencyId,
                workflow,
                RecoveryPath.AuthenticatedChange,
                ProjectionContext()),
            CancellationToken.None);

        var state = created.State!;
        foreach (var actionId in new[] { "identify-account", "change-password" })
        {
            var started = await service.ApplyAsync(
                TransitionRequest(
                    Guid.NewGuid(),
                    dependencyId,
                    state.Revision,
                    workflow,
                    AccountRecoveryExecutionTransitionKind.StartAction,
                    actionId),
                CancellationToken.None);
            state = started.State!;
            var completed = await service.ApplyAsync(
                TransitionRequest(
                    Guid.NewGuid(),
                    dependencyId,
                    state.Revision,
                    workflow,
                    AccountRecoveryExecutionTransitionKind.CompleteAction,
                    actionId) with
                {
                    CompletionCriteriaAcknowledged = true,
                },
                CancellationToken.None);
            state = completed.State!;
        }

        var dependent = session.CurrentSession!.Accounts.Single(account => account.AccountId == dependentId);
        Assert.Equal(AccountRecoveryStatus.Open, dependent.RecoveryStatus);
        Assert.Equal(AccountRecoveryStatus.FullyReviewed, state.RecoveryStatus);
    }

    public void Dispose() => _mutations.Dispose();

    private static void AssertStateEquivalent(
        AccountRecoveryExecutionState? expected,
        AccountRecoveryExecutionState? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(
            JsonSerializer.Serialize(expected),
            JsonSerializer.Serialize(actual));
    }

    private static AccountRecoveryExecutionTransitionRequest TransitionRequest(
        Guid operationId,
        Guid accountId,
        long expectedRevision,
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryExecutionTransitionKind transition,
        string actionId) =>
        new(
            operationId,
            accountId,
            expectedRevision,
            workflow,
            transition,
            actionId,
            UserReason: null,
            UserNotes: null,
            CompletionCriteriaAcknowledged: false,
            CredentialReference: null,
            ProjectionContext());

    private static AccountRecoveryProjectionContext ProjectionContext() =>
        new(AccountCriticality.Critical);

    private static RecoverySessionWorkspace CreateSession() =>
        RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Incident",
            RecoveryIncidentIntake.Empty,
            StartedAt);

    private static RecoveryAccountDashboardEntry DashboardEntry(Guid accountId) =>
        new(
            accountId,
            "dependent.example",
            AccountCriticality.Routine,
            AccountRecoveryStatus.Open,
            RequiredActionsCompleted: 0,
            RequiredActionsTotal: 0,
            CompletedRequiredWeight: 0,
            TotalRequiredWeight: 0,
            BlockedRequiredActions: 0,
            FailedRequiredActions: 0,
            UnresolvedRisks: 0,
            AccessLost: false,
            CredentialsAwaitingExport: 0,
            CredentialsAwaitingDeletion: 0,
            RecommendedActionId: null);

    private static RecoveryWorkflowDefinition CreateWorkflow()
    {
        var identifyPrefix = "Workflow.Test.Action.identify-account";
        var passwordPrefix = "Workflow.Test.Action.change-password";
        return new RecoveryWorkflowDefinition(
            "test/recovery",
            "test.example",
            "Test Provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 6),
            [
                new RecoveryLocationDefinition(
                    "settings",
                    new Uri("https://test.example/security"),
                    ["https://test.example"]),
            ],
            [
                Action("identify-account", RecoveryActionType.IdentifyAccount, [], identifyPrefix),
                Action("change-password", RecoveryActionType.ChangePassword, ["identify-account"], passwordPrefix),
            ]);
    }

    private static RecoveryActionDefinition Action(
        string id,
        RecoveryActionType type,
        string[] prerequisites,
        string prefix)
    {
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            type,
            [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
            AutomationSupport.None,
            prerequisites,
            criteria,
            new RecoveryActionGuidanceKeys(
                $"{prefix}.Title",
                $"{prefix}.Instruction",
                $"{prefix}.Warning",
                $"{prefix}.Completion",
                criteria));
    }

    private sealed class InMemoryAtomicRecordStore : IEncryptedVaultRecordStore
    {
        public Dictionary<(string Type, string Id), byte[]> Records { get; } = [];

        public bool IsVaultUnlocked => true;

        public bool FailBatchWrites { get; set; }

        public int BatchWrites { get; private set; }

        public int LastBatchSize { get; private set; }

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Records.TryGetValue((descriptor.RecordType, descriptor.RecordId), out var value)
                    ? value.ToArray()
                    : null);
        }

        public Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records[(descriptor.RecordType, descriptor.RecordId)] = plaintext.ToArray();
            return Task.CompletedTask;
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchWrites++;
            LastBatchSize = writes.Count;
            if (FailBatchWrites)
            {
                throw new IOException("Synthetic atomic write failure.");
            }

            foreach (var write in writes)
            {
                Records[(write.Descriptor.RecordType, write.Descriptor.RecordId)] = write.Plaintext.ToArray();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ProjectionSessionService(RecoverySessionWorkspace currentSession) :
        IRecoverySessionService,
        IRecoverySessionProjectionCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } = currentSession;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public bool PreparedProjectionCommitted { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ClearForLock()
        {
        }

        public Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = CurrentSession!;
            var updated = current.ReplaceAccounts(accounts, current.UpdatedAt.AddMinutes(1));
            return Task.FromResult(new PreparedRecoverySessionUpdate(
                updated,
                new VaultRecordDescriptor(
                    "recovery-session",
                    Guid.NewGuid().ToString("D"),
                    1),
                JsonSerializer.SerializeToUtf8Bytes(updated),
                current.Revision));
        }

        public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
        {
            Assert.Equal(CurrentSession!.Revision, update.ExpectedRevision);
            CurrentSession = update.State;
            PreparedProjectionCommitted = true;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
