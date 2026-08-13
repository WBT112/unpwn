using Unpwn.Core;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class RecoveryDomainTests
{
    [Fact]
    public void RequiredActionCannotBeCompletedWithoutStarting()
    {
        var action = RecoveryActionInstance.Create(RequiredAction("password"));

        var exception = Assert.Throws<InvalidOperationException>(action.Complete);

        Assert.Contains("Open to Completed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotApplicableRequiresReasonAndExplicitDisposition()
    {
        var action = RecoveryActionInstance.Create(RequiredAction("mfa"));

        Assert.Throws<ArgumentException>(() => action.MarkNotApplicable("", NotApplicableDisposition.TrulyNotApplicable));
    }

    [Fact]
    public void RequiredActionWithoutCompletionCriteriaIsRejected()
    {
        var definition = RequiredAction("sessions") with { CompletionCriteria = [] };

        Assert.Throws<ArgumentException>(() => RecoveryActionInstance.Create(definition));
    }

    [Fact]
    public void AcceptedUnresolvedRiskPreventsFullySecuredAccount()
    {
        var password = RecoveryActionInstance.Create(RequiredAction("password"));
        var sessions = RecoveryActionInstance.Create(RequiredAction("sessions"));
        password.Start();
        password.Complete();
        sessions.Start();
        sessions.AcceptUnresolvedRisk("Provider does not expose session revocation.");
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Critical, [password, sessions]);

        Assert.Equal(AccountRecoveryStatus.NotFullySecured, account.Status);
    }

    [Fact]
    public void TrulyNotApplicableActionDoesNotCreateFalseIncompleteWork()
    {
        var password = RecoveryActionInstance.Create(RequiredAction("password"));
        var tokens = RecoveryActionInstance.Create(RequiredAction("tokens"));
        password.Start();
        password.Complete();
        tokens.MarkNotApplicable(
            "The account type does not support API tokens.",
            NotApplicableDisposition.TrulyNotApplicable);
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Important, [password, tokens]);

        Assert.Equal(AccountRecoveryStatus.FullyReviewed, account.Status);
    }

    [Fact]
    public void UnavailableRequiredControlMarkedNotApplicableRemainsAnUnresolvedRisk()
    {
        var action = RecoveryActionInstance.Create(RequiredAction("sessions"));
        action.MarkNotApplicable(
            "The provider does not expose session revocation.",
            NotApplicableDisposition.UnresolvedRisk);
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Critical, [action]);
        var session = new RecoverySession(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        session.AddAccount(account);

        var progress = session.CalculateProgress();

        Assert.Equal(AccountRecoveryStatus.NotFullySecured, account.Status);
        Assert.Equal(0, progress.WeightedRequiredActionsCompleted);
        Assert.Equal(1, progress.UnresolvedRisks);
        Assert.Equal(0, progress.FailedRequiredActions);
    }

    [Fact]
    public void SessionProgressUsesDocumentedFiveThreeOneWeights()
    {
        var complete = RecoveryActionInstance.Create(RequiredAction("complete", RecoveryActionImportance.Critical));
        complete.Start();
        complete.Complete();
        var blocked = RecoveryActionInstance.Create(RequiredAction("blocked", RecoveryActionImportance.Important));
        blocked.Block("Primary email must be secured first.");
        var risk = RecoveryActionInstance.Create(RequiredAction("risk", RecoveryActionImportance.Routine));
        risk.Start();
        risk.AcceptUnresolvedRisk("User chose to document the unsupported action.");
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Critical, [complete, blocked, risk]);
        var session = new RecoverySession(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        session.AddAccount(account);

        var progress = session.CalculateProgress();

        Assert.Equal(0, progress.CriticalAccountsSecured);
        Assert.Equal(1, progress.CriticalAccountsTotal);
        Assert.Equal(0, progress.AccountsFullyReviewed);
        Assert.Equal(1, progress.AccountsTotal);
        Assert.Equal(5 / 9.0, progress.WeightedRequiredActionsCompleted);
        Assert.Equal(1, progress.BlockedRequiredActions);
        Assert.Equal(1, progress.FailedRequiredActions);
        Assert.Equal(1, progress.UnresolvedRisks);
    }

    [Fact]
    public void CriticalReadinessReportsOnlyCriticalAccountsWithBlockingSignals()
    {
        var readyAction = RecoveryActionInstance.Create(RequiredAction("ready-password", RecoveryActionImportance.Critical));
        readyAction.Start();
        readyAction.Complete();
        var ready = new Account(Guid.NewGuid(), "ready-provider", AccountCriticality.Critical, [readyAction]);
        var blockedAction = RecoveryActionInstance.Create(RequiredAction("blocked-password", RecoveryActionImportance.Critical));
        blockedAction.Block("Primary email must be secured first.");
        var blocked = new Account(Guid.NewGuid(), "blocked-provider", AccountCriticality.Critical, [blockedAction]);
        var routineAction = RecoveryActionInstance.Create(RequiredAction("routine-password", RecoveryActionImportance.Critical));
        routineAction.Start();
        routineAction.Complete();
        var routine = new Account(Guid.NewGuid(), "routine-provider", AccountCriticality.Routine, [routineAction]);
        var session = new RecoverySession(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        session.AddAccount(ready);
        session.AddAccount(blocked);
        session.AddAccount(routine);

        var readiness = session.CalculateCriticalAccountReadiness();

        Assert.Equal(2, readiness.Count);
        Assert.Contains(readiness, account => account.AccountId == ready.Id && account.IsReady);
        var blockedReadiness = Assert.Single(readiness, account => account.AccountId == blocked.Id);
        Assert.Equal(CriticalAccountReadinessStatus.NotReady, blockedReadiness.Status);
        Assert.Equal(0, blockedReadiness.RequiredActionsCompleted);
        Assert.Equal(1, blockedReadiness.RequiredActionsTotal);
        Assert.Equal(1, blockedReadiness.BlockedRequiredActions);
        Assert.Equal(0, blockedReadiness.FailedRequiredActions);
    }

    [Fact]
    public void ProgressExposesReadinessAndReviewRatios()
    {
        var criticalDone = RecoveryActionInstance.Create(RequiredAction("critical-done"));
        criticalDone.Start();
        criticalDone.Complete();
        var criticalOpen = RecoveryActionInstance.Create(RequiredAction("critical-open"));
        var routineDone = RecoveryActionInstance.Create(RequiredAction("routine-done"));
        routineDone.Start();
        routineDone.Complete();
        var session = new RecoverySession(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        session.AddAccount(new Account(Guid.NewGuid(), "critical-done", AccountCriticality.Critical, [criticalDone]));
        session.AddAccount(new Account(Guid.NewGuid(), "critical-open", AccountCriticality.Critical, [criticalOpen]));
        session.AddAccount(new Account(Guid.NewGuid(), "routine-done", AccountCriticality.Routine, [routineDone]));

        var progress = session.CalculateProgress();

        Assert.Equal(0.5, progress.CriticalAccountReadinessRatio);
        Assert.Equal(2 / 3.0, progress.AccountReviewRatio);
    }

    [Fact]
    public void StartActionBlocksUntilPrerequisitesAreCompleted()
    {
        var prerequisite = RecoveryActionInstance.Create(RequiredAction("secure-email"));
        var dependentDefinition = RequiredAction("reset-dependent", prerequisites: ["secure-email"]);
        var dependent = RecoveryActionInstance.Create(dependentDefinition);
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Critical, [prerequisite, dependent]);

        account.StartAction("reset-dependent");

        Assert.Equal(RecoveryActionStatus.Blocked, dependent.Status);
        Assert.Contains("secure-email", dependent.StatusReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEventsUseStructuredFieldsInsteadOfFreeText()
    {
        var accountId = Guid.NewGuid();

        var auditEvent = AuditEvent.Create(
            AuditEventType.RecoveryActionCompleted,
            accountId,
            RecoveryActionType.ChangePassword,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(AuditEventType.RecoveryActionCompleted, auditEvent.EventType);
        Assert.Equal(accountId, auditEvent.AccountId);
        Assert.Equal(RecoveryActionType.ChangePassword, auditEvent.ActionType);
        Assert.Equal(DateTimeOffset.UnixEpoch, auditEvent.OccurredAt);
    }

    private static RecoveryActionInstance RequiredActionInstance(string id) =>
        RecoveryActionInstance.Create(RequiredAction(id));

    private static RecoveryActionInstance CompletedAction(string id)
    {
        var action = RequiredActionInstance(id);
        action.Start();
        action.Complete();
        return action;
    }

    private static RecoveryActionDefinition RequiredAction(
        string id,
        RecoveryActionImportance importance = RecoveryActionImportance.Important,
        IReadOnlyList<string>? prerequisites = null) =>
        new(
            id,
            RecoveryActionType.ChangePassword,
            [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            importance,
            AutomationSupport.None,
            prerequisites ?? [],
            ["User confirmed the synthetic recovery action is complete."]);
}
