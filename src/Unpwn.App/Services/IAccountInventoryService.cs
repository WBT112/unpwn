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
    LoadFailed,
}

public enum AccountInventoryFailureCode
{
    None,
    Locked,
    InvalidInput,
    NotFound,
    Conflict,
    RequiresConfirmation,
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
    string? AccountUrl);

public interface IAccountInventoryService
{
    event EventHandler? InventoryChanged;

    AccountInventoryLoadState LoadState { get; }

    AccountInventoryState? CurrentInventory { get; }

    AccountRecoveryOrder? CurrentRecoveryOrder { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> UpsertAsync(
        AccountInventoryUpsertRequest request,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> CategorizeAsync(
        Guid accountId,
        AccountRecoveryCategory category,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> ClearCategoryOverrideAsync(
        Guid accountId,
        CancellationToken cancellationToken) => Task.FromResult(
            AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput));

    Task<AccountInventoryOperationResult> RemoveAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken);

    IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences();

    void ClearForLock();

    void MarkLoadFailed()
    {
    }
}
