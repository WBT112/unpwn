using System.Diagnostics.CodeAnalysis;

namespace Unpwn.App.Services;

public sealed class RecoverySessionVaultBridge : IDisposable
{
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly IRecoverySessionService _sessionService;
    private readonly IAccountInventoryService? _accountInventory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _synchronizationCancellation;
    private long _generation;
    private bool _disposed;

    public RecoverySessionVaultBridge(
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService sessionService,
        IAccountInventoryService? accountInventory = null)
    {
        _vaultLifecycle = vaultLifecycle ?? throw new ArgumentNullException(nameof(vaultLifecycle));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _accountInventory = accountInventory;
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _vaultLifecycle.VaultStateChanged -= VaultLifecycle_OnStateChanged;
        _synchronizationCancellation?.Cancel();
        _synchronizationCancellation?.Dispose();
        _synchronizationCancellation = null;
        _gate.Dispose();
        _disposed = true;
    }

    private void VaultLifecycle_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _synchronizationCancellation?.Cancel();
        _synchronizationCancellation?.Dispose();
        _synchronizationCancellation = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _generation);
        _ = SynchronizeAsync(generation, _synchronizationCancellation.Token);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This asynchronous event boundary converts unexpected workspace-load failures into explicit safe load states.")]
    private async Task SynchronizeAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_disposed || generation != Volatile.Read(ref _generation))
                {
                    return;
                }

                if (!_vaultLifecycle.Snapshot.IsUnlocked)
                {
                    _accountInventory?.ClearForLock();
                    _sessionService.ClearForLock();
                    return;
                }

                await _sessionService.InitializeAsync(cancellationToken);
                if (_disposed || generation != Volatile.Read(ref _generation) ||
                    !_vaultLifecycle.Snapshot.IsUnlocked)
                {
                    return;
                }

                if (_accountInventory is not null)
                {
                    await _accountInventory.InitializeAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception)
        {
            if (_disposed)
            {
                return;
            }

            if (_vaultLifecycle.Snapshot.IsUnlocked)
            {
                _sessionService.MarkLoadFailed();
                _accountInventory?.MarkLoadFailed();
            }
            else
            {
                _accountInventory?.ClearForLock();
                _sessionService.ClearForLock();
            }
        }
    }
}
