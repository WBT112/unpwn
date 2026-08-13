using System.Text.Json;
using System.Text.Json.Nodes;
using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class RecoveryPathSelectorTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConfirmedAuthenticatedAccessSelectsAuthenticatedChange()
    {
        var selection = RecoveryPathSelector.Select(
            Workflow(RecoveryPath.AuthenticatedChange, RecoveryPath.PasswordReset, RecoveryPath.ManualRecovery),
            RecoveryAccessState.Available);

        Assert.Equal(RecoveryPath.AuthenticatedChange, selection.Path);
        Assert.Equal(
            RecoveryPathSelectionReasonCode.ConfirmedAuthenticatedAccess,
            selection.ReasonCode);
    }

    [Fact]
    public void UnknownAccessSelectsResetBeforeManualRecovery()
    {
        var selection = RecoveryPathSelector.Select(
            Workflow(RecoveryPath.AuthenticatedChange, RecoveryPath.PasswordReset, RecoveryPath.ManualRecovery));

        Assert.Equal(RecoveryPath.PasswordReset, selection.Path);
        Assert.Equal(RecoveryPathSelectionReasonCode.PasswordResetAvailable, selection.ReasonCode);
    }

    [Fact]
    public void ManualRecoveryIsTheSafeFallbackWhenResetIsUnavailable()
    {
        var selection = RecoveryPathSelector.Select(
            Workflow(RecoveryPath.AuthenticatedChange, RecoveryPath.ManualRecovery));

        Assert.Equal(RecoveryPath.ManualRecovery, selection.Path);
        Assert.Equal(RecoveryPathSelectionReasonCode.ManualRecoveryAvailable, selection.ReasonCode);
    }

    [Fact]
    public void NoSafePathReturnsVisibleBlockedOutcome()
    {
        var selection = RecoveryPathSelector.Select(Workflow(RecoveryPath.AuthenticatedChange));

        Assert.False(selection.HasSafePath);
        Assert.Null(selection.Path);
        Assert.Equal(RecoveryPathSelectionReasonCode.NoSafeSupportedPath, selection.ReasonCode);
    }

    [Fact]
    public void MissingCurrentPathPrerequisiteIsNotTreatedAsSafe()
    {
        var workflow = Workflow(RecoveryPath.PasswordReset) with
        {
            Actions =
            [
                Action("reset", RecoveryPath.PasswordReset, ["missing-prerequisite"]),
            ],
        };

        Assert.False(RecoveryPathSelector.Select(workflow).HasSafePath);
    }

    [Fact]
    public void ProviderFailureFallsBackAndPersistsWhyThePreviousApproachEnded()
    {
        var workflow = Workflow(RecoveryPath.PasswordReset, RecoveryPath.ManualRecovery);
        var state = AccountRecoveryExecutionState.Create(Guid.NewGuid(), workflow, StartedAt)
            .StartAction(workflow, "identify-passwordreset", StartedAt.AddMinutes(1))
            .FailActionAndSelectFallback(
                workflow,
                "identify-passwordreset",
                "The provider rejected the reset request.",
                StartedAt.AddMinutes(2));

        Assert.Equal(RecoveryPath.ManualRecovery, state.SelectedPath);
        Assert.Equal(RecoveryPathSelectionReasonCode.ProviderFailureFallback, state.PathSelectionReason);
        Assert.Equal(["identify-manualrecovery"], state.Actions.Select(action => action.DefinitionId));
        var attempt = Assert.Single(state.PreviousPathAttempts);
        Assert.Equal(RecoveryPath.PasswordReset, attempt.Path);
        Assert.Equal(RecoveryPathTransitionReasonCode.ProviderFailure, attempt.TransitionReason);
        Assert.Equal("identify-passwordreset", attempt.TriggerActionDefinitionId);
        Assert.Equal("The provider rejected the reset request.", attempt.UserReason);
        state.Validate(workflow);
    }

    [Fact]
    public void ProviderFailureWithoutFallbackKeepsFailedWorkVisiblyBlocked()
    {
        var workflow = Workflow(RecoveryPath.PasswordReset);
        var state = AccountRecoveryExecutionState.Create(Guid.NewGuid(), workflow, StartedAt)
            .StartAction(workflow, "identify-passwordreset", StartedAt.AddMinutes(1))
            .FailActionAndSelectFallback(
                workflow,
                "identify-passwordreset",
                "The only supported recovery approach failed.",
                StartedAt.AddMinutes(2));

        Assert.Equal(RecoveryPath.PasswordReset, state.SelectedPath);
        Assert.Equal(RecoveryPathSelectionReasonCode.NoSafeSupportedPath, state.PathSelectionReason);
        Assert.Equal(AccountRecoveryStatus.NotFullySecured, state.RecoveryStatus);
        Assert.Equal(
            RecoveryActionStatus.Failed,
            state.GetAction("identify-passwordreset").Status);
        Assert.Empty(state.PreviousPathAttempts);
        state.Validate(workflow);
    }

    [Fact]
    public void BrowserContextCannotBeAnInputToCanonicalPathSelection()
    {
        var parameterNames = typeof(RecoveryPathSelector)
            .GetMethod(nameof(RecoveryPathSelector.Select))!
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        Assert.DoesNotContain(parameterNames, name =>
            name?.Contains("browser", StringComparison.OrdinalIgnoreCase) == true ||
            name?.Contains("url", StringComparison.OrdinalIgnoreCase) == true ||
            name?.Contains("cookie", StringComparison.OrdinalIgnoreCase) == true ||
            name?.Contains("page", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(
            RecoveryPath.PasswordReset,
            RecoveryPathSelector.Select(Workflow(RecoveryPath.PasswordReset)).Path);
    }

    [Fact]
    public void UnsupportedProviderRemainsRecoverableThroughGenericWorkflow()
    {
        var workflow = GenericManualRecoveryWorkflow.Create("unsupported.synthetic.example");

        var state = AccountRecoveryExecutionState.Create(Guid.NewGuid(), workflow, StartedAt);

        Assert.Equal(RecoveryWorkflowTrustLevel.GeneralManualGuidance, workflow.TrustLevel);
        Assert.Equal(RecoveryPath.PasswordReset, state.SelectedPath);
        Assert.NotEmpty(state.Actions);
    }

    [Fact]
    public void DevelopmentExecutionWithoutAutomaticSelectionReasonFailsClosed()
    {
        var workflow = Workflow(RecoveryPath.PasswordReset);
        var state = AccountRecoveryExecutionState.Create(Guid.NewGuid(), workflow, StartedAt);
        var json = JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();
        json.Remove(nameof(AccountRecoveryExecutionState.PathSelectionReason));
        var incompatible = JsonSerializer.Deserialize<AccountRecoveryExecutionState>(json)!;

        Assert.Throws<InvalidOperationException>(() => incompatible.Validate(workflow));
    }

    private static RecoveryWorkflowDefinition Workflow(params RecoveryPath[] paths) =>
        new(
            "synthetic/automatic-path-selection",
            "synthetic.example",
            "Synthetic provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 13),
            [],
            [.. paths.Select(path => Action($"identify-{path.ToString().ToLowerInvariant()}", path, []))]);

    private static RecoveryActionDefinition Action(
        string id,
        RecoveryPath path,
        string[] prerequisites)
    {
        var prefix = $"Workflow.Synthetic.{id}";
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            RecoveryActionType.IdentifyAccount,
            [path],
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
            AutomationSupport.None,
            prerequisites,
            criteria,
            new RecoveryActionGuidanceKeys(
                $"{prefix}.Title",
                $"{prefix}.Instruction",
                null,
                $"{prefix}.Completion",
                criteria));
    }
}
