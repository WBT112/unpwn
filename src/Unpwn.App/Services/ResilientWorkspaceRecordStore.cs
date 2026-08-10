using System.Diagnostics.CodeAnalysis;
using Unpwn.Application.Diagnostics;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public enum WorkspacePersistenceState
{
    Idle,
    Saving,
    Saved,
    Retrying,
    SaveFailed,
    Canceled,
}

public enum WorkspacePersistenceFailureCode
{
    None,
    AccessDenied,
    IoFailure,
    VersionIncompatible,
    LockedOrConflict,
}

public sealed record WorkspacePersistenceSnapshot(
    WorkspacePersistenceState State,
    WorkspacePersistenceFailureCode FailureCode,
    long Revision)
{
    public static WorkspacePersistenceSnapshot Empty { get; } =
        new(WorkspacePersistenceState.Idle, WorkspacePersistenceFailureCode.None, 0);
}

public interface IWorkspacePersistenceStatus
{
    event EventHandler? StatusChanged;

    WorkspacePersistenceSnapshot Current { get; }
}

/// <summary>
/// Observes encrypted workspace writes without retaining plaintext or retry delegates.
/// A retry is an explicit re-execution by the caller and is therefore safe and reviewable.
/// </summary>
public sealed class ResilientWorkspaceRecordStore(
    IEncryptedVaultRecordStore inner,
    SecretSafeDiagnostics diagnostics) :
    IEncryptedVaultRecordStore,
    IWorkspacePersistenceStatus
{
    private readonly IEncryptedVaultRecordStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly SecretSafeDiagnostics _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly Lock _statusGate = new();

    public event EventHandler? StatusChanged;

    public bool IsVaultUnlocked => _inner.IsVaultUnlocked;

    public WorkspacePersistenceSnapshot Current { get; private set; } =
        WorkspacePersistenceSnapshot.Empty;

    public async Task<byte[]?> ReadEncryptedRecordAsync(
        VaultRecordDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _inner.ReadEncryptedRecordAsync(descriptor, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            _diagnostics.ReportFailure(DiagnosticOperation.WorkspaceLoad, exception);
            throw;
        }
    }

    public Task WriteEncryptedRecordAsync(
        VaultRecordDescriptor descriptor,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken) =>
        ObserveWriteAsync(
            token => _inner.WriteEncryptedRecordAsync(descriptor, plaintext, token),
            cancellationToken);

    public Task WriteEncryptedRecordsAtomicallyAsync(
        IReadOnlyCollection<VaultRecordWrite> writes,
        CancellationToken cancellationToken) =>
        ObserveWriteAsync(
            token => _inner.WriteEncryptedRecordsAtomicallyAsync(writes, token),
            cancellationToken);

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the secret-safe persistence boundary; only exception type and a static diagnostic are retained.")]
    private async Task ObserveWriteAsync(
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        var retrying = Current.State == WorkspacePersistenceState.SaveFailed;
        Publish(
            retrying ? WorkspacePersistenceState.Retrying : WorkspacePersistenceState.Saving,
            WorkspacePersistenceFailureCode.None);
        try
        {
            await write(cancellationToken);
            Publish(WorkspacePersistenceState.Saved, WorkspacePersistenceFailureCode.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(WorkspacePersistenceState.Canceled, WorkspacePersistenceFailureCode.None);
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.WorkspaceSave, exception);
            Publish(WorkspacePersistenceState.SaveFailed, Map(exception));
            throw;
        }
    }

    private void Publish(
        WorkspacePersistenceState state,
        WorkspacePersistenceFailureCode failureCode)
    {
        lock (_statusGate)
        {
            Current = new WorkspacePersistenceSnapshot(
                state,
                failureCode,
                Current.Revision + 1);
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static WorkspacePersistenceFailureCode Map(Exception exception) => exception switch
    {
        UnauthorizedAccessException => WorkspacePersistenceFailureCode.AccessDenied,
        NotSupportedException => WorkspacePersistenceFailureCode.VersionIncompatible,
        InvalidOperationException => WorkspacePersistenceFailureCode.LockedOrConflict,
        _ => WorkspacePersistenceFailureCode.IoFailure,
    };

    private static bool IsExpectedStorageFailure(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        NotSupportedException or
        InvalidOperationException;
}
