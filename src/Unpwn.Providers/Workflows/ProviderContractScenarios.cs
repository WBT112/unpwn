using Unpwn.Core;

namespace Unpwn.Providers.Workflows;

public sealed record ProviderContractScenario(
    string Id,
    string WorkflowId,
    string Description,
    RecoveryPath ExpectedRecoveryPath,
    IReadOnlyList<string> ExpectedActionOrder,
    IReadOnlyDictionary<string, ContractActionExpectation> ActionExpectations,
    AccountContractOutcome ExpectedOutcome);

public sealed record ContractActionExpectation(
    RecoveryActionRequirement Requirement,
    RecoveryActionImportance Importance,
    IReadOnlyList<string> Prerequisites,
    bool IsInitiallyBlocked = false,
    bool CreatesUnresolvedRisk = false);

public enum AccountContractOutcome
{
    CanBeFullySecured,
    BlockedByDependency,
    ManualRecoveryRequired,
    NotFullySecuredWithAcceptedRisk,
    AccessCannotBeRestored,
}

public sealed record ProviderContractValidationDiagnostic(string ScenarioId, string Rule, string Message)
{
    public override string ToString() => $"{ScenarioId}: {Rule}: {Message}";
}

public sealed record ProviderContractValidationResult(IReadOnlyList<ProviderContractValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;

    public static ProviderContractValidationResult Valid { get; } = new([]);
}

public static class ProviderContractValidator
{
    public static ProviderContractValidationResult Validate(
        RecoveryWorkflowDefinition workflow,
        IReadOnlyList<ProviderContractScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(scenarios);

        List<ProviderContractValidationDiagnostic> diagnostics = [];
        Dictionary<string, RecoveryActionDefinition> actions = workflow.Actions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> scenarioIds = new(StringComparer.Ordinal);

        foreach (ProviderContractScenario scenario in scenarios)
        {
            if (!scenarioIds.Add(scenario.Id))
            {
                diagnostics.Add(new(scenario.Id, "duplicate-scenario-id", "Scenario identifiers must be unique within a workflow."));
            }

            ValidateScenario(workflow, scenario, actions, diagnostics);
        }

        return diagnostics.Count == 0 ? ProviderContractValidationResult.Valid : new ProviderContractValidationResult(diagnostics);
    }

    private static void ValidateScenario(
        RecoveryWorkflowDefinition workflow,
        ProviderContractScenario scenario,
        IReadOnlyDictionary<string, RecoveryActionDefinition> actions,
        List<ProviderContractValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(workflow.WorkflowId, scenario.WorkflowId, StringComparison.Ordinal))
        {
            diagnostics.Add(new(scenario.Id, "workflow-id-mismatch", "Scenario must reference the workflow being validated."));
        }

        if (scenario.ExpectedActionOrder.Count == 0)
        {
            diagnostics.Add(new(scenario.Id, "expected-action-order-required", "Scenario must define at least one expected action."));
            return;
        }

        if (scenario.ExpectedActionOrder.Distinct(StringComparer.Ordinal).Count() != scenario.ExpectedActionOrder.Count)
        {
            diagnostics.Add(new(scenario.Id, "duplicate-expected-action", "Expected action order must not contain duplicate actions."));
        }

        HashSet<string> orderedActionIds = new(StringComparer.Ordinal);
        foreach (string actionId in scenario.ExpectedActionOrder)
        {
            if (!actions.TryGetValue(actionId, out RecoveryActionDefinition? action))
            {
                diagnostics.Add(new(scenario.Id, "expected-action-missing", $"Expected action '{actionId}' is not defined by the workflow."));
                continue;
            }

            if (!action.SupportsPath(scenario.ExpectedRecoveryPath))
            {
                diagnostics.Add(new(scenario.Id, "action-path-mismatch", $"Expected action '{actionId}' does not support recovery path '{scenario.ExpectedRecoveryPath}'."));
            }

            foreach (string prerequisite in action.Prerequisites)
            {
                if (!actions.TryGetValue(prerequisite, out RecoveryActionDefinition? prerequisiteAction))
                {
                    continue;
                }

                if (!prerequisiteAction.SupportsPath(scenario.ExpectedRecoveryPath))
                {
                    diagnostics.Add(new(scenario.Id, "prerequisite-unavailable-for-path", $"Action '{actionId}' depends on '{prerequisite}', which is unavailable on recovery path '{scenario.ExpectedRecoveryPath}'."));
                }
                else if (!orderedActionIds.Contains(prerequisite))
                {
                    diagnostics.Add(new(scenario.Id, "action-order-violates-prerequisite", $"Expected action '{actionId}' appears before or without prerequisite '{prerequisite}'."));
                }
            }

            orderedActionIds.Add(actionId);
        }

        foreach (string expectedActionId in scenario.ExpectedActionOrder)
        {
            if (!scenario.ActionExpectations.ContainsKey(expectedActionId))
            {
                diagnostics.Add(new(scenario.Id, "action-expectation-missing", $"Expected action '{expectedActionId}' has no contract expectation."));
            }
        }

        foreach ((string actionId, ContractActionExpectation expectation) in scenario.ActionExpectations)
        {
            if (!scenario.ExpectedActionOrder.Contains(actionId, StringComparer.Ordinal))
            {
                diagnostics.Add(new(scenario.Id, "unexpected-action-expectation", $"Action expectation '{actionId}' is not present in ExpectedActionOrder."));
            }

            if (!actions.TryGetValue(actionId, out RecoveryActionDefinition? action))
            {
                diagnostics.Add(new(scenario.Id, "expected-action-missing", $"Expected action '{actionId}' is not defined by the workflow."));
                continue;
            }

            if (action.Requirement != expectation.Requirement)
            {
                diagnostics.Add(new(scenario.Id, "requirement-mismatch", $"Action '{actionId}' has requirement '{action.Requirement}' instead of '{expectation.Requirement}'."));
            }

            if (action.Importance != expectation.Importance)
            {
                diagnostics.Add(new(scenario.Id, "importance-mismatch", $"Action '{actionId}' has importance '{action.Importance}' instead of '{expectation.Importance}'."));
            }

            if (!action.Prerequisites.SequenceEqual(expectation.Prerequisites, StringComparer.Ordinal))
            {
                diagnostics.Add(new(scenario.Id, "prerequisites-mismatch", $"Action '{actionId}' prerequisites do not match the contract scenario."));
            }
        }

        if (scenario.ExpectedOutcome == AccountContractOutcome.CanBeFullySecured)
        {
            if (scenario.ActionExpectations.Values.Any(expectation => expectation.IsInitiallyBlocked || expectation.CreatesUnresolvedRisk))
            {
                diagnostics.Add(new(scenario.Id, "fully-secured-scenario-has-blockers", "A fully secured scenario cannot start with blocked actions or accepted unresolved risks."));
            }

            var missingRequiredActions = workflow.Actions
                .Where(action => action.IsRequired && action.SupportsPath(scenario.ExpectedRecoveryPath))
                .Select(action => action.Id)
                .Where(actionId => !scenario.ExpectedActionOrder.Contains(actionId, StringComparer.Ordinal))
                .ToArray();
            if (missingRequiredActions.Length > 0)
            {
                diagnostics.Add(new(
                    scenario.Id,
                    "fully-secured-scenario-incomplete",
                    $"A fully secured scenario omits required actions: {string.Join(", ", missingRequiredActions)}."));
            }
        }
    }
}
