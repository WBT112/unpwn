using Unpwn.Application.Credentials;
using Unpwn.Core;

namespace Unpwn.App.Services;

public enum RecoveryBrowserCredentialInsertionOutcomeCode
{
    AuthorizationDenied,
    InspectionStopped,
    CredentialUnavailable,
    InsertionStopped,
    InsertedAndRecordedUsed,
    InsertedStateSaveFailed,
}

public sealed record RecoveryBrowserCredentialInsertionOutcome(
    RecoveryBrowserCredentialInsertionOutcomeCode Code,
    RecoveryBrowserCredentialAssistanceResult? BrowserResult = null,
    GeneratedCredentialMetadata? Metadata = null)
{
    public bool Succeeded => Code == RecoveryBrowserCredentialInsertionOutcomeCode.InsertedAndRecordedUsed;
}

public sealed class RecoveryBrowserCredentialInsertionCoordinator(
    IGeneratedCredentialRepository credentials)
{
    private readonly IGeneratedCredentialRepository _credentials =
        credentials ?? throw new ArgumentNullException(nameof(credentials));

    public async Task<RecoveryBrowserCredentialInsertionOutcome> ExecuteAsync(
        GeneratedCredentialReference reference,
        RecoveryBrowserCredentialInsertionContract contract,
        Func<CancellationToken, Task<bool>> authorizeAsync,
        Func<RecoveryBrowserCredentialInsertionContract, CancellationToken,
            Task<RecoveryBrowserCredentialAssistanceResult>> inspectAsync,
        Func<RecoveryBrowserCredentialInsertionContract, ReadOnlyMemory<byte>, CancellationToken,
            Task<RecoveryBrowserCredentialAssistanceResult>> insertAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(authorizeAsync);
        ArgumentNullException.ThrowIfNull(inspectAsync);
        ArgumentNullException.ThrowIfNull(insertAsync);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await authorizeAsync(cancellationToken))
        {
            return new RecoveryBrowserCredentialInsertionOutcome(
                RecoveryBrowserCredentialInsertionOutcomeCode.AuthorizationDenied);
        }

        // Inspect without opening a secret lease. Provider handoffs or a changed page therefore stop
        // before the generated credential is materialized outside the vault.
        var inspection = await inspectAsync(contract, cancellationToken);
        if (inspection.State != RecoveryBrowserCredentialAssistanceState.ReadyForAuthorization)
        {
            return new RecoveryBrowserCredentialInsertionOutcome(
                RecoveryBrowserCredentialInsertionOutcomeCode.InspectionStopped,
                inspection);
        }

        using var lease = await _credentials.ReadSecretAsync(reference, cancellationToken);
        if (lease is null)
        {
            return new RecoveryBrowserCredentialInsertionOutcome(
                RecoveryBrowserCredentialInsertionOutcomeCode.CredentialUnavailable);
        }

        // The host re-validates the exact origin and page contract again immediately before writing.
        // The lease remains scoped to this single insertion operation.
        var insertion = await insertAsync(contract, lease.SecretUtf8, cancellationToken);
        if (!insertion.Succeeded)
        {
            return new RecoveryBrowserCredentialInsertionOutcome(
                RecoveryBrowserCredentialInsertionOutcomeCode.InsertionStopped,
                insertion);
        }

        var markedUsed = await _credentials.MarkUsedAsync(
            reference,
            Guid.NewGuid(),
            cancellationToken);
        if (!markedUsed.Succeeded || markedUsed.Metadata is null)
        {
            return new RecoveryBrowserCredentialInsertionOutcome(
                RecoveryBrowserCredentialInsertionOutcomeCode.InsertedStateSaveFailed,
                insertion);
        }

        return new RecoveryBrowserCredentialInsertionOutcome(
            RecoveryBrowserCredentialInsertionOutcomeCode.InsertedAndRecordedUsed,
            insertion,
            markedUsed.Metadata);
    }
}
