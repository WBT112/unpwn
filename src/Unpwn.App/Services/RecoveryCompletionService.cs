using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Unpwn.Application.Credentials;
using Unpwn.Core;

namespace Unpwn.App.Services;

public enum RecoveryCompletionFailureCode
{
    None,
    Locked,
    NoSession,
    ReadFailed,
    StateChanged,
    RiskAcceptanceRequired,
    PersistenceFailure,
    InvalidInput,
}

public sealed record RecoveryCompletionReviewResult(
    bool Succeeded,
    RecoveryCompletionFailureCode FailureCode,
    RecoveryCompletionPreflight? Preflight = null,
    RecoveryCompletionReport? Report = null,
    RecoveryCompletionRecord? ExistingCompletion = null)
{
    public static RecoveryCompletionReviewResult Success(
        RecoveryCompletionPreflight preflight,
        RecoveryCompletionReport report,
        RecoveryCompletionRecord? existingCompletion = null) =>
        new(true, RecoveryCompletionFailureCode.None, preflight, report, existingCompletion);

    public static RecoveryCompletionReviewResult Failure(RecoveryCompletionFailureCode code) =>
        new(false, code);
}

public sealed record RecoveryCompletionOperationResult(
    bool Succeeded,
    RecoveryCompletionFailureCode FailureCode,
    RecoveryCompletionRecord? Completion = null)
{
    public static RecoveryCompletionOperationResult Success(RecoveryCompletionRecord completion) =>
        new(true, RecoveryCompletionFailureCode.None, completion);

    public static RecoveryCompletionOperationResult Failure(RecoveryCompletionFailureCode code) =>
        new(false, code);
}

public interface IRecoveryCompletionService
{
    Task<RecoveryCompletionReviewResult> ReviewAsync(CancellationToken cancellationToken);

    Task<RecoveryCompletionOperationResult> CompleteAsync(
        RecoveryCompletionPreflight reviewedPreflight,
        bool unresolvedRiskExplicitlyAccepted,
        bool archive,
        CancellationToken cancellationToken);
}

public sealed class UnavailableRecoveryCompletionService : IRecoveryCompletionService
{
    public Task<RecoveryCompletionReviewResult> ReviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(RecoveryCompletionReviewResult.Failure(RecoveryCompletionFailureCode.Locked));

    public Task<RecoveryCompletionOperationResult> CompleteAsync(
        RecoveryCompletionPreflight reviewedPreflight,
        bool unresolvedRiskExplicitlyAccepted,
        bool archive,
        CancellationToken cancellationToken) =>
        Task.FromResult(RecoveryCompletionOperationResult.Failure(RecoveryCompletionFailureCode.Locked));
}

