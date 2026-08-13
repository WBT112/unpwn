using Unpwn.Core;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class AccountRecoveryExecutionCoverageTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PersistedExecutionRejectsEveryCorruptedIdentityAndEnvelopeVariant()
    {
        var workflow = CreateWorkflow();
        var valid = CreateState(workflow);

        AssertInvalid(valid with { AccountId = Guid.Empty }, workflow);
        AssertInvalid(valid with { Revision = -1 }, workflow);
        AssertInvalid(valid with { UpdatedAt = StartedAt.AddTicks(-1) }, workflow);
        Assert.Throws<ArgumentException>(() => (valid with { ProviderId = "" }).Validate(workflow));
        Assert.Throws<ArgumentException>(() => (valid with { WorkflowId = " " }).Validate(workflow));
        Assert.Throws<ArgumentException>(() => (valid with { WorkflowVersion = "" }).Validate(workflow));
        AssertInvalid(valid with { ProviderId = "different.example" }, workflow);
        AssertInvalid(valid with { WorkflowId = "different/workflow" }, workflow);
        AssertInvalid(valid with { WorkflowVersion = "9.9.9" }, workflow);
        AssertInvalid(valid with { Actions = null! }, workflow);
        AssertInvalid(valid with { Actions = [] }, workflow);
        AssertInvalid(valid with { Actions = [valid.Actions[0], valid.Actions[0]] }, workflow);
        AssertInvalid(
            valid with
            {
                Actions =
                [
                    valid.Actions[0] with { DefinitionId = "outside-selected-path" },
                    valid.Actions[1],
                ],
            },
            workflow);
    }

    [Fact]
    public void PersistedExecutionRejectsEveryAccessReasonMismatch()
    {
        var workflow = CreateWorkflow();
        var valid = CreateState(workflow);

        AssertInvalid(valid with { AccessReason = new string('x', 1001) }, workflow);
        AssertInvalid(valid with { AccessState = RecoveryAccessState.Lost, AccessReason = null }, workflow);
        AssertInvalid(
            valid with { AccessState = RecoveryAccessState.WaitingForProviderReview, AccessReason = " " },
            workflow);
        AssertInvalid(valid with { AccessState = RecoveryAccessState.Available, AccessReason = "unexpected" }, workflow);
        AssertInvalid(valid with { AccessState = RecoveryAccessState.Unknown, AccessReason = "unexpected" }, workflow);
    }

    [Fact]
    public void PersistedActionRejectsEveryCorruptedLifecycleVariant()
    {
        var workflow = CreateWorkflow();
        var definition = workflow.Actions[0];
        var accountId = Guid.NewGuid();
        var valid = RecoveryActionExecutionState.Create(definition);

        AssertActionInvalid(valid with { DefinitionId = "different" }, definition, accountId);
        AssertActionInvalid(valid with { IsRequired = false }, definition, accountId);
        AssertActionInvalid(valid with { Importance = RecoveryActionImportance.Routine }, definition, accountId);
        AssertActionInvalid(valid with { UserReason = new string('x', 1001) }, definition, accountId);
        AssertActionInvalid(valid with { UserNotes = new string('x', 4001) }, definition, accountId);
        AssertActionInvalid(valid with { StartedAt = StartedAt.AddTicks(-1) }, definition, accountId);
        AssertActionInvalid(valid with { CompletedAt = StartedAt.AddTicks(-1) }, definition, accountId);
        AssertActionInvalid(valid with { UpdatedAt = StartedAt.AddTicks(-1) }, definition, accountId);
        AssertActionInvalid(valid with { CompletedAt = StartedAt }, definition, accountId);
        AssertActionInvalid(
            valid with { StartedAt = StartedAt.AddMinutes(2), CompletedAt = StartedAt.AddMinutes(1) },
            definition,
            accountId);
        AssertActionInvalid(
            valid with { StartedAt = StartedAt.AddMinutes(2), UpdatedAt = StartedAt.AddMinutes(1) },
            definition,
            accountId);
        AssertActionInvalid(
            valid with
            {
                StartedAt = StartedAt,
                CompletedAt = StartedAt.AddMinutes(2),
                UpdatedAt = StartedAt.AddMinutes(1),
            },
            definition,
            accountId);
    }

    [Theory]
    [InlineData(RecoveryActionStatus.Blocked)]
    [InlineData(RecoveryActionStatus.Failed)]
    [InlineData(RecoveryActionStatus.NotApplicable)]
    [InlineData(RecoveryActionStatus.NeedsUserAction)]
    public void ReasonBearingStatusesRejectMissingStructuredReason(RecoveryActionStatus status)
    {
        var workflow = CreateWorkflow();
        var definition = workflow.Actions[0];
        var action = RecoveryActionExecutionState.Create(definition) with { Status = status };

        AssertActionInvalid(action, definition, Guid.NewGuid());
    }

    [Fact]
    public void PersistedActionRejectsEveryStructuredReasonAndDispositionMismatch()
    {
        var definition = CreateWorkflow().Actions[0];
        var accountId = Guid.NewGuid();
        var valid = RecoveryActionExecutionState.Create(definition);

        AssertActionInvalid(valid with { ReasonCode = RecoveryActionReasonCode.ProviderFailure }, definition, accountId);
        AssertActionInvalid(
            valid with
            {
                Status = RecoveryActionStatus.Blocked,
                ReasonCode = RecoveryActionReasonCode.WaitingForPrerequisite,
            },
            definition,
            accountId);
        AssertActionInvalid(
            valid with
            {
                Status = RecoveryActionStatus.Failed,
                ReasonCode = RecoveryActionReasonCode.ProviderFailure,
                ReasonArguments = ["unexpected"],
            },
            definition,
            accountId);
        AssertActionInvalid(
            valid with
            {
                Status = RecoveryActionStatus.NotApplicable,
                ReasonCode = RecoveryActionReasonCode.TrulyNotApplicable,
            },
            definition,
            accountId);
        AssertActionInvalid(
            valid with { NotApplicableDisposition = NotApplicableDisposition.TrulyNotApplicable },
            definition,
            accountId);
        AssertActionInvalid(
            valid with
            {
                IsRequired = false,
                Status = RecoveryActionStatus.Failed,
                HasUnresolvedRisk = true,
                ReasonCode = RecoveryActionReasonCode.UnresolvedRiskAccepted,
            },
            definition with { Requirement = RecoveryActionRequirement.Optional },
            accountId);
        AssertActionInvalid(
            valid with { HasUnresolvedRisk = true },
            definition,
            accountId);
    }

    [Fact]
    public void PersistedActionRejectsInvalidAndCrossAccountCredentialReferences()
    {
        var definition = CreateWorkflow().Actions[0];
        var accountId = Guid.NewGuid();
        var valid = RecoveryActionExecutionState.Create(definition);

        AssertActionInvalid(
            valid with { CredentialReference = new GeneratedCredentialReference(Guid.Empty, accountId) },
            definition,
            accountId);
        AssertActionInvalid(
            valid with { CredentialReference = new GeneratedCredentialReference(Guid.NewGuid(), Guid.NewGuid()) },
            definition,
            accountId);
    }

    [Theory]
    [MemberData(nameof(ActionTransitions))]
    public void EveryActionTransitionIsExplicitlyAllowedOrRejected(
        RecoveryActionStatus current,
        RecoveryActionStatus next,
        bool expectedAllowed)
    {
        var workflow = CreateWorkflow();
        var state = CreateState(workflow);
        state = state with
        {
            Actions =
            [
                state.Actions[0] with
                {
                    Status = current,
                    StartedAt = current == RecoveryActionStatus.Open ? null : StartedAt,
                    UpdatedAt = current == RecoveryActionStatus.Open ? null : StartedAt,
                    ReasonCode = RecoveryActionReasonCode.None,
                    ReasonArguments = [],
                    UserReason = null,
                    HasUnresolvedRisk = false,
                    NotApplicableDisposition = null,
                },
                state.Actions[1],
            ],
        };

        void Transition() => _ = next switch
        {
            RecoveryActionStatus.InProgress => state.StartAction(workflow, "identify-account", StartedAt.AddMinutes(1)),
            RecoveryActionStatus.Completed => state.CompleteAction(
                workflow,
                "identify-account",
                completionCriteriaAcknowledged: true,
                StartedAt.AddMinutes(1)),
            RecoveryActionStatus.Blocked => state.BlockAction(
                workflow,
                "identify-account",
                "Synthetic blocker",
                StartedAt.AddMinutes(1)),
            RecoveryActionStatus.NeedsUserAction => state.RequireUserAction(
                workflow,
                "identify-account",
                "Synthetic user action",
                StartedAt.AddMinutes(1)),
            RecoveryActionStatus.Failed => state.FailAction(
                workflow,
                "identify-account",
                "Synthetic provider failure",
                StartedAt.AddMinutes(1)),
            RecoveryActionStatus.NotApplicable => state.MarkNotApplicable(
                workflow,
                "identify-account",
                "Synthetic inapplicability",
                NotApplicableDisposition.TrulyNotApplicable,
                StartedAt.AddMinutes(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(next)),
        };

        if (expectedAllowed)
        {
            Transition();
        }
        else
        {
            Assert.Throws<InvalidOperationException>(Transition);
        }
    }

    [Fact]
    public void AccessStatesAndDashboardProjectionCoverEveryVisibleStatus()
    {
        var workflow = CreateWorkflow();
        var initial = CreateState(workflow);

        Assert.Equal(AccountRecoveryStatus.Open, initial.RecoveryStatus);
        Assert.Equal(
            AccountRecoveryStatus.InProgress,
            initial.StartAction(workflow, "identify-account", StartedAt.AddMinutes(1)).RecoveryStatus);
        Assert.Equal(
            AccountRecoveryStatus.InProgress,
            initial.RequireUserAction(
                workflow,
                "identify-account",
                "Synthetic user action",
                StartedAt.AddMinutes(1)).RecoveryStatus);
        Assert.Equal(
            AccountRecoveryStatus.NotFullySecured,
            initial.BlockAction(
                workflow,
                "identify-account",
                "Synthetic blocker",
                StartedAt.AddMinutes(1)).RecoveryStatus);
        Assert.Equal(
            RecoveryAccessState.Available,
            initial.SetAccessState(RecoveryAccessState.Available, "ignored", StartedAt.AddMinutes(1)).AccessState);
        Assert.Equal(
            RecoveryAccessState.WaitingForProviderReview,
            initial.SetAccessState(
                RecoveryAccessState.WaitingForProviderReview,
                " Synthetic provider review ",
                StartedAt.AddMinutes(1)).AccessState);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            initial.SetAccessState((RecoveryAccessState)int.MaxValue, null, StartedAt.AddMinutes(1)));

        var projected = initial with
        {
            Actions =
            [
                initial.Actions[0] with { Status = RecoveryActionStatus.Failed },
                initial.Actions[1] with { Status = RecoveryActionStatus.NeedsUserAction },
            ],
        };
        var dashboard = projected.CreateDashboardProjection(AccountCriticality.Critical);

        Assert.Equal("identify-account", dashboard.RecommendedActionId);
        Assert.Equal(1, dashboard.FailedRequiredActions);
    }

    public static TheoryData<RecoveryActionStatus, RecoveryActionStatus, bool> ActionTransitions
    {
        get
        {
            var data = new TheoryData<RecoveryActionStatus, RecoveryActionStatus, bool>();
            var targets = new[]
            {
                RecoveryActionStatus.InProgress,
                RecoveryActionStatus.Completed,
                RecoveryActionStatus.Blocked,
                RecoveryActionStatus.NeedsUserAction,
                RecoveryActionStatus.Failed,
                RecoveryActionStatus.NotApplicable,
            };
            foreach (var current in Enum.GetValues<RecoveryActionStatus>())
            {
                foreach (var next in targets)
                {
                    data.Add(current, next, IsAllowed(current, next));
                }
            }

            return data;
        }
    }

    private static bool IsAllowed(RecoveryActionStatus current, RecoveryActionStatus next) => current switch
    {
        RecoveryActionStatus.Open => next is RecoveryActionStatus.InProgress or
            RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction or
            RecoveryActionStatus.NotApplicable,
        RecoveryActionStatus.InProgress => next is RecoveryActionStatus.Completed or
            RecoveryActionStatus.Blocked or RecoveryActionStatus.NeedsUserAction or
            RecoveryActionStatus.Failed or RecoveryActionStatus.NotApplicable,
        RecoveryActionStatus.Blocked => next is RecoveryActionStatus.InProgress or
            RecoveryActionStatus.NeedsUserAction or RecoveryActionStatus.Failed or
            RecoveryActionStatus.NotApplicable,
        RecoveryActionStatus.NeedsUserAction => next is RecoveryActionStatus.InProgress or
            RecoveryActionStatus.Blocked or RecoveryActionStatus.Failed or
            RecoveryActionStatus.NotApplicable,
        RecoveryActionStatus.Failed => next is RecoveryActionStatus.InProgress or
            RecoveryActionStatus.Blocked or RecoveryActionStatus.NotApplicable,
        _ => false,
    };

    private static void AssertInvalid(
        AccountRecoveryExecutionState state,
        RecoveryWorkflowDefinition workflow)
    {
        var exception = Record.Exception(() => state.Validate(workflow));
        Assert.True(
            exception is ArgumentException or InvalidOperationException,
            $"Expected a safe validation exception, but received {exception?.GetType().Name ?? "none"}.");
    }

    private static void AssertActionInvalid(
        RecoveryActionExecutionState action,
        RecoveryActionDefinition definition,
        Guid accountId)
    {
        var exception = Record.Exception(() => action.Validate(definition, accountId, StartedAt));
        Assert.True(
            exception is ArgumentException or InvalidOperationException,
            $"Expected a safe validation exception, but received {exception?.GetType().Name ?? "none"}.");
    }

    private static AccountRecoveryExecutionState CreateState(RecoveryWorkflowDefinition workflow) =>
        AccountRecoveryExecutionState.Create(
            Guid.NewGuid(),
            workflow,
            RecoveryPath.AuthenticatedChange,
            StartedAt);

    private static RecoveryWorkflowDefinition CreateWorkflow()
    {
        const string firstPrefix = "Workflow.Coverage.Action.identify-account";
        const string secondPrefix = "Workflow.Coverage.Action.change-password";
        return new RecoveryWorkflowDefinition(
            "coverage/recovery",
            "coverage.example",
            "Coverage Provider",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 11),
            [new RecoveryLocationDefinition("settings", new Uri("https://coverage.example/security"), ["https://coverage.example"])],
            [
                Required("identify-account", RecoveryActionType.IdentifyAccount, [], firstPrefix),
                Required("change-password", RecoveryActionType.ChangePassword, ["identify-account"], secondPrefix),
            ]);
    }

    private static RecoveryActionDefinition Required(
        string id,
        RecoveryActionType type,
        string[] prerequisites,
        string prefix)
    {
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            type,
            [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
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
