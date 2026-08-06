using Unpwn.Core;
using Unpwn.Import.Csv;

namespace Unpwn.App.Services;

public enum AccountInventoryLoadState
{
    Locked,
    Loading,
    Empty,
    Loaded,
    Corrupted,
}

public enum AccountInventoryFailureCode
{
    None,
    Locked,
    InvalidInput,
    NotFound,
    Conflict,
    RequiresConfirmation,
    RequiresOverrideReason,
    Corrupted,
    IoFailure,
}

public enum ImportDuplicateResolution
{
    SkipDuplicates,
    ImportAsSeparateAccounts,
}

public sealed record AccountInventoryOperationResult(
    bool Succeeded,
    AccountInventoryFailureCode FailureCode,
    int AffectedAccounts = 0)
{
    public static AccountInventoryOperationResult Success(int affectedAccounts = 0) =>
        new(true, AccountInventoryFailureCode.None, affectedAccounts);

    public static AccountInventoryOperationResult Failure(AccountInventoryFailureCode code) =>
        new(false, code);
}

public sealed record AccountInventoryUpsertRequest(
    Guid? AccountId,
    string ProviderId,
    string? AccountName,
    string? LoginIdentifier,
    string? AccountUrl,
    AccountInventoryPriority Priority,
    AccountInventoryRole ConfirmedRoles);

public sealed record AccountDependencyRequest(
    Guid AccountId,
    Guid DependsOnAccountId,
    AccountDependencyKind Kind,
    string? OverrideReason);

public interface IAccountInventoryService
{
    event EventHandler? InventoryChanged;

    AccountInventoryLoadState LoadState { get; }

    AccountInventoryState? CurrentInventory { get; }

    AccountInventoryPlan? CurrentPlan { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> UpsertAsync(
        AccountInventoryUpsertRequest request,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> DecideRoleAsync(
        Guid accountId,
        AccountInventoryRole role,
        AccountRoleDecision decision,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> AddDependencyAsync(
        AccountDependencyRequest request,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> RemoveDependencyAsync(
        Guid accountId,
        Guid dependsOnAccountId,
        AccountDependencyKind kind,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> RemoveAccountAsync(
        Guid accountId,
        bool dependencyImpactAcknowledged,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken);

    IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences();

    void ClearForLock();
}