public sealed class RecoveryCompletionService(
    IRecoverySessionService sessionService,
    IAccountInventoryService inventoryService,
    IGeneratedCredentialRepository credentialRepository,
    Func<DateTimeOffset>? clock = null) : IRecoveryCompletionService
{
    private readonly IRecoverySessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private readonly IAccountInventoryService _inventoryService =
        inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
    private readonly IGeneratedCredentialRepository _credentialRepository =
        credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<RecoveryCompletionReviewResult> ReviewAsync(
        CancellationToken cancellationToken)
    {
        if (!_credentialRepository.IsUnlocked)
        {
            return RecoveryCompletionReviewResult.Failure(RecoveryCompletionFailureCode.Locked);
        }

        try
        {
            await _sessionService.InitializeAsync(cancellationToken);
            var session = _sessionService.CurrentSession;
            if (session is null)
            {
                return RecoveryCompletionReviewResult.Failure(RecoveryCompletionFailureCode.NoSession);
            }

            await _inventoryService.InitializeAsync(cancellationToken);
            var credentials = await _credentialRepository.ListAsync(cancellationToken);
            return BuildReview(session, _inventoryService.CurrentInventory, credentials);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or JsonException or NotSupportedException)
        {
            return RecoveryCompletionReviewResult.Failure(RecoveryCompletionFailureCode.ReadFailed);
        }
    }

    public async Task<RecoveryCompletionOperationResult> CompleteAsync(
        RecoveryCompletionPreflight reviewedPreflight,
        bool unresolvedRiskExplicitlyAccepted,
        bool archive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewedPreflight);
        reviewedPreflight.Validate();
        var current = await ReviewAsync(cancellationToken);
        if (!current.Succeeded || current.Preflight is null || current.Report is null)
        {
            return RecoveryCompletionOperationResult.Failure(current.FailureCode);
        }

        if (current.ExistingCompletion is not null)
        {
            return RecoveryCompletionOperationResult.Success(current.ExistingCompletion);
        }

        if (!Equivalent(reviewedPreflight, current.Preflight))
        {
            return RecoveryCompletionOperationResult.Failure(RecoveryCompletionFailureCode.StateChanged);
        }

        if (current.Preflight.RequiresExplicitRiskAcceptance && !unresolvedRiskExplicitlyAccepted)
        {
            return RecoveryCompletionOperationResult.Failure(
                RecoveryCompletionFailureCode.RiskAcceptanceRequired);
        }

        var occurredAt = _clock();
        var outcome = archive
            ? RecoveryCompletionOutcome.Archived
            : current.Preflight.RequiresExplicitRiskAcceptance
                ? RecoveryCompletionOutcome.FollowUpRequired
                : RecoveryCompletionOutcome.Completed;
        var completion = new RecoveryCompletionRecord(
            outcome,
            occurredAt,
            unresolvedRiskExplicitlyAccepted,
            current.Report with { GeneratedAt = occurredAt });
        var persisted = await _sessionService.CompleteAsync(
            completion,
            current.Preflight.SessionRevision,
            cancellationToken);
        return persisted.Succeeded
            ? RecoveryCompletionOperationResult.Success(completion)
            : RecoveryCompletionOperationResult.Failure(MapFailure(persisted.FailureCode));
    }

    private RecoveryCompletionReviewResult BuildReview(
        RecoverySessionWorkspace session,
        AccountInventoryState? inventory,
        IReadOnlyList<GeneratedCredentialMetadata> credentials)
    {
        if (session.Completion is { } existing)
        {
            var persistedIssues = existing.Report.Issues;
            var persistedPreflight = new RecoveryCompletionPreflight(
                session.Id,
                session.Revision,
                inventory?.Revision ?? 0,
                existing.Report.GeneratedAt,
                persistedIssues,
                credentials.Sum(credential => checked((int)credential.Revision)));
            return RecoveryCompletionReviewResult.Success(
                persistedPreflight,
                existing.Report,
                existing);
        }

        var issues = new List<RecoveryCompletionIssue>();
        foreach (var account in session.Accounts.OrderBy(account => account.AccountId))
        {
            AddAccountIssue(
                issues,
                RecoveryCompletionIssueKind.CriticalAccountNotFullyReviewed,
                RecoveryCompletionIssueSeverity.Blocking,
                account,
                account.Criticality == AccountCriticality.Critical && !account.IsFullyReviewed ? 1 : 0);
            AddAccountIssue(
                issues,
                RecoveryCompletionIssueKind.RequiredActionIncomplete,
                RecoveryCompletionIssueSeverity.Blocking,
                account,
                Math.Max(0, account.RequiredActionsTotal - account.RequiredActionsCompleted));
            AddAccountIssue(issues, RecoveryCompletionIssueKind.RequiredActionBlocked,
                RecoveryCompletionIssueSeverity.Blocking, account, account.BlockedRequiredActions);
            AddAccountIssue(issues, RecoveryCompletionIssueKind.RequiredActionFailed,
                RecoveryCompletionIssueSeverity.UnresolvedRisk, account, account.FailedRequiredActions);
            AddAccountIssue(issues, RecoveryCompletionIssueKind.LostAccountAccess,
                RecoveryCompletionIssueSeverity.UnresolvedRisk, account, account.AccessLost ? 1 : 0);
            AddAccountIssue(issues, RecoveryCompletionIssueKind.UnresolvedRisk,
                RecoveryCompletionIssueSeverity.UnresolvedRisk, account, account.UnresolvedRisks);
        }

        foreach (var group in credentials.Where(credential => !credential.IsDeleted)
                     .GroupBy(credential => credential.AccountId)
                     .OrderBy(group => group.Key))
        {
            var providerId = inventory?.Accounts.SingleOrDefault(account => account.Id == group.Key)?.ProviderId;
            AddIssue(issues, RecoveryCompletionIssueKind.CredentialNotExported,
                RecoveryCompletionIssueSeverity.Blocking, group.Key, providerId, null,
                group.Count(credential => credential.ExportedAt is null));
            AddIssue(issues, RecoveryCompletionIssueKind.PasswordManagerImportUnconfirmed,
                RecoveryCompletionIssueSeverity.UnresolvedRisk, group.Key, providerId, null,
                group.Count(credential => credential.ExportedAt is not null &&
                    credential.PasswordManagerImportConfirmedAt is null));
            AddIssue(issues, RecoveryCompletionIssueKind.CredentialRetainedInVault,
                RecoveryCompletionIssueSeverity.Warning, group.Key, providerId, null, group.Count());
            AddIssue(issues, RecoveryCompletionIssueKind.PlaintextExportCleanupPending,
                RecoveryCompletionIssueSeverity.UnresolvedRisk, group.Key, providerId, null,
                group.Count(credential => credential.IsPlaintextExportCleanupPending));
        }

        var orderedIssues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.AccountId)
            .ThenBy(issue => issue.Kind)
            .ToArray();
        var reviewedAt = _clock();
        var activeCredentials = credentials.Where(credential => !credential.IsDeleted).ToArray();
        var dashboard = session.CreateDashboardSnapshot();
        var preflight = new RecoveryCompletionPreflight(
            session.Id,
            session.Revision,
            inventory?.Revision ?? 0,
            reviewedAt,
            orderedIssues,
            credentials.Sum(credential => checked((int)credential.Revision)));
        var report = new RecoveryCompletionReport(
            session.Id,
            reviewedAt,
            dashboard.AccountsFullyReviewed,
            dashboard.AccountsTotal,
            dashboard.CriticalAccountsReady,
            dashboard.CriticalAccountsTotal,
            session.Accounts.Sum(account => Math.Max(0,
                account.RequiredActionsTotal - account.RequiredActionsCompleted)),
            dashboard.BlockedRequiredActions,
            dashboard.FailedRequiredActions,
            dashboard.AccountsWithLostAccess,
            dashboard.UnresolvedRisks,
            activeCredentials.Count(credential => credential.ExportedAt is null),
            activeCredentials.Count(credential => credential.ExportedAt is not null &&
                credential.PasswordManagerImportConfirmedAt is null),
            activeCredentials.Length,
            credentials.Count(credential => credential.IsDeleted),
            activeCredentials.Count(credential => credential.IsPlaintextExportCleanupPending),
            orderedIssues)
        {
            RequiredActionsCompleted = session.Accounts.Sum(account => account.RequiredActionsCompleted),
            RequiredActionsOpen = session.Accounts.Sum(account => account.RequiredActionsOpen),
            RequiredActionsInProgress = session.Accounts.Sum(account => account.RequiredActionsInProgress),
            RequiredActionsAwaitingUser = session.Accounts.Sum(account => account.RequiredActionsAwaitingUser),
            RequiredActionsNotApplicable = session.Accounts.Sum(account => account.RequiredActionsNotApplicable),
            AcceptedRiskActions = session.Accounts.Sum(account => account.AcceptedRiskActions),
        };
        preflight.Validate();
        report.Validate();
        return RecoveryCompletionReviewResult.Success(preflight, report);
    }

    private static bool Equivalent(
        RecoveryCompletionPreflight reviewed,
        RecoveryCompletionPreflight current) =>
        reviewed.SessionId == current.SessionId &&
        reviewed.SessionRevision == current.SessionRevision &&
        reviewed.InventoryRevision == current.InventoryRevision &&
        reviewed.CredentialMetadataRevisionSum == current.CredentialMetadataRevisionSum &&
        reviewed.Issues.SequenceEqual(current.Issues);

    private static void AddAccountIssue(
        ICollection<RecoveryCompletionIssue> issues,
        RecoveryCompletionIssueKind kind,
        RecoveryCompletionIssueSeverity severity,
        RecoveryAccountDashboardEntry account,
        int count) =>
        AddIssue(issues, kind, severity, account.AccountId, account.ProviderId,
            account.RecommendedActionId, count);

    private static void AddIssue(
        ICollection<RecoveryCompletionIssue> issues,
        RecoveryCompletionIssueKind kind,
        RecoveryCompletionIssueSeverity severity,
        Guid? accountId,
        string? providerId,
        string? actionId,
        int count)
    {
        if (count > 0)
        {
            issues.Add(new RecoveryCompletionIssue(
                kind,
                severity,
                accountId,
                providerId,
                actionId,
                count));
        }
    }

    private static RecoveryCompletionFailureCode MapFailure(
        RecoverySessionOperationFailureCode failureCode) => failureCode switch
        {
            RecoverySessionOperationFailureCode.Locked => RecoveryCompletionFailureCode.Locked,
            RecoverySessionOperationFailureCode.Conflict => RecoveryCompletionFailureCode.StateChanged,
            RecoverySessionOperationFailureCode.IoFailure => RecoveryCompletionFailureCode.PersistenceFailure,
            _ => RecoveryCompletionFailureCode.InvalidInput,
        };
}

