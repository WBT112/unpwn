using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class GitHubRecoveryWorkflowTests
{
    private static RecoveryWorkflowDefinition Workflow =>
        RepositoryWorkflowCatalog.Workflows.Single(candidate =>
            candidate.ProviderId == "github.com");

    [Fact]
    public void UsesOnlyReviewedOfficialGitHubLocations()
    {
        Assert.Equal("1.1.0", Workflow.WorkflowVersion);
        Assert.Equal(new DateOnly(2026, 8, 10), Workflow.VerifiedAt);
        Assert.Equal(
            [
                ("settings", "https://github.com/settings/security", "https://github.com"),
                ("password-reset", "https://github.com/password_reset", "https://github.com"),
                ("sessions", "https://github.com/settings/sessions", "https://github.com"),
                ("emails", "https://github.com/settings/emails", "https://github.com"),
                ("applications", "https://github.com/settings/applications", "https://github.com"),
                ("tokens", "https://github.com/settings/tokens", "https://github.com"),
                ("keys", "https://github.com/settings/keys", "https://github.com"),
                ("manual-recovery-guide", "https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa/recovering-your-account-if-you-lose-your-2fa-credentials", "https://docs.github.com"),
            ],
            Workflow.RecoveryLocations.Select(location => (
                location.Id,
                location.Url.AbsoluteUri,
                Assert.Single(location.ExpectedOrigins))));
    }

    [Theory]
    [InlineData(RecoveryPath.AuthenticatedChange)]
    [InlineData(RecoveryPath.PasswordReset)]
    public void RestoredAccessPathsRequireEveryAccountAndDeveloperCredentialReview(RecoveryPath path)
    {
        var actions = Workflow.Actions.Where(action => action.SupportsPath(path)).ToArray();

        Assert.Contains(actions, action => action.Type == RecoveryActionType.InvalidateSessions);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewMfa);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewRecoveryOptions);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewConnectedApplications);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewApiTokens);
        Assert.Contains(actions, action => action.Type == RecoveryActionType.ReviewSshAndSigningKeys);
        Assert.All(actions, action => Assert.True(action.IsRequired));
        Assert.All(actions, action => Assert.True(action.AutomationSupport <= AutomationSupport.Navigation));
    }

    [Theory]
    [InlineData(RecoveryPath.AuthenticatedChange, "review-api-tokens-auth", "review-ssh-signing-keys-auth")]
    [InlineData(RecoveryPath.PasswordReset, "review-api-tokens-reset", "review-ssh-signing-keys-reset")]
    public void DeveloperCredentialsAreSeparateRequiredCriticalActions(
        RecoveryPath path,
        string tokenActionId,
        string keyActionId)
    {
        var tokenAction = Workflow.Actions.Single(action => action.Id == tokenActionId);
        var keyAction = Workflow.Actions.Single(action => action.Id == keyActionId);

        Assert.Equal(RecoveryActionType.ReviewApiTokens, tokenAction.Type);
        Assert.Equal("tokens", tokenAction.RecoveryLocationId);
        Assert.Equal(RecoveryActionType.ReviewSshAndSigningKeys, keyAction.Type);
        Assert.Equal("keys", keyAction.RecoveryLocationId);
        Assert.All([tokenAction, keyAction], action =>
        {
            Assert.True(action.IsRequired);
            Assert.Equal(RecoveryActionImportance.Critical, action.Importance);
            Assert.Equal(AutomationSupport.Navigation, action.AutomationSupport);
            Assert.Contains(path, action.RecoveryPaths);
        });
    }

    [Fact]
    public void PasswordResetScenariosKeepSecuredEmailDependencyVisible()
    {
        var scenarios = RepositoryWorkflowCatalog.ContractScenarios
            .Where(scenario => scenario.WorkflowId == Workflow.WorkflowId)
            .ToArray();

        Assert.Contains(scenarios, scenario =>
            scenario.Id == "github-password-reset-through-secured-email" &&
            scenario.ExpectedOutcome == AccountContractOutcome.CanBeFullySecured);
        Assert.Contains(scenarios, scenario =>
            scenario.Id == "github-password-reset-blocked-by-email" &&
            scenario.ExpectedOutcome == AccountContractOutcome.BlockedByDependency);
    }

    [Fact]
    public void ManualRecoveryNavigatesOnlyToOfficialGuidance()
    {
        var manualRecovery = Workflow.Actions.Single(action =>
            action.Id == "manual-recovery");

        Assert.Equal(AutomationSupport.Navigation, manualRecovery.AutomationSupport);
        Assert.Equal("manual-recovery-guide", manualRecovery.RecoveryLocationId);
        Assert.Contains(RecoveryPath.ManualRecovery, manualRecovery.RecoveryPaths);
    }
}
