namespace Unpwn.Core;

public enum RecoveryCompletionIssueKind
{
    CriticalAccountNotFullyReviewed,
    RequiredActionIncomplete,
    RequiredActionBlocked,
    RequiredActionFailed,
    LostAccountAccess,
    UnresolvedRisk,
    CredentialNotExported,
    PasswordManagerImportUnconfirmed,
    CredentialRetainedInVault,
    PlaintextExportCleanupPending,
}

public enum RecoveryCompletionIssueSeverity
{
    Warning,
    UnresolvedRisk,
    Blocking,
}

public enum RecoveryCompletionOutcome
{
    Completed,
    Archived,
    FollowUpRequired,
}

public sealed record RecoveryCompletionIssue(
    RecoveryCompletionIssueKind Kind,
    RecoveryCompletionIssueSeverity Severity,
    Guid? AccountId,
    string? ProviderId,
    string? ActionId,
    int Count)
{
    public void Validate()
    {
        if (Count <= 0)
        {
            throw new InvalidOperationException("A completion issue must represent at least one item.");
        }

        if (AccountId == Guid.Empty)
        {
            throw new InvalidOperationException("A completion issue cannot reference an empty account identifier.");
        }
    }
}

public sealed record RecoveryCompletionPreflight(
    Guid SessionId,
    long SessionRevision,
    long InventoryRevision,
    DateTimeOffset ReviewedAt,
    RecoveryCompletionIssue[] Issues,
    int CredentialMetadataRevisionSum)
{
    public bool IsClean => Issues.Length == 0;

    public bool RequiresExplicitRiskAcceptance => Issues.Any(issue =>
        issue.Severity is RecoveryCompletionIssueSeverity.Blocking or
            RecoveryCompletionIssueSeverity.UnresolvedRisk);

    public bool HasWarnings => Issues.Any(issue =>
        issue.Severity == RecoveryCompletionIssueSeverity.Warning);

    public int BlockingIssueCount => Issues
        .Where(issue => issue.Severity == RecoveryCompletionIssueSeverity.Blocking)
        .Sum(issue => issue.Count);

    public int UnresolvedRiskCount => Issues
        .Where(issue => issue.Severity == RecoveryCompletionIssueSeverity.UnresolvedRisk)
        .Sum(issue => issue.Count);

    public void Validate()
    {
        if (SessionId == Guid.Empty || SessionRevision < 0 || InventoryRevision < 0 ||
            CredentialMetadataRevisionSum < 0)
        {
            throw new InvalidOperationException("The completion preflight identity or revision is invalid.");
        }

        ArgumentNullException.ThrowIfNull(Issues);
        foreach (var issue in Issues)
        {
            issue.Validate();
        }
    }
}

/// <summary>
/// A deliberately secret-free report. It contains only opaque identifiers, provider identifiers,
/// canonical state codes and aggregate counters. Account labels, login identifiers, URLs, notes,
/// credential identifiers and credential secrets are intentionally absent.
/// </summary>
public sealed record RecoveryCompletionReport(
    Guid SessionId,
    DateTimeOffset GeneratedAt,
    int AccountsReviewed,
    int AccountsTotal,
    int CriticalAccountsReady,
    int CriticalAccountsTotal,
    int RequiredActionsIncomplete,
    int BlockedActions,
    int FailedActions,
    int LostAccessAccounts,
    int UnresolvedRisks,
    int CredentialsNotExported,
    int PasswordManagerImportsUnconfirmed,
    int RetainedCredentials,
    int DeletedCredentials,
    int PlaintextCleanupPending,
    RecoveryCompletionIssue[] Issues)
{
    public int RequiredActionsCompleted { get; init; }

    public int RequiredActionsOpen { get; init; }

    public int RequiredActionsInProgress { get; init; }

    public int RequiredActionsAwaitingUser { get; init; }

    public int RequiredActionsNotApplicable { get; init; }

    public int AcceptedRiskActions { get; init; }

    public void Validate()
    {
        if (SessionId == Guid.Empty)
        {
            throw new InvalidOperationException("A completion report requires a recovery session.");
        }

        ArgumentNullException.ThrowIfNull(Issues);
        foreach (var issue in Issues)
        {
            issue.Validate();
        }

        int[] counters =
        [
            AccountsReviewed,
            AccountsTotal,
            CriticalAccountsReady,
            CriticalAccountsTotal,
            RequiredActionsIncomplete,
            BlockedActions,
            FailedActions,
            LostAccessAccounts,
            UnresolvedRisks,
            CredentialsNotExported,
            PasswordManagerImportsUnconfirmed,
            RetainedCredentials,
            DeletedCredentials,
            PlaintextCleanupPending,
            RequiredActionsCompleted,
            RequiredActionsOpen,
            RequiredActionsInProgress,
            RequiredActionsAwaitingUser,
            RequiredActionsNotApplicable,
            AcceptedRiskActions,
        ];
        if (counters.Any(counter => counter < 0) || AccountsReviewed > AccountsTotal ||
            CriticalAccountsReady > CriticalAccountsTotal)
        {
            throw new InvalidOperationException("A completion report contains invalid counters.");
        }
    }
}

public sealed record RecoveryCompletionRecord(
    RecoveryCompletionOutcome Outcome,
    DateTimeOffset CompletedAt,
    bool UnresolvedRiskExplicitlyAccepted,
    RecoveryCompletionReport Report)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Report);
        Report.Validate();
        if (Report.GeneratedAt > CompletedAt)
        {
            throw new InvalidOperationException("A completion report cannot be generated after completion.");
        }

        if (Outcome == RecoveryCompletionOutcome.FollowUpRequired &&
            !UnresolvedRiskExplicitlyAccepted)
        {
            throw new InvalidOperationException("Follow-up completion requires explicit unresolved-risk acceptance.");
        }
    }
}
