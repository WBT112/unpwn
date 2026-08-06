namespace Unpwn.App.Services;

public interface IVaultLifecycleService : IShellContextService, IDisposable
{
    event EventHandler? VaultStateChanged;

    VaultLifecycleSnapshot Snapshot { get; }

    IReadOnlyList<RecentVaultReference> RecentVaults { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<VaultOperationResult> CreateAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken);

    Task<VaultOperationResult> OpenAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken);

    Task<VaultOperationResult> UnlockCurrentAsync(
        string vaultPassword,
        CancellationToken cancellationToken);

    Task<VaultOperationResult> ChangePasswordAsync(
        string currentVaultPassword,
        string newVaultPassword,
        CancellationToken cancellationToken);

    Task RemoveRecentReferenceAsync(
        string path,
        CancellationToken cancellationToken);

    Task<VaultOperationResult> DeleteVaultFileAsync(
        string path,
        CancellationToken cancellationToken);

    void RecordUserActivity(DateTimeOffset occurredAt);

    Task CheckInactivityAsync(
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
