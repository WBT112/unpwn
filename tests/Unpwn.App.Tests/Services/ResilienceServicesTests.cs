using Unpwn.App.Services;
using Unpwn.Application.Diagnostics;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class ResilienceServicesTests
{
    private static readonly VaultRecordDescriptor Descriptor = new(
        "account-state",
        "2be20620-25ab-476e-b468-a020334f019b",
        1);

    [Fact]
    public async Task FailedWriteIsVisibleAndExplicitRepeatIsReportedAsRetry()
    {
        const string secret = "UNPWN_TEST_SECRET_disk-full-details";
        var inner = new FaultingRecordStore
        {
            WriteFailures = new Queue<Exception>([new IOException(secret)]),
        };
        var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
        var store = new ResilientWorkspaceRecordStore(
            inner,
            new SecretSafeDiagnostics(diagnosticStore));
        var observed = new List<WorkspacePersistenceState>();
        store.StatusChanged += (_, _) => observed.Add(store.Current.State);

        await Assert.ThrowsAsync<IOException>(() => store.WriteEncryptedRecordAsync(
            Descriptor,
            new byte[] { 1, 2, 3 },
            CancellationToken.None));
        await store.WriteEncryptedRecordAsync(
            Descriptor,
            new byte[] { 1, 2, 3 },
            CancellationToken.None);

        Assert.Equal(
            [
                WorkspacePersistenceState.Saving,
                WorkspacePersistenceState.SaveFailed,
                WorkspacePersistenceState.Retrying,
                WorkspacePersistenceState.Saved,
            ],
            observed);
        Assert.Equal(2, inner.WriteAttempts);
        Assert.DoesNotContain(
            secret,
            string.Join('|', diagnosticStore.Snapshot()),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StorageFailures))]
    public async Task StorageFailuresMapToStableSafeStatus(
        StorageFailureKind failureKind,
        WorkspacePersistenceFailureCode expectedCode)
    {
        var inner = new FaultingRecordStore
        {
            WriteFailures = new Queue<Exception>([CreateStorageFailure(failureKind)]),
        };
        var store = new ResilientWorkspaceRecordStore(
            inner,
            new SecretSafeDiagnostics(new BoundedSecretSafeDiagnosticStore()));

        await Assert.ThrowsAnyAsync<Exception>(() => store.WriteEncryptedRecordAsync(
            Descriptor,
            new byte[] { 1 },
            CancellationToken.None));

        Assert.Equal(WorkspacePersistenceState.SaveFailed, store.Current.State);
        Assert.Equal(expectedCode, store.Current.FailureCode);
    }

    public static TheoryData<StorageFailureKind, WorkspacePersistenceFailureCode> StorageFailures => new()
    {
        { StorageFailureKind.AccessDenied, WorkspacePersistenceFailureCode.AccessDenied },
        { StorageFailureKind.DiskFull, WorkspacePersistenceFailureCode.IoFailure },
        { StorageFailureKind.VersionIncompatible, WorkspacePersistenceFailureCode.VersionIncompatible },
        { StorageFailureKind.Locked, WorkspacePersistenceFailureCode.LockedOrConflict },
    };

    [Fact]
    public async Task CancelingAnInterruptedWriteDoesNotClaimItWasSaved()
    {
        var inner = new FaultingRecordStore { WaitForCancellation = true };
        var store = new ResilientWorkspaceRecordStore(
            inner,
            new SecretSafeDiagnostics(new BoundedSecretSafeDiagnosticStore()));
        using var cancellation = new CancellationTokenSource();
        var write = store.WriteEncryptedRecordAsync(
            Descriptor,
            new byte[] { 1 },
            cancellation.Token);
        await inner.WriteStarted.Task;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Equal(WorkspacePersistenceState.Canceled, store.Current.State);
        Assert.NotEqual(WorkspacePersistenceState.Saved, store.Current.State);
    }

    [Fact]
    public async Task DiagnosticExportRequiresExactPreviewAndSanitizesAgainAtExportBoundary()
    {
        const string secret = "UNPWN_TEST_SECRET_source-event";
        var source = new BoundedSecretSafeDiagnosticStore();
        source.Write(new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticOperation.WorkspaceSave,
            secret,
            secret,
            secret));
        var writer = new CapturingDiagnosticWriter();
        var service = new DiagnosticExportService(
            source,
            new SecretSafeDiagnostics(source),
            writer,
            () => DateTimeOffset.UnixEpoch,
            "1.0-test");

        var preview = service.CreatePreview();
        var notApproved = await service.ExportAsync(
            preview,
            "diagnostics.json",
            previewApproved: false,
            CancellationToken.None);
        var wrongPreview = await service.ExportAsync(
            preview with { Token = Guid.NewGuid() },
            "diagnostics.json",
            previewApproved: true,
            CancellationToken.None);
        var exported = await service.ExportAsync(
            preview,
            "diagnostics.json",
            previewApproved: true,
            CancellationToken.None);

        Assert.Equal(DiagnosticExportFailureCode.PreviewRequired, notApproved.FailureCode);
        Assert.Equal(DiagnosticExportFailureCode.PreviewRequired, wrongPreview.FailureCode);
        Assert.True(exported.Succeeded);
        Assert.Contains("UNPWN1007", preview.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, preview.Content, StringComparison.Ordinal);
        Assert.Equal(preview.Content, writer.Content);
    }

    [Theory]
    [MemberData(nameof(DiagnosticWriteFailures))]
    public async Task DiagnosticWriteFailuresAreSafeAndDoNotConsumePreview(
        DiagnosticWriteFailureKind failureKind,
        DiagnosticExportFailureCode expectedCode)
    {
        const string secret = "UNPWN_TEST_SECRET_diagnostic-write";
        var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
        var writer = new CapturingDiagnosticWriter
        {
            Failure = failureKind == DiagnosticWriteFailureKind.AccessDenied
                ? new UnauthorizedAccessException(secret)
                : new IOException(secret),
        };
        var service = new DiagnosticExportService(
            diagnosticStore,
            new SecretSafeDiagnostics(diagnosticStore),
            writer);
        var preview = service.CreatePreview();

        var result = await service.ExportAsync(
            preview,
            "diagnostics.json",
            previewApproved: true,
            CancellationToken.None);

        Assert.Equal(expectedCode, result.FailureCode);
        Assert.DoesNotContain(
            secret,
            string.Join('|', diagnosticStore.Snapshot()),
            StringComparison.Ordinal);
    }

    public static TheoryData<DiagnosticWriteFailureKind, DiagnosticExportFailureCode> DiagnosticWriteFailures => new()
    {
        { DiagnosticWriteFailureKind.AccessDenied, DiagnosticExportFailureCode.AccessDenied },
        { DiagnosticWriteFailureKind.IoFailure, DiagnosticExportFailureCode.IoFailure },
    };

    [Fact]
    public void AbnormalExitIsDetectedWithoutPersistingApplicationData()
    {
        var marker = new TestMarkerStore { MarkerExists = true };
        var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
        var service = new ApplicationRunStateService(
            marker,
            new SecretSafeDiagnostics(diagnosticStore));

        var state = service.Begin();
        service.Complete();

        Assert.True(state.PreviousExitWasAbnormal);
        Assert.False(state.MarkerUnavailable);
        Assert.True(marker.Written);
        Assert.True(marker.Deleted);
        Assert.Equal("UNPWN1009", Assert.Single(diagnosticStore.Snapshot()).EventId);
    }

    [Fact]
    public void FileMarkerDetectsInterruptedProcessAndContainsOnlyStaticState()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-run-marker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var markerPath = Path.Combine(directory, "active.marker");
            var firstDiagnostics = new BoundedSecretSafeDiagnosticStore();
            var first = new ApplicationRunStateService(
                new FileApplicationRunMarkerStore(markerPath),
                new SecretSafeDiagnostics(firstDiagnostics));

            Assert.False(first.Begin().PreviousExitWasAbnormal);
            Assert.Equal("running", File.ReadAllText(markerPath));

            var second = new ApplicationRunStateService(
                new FileApplicationRunMarkerStore(markerPath),
                new SecretSafeDiagnostics(new BoundedSecretSafeDiagnosticStore()));
            Assert.True(second.Begin().PreviousExitWasAbnormal);
            second.Complete();

            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnavailableMarkerPathKeepsStartupUsableAndEmitsSafeDiagnostic()
    {
        var marker = new TestMarkerStore
        {
            Failure = new UnauthorizedAccessException("UNPWN_TEST_SECRET_marker-path"),
        };
        var diagnosticStore = new BoundedSecretSafeDiagnosticStore();
        var service = new ApplicationRunStateService(
            marker,
            new SecretSafeDiagnostics(diagnosticStore));

        var state = service.Begin();

        Assert.True(state.MarkerUnavailable);
        Assert.DoesNotContain(
            "UNPWN_TEST_SECRET_",
            string.Join('|', diagnosticStore.Snapshot()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrashBoundaryLocksOnceAndRetainsNoSourceDetails()
    {
        const string secret = "UNPWN_TEST_SECRET_crash-state";
        var crashLock = new TestCrashLock();
        var store = new BoundedSecretSafeDiagnosticStore();
        var boundary = new ApplicationCrashBoundary(
            crashLock,
            new SecretSafeDiagnostics(store));

        boundary.Handle(new InvalidOperationException(secret));
        boundary.Handle(new InvalidOperationException("second"));

        Assert.Equal(1, crashLock.Calls);
        var diagnostic = Assert.Single(store.Snapshot());
        Assert.Equal("UNPWN1010", diagnostic.EventId);
        Assert.DoesNotContain(secret, string.Join('|', diagnostic), StringComparison.Ordinal);
    }

    private sealed class FaultingRecordStore : IEncryptedVaultRecordStore
    {
        public bool IsVaultUnlocked => true;

        public Queue<Exception> WriteFailures { get; init; } = [];

        public bool WaitForCancellation { get; init; }

        public int WriteAttempts { get; private set; }

        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]?> ReadEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

        public async Task WriteEncryptedRecordAsync(
            VaultRecordDescriptor descriptor,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            WriteAttempts++;
            WriteStarted.TrySetResult();
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (WriteFailures.TryDequeue(out var failure))
            {
                throw failure;
            }
        }

        public Task WriteEncryptedRecordsAtomicallyAsync(
            IReadOnlyCollection<VaultRecordWrite> writes,
            CancellationToken cancellationToken) =>
            WriteEncryptedRecordAsync(Descriptor, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    private sealed class CapturingDiagnosticWriter : IDiagnosticFileWriter
    {
        public Exception? Failure { get; init; }

        public string? Content { get; private set; }

        public Task WriteAtomicallyAsync(
            string destinationPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }

            Content = System.Text.Encoding.UTF8.GetString(content.Span);
            return Task.CompletedTask;
        }
    }

    private sealed class TestMarkerStore : IApplicationRunMarkerStore
    {
        public bool MarkerExists { get; init; }

        public Exception? Failure { get; init; }

        public bool Written { get; private set; }

        public bool Deleted { get; private set; }

        public bool Exists()
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return MarkerExists;
        }

        public void Write() => Written = true;

        public void Delete() => Deleted = true;
    }

    private sealed class TestCrashLock : ISafeCrashLock
    {
        public int Calls { get; private set; }

        public void LockAfterApplicationFailure() => Calls++;
    }

    public enum StorageFailureKind
    {
        AccessDenied,
        DiskFull,
        VersionIncompatible,
        Locked,
    }

    public enum DiagnosticWriteFailureKind
    {
        AccessDenied,
        IoFailure,
    }

    private static Exception CreateStorageFailure(StorageFailureKind kind) => kind switch
    {
        StorageFailureKind.AccessDenied => new UnauthorizedAccessException("UNPWN_TEST_SECRET_denied"),
        StorageFailureKind.DiskFull => new IOException("UNPWN_TEST_SECRET_disk-full"),
        StorageFailureKind.VersionIncompatible => new NotSupportedException("UNPWN_TEST_SECRET_version"),
        StorageFailureKind.Locked => new InvalidOperationException("UNPWN_TEST_SECRET_locked"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
