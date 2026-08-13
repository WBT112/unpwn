using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class AccountRecoveryExecutionService : IAccountRecoveryExecutionService
{
    private const string RecordType = "account-execution";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly IEncryptedVaultRecordStore _recordStore;
    private readonly IRecoverySessionService _sessionService;
    private readonly IRecoverySessionProjectionCoordinator _projectionCoordinator;
    private readonly WorkspaceMutationCoordinator _mutationCoordinator;
    private readonly Func<DateTimeOffset> _clock;

    public AccountRecoveryExecutionService(
        IEncryptedVaultRecordStore recordStore,
        IRecoverySessionService sessionService,
        WorkspaceMutationCoordinator mutationCoordinator,
        Func<DateTimeOffset>? clock = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _projectionCoordinator = sessionService as IRecoverySessionProjectionCoordinator
            ?? throw new ArgumentException(
                "The recovery session service must support prepared dashboard projections.",
                nameof(sessionService));
        _mutationCoordinator = mutationCoordinator ?? throw new ArgumentNullException(nameof(mutationCoordinator));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<AccountRecoveryExecutionResult> LoadAsync(
        Guid accountId,
        RecoveryWorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return _mutationCoordinator.ExecuteAsync(
            token => LoadCoreAsync(accountId, workflow, token),
            cancellationToken);
    }

    public Task<AccountRecoveryExecutionResult> CreateAsync(
        AccountRecoveryExecutionCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _mutationCoordinator.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);
    }

    public Task<AccountRecoveryExecutionResult> ApplyAsync(
        AccountRecoveryExecutionTransitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _mutationCoordinator.ExecuteAsync(
            token => ApplyCoreAsync(request, token),
            cancellationToken);
    }

    public void ClearForLock()
    {
        // The service materializes no long-lived decrypted execution state.
    }

    private async Task<AccountRecoveryExecutionResult> LoadCoreAsync(
        Guid accountId,
        RecoveryWorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        if (!_recordStore.IsVaultUnlocked)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Locked);
        }

        if (accountId == Guid.Empty)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.InvalidInput);
        }

        var loaded = await ReadAsync(accountId, workflow, cancellationToken);
        return loaded.FailureCode == AccountRecoveryExecutionFailureCode.None
            ? AccountRecoveryExecutionResult.Success(loaded.Execution!.State)
            : AccountRecoveryExecutionResult.Failure(loaded.FailureCode);
    }

    private async Task<AccountRecoveryExecutionResult> CreateCoreAsync(
        AccountRecoveryExecutionCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!_recordStore.IsVaultUnlocked)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Locked);
        }

        if (_sessionService.CurrentSession?.IsReadOnly == true)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
        }

        try
        {
            ValidateCreateRequest(request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.InvalidInput);
        }

        var loaded = await ReadAsync(request.AccountId, request.Workflow, cancellationToken);
        if (loaded.FailureCode == AccountRecoveryExecutionFailureCode.None)
        {
            return loaded.Execution!.HasOperation(request.OperationId)
                ? AccountRecoveryExecutionResult.Success(loaded.Execution.State)
                : AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
        }

        if (loaded.FailureCode != AccountRecoveryExecutionFailureCode.NotFound)
        {
            return AccountRecoveryExecutionResult.Failure(loaded.FailureCode);
        }

        AccountRecoveryExecutionState state;
        try
        {
            if (!RecoveryPathSelector.Select(request.Workflow).HasSafePath)
            {
                return AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.NoSafeRecoveryPath);
            }

            state = AccountRecoveryExecutionState.Create(
                request.AccountId,
                request.Workflow,
                _clock());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.InvalidInput);
        }

        var persisted = new PersistedAccountRecoveryExecution(
            state,
            [request.OperationId]);
        return await PersistWithProjectionAsync(
            persisted,
            request.ProjectionContext,
            cancellationToken);
    }

    private async Task<AccountRecoveryExecutionResult> ApplyCoreAsync(
        AccountRecoveryExecutionTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_recordStore.IsVaultUnlocked)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Locked);
        }

        if (_sessionService.CurrentSession?.IsReadOnly == true)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
        }

        try
        {
            ValidateTransitionRequest(request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.InvalidInput);
        }

        var loaded = await ReadAsync(request.AccountId, request.Workflow, cancellationToken);
        if (loaded.FailureCode != AccountRecoveryExecutionFailureCode.None)
        {
            return AccountRecoveryExecutionResult.Failure(loaded.FailureCode);
        }

        var current = loaded.Execution!;
        if (current.HasOperation(request.OperationId))
        {
            return AccountRecoveryExecutionResult.Success(current.State);
        }

        if (current.State.Revision != request.ExpectedRevision)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
        }

        AccountRecoveryExecutionState updated;
        try
        {
            updated = ApplyTransition(current.State, request, _clock());
            updated.Validate(request.Workflow);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
        }

        var persisted = current with
        {
            State = updated,
            AppliedOperationIds = [.. current.AppliedOperationIds, request.OperationId],
        };
        return await PersistWithProjectionAsync(
            persisted,
            request.ProjectionContext,
            cancellationToken);
    }

    private async Task<AccountRecoveryExecutionResult> PersistWithProjectionAsync(
        PersistedAccountRecoveryExecution execution,
        AccountRecoveryProjectionContext context,
        CancellationToken cancellationToken)
    {
        byte[]? plaintext = null;
        PreparedRecoverySessionUpdate? projection = null;
        try
        {
            execution.Validate();
            context.Validate();
            var session = _sessionService.CurrentSession;
            if (session is null)
            {
                return AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.Conflict);
            }

            var projectedAccount = execution.State.CreateDashboardProjection(context.Category);
            var summaries = session.Accounts
                .Where(account => account.AccountId != execution.State.AccountId)
                .Append(projectedAccount)
                .OrderBy(account => account.AccountId)
                .ToArray();
            projection = await _projectionCoordinator.PrepareAccountSummaryUpdateAsync(
                summaries,
                cancellationToken);
            plaintext = JsonSerializer.SerializeToUtf8Bytes(execution, SerializerOptions);
            await _recordStore.WriteEncryptedRecordsAtomicallyAsync(
                [
                    new VaultRecordWrite(Descriptor(execution.State.AccountId), plaintext),
                    projection.ToWrite(),
                ],
                cancellationToken);
            _projectionCoordinator.CommitPreparedUpdate(projection);
            return AccountRecoveryExecutionResult.Success(execution.State);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSafePersistenceFailure(exception))
        {
            return AccountRecoveryExecutionResult.Failure(MapPersistenceFailure(exception));
        }
        finally
        {
            projection?.Dispose();
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This encrypted read boundary converts storage failures to language-neutral result codes without exposing source exception details.")]
    private async Task<LoadResult> ReadAsync(
        Guid accountId,
        RecoveryWorkflowDefinition workflow,
        CancellationToken cancellationToken)
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = await _recordStore.ReadEncryptedRecordAsync(
                Descriptor(accountId),
                cancellationToken);
            if (plaintext is null)
            {
                return new LoadResult(AccountRecoveryExecutionFailureCode.NotFound, null);
            }

            var persisted = JsonSerializer.Deserialize<PersistedAccountRecoveryExecution>(
                plaintext,
                SerializerOptions)
                ?? throw new JsonException("The account recovery execution record is empty.");
            persisted.Validate();
            persisted.State.Validate(workflow);
            if (persisted.State.AccountId != accountId)
            {
                throw new InvalidOperationException("The account recovery execution identifier does not match its record.");
            }

            return new LoadResult(AccountRecoveryExecutionFailureCode.None, persisted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new LoadResult(
                exception is JsonException or InvalidOperationException or NotSupportedException
                    ? AccountRecoveryExecutionFailureCode.Corrupted
                    : AccountRecoveryExecutionFailureCode.PersistenceFailure,
                null);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static AccountRecoveryExecutionState ApplyTransition(
        AccountRecoveryExecutionState state,
        AccountRecoveryExecutionTransitionRequest request,
        DateTimeOffset occurredAt)
    {
        return request.Transition switch
        {
            AccountRecoveryExecutionTransitionKind.SetAccessAvailable =>
                state.SetAccessState(request.Workflow, RecoveryAccessState.Available, null, occurredAt),
            AccountRecoveryExecutionTransitionKind.SetAccessLost =>
                state.SetAccessState(request.Workflow, RecoveryAccessState.Lost, request.UserReason, occurredAt),
            AccountRecoveryExecutionTransitionKind.SetWaitingForProviderReview =>
                state.SetAccessState(
                    request.Workflow,
                    RecoveryAccessState.WaitingForProviderReview,
                    request.UserReason,
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.StartAction =>
                state.StartAction(request.Workflow, RequireActionId(request), occurredAt),
            AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements =>
                state.SetCompletionCriteriaAcknowledgements(
                    request.Workflow,
                    RequireActionId(request),
                    request.AcknowledgedCompletionCriteria
                        ?? throw new InvalidOperationException(
                            "The transition requires completion-criteria acknowledgements."),
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.CompleteAction =>
                state.CompleteAction(
                    request.Workflow,
                    RequireActionId(request),
                    request.CompletionCriteriaAcknowledged,
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.RequireUserAction =>
                state.RequireUserAction(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.BlockAction =>
                state.BlockAction(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.FailAction =>
                state.FailActionAndSelectFallback(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.MarkTrulyNotApplicable =>
                state.MarkNotApplicable(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    NotApplicableDisposition.TrulyNotApplicable,
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.AcceptNotApplicableRisk =>
                state.MarkNotApplicable(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    NotApplicableDisposition.UnresolvedRisk,
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.AcceptUnresolvedRisk =>
                state.AcceptUnresolvedRisk(
                    request.Workflow,
                    RequireActionId(request),
                    RequireReason(request),
                    occurredAt),
            AccountRecoveryExecutionTransitionKind.SetUserNotes =>
                state.SetUserNotes(RequireActionId(request), request.UserNotes, occurredAt),
            AccountRecoveryExecutionTransitionKind.AttachCredentialReference =>
                state.AttachCredentialReference(
                    RequireActionId(request),
                    request.CredentialReference
                        ?? throw new InvalidOperationException("A generated credential reference is required."),
                    occurredAt),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Transition, "Unknown account recovery transition."),
        };
    }

    private static void ValidateCreateRequest(AccountRecoveryExecutionCreateRequest request)
    {
        if (request.OperationId == Guid.Empty || request.AccountId == Guid.Empty)
        {
            throw new ArgumentException("Creation requires operation and account identifiers.");
        }

        ArgumentNullException.ThrowIfNull(request.Workflow);
        ArgumentNullException.ThrowIfNull(request.ProjectionContext);
        request.ProjectionContext.Validate();
    }

    private static void ValidateTransitionRequest(AccountRecoveryExecutionTransitionRequest request)
    {
        if (request.OperationId == Guid.Empty || request.AccountId == Guid.Empty || request.ExpectedRevision < 0 ||
            !Enum.IsDefined(request.Transition))
        {
            throw new ArgumentException("The account recovery transition request is invalid.");
        }

        ArgumentNullException.ThrowIfNull(request.Workflow);
        ArgumentNullException.ThrowIfNull(request.ProjectionContext);
        request.ProjectionContext.Validate();
    }

    private static string RequireActionId(AccountRecoveryExecutionTransitionRequest request) =>
        string.IsNullOrWhiteSpace(request.ActionDefinitionId)
            ? throw new InvalidOperationException("The transition requires an action definition identifier.")
            : request.ActionDefinitionId;

    private static string RequireReason(AccountRecoveryExecutionTransitionRequest request) =>
        string.IsNullOrWhiteSpace(request.UserReason)
            ? throw new InvalidOperationException("The transition requires a user-authored reason.")
            : request.UserReason;

    private static VaultRecordDescriptor Descriptor(Guid accountId) =>
        new(RecordType, accountId.ToString("D"), 1);

    private static bool IsSafePersistenceFailure(Exception exception) => exception is
        ArgumentException or
        InvalidOperationException or
        IOException or
        JsonException or
        NotSupportedException;

    private static AccountRecoveryExecutionFailureCode MapPersistenceFailure(Exception exception) =>
        exception is ArgumentException or InvalidOperationException
            ? AccountRecoveryExecutionFailureCode.Conflict
            : AccountRecoveryExecutionFailureCode.PersistenceFailure;

    private sealed record LoadResult(
        AccountRecoveryExecutionFailureCode FailureCode,
        PersistedAccountRecoveryExecution? Execution);

    private sealed record PersistedAccountRecoveryExecution(
        AccountRecoveryExecutionState State,
        Guid[] AppliedOperationIds)
    {
        public bool HasOperation(Guid operationId) => AppliedOperationIds.Contains(operationId);

        public void Validate()
        {
            ArgumentNullException.ThrowIfNull(State);
            ArgumentNullException.ThrowIfNull(AppliedOperationIds);
            if (AppliedOperationIds.Length == 0 ||
                AppliedOperationIds.Any(operationId => operationId == Guid.Empty) ||
                AppliedOperationIds.Distinct().Count() != AppliedOperationIds.Length)
            {
                throw new InvalidOperationException("Account recovery execution operation identifiers are invalid.");
            }
        }
    }
}
