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
            new RecoveryIncidentIntake(
                IncidentIndicator.CompromisedRecoveryChannel,
                "Unexpected recovery email changes were observed."),
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

    [Theory]
    [InlineData("password: synthetic-secret-value")]
    [InlineData("Reset link: https://example.invalid/reset")]
    [InlineData("token: abcdefghijklmnopqrstuvwxyz0123456789")]
    public void IncidentDescriptionRejectsSecretOrLinkMaterial(string description)
    {
        Assert.Throws<ArgumentException>(() => RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Unsafe intake",
            new RecoveryIncidentIntake(IncidentIndicator.None, description),
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
            actionId,
            DependencyDepth: 0,
            WaitingForAccountIds: []);
}