public enum RecoveryCompletionReportWriteFailureCode
{
    None,
    InvalidPath,
    AlreadyExists,
    IoFailure,
}

public sealed record RecoveryCompletionReportWriteResult(
    bool Succeeded,
    RecoveryCompletionReportWriteFailureCode FailureCode)
{
    public static RecoveryCompletionReportWriteResult Success { get; } =
        new(true, RecoveryCompletionReportWriteFailureCode.None);
}

public interface IRecoveryCompletionReportWriter
{
    Task<RecoveryCompletionReportWriteResult> WriteAsync(
        RecoveryCompletionReport report,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed class JsonRecoveryCompletionReportWriter : IRecoveryCompletionReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The report export boundary returns stable error codes and never exposes path or exception details.")]
    public async Task<RecoveryCompletionReportWriteResult> WriteAsync(
        RecoveryCompletionReport report,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.Validate();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return new(false, RecoveryCompletionReportWriteFailureCode.InvalidPath);
        }

        string? temporaryPath = null;
        string? fullPath = null;
        try
        {
            fullPath = Path.GetFullPath(destinationPath);
            if (File.Exists(fullPath))
            {
                return new(false, RecoveryCompletionReportWriteFailureCode.AlreadyExists);
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new(false, RecoveryCompletionReportWriteFailureCode.InvalidPath);
            }

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, report, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: false);
            temporaryPath = null;
            return RecoveryCompletionReportWriteResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException) when (fullPath is not null && File.Exists(fullPath))
        {
            return new(false, RecoveryCompletionReportWriteFailureCode.AlreadyExists);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, RecoveryCompletionReportWriteFailureCode.InvalidPath);
        }
        catch (Exception)
        {
            return new(false, RecoveryCompletionReportWriteFailureCode.IoFailure);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
