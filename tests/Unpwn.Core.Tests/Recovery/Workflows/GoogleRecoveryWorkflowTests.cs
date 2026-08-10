using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class GoogleRecoveryWorkflowTests
{
    private static RecoveryWorkflowDefinition Workflow =>
        RepositoryWorkflowCatalog.Workflows.Single(candidate =>
            candidate.ProviderId == "google.com");

    [Fact]
    public void UsesOnlyReviewedOfficialGoogleRecoveryAndSecurityLocations()
    {
        Assert.Equal(new DateOnly(2026, 8, 10), Workflow.VerifiedAt);
        Assert.Equal(
            [
                ("security", "https://myaccount.google.com/security", "https://myaccount.google.com"),
                ("account-recovery", "https://accounts.google.com/signin/recovery", "https://accounts.google.com"),
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

        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewTrustedDevices);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewMfa);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewRecoveryOptions);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewConnectedApplications);
        Assert.All(actions, action => Assert.True(action.IsRequired));
        Assert.All(actions, action => Assert.True(action.AutomationSupport <= AutomationSupport.Navigation));
    }

    [Fact]
    public void PasswordResetScenariosKeepRecoveryEmailDependencyVisible()
    {
        var scenarios = RepositoryWorkflowCatalog.ContractScenarios
            .Where(scenario => scenario.WorkflowId == Workflow.WorkflowId)
            .ToArray();

        Assert.Contains(scenarios, scenario =>
            scenario.Id == "google-password-reset-through-secured-recovery-channel" &&
            scenario.ExpectedOutcome == AccountContractOutcome.CanBeFullySecured);
        Assert.Contains(scenarios, scenario =>
            scenario.Id == "google-password-reset-blocked-by-recovery-email" &&
            scenario.ExpectedOutcome == AccountContractOutcome.BlockedByDependency);
    }

    [Fact]
    public void ManualPathUsesOfficialRecoveryWithoutClaimingAutomation()
    {
        var manualRecovery = Workflow.Actions.Single(action =>
            action.Id == "manual-recovery");

        Assert.Equal(AutomationSupport.Navigation, manualRecovery.AutomationSupport);
        Assert.Equal("account-recovery", manualRecovery.RecoveryLocationId);
        Assert.Contains(RecoveryPath.ManualRecovery, manualRecovery.RecoveryPaths);
    }
}
