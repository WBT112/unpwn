using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserSessionLifecycleTests
{
    [Fact]
    public void SameAccountReusesSessionAndDifferentAccountCannotInheritIt()
    {
        var storage = new TestSessionStorage();
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var firstAccount = Guid.NewGuid();

        var first = lifecycle.Start(firstAccount);
        var reused = lifecycle.Start(firstAccount);
        var switched = lifecycle.Start(Guid.NewGuid());

        Assert.True(first.Succeeded);
        Assert.False(first.WasReused);
        Assert.Equal(first.Session, reused.Session);
        Assert.True(reused.WasReused);
        Assert.False(switched.Succeeded);
        Assert.Equal(
            RecoveryBrowserSessionFailureCode.AccountSwitchRequiresCleanup,
            switched.FailureCode);
        Assert.Single(storage.CreatedAccounts);
    }

    [Fact]
    public async Task CleanCloseClearsBrowserThenReleasesResourcesBeforeDeletingProfile()
    {
        var operations = new List<string>();
        var storage = new TestSessionStorage(operations);
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var session = lifecycle.Start(Guid.NewGuid()).Session!;
        var resources = new TestSessionResources(operations);

        var result = await lifecycle.EndAsync(
            session.SessionId,
            resources,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["create", "mark-cleanup", "clear-browser", "release-browser", "delete-profile"],
            operations);
        Assert.Equal(RecoveryBrowserSessionLifecycleState.Idle, lifecycle.Current.State);
        Assert.Null(lifecycle.Current.ActiveSession);
    }

    [Fact]
    public async Task DirectoryDeletionRemainsAuthoritativeWhenPlatformClearFails()
    {
        var operations = new List<string>();
        var storage = new TestSessionStorage(operations);
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var session = lifecycle.Start(Guid.NewGuid()).Session!;
        var resources = new TestSessionResources(operations)
        {
            ClearFailure = new InvalidOperationException("synthetic platform failure"),
        };

        var result = await lifecycle.EndAsync(
            session.SessionId,
            resources,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["create", "mark-cleanup", "clear-browser", "release-browser", "delete-profile"],
            operations);
    }

    [Fact]
    public async Task BrowserReleaseFailureIsVisibleRetryableAndPreventsDeletionRace()
    {
        var operations = new List<string>();
        var storage = new TestSessionStorage(operations);
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var session = lifecycle.Start(Guid.NewGuid()).Session!;
        var firstResources = new TestSessionResources(operations)
        {
            ReleaseFailure = new IOException("profile still locked"),
        };

        var failed = await lifecycle.EndAsync(
            session.SessionId,
            firstResources,
            CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Equal(
            RecoveryBrowserSessionFailureCode.BrowserReleaseFailed,
            failed.FailureCode);
        Assert.Equal(RecoveryBrowserSessionLifecycleState.CleanupFailed, lifecycle.Current.State);
        Assert.True(lifecycle.Current.CanRetryCleanup);
        Assert.DoesNotContain("delete-profile", operations);

        var retried = await lifecycle.EndAsync(
            session.SessionId,
            new TestSessionResources(operations),
            CancellationToken.None);

        Assert.True(retried.Succeeded);
        Assert.Equal("delete-profile", operations[^1]);
    }

    [Fact]
    public async Task CanceledReleaseKeepsCleanupFailureVisibleAndProfileIntact()
    {
        var storage = new TestSessionStorage();
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var session = lifecycle.Start(Guid.NewGuid()).Session!;
        using var cancellation = new CancellationTokenSource();
        var resources = new CancelableSessionResources();
        var cleanup = lifecycle.EndAsync(session.SessionId, resources, cancellation.Token);
        await resources.ReleaseStarted.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanup);
        Assert.Equal(RecoveryBrowserSessionLifecycleState.CleanupFailed, lifecycle.Current.State);
        Assert.Equal(
            RecoveryBrowserSessionFailureCode.BrowserReleaseFailed,
            lifecycle.Current.FailureCode);
        Assert.Equal(session, lifecycle.Current.ActiveSession);
        Assert.Equal(0, storage.DeleteCalls);
    }

    [Fact]
    public async Task DeleteFailureBecomesOrphanAndExplicitRetryRemovesIt()
    {
        var storage = new TestSessionStorage { DeleteFailures = 1 };
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);
        var session = lifecycle.Start(Guid.NewGuid()).Session!;

        var failed = await lifecycle.EndAsync(
            session.SessionId,
            new TestSessionResources(),
            CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Null(lifecycle.Current.ActiveSession);
        Assert.Equal(
            RecoveryBrowserSessionFailureCode.StorageUnavailable,
            lifecycle.Current.FailureCode);
        Assert.Equal(session.SessionId, Assert.Single(lifecycle.Current.OrphanedSessions).SessionId);

        var retried = await lifecycle.RetryOrphanCleanupAsync(
            session.SessionId,
            CancellationToken.None);

        Assert.True(retried.Succeeded);
        Assert.False(lifecycle.Current.HasUncleanSessionData);
        Assert.Equal(RecoveryBrowserSessionLifecycleState.Idle, lifecycle.Current.State);
    }

    [Fact]
    public void StartupDetectsOrphansAndNeverAutomaticallyResumesThem()
    {
        var orphan = new RecoveryBrowserOrphanedSession(
            Guid.NewGuid(),
            "/opaque/browser/profile");
        var storage = new TestSessionStorage { Orphans = [orphan] };
        var lifecycle = new RecoveryBrowserSessionLifecycle(storage);

        var startup = lifecycle.InspectStartup();
        var start = lifecycle.Start(Guid.NewGuid());

        Assert.Equal(
            RecoveryBrowserSessionLifecycleState.OrphanedDataDetected,
            startup.State);
        Assert.Null(startup.ActiveSession);
        Assert.False(start.Succeeded);
        Assert.Equal(
            RecoveryBrowserSessionFailureCode.OrphanedDataRequiresCleanup,
            start.FailureCode);
        Assert.Equal(0, storage.DeleteCalls);
    }

    [Fact]
    public async Task FileStorageMarkerIsOpaqueAndRecursiveCleanupIncludesDownloads()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-browser-session-{Guid.NewGuid():N}");
        try
        {
            var accountId = Guid.NewGuid();
            var lifecycle = new RecoveryBrowserSessionLifecycle(root);
            Assert.Empty(lifecycle.InspectStartup().OrphanedSessions);
            var session = lifecycle.Start(accountId).Session!;
            var marker = Path.Combine(session.ProfileDataPath, ".unpwn-session");
            var download = Path.Combine(session.ProfileDataPath, "downloads", "synthetic.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(download)!);
            await File.WriteAllTextAsync(download, "synthetic download");

            var markerContent = await File.ReadAllTextAsync(marker);
            Assert.Equal("version=1\nstate=active\n", markerContent);
            Assert.DoesNotContain(accountId.ToString(), markerContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(accountId.ToString(), session.ProfileDataPath, StringComparison.OrdinalIgnoreCase);

            var restarted = new RecoveryBrowserSessionLifecycle(root);
            var orphan = Assert.Single(restarted.InspectStartup().OrphanedSessions);
            Assert.Equal(session.SessionId, orphan.SessionId);
            Assert.Null(restarted.Current.ActiveSession);

            var cleanup = await restarted.RetryOrphanCleanupAsync(
                orphan.SessionId,
                CancellationToken.None);

            Assert.True(cleanup.Succeeded);
            Assert.False(Directory.Exists(session.ProfileDataPath));
            Assert.False(File.Exists(download));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void UnexpectedProfileRootEntriesFailClosedInsteadOfBeingIgnored()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-browser-root-{Guid.NewGuid():N}");
        try
        {
            var profiles = RecoveryBrowserProfilePath.GetOwnedProfilesRoot(root);
            Directory.CreateDirectory(profiles);
            Directory.CreateDirectory(Path.Combine(profiles, "unexpected-normal-profile"));
            using var lifecycle = new RecoveryBrowserSessionLifecycle(root);

            var startup = lifecycle.InspectStartup();

            Assert.Equal(RecoveryBrowserSessionLifecycleState.CleanupFailed, startup.State);
            Assert.Equal(
                RecoveryBrowserSessionFailureCode.StorageUnavailable,
                startup.FailureCode);
            Assert.True(startup.HasUncleanSessionData);
            Assert.Null(startup.ActiveSession);
            Assert.Equal(
                RecoveryBrowserSessionFailureCode.StorageUnavailable,
                lifecycle.Start(Guid.NewGuid()).FailureCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestSessionStorage(List<string>? operations = null)
        : IRecoveryBrowserSessionStorage
    {
        private readonly List<string> _operations = operations ?? [];

        public List<Guid> CreatedAccounts { get; } = [];

        public IReadOnlyList<RecoveryBrowserOrphanedSession> Orphans { get; set; } = [];

        public int DeleteFailures { get; set; }

        public int DeleteCalls { get; private set; }

        public IReadOnlyList<RecoveryBrowserOrphanedSession> FindOrphanedSessions() => Orphans;

        public RecoveryBrowserSession Create(Guid accountId)
        {
            _operations.Add("create");
            CreatedAccounts.Add(accountId);
            var sessionId = Guid.NewGuid();
            return new RecoveryBrowserSession(
                sessionId,
                accountId,
                $"/opaque/{sessionId:N}");
        }

        public void MarkCleanupPending(RecoveryBrowserSession session) =>
            _operations.Add("mark-cleanup");

        public void Delete(string profileDataPath)
        {
            DeleteCalls++;
            if (DeleteFailures-- > 0)
            {
                throw new IOException("synthetic delete failure");
            }

            _operations.Add("delete-profile");
        }
    }

    private sealed class TestSessionResources(List<string>? operations = null)
        : IRecoveryBrowserSessionResources
    {
        private readonly List<string> _operations = operations ?? [];

        public Exception? ClearFailure { get; init; }

        public Exception? ReleaseFailure { get; init; }

        public Task ClearBrowsingDataAsync(CancellationToken cancellationToken)
        {
            _operations.Add("clear-browser");
            return ClearFailure is null
                ? Task.CompletedTask
                : Task.FromException(ClearFailure);
        }

        public Task StopAndReleaseAsync(CancellationToken cancellationToken)
        {
            _operations.Add("release-browser");
            return ReleaseFailure is null
                ? Task.CompletedTask
                : Task.FromException(ReleaseFailure);
        }
    }

    private sealed class CancelableSessionResources : IRecoveryBrowserSessionResources
    {
        public TaskCompletionSource ReleaseStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async Task StopAndReleaseAsync(CancellationToken cancellationToken)
        {
            ReleaseStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
