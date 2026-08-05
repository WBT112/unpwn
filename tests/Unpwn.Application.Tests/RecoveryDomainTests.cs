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
    public void NotApplicableRequiresReason()
    {
        var action = RecoveryActionInstance.Create(RequiredAction("mfa"));

        Assert.Throws<ArgumentException>(() => action.MarkNotApplicable(""));
    }

    [Fact]
    public void RequiredActionWithoutCompletionCriteriaIsRejected()
    {
        var definition = RequiredAction("sessions") with { CompletionCriteria = "" };

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
    public void AccountIsFullyReviewedWhenRequiredActionsAreCompletedOrNotApplicableWithReason()
    {
        var password = RecoveryActionInstance.Create(RequiredAction("password"));
        var tokens = RecoveryActionInstance.Create(RequiredAction("tokens"));
        password.Start();
        password.Complete();
        tokens.MarkNotApplicable("The account type does not support API tokens.");
        var account = new Account(Guid.NewGuid(), "synthetic", AccountCriticality.Important, [password, tokens]);

        Assert.Equal(AccountRecoveryStatus.FullyReviewed, account.Status);
    }

    [Fact]
    public void SessionProgressReportsSeparateSecuritySignals()
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
        Assert.Equal(0.5, progress.WeightedRequiredActionsCompleted);
        Assert.Equal(1, progress.BlockedRequiredActions);
        Assert.Equal(1, progress.UnresolvedRisks);
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
    public void AuditEventsRejectSyntheticSecretMarkers()
    {
        Assert.Throws<ArgumentException>(() => AuditEvent.Create("test", "leaked UNPWN_TEST_SECRET_password"));
    }

    private static RecoveryActionDefinition RequiredAction(
        string id,
        RecoveryActionImportance importance = RecoveryActionImportance.Important,
        IReadOnlyCollection<string>? prerequisites = null) =>
        new(
            id,
            RecoveryActionType.ChangePassword,
            RecoveryPath.AuthenticatedChange,
            importance,
            IsRequired: true,
            AutomationSupport.None,
            CompletionCriteria: "User confirmed the synthetic recovery action is complete.",
            PrerequisiteActionIds: prerequisites);
}
