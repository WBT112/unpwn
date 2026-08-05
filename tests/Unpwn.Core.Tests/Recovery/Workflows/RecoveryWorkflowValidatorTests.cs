using Unpwn.Core.Recovery.Workflows;
using Unpwn.Providers.Workflows;
using Xunit;
using WorkflowTypes = global::Unpwn.Core.Recovery.Workflows;

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
        WorkflowTypes.RecoveryWorkflowDefinition workflow = CreateWorkflow(
            verifiedAt: new DateOnly(2026, 8, 6),
            locations:
            [
                new WorkflowTypes.RecoveryLocationDefinition("reset", new Uri("http://example.test/reset"), ["http://example.test"])
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
        WorkflowTypes.RecoveryWorkflowDefinition workflow = CreateWorkflow(
            actions:
            [
                CreateAction(
                    "change-password",
                    automationSupport: WorkflowTypes.AutomationSupport.Automated,
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
        WorkflowTypes.RecoveryWorkflowDefinition workflow = CreateWorkflow(
            actions:
            [
                CreateAction("first", prerequisites: ["second"]),
                CreateAction("second", prerequisites: ["first"])
            ]);

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "cyclic-prerequisite");
    }

    private static WorkflowTypes.RecoveryWorkflowDefinition CreateWorkflow(
        DateOnly? verifiedAt = null,
        IReadOnlyList<WorkflowTypes.RecoveryLocationDefinition>? locations = null,
        IReadOnlyList<WorkflowTypes.RecoveryActionDefinition>? actions = null) =>
        new(
            "example.test/recovery",
            "example.test",
            "Example",
            "consumer",
            "1.0.0",
            verifiedAt ?? new DateOnly(2026, 8, 5),
            locations ?? [new WorkflowTypes.RecoveryLocationDefinition("reset", new Uri("https://example.test/reset"), ["https://example.test"])],
            actions ?? [CreateAction("change-password")]);

    private static WorkflowTypes.RecoveryActionDefinition CreateAction(
        string id,
        IReadOnlyList<string>? prerequisites = null,
        IReadOnlyList<string>? completionCriteria = null,
        WorkflowTypes.AutomationSupport automationSupport = WorkflowTypes.AutomationSupport.Navigation) =>
        new(
            id,
            WorkflowTypes.RecoveryActionType.ChangePassword,
            WorkflowTypes.RecoveryPath.AuthenticatedChange,
            WorkflowTypes.RecoveryActionRequirement.Required,
            WorkflowTypes.RecoveryActionImportance.Critical,
            automationSupport,
            prerequisites ?? [],
            completionCriteria ?? ["The password was changed through the official provider flow."]);
}

public sealed class ProviderContractValidatorTests
{
    [Fact]
    public void RepositoryCatalogContractScenariosAreValid()
    {
        ProviderContractValidationResult result = RepositoryWorkflowCatalog.ValidateContractScenarios();

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void ContractValidationRejectsUnavailableExpectedPathsAndMissingActions()
    {
        var workflow = RepositoryWorkflowCatalog.Workflows.Single();
        var scenario = new ProviderContractScenario(
            "bad-contract",
            workflow.WorkflowId,
            "Invalid scenario for regression coverage.",
            WorkflowTypes.RecoveryPath.PasswordReset,
            ["missing-action"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["missing-action"] = new(WorkflowTypes.RecoveryActionRequirement.Required, WorkflowTypes.RecoveryActionImportance.Critical, []),
            },
            AccountContractOutcome.CanBeFullySecured);

        ProviderContractValidationResult result = ProviderContractValidator.Validate(workflow, [scenario]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "expected-action-missing");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "expected-path-unavailable");
    }
}
