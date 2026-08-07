using System.Text.Json;
using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountRecoveryExecutionTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 6, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PrerequisiteFailureUsesStructuredIdentifiersInsteadOfEnglishReason()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);

        state = state.StartAction(
            workflow,
            "change-password",
            StartedAt.AddMinutes(1));

        var action = state.GetAction("change-password");
        Assert.Equal(RecoveryActionStatus.Blocked, action.Status);
        Assert.Equal(RecoveryActionReasonCode.WaitingForPrerequisite, action.ReasonCode);
        Assert.Equal(["identify-account"], action.ReasonArguments);
        Assert.Null(action.UserReason);
        Assert.DoesNotContain(
            "Waiting for prerequisite actions",
            JsonSerializer.Serialize(action),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredActionsFollowExplicitCompletionCriteriaAcknowledgement()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);
        state = state.StartAction(workflow, "identify-account", StartedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => state.CompleteAction(
            workflow,
            "identify-account",
            completionCriteriaAcknowledged: false,
            StartedAt.AddMinutes(2)));

        state = state.CompleteAction(
            workflow,
            "identify-account",
            completionCriteriaAcknowledged: true,
            StartedAt.AddMinutes(2));
        state = state.StartAction(workflow, "change-password", StartedAt.AddMinutes(3));
        state = state.CompleteAction(
            workflow,
            "change-password",
            completionCriteriaAcknowledged: true,
            StartedAt.AddMinutes(4));

        Assert.Equal(AccountRecoveryStatus.FullyReviewed, state.RecoveryStatus);
        state.Validate(workflow);
    }

    [Fact]
    public void UserNotesRemainUserContentAndDoNotControlTransitions()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);
        const string note = "Contacted provider; case 123. No secrets included.";

        state = state.SetUserNotes("identify-account", note, StartedAt.AddMinutes(1));

        Assert.Equal(note, state.GetAction("identify-account").UserNotes);
        Assert.Equal(RecoveryActionStatus.Open, state.GetAction("identify-account").Status);
        Assert.Equal(RecoveryActionReasonCode.None, state.GetAction("identify-account").ReasonCode);
    }

    [Fact]
    public void GeneratedCredentialIsReferencedWithoutSecretValue()
    {
        const string syntheticSecret = "synthetic-generated-secret-value";
        var workflow = CreateWorkflow();
        var accountId = Guid.NewGuid();
        var state = AccountRecoveryExecutionState.Create(
            accountId,
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);
        var reference = new GeneratedCredentialReference(Guid.NewGuid(), accountId);

        state = state.AttachCredentialReference(
            "change-password",
            reference,
            StartedAt.AddMinutes(1));

        Assert.Equal(reference, state.GetAction("change-password").CredentialReference);
        var serialized = JsonSerializer.Serialize(state);
        Assert.Contains(reference.CredentialId.ToString("D"), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(syntheticSecret, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedStateReloadsWithStableWorkflowIdentity()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt)
            .SetUserNotes(
                "identify-account",
                "Non-secret note",
                StartedAt.AddMinutes(1));
        var json = JsonSerializer.SerializeToUtf8Bytes(state);

        var reloaded = JsonSerializer.Deserialize<AccountRecoveryExecutionState>(json);

        Assert.NotNull(reloaded);
        reloaded.Validate(workflow);
        Assert.Equal(state.AccountId, reloaded.AccountId);
        Assert.Equal(state.WorkflowId, reloaded.WorkflowId);
        Assert.Equal(state.WorkflowVersion, reloaded.WorkflowVersion);
        Assert.Equal(state.SelectedPath, reloaded.SelectedPath);
        Assert.Equal(state.Revision, reloaded.Revision);
        Assert.Equal(state.Actions.Length, reloaded.Actions.Length);
        for (var index = 0; index < state.Actions.Length; index++)
        {
            Assert.Equal(state.Actions[index].DefinitionId, reloaded.Actions[index].DefinitionId);
            Assert.Equal(state.Actions[index].IsRequired, reloaded.Actions[index].IsRequired);
            Assert.Equal(state.Actions[index].Importance, reloaded.Actions[index].Importance);
            Assert.Equal(state.Actions[index].Status, reloaded.Actions[index].Status);
            Assert.Equal(state.Actions[index].ReasonCode, reloaded.Actions[index].ReasonCode);
            Assert.Equal(state.Actions[index].ReasonArguments, reloaded.Actions[index].ReasonArguments);
            Assert.Equal(state.Actions[index].UserNotes, reloaded.Actions[index].UserNotes);
        }
    }

    [Fact]
    public void LostAccessRemainsSeparateFromProgressAndUnresolvedRisk()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt)
            .SetAccessState(
                RecoveryAccessState.Lost,
                "Provider denied access",
                StartedAt.AddMinutes(1));

        Assert.Equal(AccountRecoveryStatus.AccessNotRestored, state.RecoveryStatus);
        Assert.Equal(RecoveryAccessState.Lost, state.AccessState);
        Assert.Equal("Provider denied access", state.AccessReason);
        Assert.DoesNotContain(state.Actions, action => action.HasUnresolvedRisk);
        state.Validate(workflow);
    }

    [Fact]
    public void LostAccessDuringActiveActionRemainsValidAndResumable()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt)
            .StartAction(workflow, "identify-account", StartedAt.AddMinutes(1))
            .SetAccessState(
                RecoveryAccessState.Lost,
                "Provider denied access",
                StartedAt.AddMinutes(2));

        Assert.Equal(AccountRecoveryStatus.AccessNotRestored, state.RecoveryStatus);
        var action = state.GetAction("identify-account");
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
        Assert.Equal(RecoveryActionReasonCode.AccessLost, action.ReasonCode);
        Assert.False(action.HasUnresolvedRisk);
        state.Validate(workflow);
    }

    [Fact]
    public void DashboardProjectionUsesRequiredActionWeights()
    {
        var workflow = CreateWorkflow();
        var state = AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt)
            .StartAction(workflow, "identify-account", StartedAt.AddMinutes(1))
            .CompleteAction(
                workflow,
                "identify-account",
                completionCriteriaAcknowledged: true,
                StartedAt.AddMinutes(2));

        var projection = state.CreateDashboardProjection(
            AccountCriticality.Critical,
            dependencyDepth: 0,
            waitingForAccountIds: []);
        var (
            _,
            _,
            _,
            _,
            completedRequiredActions,
            totalRequiredActions,
            completedActionWeight,
            totalActionWeight,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _) = projection;

        Assert.Equal(1, completedRequiredActions);
        Assert.Equal(2, totalRequiredActions);
        Assert.Equal((int)RecoveryActionImportance.Important, completedActionWeight);
        Assert.Equal(
            (int)RecoveryActionImportance.Important + (int)RecoveryActionImportance.Critical,
            totalActionWeight);
    }

    private static RecoveryWorkflowDefinition CreateWorkflow()
    {
        var firstPrefix = "Workflow.Test.Action.identify-account";
        var secondPrefix = "Workflow.Test.Action.change-password";
        return new RecoveryWorkflowDefinition(
            "test/recovery",
            "test.example",
            "Test Provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 6),
            [
                new RecoveryLocationDefinition(
                    "settings",
                    new Uri("https://test.example/security"),
                    ["https://test.example"]),
            ],
            [
                Required(
                    "identify-account",
                    RecoveryActionType.IdentifyAccount,
                    [],
                    firstPrefix,
                    RecoveryActionImportance.Important),
                Required(
                    "change-password",
                    RecoveryActionType.ChangePassword,
                    ["identify-account"],
                    secondPrefix,
                    RecoveryActionImportance.Critical),
            ]);
    }

    private static RecoveryActionDefinition Required(
        string id,
        RecoveryActionType type,
        string[] prerequisites,
        string prefix,
        RecoveryActionImportance importance)
    {
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            type,
            [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            importance,
            AutomationSupport.None,
            prerequisites,
            criteria,
            new RecoveryActionGuidanceKeys(
                $"{prefix}.Title",
                $"{prefix}.Instruction",
                $"{prefix}.Warning",
                $"{prefix}.Completion",
                criteria));
    }
}
