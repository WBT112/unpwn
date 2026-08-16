using System.Diagnostics.CodeAnalysis;

namespace Unpwn.App.Services;

public enum RecoveryBrowserSessionLifecycleState
{
    Idle,
    Active,
    Cleaning,
    CleanupFailed,
    OrphanedDataDetected,
}

public enum RecoveryBrowserSessionFailureCode
{
    None,
    InvalidAccount,
    AccountSwitchRequiresCleanup,
    OrphanedDataRequiresCleanup,
    SessionNotFound,
    BrowserReleaseFailed,
    StorageUnavailable,
}

public sealed record RecoveryBrowserSession(
    Guid SessionId,
    Guid AccountId,
    string ProfileDataPath);

public sealed record RecoveryBrowserOrphanedSession(
    Guid SessionId,
    string ProfileDataPath);

public sealed record RecoveryBrowserSessionLifecycleSnapshot(
    RecoveryBrowserSessionLifecycleState State,
    RecoveryBrowserSession? ActiveSession,
    IReadOnlyList<RecoveryBrowserOrphanedSession> OrphanedSessions,
    RecoveryBrowserSessionFailureCode FailureCode)
{
    public bool HasUncleanSessionData =>
        OrphanedSessions.Count > 0 ||
        FailureCode == RecoveryBrowserSessionFailureCode.StorageUnavailable;

    public bool CanRetryCleanup =>
        State is RecoveryBrowserSessionLifecycleState.CleanupFailed or
            RecoveryBrowserSessionLifecycleState.OrphanedDataDetected;
}

public sealed record RecoveryBrowserSessionStartResult(
    RecoveryBrowserSession? Session,
    bool WasReused,
    RecoveryBrowserSessionFailureCode FailureCode)
{
    public bool Succeeded => Session is not null;
}

public sealed record RecoveryBrowserSessionCleanupResult(
    bool Succeeded,
    RecoveryBrowserSessionFailureCode FailureCode);

public interface IRecoveryBrowserSessionResources
{
    Task ClearBrowsingDataAsync(CancellationToken cancellationToken);

    Task StopAndReleaseAsync(CancellationToken cancellationToken);
}

public interface IRecoveryBrowserSessionLifecycle
{
    event EventHandler<RecoveryBrowserSessionLifecycleSnapshot>? StateChanged;

    RecoveryBrowserSessionLifecycleSnapshot Current { get; }

    RecoveryBrowserSessionLifecycleSnapshot InspectStartup();

    RecoveryBrowserSessionStartResult Start(Guid accountId);

    Task<RecoveryBrowserSessionCleanupResult> EndAsync(
        Guid sessionId,
        IRecoveryBrowserSessionResources resources,
        CancellationToken cancellationToken);

    Task<RecoveryBrowserSessionCleanupResult> RetryOrphanCleanupAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}

internal interface IRecoveryBrowserSessionStorage
{
    IReadOnlyList<RecoveryBrowserOrphanedSession> FindOrphanedSessions();

    RecoveryBrowserSession Create(Guid accountId);

    void MarkCleanupPending(RecoveryBrowserSession session);

    void Delete(string profileDataPath);
}

public sealed class RecoveryBrowserSessionLifecycle : IRecoveryBrowserSessionLifecycle, IDisposable
{
    private readonly IRecoveryBrowserSessionStorage _storage;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RecoveryBrowserSessionLifecycleSnapshot _current = EmptySnapshot;

    public RecoveryBrowserSessionLifecycle(string applicationDataRoot)
        : this(new FileRecoveryBrowserSessionStorage(applicationDataRoot))
    {
    }

    internal RecoveryBrowserSessionLifecycle(IRecoveryBrowserSessionStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        InspectStartup();
    }

    public event EventHandler<RecoveryBrowserSessionLifecycleSnapshot>? StateChanged;

