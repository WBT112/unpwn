using Unpwn.Core;
using Unpwn.Import.Csv;

namespace Unpwn.App.Services;

internal sealed class UnavailableAccountInventoryService : IAccountInventoryService
{
    public event EventHandler? InventoryChanged;

    public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Locked;

    public AccountInventoryState? CurrentInventory => null;

    public AccountInventoryPlan? CurrentPlan => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InventoryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<AccountInventoryOperationResult> UpsertAsync(
        AccountInventoryUpsertRequest request,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public Task<AccountInventoryOperationResult> DecideRoleAsync(
        Guid accountId,
        AccountInventoryRole role,
        AccountRoleDecision decision,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public Task<AccountInventoryOperationResult> AddDependencyAsync(
        AccountDependencyRequest request,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
        Guid accountId,
        Guid dependsOnAccountId,
        AccountDependencyKind kind,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public Task<AccountInventoryOperationResult> RemoveAccountAsync(
        Guid accountId,
        bool dependencyImpactAcknowledged,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken) => Failure(cancellationToken);

    public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

    public void ClearForLock() => InventoryChanged?.Invoke(this, EventArgs.Empty);

    public void MarkLoadFailed() => InventoryChanged?.Invoke(this, EventArgs.Empty);

    private static Task<AccountInventoryOperationResult> Failure(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AccountInventoryOperationResult.Failure(
            AccountInventoryFailureCode.Locked));
    }
}
