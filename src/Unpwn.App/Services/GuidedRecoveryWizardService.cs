using Unpwn.Application;
using Unpwn.Core;

namespace Unpwn.App.Services;

public enum GuidedRecoveryMoveFailureCode
{
    None,
    Blocked,
    Locked,
    Conflict,
    PersistenceFailure,
}

public sealed record GuidedRecoveryMoveResult(
    bool Succeeded,
    GuidedRecoveryMoveFailureCode FailureCode,
    GuidedRecoveryDecision Decision)
{
    public static GuidedRecoveryMoveResult Success(GuidedRecoveryDecision decision) =>
        new(true, GuidedRecoveryMoveFailureCode.None, decision);

    public static GuidedRecoveryMoveResult Failure(
        GuidedRecoveryMoveFailureCode code,
        GuidedRecoveryDecision decision) =>
        new(false, code, decision);
}

public interface IGuidedRecoveryWizardService
{
    event EventHandler? GuidanceChanged;

    RecoveryWizardState Current { get; }

    GuidedRecoveryDecision NextDecision { get; }

    GuidedRecoveryDecision PreviousDecision { get; }

    Task<GuidedRecoveryMoveResult> AdvanceAsync(CancellationToken cancellationToken);

    Task<GuidedRecoveryMoveResult> GoBackAsync(CancellationToken cancellationToken);

    Task<GuidedRecoveryMoveResult> BeginCompletionReviewAsync(CancellationToken cancellationToken);

    Task<GuidedRecoveryMoveResult> MarkCompletionReviewReadyAsync(CancellationToken cancellationToken);
}

public sealed class GuidedRecoveryWizardService : IGuidedRecoveryWizardService, IDisposable
{
    private readonly IEncryptedVaultRecordStore _recordStore;
    private readonly RecoveryWizardSessionService _wizard;
    private readonly IRecoverySessionService _session;
    private readonly IAccountInventoryService _inventory;
    private readonly WorkspaceMutationCoordinator _mutations;
    private readonly Func<DateTimeOffset> _clock;
    private bool _disposed;

    public GuidedRecoveryWizardService(
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

    public event EventHandler? GuidanceChanged;

    public RecoveryWizardState Current => _wizard.Current;

    public GuidedRecoveryDecision NextDecision =>
        GuidedRecoveryWizard.GetNext(Current, CreateContext());

    public GuidedRecoveryDecision PreviousDecision =>
        GuidedRecoveryWizard.GetPrevious(Current);

    public Task<GuidedRecoveryMoveResult> AdvanceAsync(CancellationToken cancellationToken) =>
        MoveAsync(NextDecision, backwards: false, cancellationToken);

    public Task<GuidedRecoveryMoveResult> GoBackAsync(CancellationToken cancellationToken) =>
        MoveAsync(PreviousDecision, backwards: true, cancellationToken);

    public Task<GuidedRecoveryMoveResult> BeginCompletionReviewAsync(
        CancellationToken cancellationToken)
    {
        var decision = new GuidedRecoveryDecision(
            Current.CurrentStep,
            RecoveryWizardStepId.CompletionPreflight,
            GuidedRecoveryBlockCode.None);
        return PersistAsync(
            decision,
            () => _wizard.PrepareCompletionReview(_clock()),
            cancellationToken);
    }

    public Task<GuidedRecoveryMoveResult> MarkCompletionReviewReadyAsync(
        CancellationToken cancellationToken)
    {
        var decision = NextDecision;
        return Current.CurrentStep != RecoveryWizardStepId.CompletionPreflight
            ? Task.FromResult(GuidedRecoveryMoveResult.Success(decision))
            : MoveAsync(decision, backwards: false, cancellationToken);
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

    private Task<GuidedRecoveryMoveResult> MoveAsync(
        GuidedRecoveryDecision decision,
        bool backwards,
        CancellationToken cancellationToken)
    {
        if (!decision.CanMove || decision.TargetStep is null)
        {
            return Task.FromResult(GuidedRecoveryMoveResult.Failure(
                GuidedRecoveryMoveFailureCode.Blocked,
                decision));
        }

        return PersistAsync(
            decision,
            () => _wizard.PrepareNavigation(decision.TargetStep, backwards, _clock()),
            cancellationToken);
    }

    private Task<GuidedRecoveryMoveResult> PersistAsync(
        GuidedRecoveryDecision decision,
        Func<PreparedRecoveryWizardUpdate> prepare,
        CancellationToken cancellationToken) =>
        _mutations.ExecuteAsync(
            async token =>
            {
                if (!_recordStore.IsVaultUnlocked)
                {
                    return GuidedRecoveryMoveResult.Failure(
                        GuidedRecoveryMoveFailureCode.Locked,
                        decision);
                }

                try
                {
                    using var update = prepare();
                    await _recordStore.WriteEncryptedRecordAsync(
                        update.Descriptor,
                        update.Plaintext,
                        token);
                    _wizard.CommitPreparedTransition(update);
                    return GuidedRecoveryMoveResult.Success(decision);
                }
                catch (InvalidOperationException)
                {
                    return GuidedRecoveryMoveResult.Failure(
                        GuidedRecoveryMoveFailureCode.Conflict,
                        decision);
                }
                catch (IOException)
                {
                    return GuidedRecoveryMoveResult.Failure(
                        GuidedRecoveryMoveFailureCode.PersistenceFailure,
                        decision);
                }
            },
            cancellationToken);

    private GuidedRecoveryContext CreateContext()
    {
        var accounts = _inventory.CurrentInventory?.Accounts ?? [];
        var dashboard = _session.Dashboard;
        var suggestedRoles = accounts.Sum(account => account.Roles.Count(role =>
            role.Decision == AccountRoleDecision.Suggested));
        var hasOutstandingWork = dashboard is not null &&
            (dashboard.AccountsFullyReviewed < dashboard.AccountsTotal ||
             dashboard.BlockedRequiredActions > 0 ||
             dashboard.FailedRequiredActions > 0 ||
             dashboard.UnresolvedRisks > 0 ||
             dashboard.AccountsWithLostAccess > 0);
        var hasPendingCredentials = dashboard is not null &&
            (dashboard.CredentialsAwaitingExport > 0 ||
             dashboard.CredentialsAwaitingDeletion > 0);

        return new GuidedRecoveryContext(
            accounts.Length,
            suggestedRoles,
            hasOutstandingWork,
            hasPendingCredentials,
            dashboard?.Recommendation.AccountId ?? _inventory.CurrentPlan?.Recommended?.AccountId,
            dashboard?.Recommendation.ActionId);
    }

    private void Source_OnChanged(object? sender, EventArgs eventArgs) =>
        GuidanceChanged?.Invoke(this, EventArgs.Empty);
}
