using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class RecoveryVaultLifecycleService : IVaultLifecycleService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IRecentVaultStore _recentVaultStore;
    private readonly RecoveryWizardSessionService _wizard;
    private readonly VaultInactivityPolicy _inactivityPolicy;
    private readonly Func<DateTimeOffset> _clock;
    private RecoveryVault? _vault;
    private DateTimeOffset _lastActivity;
    private bool _disposed;

    public RecoveryVaultLifecycleService(
        IRecentVaultStore recentVaultStore,
        RecoveryWizardSessionService wizard,
        VaultInactivityPolicy? inactivityPolicy = null,
        Func<DateTimeOffset>? clock = null)
    {
        _recentVaultStore = recentVaultStore ?? throw new ArgumentNullException(nameof(recentVaultStore));
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _inactivityPolicy = inactivityPolicy ?? VaultInactivityPolicy.Default;
        _inactivityPolicy.Validate();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastActivity = _clock();
    }

    public event EventHandler? ContextChanged;

    public event EventHandler? VaultStateChanged;

    public ShellContext Current { get; private set; } = ShellContext.Locked;

    public VaultLifecycleSnapshot Snapshot { get; private set; } = VaultLifecycleSnapshot.Empty;

    public IReadOnlyList<RecentVaultReference> RecentVaults { get; private set; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        RecentVaults = await _recentVaultStore.LoadAsync(cancellationToken);
        PublishState(contextChanged: false);
    }

    public Task<VaultOperationResult> CreateAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken) =>
        OpenOrCreateAsync(path, vaultPassword, create: true, cancellationToken);

    public Task<VaultOperationResult> OpenAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken) =>
        OpenOrCreateAsync(path, vaultPassword, create: false, cancellationToken);

    public async Task<VaultOperationResult> UnlockCurrentAsync(
        string vaultPassword,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_vault is null || !_vault.IsLocked || Snapshot.CurrentPath is null)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.InvalidInput);
        }

        var result = await ExecuteVaultOperationAsync(
            () => _vault.Unlock(vaultPassword),
            cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        try
        {
            _wizard.ResumeAfterUnlock(_vault, _clock());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or NotSupportedException)
        {
            _vault.Lock();
            return VaultOperationResult.Failure(VaultOperationFailureCode.AuthenticationOrIntegrity);
        }

        _lastActivity = _clock();
        Snapshot = Snapshot with
        {
            Status = VaultLifecycleStatus.Unlocked,
            LastLockReason = VaultLockReason.None,
            IsInactivityWarningVisible = false,
            InactivityLocksAt = null,
        };
        Current = ShellContext.Unlocked(
            Snapshot.CurrentDisplayName ?? Path.GetFileName(Snapshot.CurrentPath));
        PublishState(contextChanged: true);
        return VaultOperationResult.Success;
    }

    public async Task<VaultOperationResult> ChangePasswordAsync(
        string currentVaultPassword,
        string newVaultPassword,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_vault is null || _vault.IsLocked)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.InvalidInput);
        }

        return await ExecuteVaultOperationAsync(
            () => _vault.ChangePassword(
                currentVaultPassword,
                newVaultPassword,
                Argon2idParameters.Interactive),
            cancellationToken);
    }

    public Task LockAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return LockInternalAsync(VaultLockReason.User, _clock(), cancellationToken);
    }

    public async Task RemoveRecentReferenceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        RecentVaults = RecentVaults
            .Where(reference => !PathComparer.Equals(reference.Path, fullPath))
            .ToArray();
        await SaveRecentVaultsBestEffortAsync(cancellationToken);
        PublishState(contextChanged: false);
    }

    public async Task<VaultOperationResult> DeleteVaultFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!TryNormalizePath(path, out var fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.InvalidInput);
        }

        if (!File.Exists(fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.NotFound);
        }

        if (Snapshot.IsUnlocked &&
            Snapshot.CurrentPath is not null &&
            PathComparer.Equals(Snapshot.CurrentPath, fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.CurrentVaultInUse);
        }

        if (Snapshot.CurrentPath is not null && PathComparer.Equals(Snapshot.CurrentPath, fullPath))
        {
            _vault?.Dispose();
            _vault = null;
            Current = ShellContext.Locked;
            Snapshot = VaultLifecycleSnapshot.Empty;
        }

        var result = await ExecuteVaultOperationAsync(
            () => File.Delete(fullPath),
            cancellationToken);
        if (result.Succeeded)
        {
            await RemoveRecentReferenceAsync(fullPath, cancellationToken);
            PublishState(contextChanged: true);
        }

        return result;
    }

    public void RecordUserActivity(DateTimeOffset occurredAt)
    {
        ThrowIfDisposed();
        if (!Snapshot.IsUnlocked || occurredAt < _lastActivity)
        {
            return;
        }

        _lastActivity = occurredAt;
        if (Snapshot.IsInactivityWarningVisible)
        {
            Snapshot = Snapshot with
            {
                IsInactivityWarningVisible = false,
                InactivityLocksAt = null,
            };
            PublishState(contextChanged: false);
        }
    }

    public async Task CheckInactivityAsync(
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!Snapshot.IsUnlocked || occurredAt < _lastActivity)
        {
            return;
        }

        var inactiveFor = occurredAt - _lastActivity;
        if (inactiveFor >= _inactivityPolicy.LockAfter)
        {
            await LockInternalAsync(VaultLockReason.Inactivity, occurredAt, cancellationToken);
            return;
        }

        var warningVisible = inactiveFor >= _inactivityPolicy.WarningAfter;
        if (warningVisible != Snapshot.IsInactivityWarningVisible)
        {
            Snapshot = Snapshot with
            {
                IsInactivityWarningVisible = warningVisible,
                InactivityLocksAt = warningVisible
                    ? _lastActivity + _inactivityPolicy.LockAfter
                    : null,
            };
            PublishState(contextChanged: false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_vault is { IsLocked: false } vault)
        {
            try
            {
                _wizard.PrepareForLock(vault, _clock());
            }
            catch (InvalidOperationException)
            {
                // The vault is still disposed and its in-memory key is cleared.
            }
            catch (IOException)
            {
                // The vault is still disposed and its in-memory key is cleared.
            }
        }

        _vault?.Dispose();
        _vault = null;
        Current = ShellContext.Locked;
        Snapshot = VaultLifecycleSnapshot.Empty;
        _disposed = true;
    }

    private async Task<VaultOperationResult> OpenOrCreateAsync(
        string path,
        string vaultPassword,
        bool create,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(vaultPassword) || !TryNormalizePath(path, out var fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.InvalidInput);
        }

        if (create && File.Exists(fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.AlreadyExists);
        }

        if (!create && !File.Exists(fullPath))
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.NotFound);
        }

        RecoveryVault? openedVault = null;
        var result = await ExecuteVaultOperationAsync(
            () => openedVault = create
                ? RecoveryVault.Create(fullPath, vaultPassword, Argon2idParameters.Interactive)
                : RecoveryVault.Open(fullPath, vaultPassword),
            cancellationToken);
        if (!result.Succeeded || openedVault is null)
        {
            openedVault?.Dispose();
            return result;
        }

        try
        {
            if (create)
            {
                _wizard.AttachNewVault(openedVault, _clock());
            }
            else
            {
                _wizard.AttachExistingVault(openedVault, _clock());
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or NotSupportedException)
        {
            openedVault.Dispose();
            return VaultOperationResult.Failure(VaultOperationFailureCode.AuthenticationOrIntegrity);
        }

        _vault?.Dispose();
        _vault = openedVault;
        _lastActivity = _clock();
        var displayName = GetDisplayName(fullPath);
        Snapshot = new VaultLifecycleSnapshot(
            VaultLifecycleStatus.Unlocked,
            fullPath,
            displayName,
            VaultLockReason.None,
            IsInactivityWarningVisible: false,
            InactivityLocksAt: null);
        Current = ShellContext.Unlocked(displayName);
        await AddRecentVaultAsync(fullPath, displayName, cancellationToken);
        PublishState(contextChanged: true);
        return VaultOperationResult.Success;
    }

    private Task LockInternalAsync(
        VaultLockReason reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_vault is null || _vault.IsLocked)
        {
            return Task.CompletedTask;
        }

        _wizard.PrepareForLock(_vault, occurredAt);
        _vault.Lock();
        Current = ShellContext.Locked;
        Snapshot = Snapshot with
        {
            Status = VaultLifecycleStatus.Locked,
            LastLockReason = reason,
            IsInactivityWarningVisible = false,
            InactivityLocksAt = null,
        };
        PublishState(contextChanged: true);
        return Task.CompletedTask;
    }

    private async Task AddRecentVaultAsync(
        string path,
        string displayName,
        CancellationToken cancellationToken)
    {
        var reference = new RecentVaultReference(path, displayName, _clock());
        RecentVaults =
        [
            reference,
            .. RecentVaults
                .Where(existing => !PathComparer.Equals(existing.Path, path))
                .OrderByDescending(existing => existing.LastOpenedAt)
                .Take(7),
        ];
        await SaveRecentVaultsBestEffortAsync(cancellationToken);
    }

    private async Task SaveRecentVaultsBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _recentVaultStore.SaveAsync(RecentVaults, cancellationToken);
        }
        catch (IOException)
        {
            // Recent references are convenience metadata and must not break vault access.
        }
        catch (UnauthorizedAccessException)
        {
            // Recent references are convenience metadata and must not break vault access.
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the secret-safe vault operation boundary; source exception details must not reach presentation code.")]
    private static async Task<VaultOperationResult> ExecuteVaultOperationAsync(
        Action operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation();
            }, cancellationToken);
            return VaultOperationResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.AccessDenied);
        }
        catch (NotSupportedException)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.UnsupportedVersion);
        }
        catch (InvalidOperationException)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.AuthenticationOrIntegrity);
        }
        catch (Exception)
        {
            return VaultOperationResult.Failure(VaultOperationFailureCode.IoFailure);
        }
    }

    private static bool TryNormalizePath(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string GetDisplayName(string path)
    {
        var displayName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(path)
            : displayName;
    }

    private void PublishState(bool contextChanged)
    {
        if (contextChanged)
        {
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }

        VaultStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
