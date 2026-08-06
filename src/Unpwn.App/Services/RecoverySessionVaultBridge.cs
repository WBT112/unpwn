using System.Diagnostics.CodeAnalysis;

namespace Unpwn.App.Services;

public sealed class RecoverySessionVaultBridge : IDisposable
{
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly IRecoverySessionService _sessionService;
    private bool _disposed;

    public RecoverySessionVaultBridge(
        IVaultLifecycleService vaultLifecycle,
        IRecoverySessionService sessionService)
    {
        _vaultLifecycle = vaultLifecycle ?? throw new ArgumentNullException(nameof(vaultLifecycle));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _vaultLifecycle.VaultStateChanged -= VaultLifecycle_OnStateChanged;
        _disposed = true;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This event boundary must not allow a session-load failure to crash the Avalonia UI thread.")]
    private async void VaultLifecycle_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (_vaultLifecycle.Snapshot.IsUnlocked)
            {
                await _sessionService.InitializeAsync(CancellationToken.None);
            }
            else
            {
                _sessionService.ClearForLock();
            }
        }
        catch (Exception)
        {
            _sessionService.ClearForLock();
        }
    }
}
