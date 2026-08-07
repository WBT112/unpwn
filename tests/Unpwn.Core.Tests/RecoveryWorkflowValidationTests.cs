using Unpwn.Core;
using Unpwn.Core.Recovery.Workflows;
using Xunit;

namespace Unpwn.Core.Tests;

public sealed class RecoveryWorkflowValidationTests
{
    private static readonly DateOnly Today = new(2026, 8, 5);

    [Fact]
    public void ValidWorkflowPassesValidation()
    {
        var result = RecoveryWorkflowValidator.Validate(CreateValidWorkflow(), Today);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void InsecureRecoveryLocationFailsValidation()
    {
        var workflow = CreateValidWorkflow() with
        {
            RecoveryLocations =
            [
                new RecoveryLocationDefinition(
                    "settings",
                    new Uri("http://example.test/settings/security"),
                    ["http://example.test"]),
            ],
        };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "location-url-must-use-https");
    }

    [Fact]
    public void MissingCompletionCriteriaFailsValidation()
    {
        var workflow = CreateValidWorkflow();
        var invalidAction = workflow.Actions[0] with
        {
            CompletionCriteria = [],
            Guidance = workflow.Actions[0].Guidance with { CompletionCriteriaKeys = [] },
        };
        workflow = workflow with { Actions = [invalidAction] };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "required-action-completion-criteria");
    }

    [Fact]
    public void InlineCompletionSentenceFailsValidation()
    {
        var workflow = CreateValidWorkflow();
        var invalidAction = workflow.Actions[0] with
        {
            CompletionCriteria = ["The password has changed."],
            Guidance = workflow.Actions[0].Guidance with
            {
                CompletionCriteriaKeys = ["The password has changed."],
            },
        };
        workflow = workflow with { Actions = [invalidAction] };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "completion-criterion-resource-key-required");
    }

    [Fact]
    public void MissingPrerequisiteFailsValidation()
    {
        var workflow = CreateValidWorkflow();
        var invalidAction = workflow.Actions[0] with
        {
            Prerequisites = ["missing-action"],
        };
        workflow = workflow with { Actions = [invalidAction] };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "missing-prerequisite");
    }

    [Fact]
    public void CyclicActionsFailValidation()
    {
        var workflow = CreateValidWorkflow();
        var actionA = Action("a", prerequisites: ["b"]);
        var actionB = Action("b", prerequisites: ["a"]);
        workflow = workflow with { Actions = [actionA, actionB] };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "cyclic-prerequisite");
    }

    [Fact]
    public void AutomatedSupportFailsValidation()
    {
        var workflow = CreateValidWorkflow();
        workflow = workflow with
        {
            Actions =
            [
                workflow.Actions[0] with
                {
                    AutomationSupport = AutomationSupport.Automated,
                },
            ],
        };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "automation-support-too-high");
    }

    [Fact]
    public void LocationOriginMustBeDeclared()
    {
        var workflow = CreateValidWorkflow() with
        {
            RecoveryLocations =
            [
                new RecoveryLocationDefinition(
                    "settings",
                    new Uri("https://accounts.example.test/settings"),
                    ["https://example.test"]),
            ],
        };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "location-origin-mismatch");
    }

    [Fact]
    public void FutureVerificationDateFailsValidation()
    {
        var workflow = CreateValidWorkflow() with
        {
            VerifiedAt = Today.AddDays(1),
        };

        var result = RecoveryWorkflowValidator.Validate(workflow, Today);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Rule == "verification-date-in-future");
    }

    private static RecoveryWorkflowDefinition CreateValidWorkflow() =>
        new(
            "example.test/consumer-recovery",
            "example.test",
            "Example",
            "consumer",
            "1.0.0",
            Today,
            [
                new RecoveryLocationDefinition(
                    "settings",
                    new Uri("https://example.test/settings/security"),
                    ["https://example.test"]),
            ],
            [Action("identify-account")]);

    private static RecoveryActionDefinition Action(
        string id,
        IReadOnlyList<string>? prerequisites = null)
    {
        var prefix = $"Workflow.Test.Action.{id}";
        var criteria = new[] { $"{prefix}.Criterion.1" };
        return new RecoveryActionDefinition(
            id,
            RecoveryActionType.IdentifyAccount,
            [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
            AutomationSupport.None,
            prerequisites ?? [],
            criteria,
            new RecoveryActionGuidanceKeys(
                $"{prefix}.Title",
                $"{prefix}.Instruction",
                $"{prefix}.Warning",
                $"{prefix}.Completion",
                criteria));
    }
}
