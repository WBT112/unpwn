using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Unpwn.Core;
using Unpwn.Import.Csv;
using Unpwn.Vault.Cryptography;
using Unpwn.Vault.Storage;

namespace Unpwn.App.Services;

public sealed class AccountInventoryService : IAccountInventoryService, IDisposable
{
    private const string InventoryRecordId = "b3ed4b71-55ad-4e47-af52-ed0c04454de2";
    private static readonly VaultRecordDescriptor InventoryDescriptor = new(
        "account-state",
        InventoryRecordId,
        1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
    private static readonly AccountInventoryRole[] IndividualRoles =
    [
        AccountInventoryRole.EmailMailbox,
        AccountInventoryRole.PasswordManager,
        AccountInventoryRole.IdentityProvider,
        AccountInventoryRole.RecoveryEmail,
        AccountInventoryRole.TelephoneRecovery,
        AccountInventoryRole.OrganizationManagedSignIn,
    ];

    private readonly IEncryptedVaultRecordStore _recordStore;
    private readonly IRecoverySessionService _recoverySession;
    private readonly IRecoverySessionProjectionCoordinator? _projectionCoordinator;
    private readonly Func<DateTimeOffset> _clock;
    private readonly WorkspaceMutationCoordinator _mutationCoordinator;
    private readonly bool _ownsMutationCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AccountInventoryService(
        IEncryptedVaultRecordStore recordStore,
        IRecoverySessionService recoverySession,
        Func<DateTimeOffset>? clock = null,
        WorkspaceMutationCoordinator? mutationCoordinator = null)
    {
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _recoverySession = recoverySession ?? throw new ArgumentNullException(nameof(recoverySession));
        _projectionCoordinator = recoverySession as IRecoverySessionProjectionCoordinator;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _mutationCoordinator = mutationCoordinator ?? new WorkspaceMutationCoordinator();
        _ownsMutationCoordinator = mutationCoordinator is null;
    }

    public event EventHandler? InventoryChanged;

    public AccountInventoryLoadState LoadState { get; private set; } = AccountInventoryLoadState.Locked;

    public AccountInventoryState? CurrentInventory { get; private set; }

    public AccountInventoryPlan? CurrentPlan => CurrentInventory?.CreatePlan(
        _recoverySession.CurrentSession?.Incident.Indicators ?? IncidentIndicator.None);

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _mutationCoordinator.ExecuteAsync(
            async token =>
            {
                await InitializeCoreAsync(token);
                return true;
            },
            cancellationToken);

    public Task<AccountInventoryOperationResult> UpsertAsync(
        AccountInventoryUpsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(
            inventory =>
            {
                var accounts = inventory.Accounts.ToList();
                var accountId = request.AccountId ?? Guid.NewGuid();
                var existingIndex = accounts.FindIndex(account => account.Id == accountId);
                var existing = existingIndex >= 0 ? accounts[existingIndex] : null;
                var updated = new AccountInventoryEntry(
                    accountId,
                    request.ProviderId,
                    request.AccountName,
                    request.LoginIdentifier,
                    request.AccountUrl,
                    request.Priority,
                    existing?.Roles ?? [],
                    existing?.Dependencies ?? [],
                    _clock()).NormalizeAndInfer(_clock());
                if (existingIndex >= 0)
                {
                    accounts[existingIndex] = updated;
                }
                else
                {
                    accounts.Add(updated);
                }

                return inventory.ReplaceAccounts(accounts, _clock());
            },
            cancellationToken);
    }

    public Task<AccountInventoryOperationResult> DecideRoleAsync(
        Guid accountId,
        AccountInventoryRole role,
        AccountRoleDecision decision,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty || !IndividualRoles.Contains(role))
        {
            return Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.InvalidInput));
        }

        return MutateAsync(
            inventory =>
            {
                var accounts = inventory.Accounts.ToList();
                var index = accounts.FindIndex(account => account.Id == accountId);
                if (index < 0)
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.NotFound);
                }

                var account = accounts[index];
                var roles = account.Roles
                    .Where(candidate => candidate.Role != role)
                    .Append(new AccountRoleState(role, decision))
                    .OrderBy(candidate => candidate.Role)
                    .ToArray();
                accounts[index] = (account with { Roles = roles }).NormalizeAndInfer(_clock());
                return inventory.ReplaceAccounts(accounts, _clock());
            },
            cancellationToken);
    }

    public Task<AccountInventoryOperationResult> AddDependencyAsync(
        AccountDependencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AccountId == Guid.Empty || request.DependsOnAccountId == Guid.Empty ||
            request.AccountId == request.DependsOnAccountId)
        {
            return Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.InvalidInput));
        }

        return MutateAsync(
            inventory =>
            {
                var accounts = inventory.Accounts.ToList();
                var index = accounts.FindIndex(account => account.Id == request.AccountId);
                if (index < 0 || accounts.All(account => account.Id != request.DependsOnAccountId))
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.NotFound);
                }

                var account = accounts[index];
                if (account.Dependencies.Any(dependency =>
                        dependency.DependsOnAccountId == request.DependsOnAccountId &&
                        dependency.Kind == request.Kind))
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.Conflict);
                }

                var dependency = new AccountInventoryDependency(
                    request.DependsOnAccountId,
                    request.Kind,
                    IsOverride: false,
                    OverrideReason: null);
                accounts[index] = account with
                {
                    Dependencies = [.. account.Dependencies, dependency],
                };
                var proposed = inventory.ReplaceAccounts(accounts, _clock());
                var createsCycle = proposed.CreatePlan(
                        _recoverySession.CurrentSession?.Incident.Indicators ?? IncidentIndicator.None)
                    .Issues.Any(issue =>
                        issue.Kind == AccountInventoryIssueKind.DependencyCycle &&
                        issue.AccountId == request.AccountId);
                if (!createsCycle)
                {
                    return proposed;
                }

                if (string.IsNullOrWhiteSpace(request.OverrideReason))
                {
                    throw new AccountInventoryMutationException(
                        AccountInventoryFailureCode.RequiresOverrideReason);
                }

                accounts[index] = account with
                {
                    Dependencies =
                    [
                        .. account.Dependencies,
                        dependency with
                        {
                            IsOverride = true,
                            OverrideReason = request.OverrideReason.Trim(),
                        },
                    ],
                };
                return inventory.ReplaceAccounts(accounts, _clock());
            },
            cancellationToken);
    }

    public Task<AccountInventoryOperationResult> RemoveDependencyAsync(
        Guid accountId,
        Guid dependsOnAccountId,
        AccountDependencyKind kind,
        CancellationToken cancellationToken) =>
        MutateAsync(
            inventory =>
            {
                var accounts = inventory.Accounts.ToList();
                var index = accounts.FindIndex(account => account.Id == accountId);
                if (index < 0)
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.NotFound);
                }

                var account = accounts[index];
                var dependencies = account.Dependencies.Where(dependency =>
                        dependency.DependsOnAccountId != dependsOnAccountId || dependency.Kind != kind)
                    .ToArray();
                if (dependencies.Length == account.Dependencies.Length)
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.NotFound);
                }

                accounts[index] = account with { Dependencies = dependencies };
                return inventory.ReplaceAccounts(accounts, _clock());
            },
            cancellationToken);

    public Task<AccountInventoryOperationResult> RemoveAccountAsync(
        Guid accountId,
        bool dependencyImpactAcknowledged,
        CancellationToken cancellationToken) =>
        MutateAsync(
            inventory =>
            {
                if (inventory.Accounts.All(account => account.Id != accountId))
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.NotFound);
                }

                var hasDependents = inventory.Accounts.Any(account => account.Dependencies.Any(dependency =>
                    dependency.DependsOnAccountId == accountId));
                if (hasDependents && !dependencyImpactAcknowledged)
                {
                    throw new AccountInventoryMutationException(
                        AccountInventoryFailureCode.RequiresConfirmation);
                }

                return inventory.ReplaceAccounts(
                    inventory.Accounts.Where(account => account.Id != accountId),
                    _clock());
            },
            cancellationToken);

    public Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.InvalidInput));
        }

        var hasDuplicates = candidates.Any(candidate => candidate.DuplicateKind != CsvDuplicateKind.None);
        if (hasDuplicates && duplicateResolution is null)
        {
            return Task.FromResult(AccountInventoryOperationResult.Failure(
                AccountInventoryFailureCode.RequiresConfirmation));
        }

        return MutateAsync(
            inventory =>
            {
                var accounts = inventory.Accounts.ToList();
                var importable = duplicateResolution == ImportDuplicateResolution.SkipDuplicates
                    ? candidates.Where(candidate => candidate.DuplicateKind == CsvDuplicateKind.None)
                    : candidates;
                var imported = 0;
                foreach (var candidate in importable)
                {
                    var providerId = ResolveProviderId(candidate);
                    var entry = new AccountInventoryEntry(
                        Guid.NewGuid(),
                        providerId,
                        candidate.AccountName,
                        candidate.LoginIdentifier,
                        candidate.AccountUrl,
                        AccountInventoryPriority.Normal,
                        [],
                        [],
                        _clock()).NormalizeAndInfer(_clock());
                    accounts.Add(entry);
                    imported++;
                }

                if (imported == 0)
                {
                    throw new AccountInventoryMutationException(AccountInventoryFailureCode.Conflict);
                }

                return inventory.ReplaceAccounts(accounts, _clock());
            },
            cancellationToken,
            candidates.Count);
    }

    public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() =>
        CurrentInventory?.Accounts.Select(account => new ExistingAccountReference(
                account.Id.ToString("D"),
                account.ProviderId,
                account.AccountName,
                account.LoginIdentifier,
                account.AccountUrl))
            .ToArray() ?? [];

    public void ClearForLock()
    {
        ThrowIfDisposed();
        SetState(AccountInventoryLoadState.Locked, null);
    }

    public void MarkLoadFailed()
    {
        ThrowIfDisposed();
        SetState(AccountInventoryLoadState.LoadFailed, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CurrentInventory = null;
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
                SetState(AccountInventoryLoadState.Locked, null);
                return;
            }

            var session = _recoverySession.CurrentSession;
            if (session is null)
            {
                SetState(AccountInventoryLoadState.Empty, null);
                return;
            }

            SetState(AccountInventoryLoadState.Loading, null);
            byte[]? plaintext = null;
            try
            {
                plaintext = await _recordStore.ReadEncryptedRecordAsync(
                    InventoryDescriptor,
                    cancellationToken);
                var inventory = plaintext is null
                    ? AccountInventoryState.Empty(session.Id, _clock())
                    : JsonSerializer.Deserialize<AccountInventoryState>(plaintext, SerializerOptions)
                        ?? throw new JsonException("The account inventory record is empty.");
                inventory.Validate();
                if (inventory.SessionId != session.Id)
                {
                    throw new InvalidOperationException("The account inventory belongs to another recovery session.");
                }

                var projectionResult = await ReconcileDashboardAsync(inventory, cancellationToken);
                if (!projectionResult.Succeeded)
                {
                    SetState(AccountInventoryLoadState.LoadFailed, null);
                    return;
                }

                SetState(
                    plaintext is null ? AccountInventoryLoadState.Empty : AccountInventoryLoadState.Loaded,
                    inventory);
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                SetState(AccountInventoryLoadState.Corrupted, null);
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

    private Task<AccountInventoryOperationResult> MutateAsync(
        Func<AccountInventoryState, AccountInventoryState> mutation,
        CancellationToken cancellationToken,
        int affectedAccounts = 1)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mutation);
        return _mutationCoordinator.ExecuteAsync(
            token => MutateCoreAsync(mutation, affectedAccounts, token),
            cancellationToken);
    }

    private async Task<AccountInventoryOperationResult> MutateCoreAsync(
        Func<AccountInventoryState, AccountInventoryState> mutation,
        int affectedAccounts,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_recordStore.IsVaultUnlocked)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Locked);
            }

            if (LoadState == AccountInventoryLoadState.Corrupted)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Corrupted);
            }

            if (LoadState == AccountInventoryLoadState.LoadFailed)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.IoFailure);
            }

            var inventory = CurrentInventory;
            var session = _recoverySession.CurrentSession;
            if (session is null)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Conflict);
            }

            inventory ??= AccountInventoryState.Empty(session.Id, _clock());
            AccountInventoryState updated;
            try
            {
                updated = mutation(inventory);
                updated.Validate();
            }
            catch (AccountInventoryMutationException exception)
            {
                return AccountInventoryOperationResult.Failure(exception.Code);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput);
            }

            var persisted = await PersistInventoryAndProjectionAsync(updated, cancellationToken);
            if (!persisted.Succeeded)
            {
                return persisted;
            }

            SetState(AccountInventoryLoadState.Loaded, updated);
            return AccountInventoryOperationResult.Success(affectedAccounts);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountInventoryOperationResult> PersistInventoryAndProjectionAsync(
        AccountInventoryState inventory,
        CancellationToken cancellationToken)
    {
        if (_projectionCoordinator is null)
        {
            var inventoryResult = await PersistInventoryOnlyAsync(inventory, cancellationToken);
            if (!inventoryResult.Succeeded)
            {
                return inventoryResult;
            }

            return await SyncDashboardLegacyAsync(inventory, cancellationToken);
        }

        var summaries = BuildDashboardSummaries(inventory);
        PreparedRecoverySessionUpdate sessionUpdate;
        try
        {
            sessionUpdate = await _projectionCoordinator.PrepareAccountSummaryUpdateAsync(
                summaries,
                cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.Conflict);
        }

        using (sessionUpdate)
        {
            var inventoryPlaintext = JsonSerializer.SerializeToUtf8Bytes(inventory, SerializerOptions);
            try
            {
                await _recordStore.WriteEncryptedRecordsAtomicallyAsync(
                    [
                        new VaultRecordWrite(InventoryDescriptor, inventoryPlaintext),
                        sessionUpdate.ToWrite(),
                    ],
                    cancellationToken);
                _projectionCoordinator.CommitPreparedUpdate(sessionUpdate);
                return AccountInventoryOperationResult.Success();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.IoFailure);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inventoryPlaintext);
            }
        }
    }

    private async Task<AccountInventoryOperationResult> ReconcileDashboardAsync(
        AccountInventoryState inventory,
        CancellationToken cancellationToken)
    {
        if (_projectionCoordinator is null)
        {
            return await SyncDashboardLegacyAsync(inventory, cancellationToken);
        }

        using var update = await _projectionCoordinator.PrepareAccountSummaryUpdateAsync(
            BuildDashboardSummaries(inventory),
            cancellationToken);
        try
        {
            await _recordStore.WriteEncryptedRecordsAtomicallyAsync([update.ToWrite()], cancellationToken);
            _projectionCoordinator.CommitPreparedUpdate(update);
            return AccountInventoryOperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.IoFailure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This encrypted persistence boundary must not expose source exception details.")]
    private async Task<AccountInventoryOperationResult> PersistInventoryOnlyAsync(
        AccountInventoryState inventory,
        CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(inventory, SerializerOptions);
        try
        {
            await _recordStore.WriteEncryptedRecordAsync(
                InventoryDescriptor,
                plaintext,
                cancellationToken);
            return AccountInventoryOperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.IoFailure);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<AccountInventoryOperationResult> SyncDashboardLegacyAsync(
        AccountInventoryState inventory,
        CancellationToken cancellationToken)
    {
        var result = await _recoverySession.ReplaceAccountSummariesAsync(
            BuildDashboardSummaries(inventory),
            cancellationToken);
        return result.Succeeded
            ? AccountInventoryOperationResult.Success()
            : AccountInventoryOperationResult.Failure(result.FailureCode switch
            {
                RecoverySessionOperationFailureCode.Locked => AccountInventoryFailureCode.Locked,
                RecoverySessionOperationFailureCode.Corrupted => AccountInventoryFailureCode.Corrupted,
                RecoverySessionOperationFailureCode.InvalidInput => AccountInventoryFailureCode.InvalidInput,
                RecoverySessionOperationFailureCode.Conflict => AccountInventoryFailureCode.Conflict,
                _ => AccountInventoryFailureCode.IoFailure,
            });
    }

    private RecoveryAccountDashboardEntry[] BuildDashboardSummaries(AccountInventoryState inventory)
    {
        var plan = inventory.CreatePlan(
            _recoverySession.CurrentSession?.Incident.Indicators ?? IncidentIndicator.None);
        var planByAccount = plan.Items.ToDictionary(item => item.AccountId);
        var existingByAccount = _recoverySession.CurrentSession?.Accounts
            .ToDictionary(account => account.AccountId) ?? [];
        return inventory.Accounts.Select(account =>
        {
            var planItem = planByAccount[account.Id];
            var isBlocked = planItem.Status is
                AccountInventoryPlanStatus.BlockedCycle or
                AccountInventoryPlanStatus.BlockedMissingDependency;
            existingByAccount.TryGetValue(account.Id, out var existing);
            var hasExecution = existing?.RequiredActionsTotal > 0;
            var executionBlocked = hasExecution
                ? Math.Max(0, existing!.BlockedRequiredActions - existing.InventoryBlockedIssues)
                : 0;
            var executionUnresolved = hasExecution
                ? Math.Max(0, existing!.UnresolvedRisks - existing.InventoryUnresolvedRisks)
                : 0;
            var waitingFor = planItem.WaitingForAccountIds
                .Where(dependencyId =>
                    !existingByAccount.TryGetValue(dependencyId, out var dependency) ||
                    !dependency.IsFullyReviewed)
                .ToArray();
            return new RecoveryAccountDashboardEntry(
                account.Id,
                account.ProviderId,
                account.DashboardCriticality,
                existing?.RecoveryStatus ?? AccountRecoveryStatus.Open,
                existing?.RequiredActionsCompleted ?? 0,
                existing?.RequiredActionsTotal ?? 0,
                existing?.CompletedRequiredWeight ?? 0,
                existing?.TotalRequiredWeight ?? 0,
                BlockedRequiredActions: executionBlocked + (isBlocked ? 1 : 0),
                existing?.FailedRequiredActions ?? 0,
                UnresolvedRisks: executionUnresolved + (planItem.HasDependencyOverride ? 1 : 0),
                existing?.AccessLost ?? false,
                existing?.CredentialsAwaitingExport ?? 0,
                existing?.CredentialsAwaitingDeletion ?? 0,
                existing?.RecommendedActionId,
                planItem.DependencyDepth,
                waitingFor)
            {
                InventoryBlockedIssues = isBlocked ? 1 : 0,
                InventoryUnresolvedRisks = planItem.HasDependencyOverride ? 1 : 0,
            };
        }).ToArray();
    }

    private static string ResolveProviderId(ImportAccountCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ServiceName))
        {
            return candidate.ServiceName.Trim();
        }

        if (Uri.TryCreate(candidate.AccountUrl, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "manual-import";
    }

    private void SetState(AccountInventoryLoadState loadState, AccountInventoryState? inventory)
    {
        LoadState = loadState;
        CurrentInventory = inventory;
        InventoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class AccountInventoryMutationException(AccountInventoryFailureCode code) : Exception
    {
        public AccountInventoryFailureCode Code { get; } = code;
    }
}