    public RecoveryBrowserSessionLifecycleSnapshot Current => _current;

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public RecoveryBrowserSessionLifecycleSnapshot InspectStartup()
    {
        _gate.Wait();
        try
        {
            if (_current.ActiveSession is not null)
            {
                return _current;
            }

            var orphans = _storage.FindOrphanedSessions();
            Publish(new RecoveryBrowserSessionLifecycleSnapshot(
                orphans.Count == 0
                    ? RecoveryBrowserSessionLifecycleState.Idle
                    : RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
                ActiveSession: null,
                orphans,
                RecoveryBrowserSessionFailureCode.None));
            return _current;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            Publish(_current with
            {
                State = RecoveryBrowserSessionLifecycleState.CleanupFailed,
                FailureCode = RecoveryBrowserSessionFailureCode.StorageUnavailable,
            });
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public RecoveryBrowserSessionStartResult Start(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            return new RecoveryBrowserSessionStartResult(
                null,
                WasReused: false,
                RecoveryBrowserSessionFailureCode.InvalidAccount);
        }

        _gate.Wait();
        try
        {
            if (_current.HasUncleanSessionData)
            {
                return new RecoveryBrowserSessionStartResult(
                    null,
                    WasReused: false,
                    _current.FailureCode == RecoveryBrowserSessionFailureCode.StorageUnavailable
                        ? RecoveryBrowserSessionFailureCode.StorageUnavailable
                        : RecoveryBrowserSessionFailureCode.OrphanedDataRequiresCleanup);
            }

            if (_current.ActiveSession is { } active)
            {
                return active.AccountId == accountId
                    ? new RecoveryBrowserSessionStartResult(
                        active,
                        WasReused: true,
                        RecoveryBrowserSessionFailureCode.None)
                    : new RecoveryBrowserSessionStartResult(
                        null,
                        WasReused: false,
                        RecoveryBrowserSessionFailureCode.AccountSwitchRequiresCleanup);
            }

            try
            {
                var session = _storage.Create(accountId);
                Publish(new RecoveryBrowserSessionLifecycleSnapshot(
                    RecoveryBrowserSessionLifecycleState.Active,
                    session,
                    [],
                    RecoveryBrowserSessionFailureCode.None));
                return new RecoveryBrowserSessionStartResult(
                    session,
                    WasReused: false,
                    RecoveryBrowserSessionFailureCode.None);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                Publish(_current with
                {
                    State = RecoveryBrowserSessionLifecycleState.CleanupFailed,
                    FailureCode = RecoveryBrowserSessionFailureCode.StorageUnavailable,
                });
                return new RecoveryBrowserSessionStartResult(
                    null,
                    WasReused: false,
                    RecoveryBrowserSessionFailureCode.StorageUnavailable);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryBrowserSessionCleanupResult> EndAsync(
        Guid sessionId,
        IRecoveryBrowserSessionResources resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = _current.ActiveSession;
            if (session is null || session.SessionId != sessionId)
            {
                return Failure(RecoveryBrowserSessionFailureCode.SessionNotFound);
            }

            Publish(_current with
            {
                State = RecoveryBrowserSessionLifecycleState.Cleaning,
                FailureCode = RecoveryBrowserSessionFailureCode.None,
            });
            TryMarkCleanupPending(session);

            bool releaseSucceeded;
            try
            {
                releaseSucceeded = await ReleaseResourcesAsync(resources, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Publish(_current with
                {
                    State = RecoveryBrowserSessionLifecycleState.CleanupFailed,
                    FailureCode = RecoveryBrowserSessionFailureCode.BrowserReleaseFailed,
                });
                throw;
            }
            if (!releaseSucceeded)
            {
                Publish(_current with
                {
                    State = RecoveryBrowserSessionLifecycleState.CleanupFailed,
                    FailureCode = RecoveryBrowserSessionFailureCode.BrowserReleaseFailed,
                });
                return Failure(RecoveryBrowserSessionFailureCode.BrowserReleaseFailed);
            }

            return DeleteSession(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryBrowserSessionCleanupResult> RetryOrphanCleanupAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var orphan = _current.OrphanedSessions.SingleOrDefault(
                candidate => candidate.SessionId == sessionId);
            if (orphan is null)
            {
                return Failure(RecoveryBrowserSessionFailureCode.SessionNotFound);
            }

            Publish(_current with
            {
                State = RecoveryBrowserSessionLifecycleState.Cleaning,
                FailureCode = RecoveryBrowserSessionFailureCode.None,
            });
            try
            {
                _storage.Delete(orphan.ProfileDataPath);
                var remaining = _current.OrphanedSessions
                    .Where(candidate => candidate.SessionId != sessionId)
                    .ToArray();
                Publish(new RecoveryBrowserSessionLifecycleSnapshot(
                    remaining.Length == 0
                        ? RecoveryBrowserSessionLifecycleState.Idle
                        : RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
                    ActiveSession: null,
                    remaining,
                    RecoveryBrowserSessionFailureCode.None));
                return new RecoveryBrowserSessionCleanupResult(
                    Succeeded: true,
                    RecoveryBrowserSessionFailureCode.None);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                Publish(_current with
                {
                    State = RecoveryBrowserSessionLifecycleState.CleanupFailed,
                    FailureCode = RecoveryBrowserSessionFailureCode.StorageUnavailable,
                });
                return Failure(RecoveryBrowserSessionFailureCode.StorageUnavailable);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Engine-level clearing is defense in depth; complete directory deletion remains authoritative and source details are never exposed.")]
    private static async Task<bool> ReleaseResourcesAsync(
        IRecoveryBrowserSessionResources resources,
        CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await resources.ClearBrowsingDataAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Directory deletion remains authoritative if the platform clear API fails.
            }

            await resources.StopAndReleaseAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsBrowserCleanupFailure(exception))
        {
            return false;
        }
    }

    private RecoveryBrowserSessionCleanupResult DeleteSession(
        RecoveryBrowserSession session)
    {
        try
        {
            _storage.Delete(session.ProfileDataPath);
            Publish(EmptySnapshot);
            return new RecoveryBrowserSessionCleanupResult(
                Succeeded: true,
                RecoveryBrowserSessionFailureCode.None);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            var orphan = new RecoveryBrowserOrphanedSession(
                session.SessionId,
                session.ProfileDataPath);
            Publish(new RecoveryBrowserSessionLifecycleSnapshot(
                RecoveryBrowserSessionLifecycleState.CleanupFailed,
                ActiveSession: null,
                [orphan],
                RecoveryBrowserSessionFailureCode.StorageUnavailable));
            return Failure(RecoveryBrowserSessionFailureCode.StorageUnavailable);
        }
    }

    private void TryMarkCleanupPending(RecoveryBrowserSession session)
    {
        try
        {
            _storage.MarkCleanupPending(session);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // The active marker or profile directory still makes the residue discoverable.
        }
    }

    private static RecoveryBrowserSessionCleanupResult Failure(
        RecoveryBrowserSessionFailureCode code) => new(Succeeded: false, code);

    private void Publish(RecoveryBrowserSessionLifecycleSnapshot snapshot)
    {
        _current = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException;

    private static bool IsBrowserCleanupFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            TimeoutException or DllNotFoundException or EntryPointNotFoundException or
            System.Runtime.InteropServices.COMException;

    private static RecoveryBrowserSessionLifecycleSnapshot EmptySnapshot { get; } = new(
        RecoveryBrowserSessionLifecycleState.Idle,
        ActiveSession: null,
        [],
        RecoveryBrowserSessionFailureCode.None);
}

internal sealed class FileRecoveryBrowserSessionStorage : IRecoveryBrowserSessionStorage
{
    private const string ActiveMarker = "version=1\nstate=active\n";
    private const string CleanupPendingMarker = "version=1\nstate=cleanup-pending\n";
    private const string MarkerFileName = ".unpwn-session";
    private readonly string _applicationDataRoot;
    private readonly string _profilesRoot;

    public FileRecoveryBrowserSessionStorage(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        _applicationDataRoot = Path.GetFullPath(applicationDataRoot);
        _profilesRoot = RecoveryBrowserProfilePath.GetOwnedProfilesRoot(_applicationDataRoot);
    }

    public IReadOnlyList<RecoveryBrowserOrphanedSession> FindOrphanedSessions()
    {
        if (!Directory.Exists(_profilesRoot))
        {
            return [];
        }

        RecoveryBrowserProfilePath.ValidateOwnedProfilesRoot(_applicationDataRoot);
        RecoveryBrowserFilePermissions.EnsurePrivateDirectory(
            Path.GetDirectoryName(_profilesRoot)!);
        RecoveryBrowserFilePermissions.EnsurePrivateDirectory(_profilesRoot);
        RecoveryBrowserProfilePath.ValidateOwnedProfilesRoot(_applicationDataRoot);

        return
        [
            .. Directory.EnumerateFileSystemEntries(_profilesRoot)
                .Select(TryCreateOrphan)
                .OrderBy(orphan => orphan.SessionId),
        ];
    }

    public RecoveryBrowserSession Create(Guid accountId)
    {
        var sessionId = Guid.NewGuid();
        var path = RecoveryBrowserProfilePath.GetOwnedProfileRoot(
            _applicationDataRoot,
            sessionId);
        RecoveryBrowserFilePermissions.EnsurePrivateOwnedProfileHierarchy(
            _applicationDataRoot,
            path);
        WriteMarker(path, ActiveMarker);
        return new RecoveryBrowserSession(sessionId, accountId, path);
    }

    public void MarkCleanupPending(RecoveryBrowserSession session)
    {
        RecoveryBrowserFilePermissions.EnsurePrivateOwnedProfileHierarchy(
            _applicationDataRoot,
            session.ProfileDataPath);
        WriteMarker(session.ProfileDataPath, CleanupPendingMarker);
    }

    public void Delete(string profileDataPath)
    {
        RecoveryBrowserProfilePath.ValidateOwnedProfileRoot(
            profileDataPath,
            _applicationDataRoot);
        if (!Directory.Exists(profileDataPath))
        {
            return;
        }

        Directory.Delete(profileDataPath, recursive: true);
        if (Directory.Exists(profileDataPath))
        {
            throw new IOException(
                "The Recovery Browser profile directory still exists after cleanup.");
        }
    }

    private RecoveryBrowserOrphanedSession TryCreateOrphan(string path)
    {
        var name = Path.GetFileName(path);
        if (!Directory.Exists(path) ||
            !Guid.TryParseExact(name, "N", out var sessionId))
        {
            throw new IOException(
                "The Recovery Browser profile root contains an unexpected entry.");
        }

        RecoveryBrowserFilePermissions.EnsurePrivateOwnedProfileHierarchy(
            _applicationDataRoot,
            path);
        RecoveryBrowserFilePermissions.EnsurePrivateFile(
            Path.Combine(path, MarkerFileName));

        return new RecoveryBrowserOrphanedSession(sessionId, path);
    }

    private static void WriteMarker(string profileDataPath, string content)
    {
        var markerPath = Path.Combine(profileDataPath, MarkerFileName);
        var temporaryPath = Path.Combine(profileDataPath, $".{Guid.NewGuid():N}.tmp");
        try
        {
            RecoveryBrowserFilePermissions.EnsurePrivateFile(markerPath);
            using (var stream = RecoveryBrowserFilePermissions.CreatePrivateFile(temporaryPath))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            RecoveryBrowserFilePermissions.EnsurePrivateFile(temporaryPath);
            File.Move(temporaryPath, markerPath, overwrite: true);
            RecoveryBrowserFilePermissions.EnsurePrivateFile(markerPath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failed best-effort removal must not replace the original marker-write failure.")]
    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
