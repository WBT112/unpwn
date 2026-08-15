using System.Text.Json;
using Unpwn.App.Services;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class LowRiskAccountRecoveryExecutionTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 15, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NonCriticalReviewedProviderExecutesOnlyPasswordResetButPreservesFullCanonicalState()
    {
        using var mutations = new WorkspaceMutationCoordinator();
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var clock = StartedAt;
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            mutations,
            () => clock = clock.AddMinutes(1));
        var workflow = RepositoryWorkflowCatalog.Workflows.Single(candidate =>
            candidate.ProviderId == "github.com");
        var accountId = Guid.NewGuid();
        var context = new AccountRecoveryProjectionContext(AccountRecoveryCategory.NonCritical);

        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                accountId,
                workflow,
                context),
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(RecoveryPath.PasswordReset, created.State!.SelectedPath);
        Assert.Equal("reset-password", Assert.Single(created.State.Actions).DefinitionId);
        var dashboard = Assert.Single(session.CurrentSession!.Accounts);
        Assert.Equal(1, dashboard.RequiredActionsTotal);
        Assert.Equal(0, dashboard.UnresolvedRisks);
        Assert.Equal("reset-password", dashboard.RecommendedActionId);

        var fullReload = await service.LoadAsync(accountId, workflow, CancellationToken.None);
        Assert.True(fullReload.Succeeded);
        Assert.True(fullReload.State!.Actions.Length > created.State.Actions.Length);
        Assert.Contains(fullReload.State.Actions, action => action.DefinitionId == "review-mfa-reset");
        Assert.Equal(
            RecoveryActionStatus.Open,
            fullReload.State.GetAction("review-mfa-reset").Status);

        var hiddenAction = await service.ApplyAsync(
            Transition(
                accountId,
                created.State.Revision,
                workflow,
                context,
                AccountRecoveryExecutionTransitionKind.StartAction,
                "review-mfa-reset"),
            CancellationToken.None);
        Assert.False(hiddenAction.Succeeded);
        Assert.Equal(AccountRecoveryExecutionFailureCode.Conflict, hiddenAction.FailureCode);

        var started = await service.ApplyAsync(
            Transition(
                accountId,
                created.State.Revision,
                workflow,
                context,
                AccountRecoveryExecutionTransitionKind.StartAction,
                "reset-password"),
            CancellationToken.None);
        Assert.True(started.Succeeded);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            Assert.Single(started.State!.Actions).Status);

        var criterion = workflow.Actions.Single(action => action.Id == "reset-password")
            .Guidance.CompletionCriteriaKeys.Single();
        var acknowledged = await service.ApplyAsync(
            Transition(
                accountId,
                started.State.Revision,
                workflow,
                context,
                AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements,
                "reset-password") with
            {
                AcknowledgedCompletionCriteria = [criterion],
            },
            CancellationToken.None);
        Assert.True(acknowledged.Succeeded);

        var completed = await service.ApplyAsync(
            Transition(
                accountId,
                acknowledged.State!.Revision,
                workflow,
                context,
                AccountRecoveryExecutionTransitionKind.CompleteAction,
                "reset-password") with
            {
                CompletionCriteriaAcknowledged = true,
            },
            CancellationToken.None);

        Assert.True(completed.Succeeded);
        Assert.Equal(AccountRecoveryStatus.FullyReviewed, completed.State!.RecoveryStatus);
        var completedDashboard = Assert.Single(session.CurrentSession!.Accounts);
        Assert.Equal(AccountRecoveryStatus.FullyReviewed, completedDashboard.RecoveryStatus);
        Assert.Equal(1, completedDashboard.RequiredActionsCompleted);
        Assert.Equal(1, completedDashboard.RequiredActionsTotal);
        Assert.Equal(0, completedDashboard.BlockedRequiredActions);
        Assert.Equal(0, completedDashboard.FailedRequiredActions);
        Assert.Equal(0, completedDashboard.UnresolvedRisks);

        var afterCompletionFull = await service.LoadAsync(accountId, workflow, CancellationToken.None);
        Assert.Equal(
            RecoveryActionStatus.Completed,
            afterCompletionFull.State!.GetAction("reset-password").Status);
        Assert.Equal(
            RecoveryActionStatus.Open,
            afterCompletionFull.State.GetAction("review-mfa-reset").Status);

        var upgraded = await service.ApplyAsync(
            Transition(
                accountId,
                afterCompletionFull.State.Revision,
                workflow,
                new AccountRecoveryProjectionContext(AccountRecoveryCategory.Critical),
                AccountRecoveryExecutionTransitionKind.StartAction,
                "review-mfa-reset"),
            CancellationToken.None);
        Assert.True(upgraded.Succeeded);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            upgraded.State!.GetAction("review-mfa-reset").Status);
    }

    [Fact]
    public async Task NonCriticalGenericProviderExposesOnlySafePasswordReset()
    {
        using var mutations = new WorkspaceMutationCoordinator();
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            mutations,
            () => StartedAt.AddMinutes(1));
        var workflow = RepositoryWorkflowCatalog.CreateGenericManualWorkflow("service.example");
        var context = new AccountRecoveryProjectionContext(AccountRecoveryCategory.NonCritical);

        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                workflow,
                context),
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(RecoveryPath.PasswordReset, created.State!.SelectedPath);
        var action = Assert.Single(created.State.Actions);
        Assert.Equal("reset-password", action.DefinitionId);
        Assert.Equal(RecoveryActionType.ResetPassword,
            workflow.Actions.Single(definition => definition.Id == action.DefinitionId).Type);
        Assert.DoesNotContain(created.State.Actions, candidate =>
            candidate.DefinitionId.Contains("session", StringComparison.Ordinal));
        Assert.DoesNotContain(created.State.Actions, candidate =>
            candidate.DefinitionId.Contains("sign-in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonCriticalManualOnlyWorkflowFailsClosedWithoutPersistingExecution()
    {
        using var mutations = new WorkspaceMutationCoordinator();
        var store = new InMemoryAtomicRecordStore();
        var session = new ProjectionSessionService(CreateSession());
        var service = new AccountRecoveryExecutionService(
            store,
            session,
            mutations,
            () => StartedAt.AddMinutes(1));
        var workflow = CreateManualOnlyWorkflow();

        var created = await service.CreateAsync(
            new AccountRecoveryExecutionCreateRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                workflow,
                new AccountRecoveryProjectionContext(AccountRecoveryCategory.NonCritical)),
            CancellationToken.None);

        Assert.False(created.Succeeded);
        Assert.Equal(AccountRecoveryExecutionFailureCode.NoSafeRecoveryPath, created.FailureCode);
        Assert.Empty(store.Records);
        Assert.Empty(session.CurrentSession!.Accounts);
    }

    private static AccountRecoveryExecutionTransitionRequest Transition(
        Guid accountId,
        long revision,
        RecoveryWorkflowDefinition workflow,
        AccountRecoveryProjectionContext context,
        AccountRecoveryExecutionTransitionKind transition,
        string actionId) =>
        new(
            Guid.NewGuid(),
            accountId,
            revision,
            workflow,
            transition,
            actionId,
            UserReason: null,
            UserNotes: null,
            CompletionCriteriaAcknowledged: false,
            CredentialReference: null,
            context);

    private static RecoverySessionWorkspace CreateSession() =>
        RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Low-risk recovery test",
            RecoveryIncidentIntake.Empty,
            StartedAt);

    private static RecoveryWorkflowDefinition CreateManualOnlyWorkflow()
    {
        const string prefix = "Workflow.Test.ManualOnly";
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryWorkflowDefinition(
            "test/manual-only",
            "manual.example",
            "Manual Example",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 15),
            [],
            [
                new RecoveryActionDefinition(
                    "manual-recovery",
                    RecoveryActionType.ManualRecovery,
                    [RecoveryPath.ManualRecovery],
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.None,
                    [],
                    criteria,
                    new RecoveryActionGuidanceKeys(
                        $"{prefix}.Title",
                        $"{prefix}.Instruction",
                        $"{prefix}.Warning",
                        $"{prefix}.Completion",
                        criteria)),
            ]);
    }

    private sealed class InMemoryAtomicRecordStore : IEncryptedVaultRecordStore
    {
        public Dictionary<(string Type, string Id), byte[]> Records { get; } = [];

        public bool IsVaultUnlocked => true;

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
            foreach (var write in writes)
            {
                Records[(write.Descriptor.RecordType, write.Descriptor.RecordId)] = write.Plaintext.ToArray();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ProjectionSessionService(RecoverySessionWorkspace currentSession) :
        IRecoverySessionWorkspaceCoordinator
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } = currentSession;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
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
            if (CurrentSession!.Revision != update.ExpectedRevision)
            {
                throw new InvalidOperationException("Synthetic projection revision conflict.");
            }

            CurrentSession = update.State;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
