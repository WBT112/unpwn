using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class RecoverySessionWorkspaceTests
{
    [Fact]
    public void CompromisedRecoveryChannelCreatesAdvisoryFirstRecommendation()
    {
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Incident review",
            new RecoveryIncidentIntake(IncidentIndicator.CompromisedRecoveryChannel),
            DateTimeOffset.UnixEpoch);

        var dashboard = session.CreateDashboardSnapshot();

        Assert.True(session.Incident.RequiresEmergencyAttention);
        Assert.Equal(
            RecoveryDashboardRecommendationCode.SecureRecoveryChannel,
            dashboard.Recommendation.Code);
        Assert.Null(dashboard.Recommendation.AccountId);
    }

    [Fact]
    public void DashboardKeepsCriticalReadinessAndRisksSeparateFromWeightedProgress()
    {
        var criticalAccountId = Guid.NewGuid();
        var importantAccountId = Guid.NewGuid();
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Mixed recovery",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [
                CreateAccount(
                    criticalAccountId,
                    "primary-email",
                    AccountCriticality.Critical,
                    AccountRecoveryStatus.NotFullySecured,
                    completedActions: 1,
                    totalActions: 2,
                    completedWeight: 5,
                    totalWeight: 8,
                    blocked: 1,
                    failed: 0,
                    unresolved: 1,
                    accessLost: true,
                    awaitingExport: 0,
                    awaitingDeletion: 0,
                    actionId: "restore-access"),
                CreateAccount(
                    importantAccountId,
                    "payments",
                    AccountCriticality.Important,
                    AccountRecoveryStatus.FullyReviewed,
                    completedActions: 2,
                    totalActions: 2,
                    completedWeight: 4,
                    totalWeight: 4,
                    blocked: 0,
                    failed: 0,
                    unresolved: 0,
                    accessLost: false,
                    awaitingExport: 1,
                    awaitingDeletion: 1,
                    actionId: "export-credential"),
            ],
            DateTimeOffset.UnixEpoch.AddMinutes(1));

        var dashboard = session.CreateDashboardSnapshot();

        Assert.Equal(0, dashboard.CriticalAccountsReady);
        Assert.Equal(1, dashboard.CriticalAccountsTotal);
        Assert.Equal(1, dashboard.AccountsFullyReviewed);
        Assert.Equal(2, dashboard.AccountsTotal);
        Assert.Equal(0.75, dashboard.WeightedRequiredActionProgress, precision: 3);
        Assert.Equal(1, dashboard.BlockedRequiredActions);
        Assert.Equal(1, dashboard.UnresolvedRisks);
        Assert.Equal(1, dashboard.AccountsWithLostAccess);
        Assert.Equal(1, dashboard.CredentialsAwaitingExport);
        Assert.Equal(1, dashboard.CredentialsAwaitingDeletion);
        Assert.Equal(
            RecoveryDashboardRecommendationCode.RestoreCriticalAccess,
            dashboard.Recommendation.Code);
        Assert.Equal(criticalAccountId, dashboard.Recommendation.AccountId);
        Assert.Contains(
            dashboard.Alerts,
            alert => alert.Kind == RecoveryDashboardAlertKind.LostAccess &&
                     alert.AccountId == criticalAccountId);
        Assert.Contains(
            dashboard.Alerts,
            alert => alert.Kind == RecoveryDashboardAlertKind.CredentialExport &&
                     alert.AccountId == importantAccountId);
    }

    [Fact]
    public void RemovedIncidentIndicatorsFailClosed()
    {
        var removedIndicator = (IncidentIndicator)(1 << 5);

        Assert.Throws<InvalidOperationException>(() => RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Unsupported intake",
            new RecoveryIncidentIntake(removedIndicator),
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void PauseResumeAndArchiveAreExplicitPersistableTransitions()
    {
        var created = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Lifecycle",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch);

        var paused = created.Pause(DateTimeOffset.UnixEpoch.AddMinutes(1));
        var resumed = paused.Resume(DateTimeOffset.UnixEpoch.AddMinutes(2));
        var archived = resumed.Archive(DateTimeOffset.UnixEpoch.AddMinutes(3));

        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Paused, paused.Status);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Active, resumed.Status);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Archived, archived.Status);
        Assert.Equal(3, archived.Revision);
        Assert.Throws<InvalidOperationException>(() =>
            archived.Resume(DateTimeOffset.UnixEpoch.AddMinutes(4)));
    }

    [Fact]
    public void EmptyDashboardAndLifecycleStatesHaveExplicitRecommendations()
    {
        var active = RecoverySessionWorkspace.Create(
            Guid.NewGuid(), "Lifecycle", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch);

        Assert.Equal(
            RecoveryDashboardRecommendationCode.ImportAccounts,
            active.CreateDashboardSnapshot().Recommendation.Code);
        Assert.False(active.CreateDashboardSnapshot().HasCriticalAccounts);
        Assert.False(active.CreateDashboardSnapshot().AreAllCriticalAccountsReady);
        Assert.Equal(
            RecoveryDashboardRecommendationCode.ResumeSession,
            active.Pause(DateTimeOffset.UnixEpoch.AddMinutes(1))
                .CreateDashboardSnapshot().Recommendation.Code);
        Assert.Equal(
            RecoveryDashboardRecommendationCode.ArchivedSession,
            active.Archive(DateTimeOffset.UnixEpoch.AddMinutes(1))
                .CreateDashboardSnapshot().Recommendation.Code);
    }

    [Theory]
    [InlineData(AccountCriticality.Critical, false, 1, 0, 0, RecoveryDashboardRecommendationCode.ResolveCriticalBlocker)]
    [InlineData(AccountCriticality.Critical, false, 0, 1, 0, RecoveryDashboardRecommendationCode.ResolveCriticalBlocker)]
    [InlineData(AccountCriticality.Important, false, 0, 0, 1, RecoveryDashboardRecommendationCode.AddressUnresolvedRisk)]
    [InlineData(AccountCriticality.Critical, false, 0, 0, 0, RecoveryDashboardRecommendationCode.ReviewCriticalAccount)]
    [InlineData(AccountCriticality.Routine, false, 0, 0, 0, RecoveryDashboardRecommendationCode.ReviewNextAccount)]
    public void DashboardSelectsRecommendationForOutstandingAccountState(
        AccountCriticality criticality,
        bool accessLost,
        int blocked,
        int failed,
        int unresolved,
        RecoveryDashboardRecommendationCode expected)
    {
        var account = CreateAccount(
            Guid.NewGuid(), "provider", criticality, AccountRecoveryStatus.NotFullySecured,
            0, 1, 0, 1, blocked, failed, unresolved, accessLost, 0, 0, "next");
        var session = RecoverySessionWorkspace.Create(
                Guid.NewGuid(), "Recommendation", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, session.CreateDashboardSnapshot().Recommendation.Code);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void CompletedAccountsRecommendCredentialHandoffWhenNeeded(int awaitingExport, int awaitingDeletion)
    {
        var account = CreateAccount(
            Guid.NewGuid(), "provider", AccountCriticality.Important, AccountRecoveryStatus.FullyReviewed,
            1, 1, 1, 1, 0, 0, 0, false, awaitingExport, awaitingDeletion, "handoff");
        var session = RecoverySessionWorkspace.Create(
                Guid.NewGuid(), "Handoff", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch);

        Assert.Equal(
            RecoveryDashboardRecommendationCode.ExportGeneratedCredentials,
            session.CreateDashboardSnapshot().Recommendation.Code);
    }

    [Fact]
    public void DashboardEmitsEveryNonZeroAlertAndOmitsZeroAlerts()
    {
        var account = CreateAccount(
            Guid.NewGuid(), "provider", AccountCriticality.Important, AccountRecoveryStatus.NotFullySecured,
            0, 1, 0, 1, 1, 2, 3, true, 4, 5, "action");
        var session = RecoverySessionWorkspace.Create(
                Guid.NewGuid(), "Alerts", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch);

        var alerts = session.CreateDashboardSnapshot().Alerts;

        Assert.Equal(6, alerts.Count);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.BlockedAction && alert.Count == 1);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.FailedAction && alert.Count == 2);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.UnresolvedRisk && alert.Count == 3);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.LostAccess && alert.Count == 1);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.CredentialExport && alert.Count == 4);
        Assert.Contains(alerts, alert => alert.Kind == RecoveryDashboardAlertKind.CredentialDeletion && alert.Count == 5);
    }

    [Theory]
    [InlineData(RecoveryWorkspaceLifecycleStatus.Active, RecoveryWorkspaceLifecycleStatus.Paused)]
    [InlineData(RecoveryWorkspaceLifecycleStatus.Paused, RecoveryWorkspaceLifecycleStatus.Active)]
    [InlineData(RecoveryWorkspaceLifecycleStatus.Active, RecoveryWorkspaceLifecycleStatus.Archived)]
    [InlineData(RecoveryWorkspaceLifecycleStatus.Paused, RecoveryWorkspaceLifecycleStatus.Archived)]
    public void AllowedLifecycleTransitionsAreCovered(
        RecoveryWorkspaceLifecycleStatus initial,
        RecoveryWorkspaceLifecycleStatus target)
    {
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(), "Transitions", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch);
        if (initial == RecoveryWorkspaceLifecycleStatus.Paused)
        {
            session = session.Pause(DateTimeOffset.UnixEpoch);
        }

        var transitioned = target switch
        {
            RecoveryWorkspaceLifecycleStatus.Active => session.Resume(DateTimeOffset.UnixEpoch),
            RecoveryWorkspaceLifecycleStatus.Paused => session.Pause(DateTimeOffset.UnixEpoch),
            RecoveryWorkspaceLifecycleStatus.Archived => session.Archive(DateTimeOffset.UnixEpoch),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        Assert.Equal(target, transitioned.Status);
    }

    [Fact]
    public void WorkspaceRejectsInvalidIdentityNameTimeAndDuplicateAccounts()
    {
        Assert.Throws<ArgumentException>(() => RecoverySessionWorkspace.Create(
            Guid.Empty, "Session", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => RecoverySessionWorkspace.Create(
            Guid.NewGuid(), " ", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoverySessionWorkspace.Create(
            Guid.NewGuid(), new string('x', 121), RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentNullException>(() => RecoverySessionWorkspace.Create(
            Guid.NewGuid(), "Session", null!, DateTimeOffset.UnixEpoch));

        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(), " Session ", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch);
        var account = CreateAccount(
            Guid.NewGuid(), "provider", AccountCriticality.Routine, AccountRecoveryStatus.FullyReviewed,
            0, 0, 0, 0, 0, 0, 0, false, 0, 0, "next");
        Assert.Equal("Session", session.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Pause(DateTimeOffset.UnixEpoch.AddTicks(-1)));
        Assert.Throws<ArgumentNullException>(() => session.ReplaceAccounts(null!, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => session.ReplaceAccounts(
            [account, account], DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(0, 0, -1)]
    public void DashboardAccountValidationRejectsInvalidCounters(
        int completed,
        int total,
        int blocked)
    {
        var account = CreateAccount(
            Guid.NewGuid(), "provider", AccountCriticality.Routine, AccountRecoveryStatus.NotFullySecured,
            completed, total, 0, 0, blocked, 0, 0, false, 0, 0, "next");

        Assert.ThrowsAny<ArgumentException>(account.Validate);
    }

    [Fact]
    public void RetainedIncidentIndicatorHasEmergencyAdvisorySemantics()
    {
        var compromisedChannel = new RecoveryIncidentIntake(
            IncidentIndicator.CompromisedRecoveryChannel);

        Assert.True(compromisedChannel.RequiresEmergencyAttention);
    }

    [Fact]
    public void DashboardRecommendationUsesCanonicalCategoryQueue()
    {
        var email = CreateAccount(
            Guid.Parse("10000000-0000-0000-0000-000000000000"),
            "z-email",
            AccountCriticality.Important,
            AccountRecoveryStatus.Open,
            0, 1, 0, 1, 0, 0, 0, false, 0, 0, "next") with
        {
            Category = AccountRecoveryCategory.Email,
        };
        var critical = email with
        {
            AccountId = Guid.Parse("20000000-0000-0000-0000-000000000000"),
            ProviderId = "a-critical",
            Criticality = AccountCriticality.Critical,
            Category = AccountRecoveryCategory.Critical,
        };
        var unknown = email with
        {
            AccountId = Guid.Parse("30000000-0000-0000-0000-000000000000"),
            ProviderId = "a-unknown",
            Criticality = AccountCriticality.Routine,
            Category = AccountRecoveryCategory.Unknown,
        };
        var nonCritical = email with
        {
            AccountId = Guid.Parse("40000000-0000-0000-0000-000000000000"),
            ProviderId = "a-non-critical",
            Criticality = AccountCriticality.Routine,
            Category = AccountRecoveryCategory.NonCritical,
        };
        var workspace = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Category queue",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
                [nonCritical, unknown, critical, email],
                DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(email.AccountId, workspace.CreateDashboardSnapshot().Recommendation.AccountId);

        workspace = workspace.ReplaceAccounts(
            [nonCritical, unknown, critical, email with { RecoveryStatus = AccountRecoveryStatus.FullyReviewed }],
            DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(critical.AccountId, workspace.CreateDashboardSnapshot().Recommendation.AccountId);

        workspace = workspace.ReplaceAccounts(
            [nonCritical, unknown, critical with { RecoveryStatus = AccountRecoveryStatus.FullyReviewed },
                email with { RecoveryStatus = AccountRecoveryStatus.FullyReviewed }],
            DateTimeOffset.UnixEpoch.AddSeconds(3));
        Assert.Equal(unknown.AccountId, workspace.CreateDashboardSnapshot().Recommendation.AccountId);
    }

    [Fact]
    public void DeferringAnAccountMovesItBehindTheCurrentPassWithoutResolvingIt()
    {
        var first = CreateAccount(
            Guid.Parse("10000000-0000-0000-0000-000000000000"),
            "first-email",
            AccountCriticality.Important,
            AccountRecoveryStatus.Open,
            0, 1, 0, 1, 0, 0, 0, false, 0, 0, "reset") with
        {
            Category = AccountRecoveryCategory.Email,
        };
        var second = first with
        {
            AccountId = Guid.Parse("20000000-0000-0000-0000-000000000000"),
            ProviderId = "second-email",
        };
        var workspace = RecoverySessionWorkspace.Create(
                Guid.NewGuid(), "Deferral", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([first, second], DateTimeOffset.UnixEpoch.AddSeconds(1));

        var deferred = workspace.DeferAccount(
            first.AccountId,
            DateTimeOffset.UnixEpoch.AddSeconds(2));

        Assert.Equal(second.AccountId, deferred.CreateDashboardSnapshot().Recommendation.AccountId);
        var persisted = deferred.Accounts.Single(account => account.AccountId == first.AccountId);
        Assert.Equal(AccountRecoveryStatus.Open, persisted.RecoveryStatus);
        Assert.Equal(1, persisted.DeferralCount);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), persisted.DeferredAt);

        var refreshed = deferred.ReplaceAccounts(
            [first, second with { RecoveryStatus = AccountRecoveryStatus.FullyReviewed }],
            DateTimeOffset.UnixEpoch.AddSeconds(3));
        Assert.Equal(1, refreshed.Accounts.Single(account => account.AccountId == first.AccountId).DeferralCount);
        Assert.Equal(first.AccountId, refreshed.CreateDashboardSnapshot().Recommendation.AccountId);
    }

    [Fact]
    public void CompletedOrInactiveAccountWorkCannotBeDeferred()
    {
        var account = CreateAccount(
            Guid.NewGuid(), "done", AccountCriticality.Routine, AccountRecoveryStatus.FullyReviewed,
            1, 1, 1, 1, 0, 0, 0, false, 0, 0, "done") with
        {
            Category = AccountRecoveryCategory.NonCritical,
        };
        var active = RecoverySessionWorkspace.Create(
                Guid.NewGuid(), "Deferral", RecoveryIncidentIntake.Empty, DateTimeOffset.UnixEpoch)
            .ReplaceAccounts([account], DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => active.DeferAccount(
            account.AccountId,
            DateTimeOffset.UnixEpoch.AddSeconds(2)));
        var paused = active.ReplaceAccounts(
                [account with { RecoveryStatus = AccountRecoveryStatus.Open }],
                DateTimeOffset.UnixEpoch.AddSeconds(2))
            .Pause(DateTimeOffset.UnixEpoch.AddSeconds(3));
        Assert.Throws<InvalidOperationException>(() => paused.DeferAccount(
            account.AccountId,
            DateTimeOffset.UnixEpoch.AddSeconds(4)));
    }

    private static RecoveryAccountDashboardEntry CreateAccount(
        Guid accountId,
        string providerId,
        AccountCriticality criticality,
        AccountRecoveryStatus recoveryStatus,
        int completedActions,
        int totalActions,
        int completedWeight,
        int totalWeight,
        int blocked,
        int failed,
        int unresolved,
        bool accessLost,
        int awaitingExport,
        int awaitingDeletion,
        string actionId) =>
        new(
            accountId,
            providerId,
            criticality,
            recoveryStatus,
            completedActions,
            totalActions,
            completedWeight,
            totalWeight,
            blocked,
            failed,
            unresolved,
            accessLost,
            awaitingExport,
            awaitingDeletion,
            actionId)
        {
            Category = criticality switch
            {
                AccountCriticality.Critical => AccountRecoveryCategory.Critical,
                AccountCriticality.Important => AccountRecoveryCategory.Email,
                _ => AccountRecoveryCategory.NonCritical,
            },
        };
}
