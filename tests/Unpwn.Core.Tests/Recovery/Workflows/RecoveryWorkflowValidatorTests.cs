using Unpwn.Core;
using Unpwn.Core.Recovery.Workflows;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.Core.Tests.Recovery.Workflows;

public sealed class RecoveryWorkflowValidatorTests
{
    [Fact]
    public void RepositoryCatalogWorkflowsAreValid()
    {
        WorkflowValidationResult result = RepositoryWorkflowCatalog.ValidateAll(new DateOnly(2026, 8, 5));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void ValidateUsesSuppliedCurrentDateInsteadOfACompiledConstant()
    {
        var workflow = CreateWorkflow(verifiedAt: new DateOnly(2027, 1, 2));

        var beforeVerification = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2027, 1, 1));
        var onVerificationDate = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2027, 1, 2));

        Assert.Contains(beforeVerification.Diagnostics, diagnostic => diagnostic.Rule == "verification-date-in-future");
        Assert.DoesNotContain(onVerificationDate.Diagnostics, diagnostic => diagnostic.Rule == "verification-date-in-future");
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

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2026, 8, 5));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "verification-date-in-future");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "location-url-must-use-https");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "expected-origin-invalid");
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

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2026, 8, 5));

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

        WorkflowValidationResult result = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2026, 8, 5));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "cyclic-prerequisite");
    }

    [Fact]
    public void ValidateRejectsAnActionThatReferencesAnUnknownRecoveryLocation()
    {
        var workflow = RepositoryWorkflowCatalog.Workflows.Single();
        workflow = workflow with
        {
            Actions =
            [
                workflow.Actions[0] with { RecoveryLocationId = "missing-location" },
                .. workflow.Actions.Skip(1),
            ],
        };

        var result = RecoveryWorkflowValidator.Validate(workflow, new DateOnly(2026, 8, 5));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Rule == "missing-action-recovery-location");
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
        AutomationSupport automationSupport = AutomationSupport.Navigation,
        IReadOnlyList<RecoveryPath>? recoveryPaths = null) =>
        new(
            id,
            RecoveryActionType.ChangePassword,
            recoveryPaths ?? [RecoveryPath.AuthenticatedChange],
            RecoveryActionRequirement.Required,
            RecoveryActionImportance.Critical,
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
            RecoveryPath.PasswordReset,
            ["change-password", "missing-action"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["change-password"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["identify-account-auth"]),
                ["missing-action"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, []),
            },
            AccountContractOutcome.CanBeFullySecured);

        ProviderContractValidationResult result = ProviderContractValidator.Validate(workflow, [scenario]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "expected-action-missing");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "action-path-mismatch");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "fully-secured-scenario-incomplete");
    }

    [Fact]
    public void ContractValidationRejectsPrerequisitesMissingFromTheScenarioPath()
    {
        var workflow = new RecoveryWorkflowDefinition(
            "example.test/recovery",
            "example.test",
            "Example",
            "consumer",
            "1.0.0",
            new DateOnly(2026, 8, 5),
            [new RecoveryLocationDefinition("reset", new Uri("https://example.test/reset"), ["https://example.test"])],
            [
                new RecoveryActionDefinition(
                    "auth-only",
                    RecoveryActionType.ConfirmAccess,
                    [RecoveryPath.AuthenticatedChange],
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.None,
                    [],
                    ["Authenticated access is confirmed."]),
                new RecoveryActionDefinition(
                    "reset-action",
                    RecoveryActionType.ResetPassword,
                    [RecoveryPath.PasswordReset],
                    RecoveryActionRequirement.Required,
                    RecoveryActionImportance.Critical,
                    AutomationSupport.Navigation,
                    ["auth-only"],
                    ["The password was reset."])
            ]);
        var scenario = new ProviderContractScenario(
            "impossible-reset",
            workflow.WorkflowId,
            "The reset action depends on an action unavailable on the reset path.",
            RecoveryPath.PasswordReset,
            ["reset-action"],
            new Dictionary<string, ContractActionExpectation>(StringComparer.Ordinal)
            {
                ["reset-action"] = new(RecoveryActionRequirement.Required, RecoveryActionImportance.Critical, ["auth-only"]),
            },
            AccountContractOutcome.BlockedByDependency);

        var result = ProviderContractValidator.Validate(workflow, [scenario]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Rule == "prerequisite-unavailable-for-path");
    }
}
