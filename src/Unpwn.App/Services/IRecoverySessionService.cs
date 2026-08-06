using Unpwn.Core;
using Unpwn.Vault.Cryptography;

namespace Unpwn.App.Services;

public enum RecoverySessionLoadState
{
    Locked,
    Loading,
    Empty,
    Loaded,
    Corrupted,
}

public enum RecoverySessionOperationFailureCode
{
    None,
    Locked,
    InvalidInput,
    Corrupted,
    Conflict,
    IoFailure,
}

public sealed record RecoverySessionOperationResult(
    bool Succeeded,
    RecoverySessionOperationFailureCode FailureCode)
{
    public static RecoverySessionOperationResult Success { get; } =
        new(true, RecoverySessionOperationFailureCode.None);

    public static RecoverySessionOperationResult Failure(
        RecoverySessionOperationFailureCode failureCode) =>
        new(false, failureCode);
}

public sealed record RecoverySessionCreateRequest(
    string Name,
    string? IncidentDescription,
    IncidentIndicator Indicators,
    bool SecurityWarningAcknowledged);

public interface IEncryptedVaultRecordStore
{
    bool IsVaultUnlocked { get; }

    Task<byte[]?> ReadEncryptedRecordAsync(
        VaultRecordDescriptor descriptor,
        CancellationToken cancellationToken);

    Task WriteEncryptedRecordAsync(
        VaultRecordDescriptor descriptor,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);
}

public interface IRecoverySessionService
{
    event EventHandler? SessionChanged;

    RecoverySessionLoadState LoadState { get; }

    RecoverySessionWorkspace? CurrentSession { get; }

    RecoveryDashboardSnapshot? Dashboard { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<RecoverySessionOperationResult> CreateAsync(
        RecoverySessionCreateRequest request,
        CancellationToken cancellationToken);

    Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken);

    Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken);

    Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken);

    Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken);

    void ClearForLock();
}
