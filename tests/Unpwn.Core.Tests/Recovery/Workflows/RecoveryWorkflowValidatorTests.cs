using Unpwn.Core.Recovery.Workflows;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class RecoveryWorkflowValidatorTests
{
    [Fact]
    public void RepositoryCatalogWorkflowsAreValid()
    {
        WorkflowValidationResult result = RepositoryWorkflowCatalog.ValidateAll();

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void ValidateRejectsDuplicateActionsMissingPrerequisitesFutureDatesAndUnsafeUrls()
    {
        RecoveryWorkflowDefinition workflow = CreateWorkflow(
            verifiedAt: new DateOnly(2026, 8, 6),
            locations:
            [
                new RecoveryLocationDefinition("reset", new Uri("http://example.test/reset"), ["http://example.test"])
            ],
            actions:
            [
                CreateAction("change-password", prerequisites: ["missing-action"]),
                CreateAction("change-password", prerequisites: [])
            ]);

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "verification-date-in-future");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "location-url-must-use-https");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "duplicate-action-id");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "missing-prerequisite");
    }

    [Fact]
    public void ValidateRejectsRequiredActionsWithoutCompletionCriteriaAndFullyAutomatedClaims()
    {
        RecoveryWorkflowDefinition workflow = CreateWorkflow(
            actions:
            [
                CreateAction(
                    "change-password",
                    automationSupport: AutomationSupport.Automated,
                    completionCriteria: [])
            ]);

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "required-action-completion-criteria");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "automation-support-too-high");
    }

    [Fact]
    public void ValidateRejectsCyclicPrerequisites()
    {
        RecoveryWorkflowDefinition workflow = CreateWorkflow(
            actions:
            [
                CreateAction("first", prerequisites: ["second"]),
                CreateAction("second", prerequisites: ["first"])
            ]);

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "cyclic-prerequisite");
    }

    private static RecoveryWorkflowDefinition CreateWorkflow(
        DateOnly? verifiedAt = null,
        IReadOnlyList<RecoveryLocationDefinition>? locations = null,
        IReadOnlyList<RecoveryActionDefinition>? actions = null) =>
        new(
            "example.test/recovery",
            "example.test",
            "Example",
            "consumer",
            "1.0.0",
            verifiedAt ?? new DateOnly(2026, 8, 5),
            locations ?? [new RecoveryLocationDefinition("reset", new Uri("https://example.test/reset"), ["https://example.test"])],
            actions ?? [CreateAction("change-password")]);

    private static RecoveryActionDefinition CreateAction(
        string id,
        IReadOnlyList<string>? prerequisites = null,
        IReadOnlyList<string>? completionCriteria = null,
        AutomationSupport automationSupport = AutomationSupport.Navigation) =>
        new(
            id,
            RecoveryActionType.ChangePassword,
            RecoveryPath.AuthenticatedChange,
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
            automationSupport,
            prerequisites ?? [],
            completionCriteria ?? ["The password was changed through the official provider flow."]);
}
