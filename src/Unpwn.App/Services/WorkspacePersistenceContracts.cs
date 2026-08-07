using System.Security.Cryptography;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class WorkspaceMutationCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }
}

public sealed class PreparedRecoverySessionUpdate : IDisposable
{
    private byte[]? _plaintext;

    public PreparedRecoverySessionUpdate(
        RecoverySessionWorkspace state,
        VaultRecordDescriptor descriptor,
        byte[] plaintext,
        long expectedRevision)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _plaintext = plaintext ?? throw new ArgumentNullException(nameof(plaintext));
        ExpectedRevision = expectedRevision;
    }

    public RecoverySessionWorkspace State { get; }

    public VaultRecordDescriptor Descriptor { get; }

    public long ExpectedRevision { get; }

    public ReadOnlyMemory<byte> Plaintext => _plaintext ?? throw new ObjectDisposedException(nameof(PreparedRecoverySessionUpdate));

    public VaultRecordWrite ToWrite() => new(Descriptor, Plaintext);

    public void Dispose()
    {
        if (_plaintext is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_plaintext);
        _plaintext = null;
    }
}

public sealed class PreparedRecoveryWizardUpdate : IDisposable
{
    private byte[]? _plaintext;

    public PreparedRecoveryWizardUpdate(
        RecoveryWizardState state,
        VaultRecordDescriptor descriptor,
        byte[] plaintext,
        long expectedRevision)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _plaintext = plaintext ?? throw new ArgumentNullException(nameof(plaintext));
        ExpectedRevision = expectedRevision;
    }

    public RecoveryWizardState State { get; }

    public VaultRecordDescriptor Descriptor { get; }

    public long ExpectedRevision { get; }

    public ReadOnlyMemory<byte> Plaintext => _plaintext ?? throw new ObjectDisposedException(nameof(PreparedRecoveryWizardUpdate));

    public VaultRecordWrite ToWrite() => new(Descriptor, Plaintext);

    public void Dispose()
    {
        if (_plaintext is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_plaintext);
        _plaintext = null;
    }
}

public interface IRecoverySessionProjectionCoordinator
{
    Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken);

    void CommitPreparedUpdate(PreparedRecoverySessionUpdate update);
}

public interface IRecoveryWizardPersistenceCoordinator
{
    PreparedRecoveryWizardUpdate PrepareTransition(
        RecoverySessionWizardTransition transition,
        DateTimeOffset occurredAt);

    void CommitPreparedTransition(PreparedRecoveryWizardUpdate update);
}
