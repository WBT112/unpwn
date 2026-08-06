using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;

namespace Unpwn.App.Services;

public sealed class RecoverySessionService(
    IEncryptedVaultRecordStore recordStore,
    IRecoveryWizardVaultCoordinator wizardCoordinator,
    Func<DateTimeOffset>? clock = null)
    : IRecoverySessionService, IDisposable
{
    private const string SessionRecordId = "8cf13bd9-2ccc-4b71-958a-439fefc90ac6";
    private static readonly VaultRecordDescriptor SessionDescriptor = new(
        "recovery-session",
        SessionRecordId,
        1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    private readonly IEncryptedVaultRecordStore _recordStore =
        recordStore ?? throw new ArgumentNullException(nameof(recordStore));
    private readonly IRecoveryWizardVaultCoordinator _wizardCoordinator =
        wizardCoordinator ?? throw new ArgumentNullException(nameof(wizardCoordinator));
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public event EventHandler? SessionChanged;

    public RecoverySessionLoadState LoadState { get; private set; } = RecoverySessionLoadState.Locked;

    public RecoverySessionWorkspace? CurrentSession { get; private set; }

    public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                SetState(RecoverySessionLoadState.Locked, null);
                return;
            }

            SetState(RecoverySessionLoadState.Loading, null);
            byte[]? plaintext = null;
            try
            {
                plaintext = await _recordStore.ReadEncryptedRecordAsync(
                    SessionDescriptor,
                    cancellationToken);
                if (plaintext is null)
                {
                    SetState(RecoverySessionLoadState.Empty, null);
                    return;
                }

                var session = JsonSerializer.Deserialize<RecoverySessionWorkspace>(
                    plaintext,
                    SerializerOptions)
                    ?? throw new JsonException("The recovery session record is empty.");
                session.Validate();
                SetState(RecoverySessionLoadState.Loaded, session);
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                SetState(RecoverySessionLoadState.Corrupted, null);
            }
            finally
            {
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoverySessionOperationResult> CreateAsync(
        RecoverySessionCreateRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!_recordStore.IsVaultUnlocked)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Locked);
        }

        if (!request.SecurityWarningAcknowledged)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput);
        }

        RecoverySessionWorkspace session;
        try
        {
            session = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                request.Name,
                new RecoveryIncidentIntake(request.Indicators, request.IncidentDescription),
                _clock());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession is not null || LoadState == RecoverySessionLoadState.Corrupted)
            {
                return RecoverySessionOperationResult.Failure(
                    LoadState == RecoverySessionLoadState.Corrupted
                        ? RecoverySessionOperationFailureCode.Corrupted
                        : RecoverySessionOperationFailureCode.Conflict);
            }

            if (_wizardCoordinator.CurrentWizard.CurrentStep != RecoveryWizardStepId.IncidentIntake ||
                _wizardCoordinator.CurrentWizard.Status != RecoveryWizardLifecycleStatus.Active)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            var persisted = await PersistAsync(session, cancellationToken);
            if (!persisted.Succeeded)
            {
                return persisted;
            }

            try
            {
                await _wizardCoordinator.ApplyWizardTransitionAsync(
                    RecoverySessionWizardTransition.CompleteIncidentIntake,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.IoFailure);
            }

            SetState(RecoverySessionLoadState.Loaded, session);
            return RecoverySessionOperationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) =>
        TransitionAsync(
            session => session.Pause(_clock()),
            RecoverySessionWizardTransition.Pause,
            cancellationToken);

    public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) =>
        TransitionAsync(
            session => session.Resume(_clock()),
            RecoverySessionWizardTransition.Resume,
            cancellationToken);

    public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
        TransitionAsync(
            session => session.Archive(_clock()),
            RecoverySessionWizardTransition.Archive,
            cancellationToken);

    public async Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(accounts);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Locked);
            }

            if (CurrentSession is null)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            RecoverySessionWorkspace updated;
            try
            {
                updated = CurrentSession.ReplaceAccounts(accounts, _clock());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput);
            }

            var result = await PersistAsync(updated, cancellationToken);
            if (result.Succeeded)
            {
                SetState(RecoverySessionLoadState.Loaded, updated);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ClearForLock()
    {
        ThrowIfDisposed();
        SetState(RecoverySessionLoadState.Locked, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CurrentSession = null;
        _wizardCoordinator.SetSessionDisplayName(null);
        _gate.Dispose();
        _disposed = true;
    }

    private async Task<RecoverySessionOperationResult> TransitionAsync(
        Func<RecoverySessionWorkspace, RecoverySessionWorkspace> transition,
        RecoverySessionWizardTransition wizardTransition,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(transition);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Locked);
            }

            if (CurrentSession is null)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            RecoverySessionWorkspace updated;
            try
            {
                updated = transition(CurrentSession);
            }
            catch (InvalidOperationException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            var persisted = await PersistAsync(updated, cancellationToken);
            if (!persisted.Succeeded)
            {
                return persisted;
            }

            try
            {
                await _wizardCoordinator.ApplyWizardTransitionAsync(
                    wizardTransition,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.IoFailure);
            }

            SetState(RecoverySessionLoadState.Loaded, updated);
            return RecoverySessionOperationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the encrypted persistence boundary; source exception details must not reach presentation code.")]
    private async Task<RecoverySessionOperationResult> PersistAsync(
        RecoverySessionWorkspace session,
        CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, SerializerOptions);
        try
        {
            await _recordStore.WriteEncryptedRecordAsync(
                SessionDescriptor,
                plaintext,
                cancellationToken);
            return RecoverySessionOperationResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.IoFailure);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void SetState(
        RecoverySessionLoadState loadState,
        RecoverySessionWorkspace? session)
    {
        LoadState = loadState;
        CurrentSession = session;
        _wizardCoordinator.SetSessionDisplayName(session?.Name);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
