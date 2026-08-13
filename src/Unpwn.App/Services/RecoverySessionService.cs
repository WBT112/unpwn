using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class RecoverySessionService :
    IRecoverySessionService,
    IRecoverySessionProjectionCoordinator,
    IDisposable
{
    private const string SessionRecordId = "8cf13bd9-2ccc-4b71-958a-439fefc90ac6";
    private static readonly VaultRecordDescriptor SessionDescriptor = new(
        "recovery-session",
        SessionRecordId,
        1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IEncryptedVaultRecordStore _recordStore;
    private readonly IRecoveryWizardVaultCoordinator _wizardCoordinator;
    private readonly Func<DateTimeOffset> _clock;
    private readonly WorkspaceMutationCoordinator _mutationCoordinator;
    private readonly bool _ownsMutationCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public RecoverySessionService(
        IEncryptedVaultRecordStore recordStore,
        IRecoveryWizardVaultCoordinator wizardCoordinator,
        Func<DateTimeOffset>? clock = null,
        WorkspaceMutationCoordinator? mutationCoordinator = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _wizardCoordinator = wizardCoordinator ?? throw new ArgumentNullException(nameof(wizardCoordinator));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _mutationCoordinator = mutationCoordinator ?? new WorkspaceMutationCoordinator();
        _ownsMutationCoordinator = mutationCoordinator is null;
    }

    public event EventHandler? SessionChanged;

    public RecoverySessionLoadState LoadState { get; private set; } = RecoverySessionLoadState.Locked;

    public RecoverySessionWorkspace? CurrentSession { get; private set; }

    public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _mutationCoordinator.ExecuteAsync(
            async token =>
            {
                await InitializeCoreAsync(token);
                return true;
            },
            cancellationToken);

    public Task<RecoverySessionOperationResult> CreateAsync(
        RecoverySessionCreateRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        return _mutationCoordinator.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);
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

    public Task<RecoverySessionOperationResult> DeferAccountAsync(
        Guid accountId,
        long expectedSessionRevision,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _mutationCoordinator.ExecuteAsync(
            token => DeferAccountCoreAsync(accountId, expectedSessionRevision, token),
            cancellationToken);
    }

    public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) =>
        TransitionAsync(
            session => session.Archive(_clock()),
            RecoverySessionWizardTransition.Archive,
            cancellationToken);

    public Task<RecoverySessionOperationResult> CompleteAsync(
        RecoveryCompletionRecord completion,
        long expectedSessionRevision,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(completion);
        return _mutationCoordinator.ExecuteAsync(
            token => CompleteCoreAsync(completion, expectedSessionRevision, token),
            cancellationToken);
    }

    public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(accounts);
        return _mutationCoordinator.ExecuteAsync(
            token => ReplaceAccountSummariesCoreAsync(accounts, token),
            cancellationToken);
    }

    public async Task<PreparedRecoverySessionUpdate> PrepareAccountSummaryUpdateAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(accounts);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked || CurrentSession is null)
            {
                throw new InvalidOperationException("A loaded recovery session is required.");
            }

            var updated = CurrentSession.ReplaceAccounts(accounts, _clock());
            return PrepareState(updated, CurrentSession.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void CommitPreparedUpdate(PreparedRecoverySessionUpdate update)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(update);
        var currentRevision = CurrentSession?.Revision ?? -1;
        if (currentRevision != update.ExpectedRevision)
        {
            throw new InvalidOperationException("The recovery session changed before the prepared update was committed.");
        }

        SetState(RecoverySessionLoadState.Loaded, update.State);
    }

    public void ClearForLock()
    {
        ThrowIfDisposed();
        SetState(RecoverySessionLoadState.Locked, null);
    }

    public void MarkLoadFailed()
    {
        ThrowIfDisposed();
        SetState(RecoverySessionLoadState.LoadFailed, null);
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
        if (_ownsMutationCoordinator)
        {
            _mutationCoordinator.Dispose();
        }

        _disposed = true;
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
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

    private async Task<RecoverySessionOperationResult> CreateCoreAsync(
        RecoverySessionCreateRequest request,
        CancellationToken cancellationToken)
    {
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
                new RecoveryIncidentIntake(request.Indicators),
                _clock());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession is not null ||
                LoadState is RecoverySessionLoadState.Corrupted or RecoverySessionLoadState.LoadFailed)
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

            if (_wizardCoordinator is not IRecoveryWizardPersistenceCoordinator wizardPersistence)
            {
                return await PersistLegacyCreateAsync(session, cancellationToken);
            }

            using var sessionUpdate = PrepareState(session, expectedRevision: -1);
            using var wizardUpdate = wizardPersistence.PrepareTransition(
                RecoverySessionWizardTransition.CompleteIncidentIntake,
                _clock());
            var persisted = await PersistBatchAsync(
                [sessionUpdate.ToWrite(), wizardUpdate.ToWrite()],
                cancellationToken);
            if (!persisted.Succeeded)
            {
                return persisted;
            }

            wizardPersistence.CommitPreparedTransition(wizardUpdate);
            SetState(RecoverySessionLoadState.Loaded, session);
            return RecoverySessionOperationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<RecoverySessionOperationResult> TransitionAsync(
        Func<RecoverySessionWorkspace, RecoverySessionWorkspace> transition,
        RecoverySessionWizardTransition wizardTransition,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(transition);
        return _mutationCoordinator.ExecuteAsync(
            token => TransitionCoreAsync(transition, wizardTransition, token),
            cancellationToken);
    }

    private async Task<RecoverySessionOperationResult> CompleteCoreAsync(
        RecoveryCompletionRecord completion,
        long expectedSessionRevision,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Locked);
            }

            if (CurrentSession is null || CurrentSession.Revision != expectedSessionRevision ||
                _wizardCoordinator is not IRecoveryWizardPersistenceCoordinator wizardPersistence)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            RecoverySessionWorkspace updated;
            try
            {
                completion.Validate();
                updated = CurrentSession.Complete(completion, _clock());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            var transition = completion.Outcome switch
            {
                RecoveryCompletionOutcome.Completed => RecoverySessionWizardTransition.Complete,
                RecoveryCompletionOutcome.FollowUpRequired => RecoverySessionWizardTransition.CompleteWithFollowUp,
                RecoveryCompletionOutcome.Archived => RecoverySessionWizardTransition.CompleteAndArchive,
                _ => throw new ArgumentOutOfRangeException(nameof(completion)),
            };

            try
            {
                using var sessionUpdate = PrepareState(updated, CurrentSession.Revision);
                using var wizardUpdate = wizardPersistence.PrepareTransition(transition, _clock());
                var persisted = await PersistBatchAsync(
                    [sessionUpdate.ToWrite(), wizardUpdate.ToWrite()],
                    cancellationToken);
                if (!persisted.Succeeded)
                {
                    return persisted;
                }

                wizardPersistence.CommitPreparedTransition(wizardUpdate);
                SetState(RecoverySessionLoadState.Loaded, updated);
                return RecoverySessionOperationResult.Success;
            }
            catch (InvalidOperationException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RecoverySessionOperationResult> TransitionCoreAsync(
        Func<RecoverySessionWorkspace, RecoverySessionWorkspace> transition,
        RecoverySessionWizardTransition wizardTransition,
        CancellationToken cancellationToken)
    {
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

            if (_wizardCoordinator is not IRecoveryWizardPersistenceCoordinator wizardPersistence)
            {
                return await PersistLegacyTransitionAsync(updated, wizardTransition, cancellationToken);
            }

            using var sessionUpdate = PrepareState(updated, CurrentSession.Revision);
            using var wizardUpdate = wizardPersistence.PrepareTransition(wizardTransition, _clock());
            var persisted = await PersistBatchAsync(
                [sessionUpdate.ToWrite(), wizardUpdate.ToWrite()],
                cancellationToken);
            if (!persisted.Succeeded)
            {
                return persisted;
            }

            wizardPersistence.CommitPreparedTransition(wizardUpdate);
            SetState(RecoverySessionLoadState.Loaded, updated);
            return RecoverySessionOperationResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RecoverySessionOperationResult> ReplaceAccountSummariesCoreAsync(
        IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
        CancellationToken cancellationToken)
    {
        PreparedRecoverySessionUpdate update;
        try
        {
            update = await PrepareAccountSummaryUpdateAsync(accounts, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return RecoverySessionOperationResult.Failure(
                _recordStore.IsVaultUnlocked
                    ? RecoverySessionOperationFailureCode.InvalidInput
                    : RecoverySessionOperationFailureCode.Locked);
        }

        using (update)
        {
            var result = await PersistBatchAsync([update.ToWrite()], cancellationToken);
            if (result.Succeeded)
            {
                CommitPreparedUpdate(update);
            }

            return result;
        }
    }

    private async Task<RecoverySessionOperationResult> DeferAccountCoreAsync(
        Guid accountId,
        long expectedSessionRevision,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Locked);
            }

            if (CurrentSession is null || CurrentSession.Revision != expectedSessionRevision)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.Conflict);
            }

            RecoverySessionWorkspace updated;
            try
            {
                updated = CurrentSession.DeferAccount(accountId, _clock());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            {
                return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput);
            }

            using var update = PrepareState(updated, CurrentSession.Revision);
            var result = await PersistBatchAsync([update.ToWrite()], cancellationToken);
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

    private async Task<RecoverySessionOperationResult> PersistLegacyCreateAsync(
        RecoverySessionWorkspace session,
        CancellationToken cancellationToken)
    {
        var persisted = await PersistSingleAsync(session, cancellationToken);
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

    private async Task<RecoverySessionOperationResult> PersistLegacyTransitionAsync(
        RecoverySessionWorkspace updated,
        RecoverySessionWizardTransition wizardTransition,
        CancellationToken cancellationToken)
    {
        var persisted = await PersistSingleAsync(updated, cancellationToken);
        if (!persisted.Succeeded)
        {
            return persisted;
        }

        try
        {
            await _wizardCoordinator.ApplyWizardTransitionAsync(wizardTransition, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.IoFailure);
        }

        SetState(RecoverySessionLoadState.Loaded, updated);
        return RecoverySessionOperationResult.Success;
    }

    private static PreparedRecoverySessionUpdate PrepareState(
        RecoverySessionWorkspace state,
        long expectedRevision)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
        return new PreparedRecoverySessionUpdate(
            state,
            SessionDescriptor,
            plaintext,
            expectedRevision);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the atomic encrypted persistence boundary; source exception details must not reach presentation code.")]
    private async Task<RecoverySessionOperationResult> PersistBatchAsync(
        IReadOnlyCollection<VaultRecordWrite> writes,
        CancellationToken cancellationToken)
    {
        try
        {
            await _recordStore.WriteEncryptedRecordsAtomicallyAsync(writes, cancellationToken);
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
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This is the encrypted persistence boundary; source exception details must not reach presentation code.")]
    private async Task<RecoverySessionOperationResult> PersistSingleAsync(
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
