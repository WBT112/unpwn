using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountRecoveryWorkflowScopeTests
{
    [Fact]
    public void NonCriticalReviewedProviderKeepsOnlyPasswordActions()
    {
        var github = RepositoryWorkflowCatalog.Workflows.Single(workflow =>
            workflow.ProviderId == "github.com");

        var scoped = AccountRecoveryWorkflowScope.Project(
            github,
            AccountRecoveryCategory.NonCritical);

        Assert.Equal(2, scoped.Actions.Count);
        Assert.Collection(
            scoped.Actions.OrderBy(action => action.Id),
            action =>
            {
                Assert.Equal("change-password", action.Id);
                Assert.Equal(RecoveryActionType.ChangePassword, action.Type);
                Assert.Equal([RecoveryPath.AuthenticatedChange], action.RecoveryPaths);
                Assert.Empty(action.Prerequisites);
            },
            action =>
            {
                Assert.Equal("reset-password", action.Id);
                Assert.Equal(RecoveryActionType.ResetPassword, action.Type);
                Assert.Equal([RecoveryPath.PasswordReset], action.RecoveryPaths);
                Assert.Empty(action.Prerequisites);
            });
        Assert.NotNull(scoped.UnscopedActions);
        Assert.Equal(github.Actions.Count, scoped.UnscopedActions!.Count);
        Assert.DoesNotContain(scoped.Actions, action => action.Type is
            RecoveryActionType.ReviewMfa or
            RecoveryActionType.InvalidateSessions or
            RecoveryActionType.ReviewRecoveryOptions or
            RecoveryActionType.ReviewConnectedApplications or
            RecoveryActionType.ReviewApiTokens or
            RecoveryActionType.ReviewSshAndSigningKeys);
    }

    [Fact]
    public void NonCriticalGenericProviderKeepsOnlyPasswordActionsAndDiscoveryMetadata()
    {
        var generic = RepositoryWorkflowCatalog.CreateGenericManualWorkflow("service.example");

        var scoped = AccountRecoveryWorkflowScope.Project(
            generic,
            AccountRecoveryCategory.NonCritical);

        Assert.Equal(2, scoped.Actions.Count);
        Assert.Contains(scoped.Actions, action => action.Type == RecoveryActionType.ChangePassword);
        Assert.Contains(scoped.Actions, action => action.Type == RecoveryActionType.ResetPassword);
        Assert.DoesNotContain(scoped.Actions, action => action.SupportsPath(RecoveryPath.ManualRecovery));
        Assert.True(scoped.AllowsAccountOriginDiscovery);
        Assert.Equal(RecoveryWorkflowTrustLevel.GeneralManualGuidance, scoped.TrustLevel);
        Assert.Equal(
            RecoveryPath.PasswordReset,
            RecoveryPathSelector.Select(scoped).Path);
    }

    [Theory]
    [InlineData(AccountRecoveryCategory.Email)]
    [InlineData(AccountRecoveryCategory.Critical)]
    [InlineData(AccountRecoveryCategory.Unknown)]
    public void HigherRiskAndUnknownCategoriesKeepFullWorkflow(AccountRecoveryCategory category)
    {
        var github = RepositoryWorkflowCatalog.Workflows.Single(workflow =>
            workflow.ProviderId == "github.com");

        var scoped = AccountRecoveryWorkflowScope.Project(github, category);

        Assert.Same(github, scoped);
        Assert.Equal(github.Actions.Count, scoped.Actions.Count);
        Assert.Contains(scoped.Actions, action => action.Type == RecoveryActionType.ReviewMfa);
        Assert.Contains(scoped.Actions, action => action.Type == RecoveryActionType.InvalidateSessions);
    }

    [Fact]
    public void NonCriticalWorkflowWithoutPasswordActionHasNoSafePath()
    {
        var manual = CreateManualOnlyWorkflow();

        var scoped = AccountRecoveryWorkflowScope.Project(
            manual,
            AccountRecoveryCategory.NonCritical);

        Assert.Empty(scoped.Actions);
        Assert.False(RecoveryPathSelector.Select(scoped).HasSafePath);
    }

    [Fact]
    public void ProjectedViewOmitsHigherRiskActionsWithoutMarkingThemNotApplicable()
    {
        var github = RepositoryWorkflowCatalog.Workflows.Single(workflow =>
            workflow.ProviderId == "github.com");
        var scoped = AccountRecoveryWorkflowScope.Project(
            github,
            AccountRecoveryCategory.NonCritical);
        var fullState = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            github,
            DateTimeOffset.UnixEpoch);

        var projected = AccountRecoveryWorkflowScope.ProjectStateForView(
            fullState,
            scoped);

        var password = Assert.Single(projected.Actions);
        Assert.Equal("reset-password", password.DefinitionId);
        Assert.Equal(RecoveryActionStatus.Open, password.Status);
        Assert.DoesNotContain(projected.Actions, action =>
            action.Status == RecoveryActionStatus.NotApplicable);
        Assert.Equal(AccountRecoveryStatus.Open, projected.RecoveryStatus);
        Assert.True(fullState.Actions.Length > projected.Actions.Length);
    }

    [Fact]
    public void ExistingManualPathFailsClosedInNonCriticalViewButCanonicalStateIsUnchanged()
    {
        var workflow = CreateManualOnlyWorkflow();
        var fullState = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            DateTimeOffset.UnixEpoch);
        var scoped = AccountRecoveryWorkflowScope.Project(
            workflow,
            AccountRecoveryCategory.NonCritical);

        var projected = AccountRecoveryWorkflowScope.ProjectStateForView(fullState, scoped);

        Assert.Empty(projected.Actions);
        Assert.Equal(
            RecoveryPathSelectionReasonCode.NoSafeSupportedPath,
            projected.PathSelectionReason);
        Assert.Equal(
            RecoveryPathSelectionReasonCode.ManualRecoveryAvailable,
            fullState.PathSelectionReason);
        Assert.NotEmpty(fullState.Actions);
    }

    private static RecoveryWorkflowDefinition CreateManualOnlyWorkflow()
    {
        const string prefix = "Workflow.Test.Manual";
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryWorkflowDefinition(
            "test/manual-only",
            "manual.example",
            "Manual Example",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 15),
            [],
            [
                new RecoveryActionDefinition(
                    "manual-recovery",
                    RecoveryActionType.ManualRecovery,
                    [RecoveryPath.ManualRecovery],
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.None,
                    [],
                    criteria,
                    new RecoveryActionGuidanceKeys(
                        $"{prefix}.Title",
                        $"{prefix}.Instruction",
                        $"{prefix}.Warning",
                        $"{prefix}.Completion",
                        criteria)),
            ]);
    }
}
