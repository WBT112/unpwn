using Unpwn.Application;
using Unpwn.Core;

namespace Unpwn.App.Services;

public enum RecoveryFlowMoveFailureCode
{
    None,
    Blocked,
    Locked,
    Conflict,
    PersistenceFailure,
}

public sealed record RecoveryFlowMoveResult(
    bool Succeeded,
    RecoveryFlowMoveFailureCode FailureCode,
    NextUserTask Task)
{
    public static RecoveryFlowMoveResult Success(NextUserTask task) =>
        new(true, RecoveryFlowMoveFailureCode.None, task);

    public static RecoveryFlowMoveResult Failure(
        RecoveryFlowMoveFailureCode code,
        NextUserTask task) =>
        new(false, code, task);
}

public interface IRecoveryFlowService
{
    event EventHandler? NextTaskChanged;

    RecoveryWizardState Current { get; }

    NextUserTask NextTask { get; }

    Task<RecoveryFlowMoveResult> AdvanceAsync(CancellationToken cancellationToken);

    Task<RecoveryFlowMoveResult> BeginCompletionReviewAsync(CancellationToken cancellationToken);

    Task<RecoveryFlowMoveResult> MarkCompletionReviewReadyAsync(CancellationToken cancellationToken);
}

public sealed class RecoveryFlowService : IRecoveryFlowService, IDisposable
{
    private readonly IEncryptedVaultRecordStore _recordStore;
    private readonly RecoveryWizardSessionService _wizard;
    private readonly IRecoverySessionService _session;
    private readonly IAccountInventoryService _inventory;
    private readonly WorkspaceMutationCoordinator _mutations;
    private readonly Func<DateTimeOffset> _clock;
    private bool _disposed;

    public RecoveryFlowService(
        IEncryptedVaultRecordStore recordStore,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService session,
        IAccountInventoryService inventory,
        WorkspaceMutationCoordinator mutations,
        Func<DateTimeOffset>? clock = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _wizard.StateChanged += Source_OnChanged;
        _session.SessionChanged += Source_OnChanged;
        _inventory.InventoryChanged += Source_OnChanged;
    }

    public event EventHandler? NextTaskChanged;

    public RecoveryWizardState Current => _wizard.Current;

    public NextUserTask NextTask => RecoveryNextUserTask.Project(Current, CreateContext());

    public Task<RecoveryFlowMoveResult> AdvanceAsync(CancellationToken cancellationToken)
    {
        var task = NextTask;
        if (!task.RequiresTransition || task.TransitionStep is null)
        {
            return Task.FromResult(RecoveryFlowMoveResult.Failure(
                RecoveryFlowMoveFailureCode.Blocked,
                task));
        }

        return PersistAsync(
            task,
            () => _wizard.PrepareNavigation(task.TransitionStep, backwards: false, _clock()),
            cancellationToken);
    }

    public Task<RecoveryFlowMoveResult> BeginCompletionReviewAsync(
        CancellationToken cancellationToken)
    {
        var task = NextTask;
        if (task.Target != NextUserTaskTarget.CompletionReview)
        {
            return Task.FromResult(RecoveryFlowMoveResult.Failure(
                RecoveryFlowMoveFailureCode.Blocked,
                task));
        }

        if (Current.CurrentStep is var step &&
            (step == RecoveryWizardStepId.CompletionPreflight ||
             step == RecoveryWizardStepId.FinalReport))
        {
            return Task.FromResult(RecoveryFlowMoveResult.Success(task));
        }

        return PersistAsync(
            task,
            () => _wizard.PrepareCompletionReview(_clock()),
            cancellationToken);
    }

    public Task<RecoveryFlowMoveResult> MarkCompletionReviewReadyAsync(
        CancellationToken cancellationToken)
    {
        var task = NextTask;
        return Current.CurrentStep != RecoveryWizardStepId.CompletionPreflight
            ? Task.FromResult(RecoveryFlowMoveResult.Success(task))
            : AdvanceAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _wizard.StateChanged -= Source_OnChanged;
        _session.SessionChanged -= Source_OnChanged;
        _inventory.InventoryChanged -= Source_OnChanged;
        _disposed = true;
    }

    private Task<RecoveryFlowMoveResult> PersistAsync(
        NextUserTask task,
        Func<PreparedRecoveryWizardUpdate> prepare,
        CancellationToken cancellationToken) =>
        _mutations.ExecuteAsync(
            async token =>
            {
                if (!_recordStore.IsVaultUnlocked)
                {
                    return RecoveryFlowMoveResult.Failure(
                        RecoveryFlowMoveFailureCode.Locked,
                        task);
                }

                try
                {
                    using var update = prepare();
                    await _recordStore.WriteEncryptedRecordAsync(
                        update.Descriptor,
                        update.Plaintext,
                        token);
                    _wizard.CommitPreparedTransition(update);
                    return RecoveryFlowMoveResult.Success(task);
                }
                catch (InvalidOperationException)
                {
                    return RecoveryFlowMoveResult.Failure(
                        RecoveryFlowMoveFailureCode.Conflict,
                        task);
                }
                catch (IOException)
                {
                    return RecoveryFlowMoveResult.Failure(
                        RecoveryFlowMoveFailureCode.PersistenceFailure,
                        task);
                }
            },
            cancellationToken);

    private RecoveryFlowContext CreateContext()
    {
        var accounts = _inventory.CurrentInventory?.Accounts ?? [];
        var dashboard = _session.Dashboard;
        var hasOutstandingWork = dashboard is not null &&
            (dashboard.AccountsFullyReviewed < dashboard.AccountsTotal ||
             dashboard.BlockedRequiredActions > 0 ||
             dashboard.FailedRequiredActions > 0 ||
             dashboard.UnresolvedRisks > 0 ||
             dashboard.AccountsWithLostAccess > 0);
        var hasPendingCredentials = dashboard is not null &&
            (dashboard.CredentialsAwaitingExport > 0 ||
             dashboard.CredentialsAwaitingDeletion > 0);

        return new RecoveryFlowContext(
            accounts.Length,
            accounts.Count(account => !account.IsCategorized),
            hasOutstandingWork,
            hasPendingCredentials,
            dashboard?.Recommendation.AccountId ?? _inventory.CurrentRecoveryOrder?.Recommended?.AccountId,
            dashboard?.Recommendation.ActionId);
    }

    private void Source_OnChanged(object? sender, EventArgs eventArgs) =>
        NextTaskChanged?.Invoke(this, EventArgs.Empty);
}
