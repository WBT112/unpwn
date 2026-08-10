using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class MicrosoftRecoveryWorkflowTests
{
    private static RecoveryWorkflowDefinition Workflow =>
        RepositoryWorkflowCatalog.Workflows.Single(candidate =>
            candidate.ProviderId == "microsoft.com");

    [Fact]
    public void UsesOnlyReviewedOfficialPersonalAccountLocations()
    {
        Assert.Equal("personal", Workflow.SupportedAccountType);
        Assert.Equal(new DateOnly(2026, 8, 10), Workflow.VerifiedAt);
        Assert.Equal(
            [
                ("security", "https://account.microsoft.com/security", "https://account.microsoft.com"),
                ("password-reset", "https://account.live.com/password/reset", "https://account.live.com"),
                ("account-recovery-form", "https://account.live.com/acsr", "https://account.live.com"),
                ("recent-activity", "https://account.live.com/Activity", "https://account.live.com"),
                ("advanced-security", "https://account.live.com/proofs/manage/additional", "https://account.live.com"),
                ("devices", "https://account.microsoft.com/devices", "https://account.microsoft.com"),
                ("privacy", "https://account.microsoft.com/privacy", "https://account.microsoft.com"),
            ],
            Workflow.RecoveryLocations.Select(location => (
                location.Id,
                location.Url.AbsoluteUri,
                Assert.Single(location.ExpectedOrigins))));
    }

    [Theory]
    [InlineData(RecoveryPath.AuthenticatedChange)]
    [InlineData(RecoveryPath.PasswordReset)]
    public void RestoredAccessPathsRequireEverySecurityReview(RecoveryPath path)
    {
        var actions = Workflow.Actions.Where(action => action.SupportsPath(path)).ToArray();

        Assert.Contains(actions, action => action.Type == RecoveryActionType.InvalidateSessions);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewTrustedDevices);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewMfa);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewRecoveryOptions);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewConnectedApplications);
        Assert.All(actions, action => Assert.True(action.IsRequired));
        Assert.All(actions, action => Assert.True(action.AutomationSupport <= AutomationSupport.Navigation));
    }

    [Fact]
    public void PasswordResetScenariosKeepVerificationDependencyVisible()
    {
        var scenarios = RepositoryWorkflowCatalog.ContractScenarios
            .Where(scenario => scenario.WorkflowId == Workflow.WorkflowId)
            .ToArray();

        Assert.Contains(scenarios, scenario =>
            scenario.Id == "microsoft-password-reset-through-secured-verification-method" &&
            scenario.ExpectedOutcome == AccountContractOutcome.CanBeFullySecured);
        Assert.Contains(scenarios, scenario =>
            scenario.Id == "microsoft-password-reset-blocked-by-verification-method" &&
            scenario.ExpectedOutcome == AccountContractOutcome.BlockedByDependency);
    }

    [Fact]
    public void ManualPathUsesProviderReviewedRecoveryFormWithoutClaimingAutomation()
    {
        var manualRecovery = Workflow.Actions.Single(action =>
            action.Id == "manual-recovery");

        Assert.Equal(AutomationSupport.Navigation, manualRecovery.AutomationSupport);
        Assert.Equal("account-recovery-form", manualRecovery.RecoveryLocationId);
        Assert.Contains(RecoveryPath.ManualRecovery, manualRecovery.RecoveryPaths);
    }
}
