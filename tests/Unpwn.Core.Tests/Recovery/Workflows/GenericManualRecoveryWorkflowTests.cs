using Unpwn.Core;
using Unpwn.Core.Recovery.Workflows;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class GenericManualRecoveryWorkflowTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsRepositoryControlledWithoutProviderLocationsOrTrustedOrigins()
    {
        var workflow = GenericManualRecoveryWorkflow.Create("unsupported.example");

        Assert.True(RecoveryWorkflowValidator.Validate(
            workflow,
            new DateOnly(2026, 8, 12)).IsValid);
        Assert.Equal(GenericManualRecoveryWorkflow.WorkflowId, workflow.WorkflowId);
        Assert.Equal(RecoveryWorkflowTrustLevel.GeneralManualGuidance, workflow.TrustLevel);
        Assert.True(workflow.AllowsAccountOriginDiscovery);
        Assert.Empty(workflow.RecoveryLocations);
        Assert.Equal(
            [RecoveryPath.AuthenticatedChange, RecoveryPath.PasswordReset, RecoveryPath.ManualRecovery],
            workflow.Actions.SelectMany(action => action.RecoveryPaths).Distinct().Order().ToArray());
        Assert.All(
            workflow.Actions.Where(action => action.AutomationSupport == AutomationSupport.Navigation),
            action => Assert.Equal(RecoveryActionType.ChangePassword, action.Type));
    }

    [Fact]
    public void ArbitraryProviderMetadataCannotAddLocationsOrChangeStableSemantics()
    {
        var first = GenericManualRecoveryWorkflow.Create("first.unsupported.example");
        var second = GenericManualRecoveryWorkflow.Create("second.unsupported.example");

        Assert.Empty(first.RecoveryLocations);
        Assert.Empty(second.RecoveryLocations);
        Assert.Equal(first.WorkflowId, second.WorkflowId);
        Assert.Equal(first.WorkflowVersion, second.WorkflowVersion);
        Assert.Equal(
            first.Actions.Select(Semantics).ToArray(),
            second.Actions.Select(Semantics).ToArray());
    }

    [Fact]
    public void ProviderDependentControlRequiresExplicitNotApplicableReasonAndCanThenFinish()
    {
        var workflow = GenericManualRecoveryWorkflow.Create("unsupported.example");
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);
        var time = StartedAt;

        foreach (var action in state.Actions)
        {
            time = time.AddMinutes(1);
            state = state.StartAction(workflow, action.DefinitionId, time);
            time = time.AddMinutes(1);
            state = action.DefinitionId == "review-connected-access-auth"
                ? state.MarkNotApplicable(
                    workflow,
                    action.DefinitionId,
                    "The synthetic service exposes no connected-access control.",
                    NotApplicableDisposition.TrulyNotApplicable,
                    time)
                : state.CompleteAction(workflow, action.DefinitionId, true, time);
        }

        Assert.Equal(AccountRecoveryStatus.FullyReviewed, state.RecoveryStatus);
        Assert.Equal(
            NotApplicableDisposition.TrulyNotApplicable,
            state.GetAction("review-connected-access-auth").NotApplicableDisposition);
    }

    [Fact]
    public void LostAccessAndAcceptedRiskRemainVisible()
    {
        var workflow = GenericManualRecoveryWorkflow.Create("unsupported.example");
        var lost = AccountRecoveryExecutionState.Create(
                Guid.NewGuid(), workflow, RecoveryPath.ManualRecovery, StartedAt)
            .SetAccessState(
                RecoveryAccessState.Lost,
                "Synthetic provider recovery did not restore access.",
                StartedAt.AddMinutes(1));
        var risk = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(), workflow, RecoveryPath.PasswordReset, StartedAt);
        risk = risk.StartAction(workflow, "identify-account-reset", StartedAt.AddMinutes(1));
        risk = risk.AcceptUnresolvedRisk(
            workflow,
            "identify-account-reset",
            "Synthetic ownership context remains unresolved.",
            StartedAt.AddMinutes(2));

        Assert.Equal(AccountRecoveryStatus.AccessNotRestored, lost.RecoveryStatus);
        Assert.Equal(AccountRecoveryStatus.NotFullySecured, risk.RecoveryStatus);
        Assert.True(risk.GetAction("identify-account-reset").HasUnresolvedRisk);
    }

    [Fact]
    public void ValidatorRejectsProviderOriginsOnGeneralGuidance()
    {
        var workflow = GenericManualRecoveryWorkflow.Create("unsupported.example") with
        {
            RecoveryLocations =
            [
                new RecoveryLocationDefinition(
                    "guessed",
                    new Uri("https://unsupported.example/recovery"),
                    ["https://unsupported.example"]),
            ],
        };

        var result = RecoveryWorkflowValidator.Validate(
            workflow,
            new DateOnly(2026, 8, 12));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Rule == "general-workflow-provider-location-forbidden");
    }

    private static string Semantics(RecoveryActionDefinition action) => string.Join(
        '|',
        action.Id,
        action.Type,
        string.Join(',', action.RecoveryPaths),
        action.Requirement,
        action.Importance,
        action.AutomationSupport,
        string.Join(',', action.Prerequisites),
        action.RecoveryLocationId ?? "<none>");
}
