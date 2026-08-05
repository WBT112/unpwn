using Unpwn.Core.Recovery.Workflows;

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
    AccessCannotBeRestored
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
        Dictionary<string, RecoveryActionDefinition> actions = workflow.Actions.ToDictionary(action => action.Id, StringComparer.Ordinal);

        foreach (ProviderContractScenario scenario in scenarios)
        {
            ValidateScenario(workflow, scenario, actions, diagnostics);
        }

        return diagnostics.Count == 0 ? ProviderContractValidationResult.Valid : new ProviderContractValidationResult(diagnostics);
    }

    private static void ValidateScenario(
        RecoveryWorkflowDefinition workflow,
        ProviderContractScenario scenario,
        Dictionary<string, RecoveryActionDefinition> actions,
        List<ProviderContractValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(workflow.WorkflowId, scenario.WorkflowId, StringComparison.Ordinal))
        {
            diagnostics.Add(new(scenario.Id, "workflow-id-mismatch", "Scenario must reference the workflow being validated."));
        }

        if (!workflow.Actions.Any(action => action.RecoveryPath == scenario.ExpectedRecoveryPath))
        {
            diagnostics.Add(new(scenario.Id, "expected-path-unavailable", "Scenario expects a recovery path not modeled by the workflow."));
        }

        HashSet<string> orderedActionIds = new(StringComparer.Ordinal);
        foreach (string actionId in scenario.ExpectedActionOrder)
        {
            if (!actions.TryGetValue(actionId, out RecoveryActionDefinition? action))
            {
                diagnostics.Add(new(scenario.Id, "expected-action-missing", $"Expected action '{actionId}' is not defined by the workflow."));
                continue;
            }

            foreach (string prerequisite in action.Prerequisites)
            {
                if (scenario.ExpectedActionOrder.Contains(prerequisite, StringComparer.Ordinal) && !orderedActionIds.Contains(prerequisite))
                {
                    diagnostics.Add(new(scenario.Id, "action-order-violates-prerequisite", $"Expected action '{actionId}' appears before prerequisite '{prerequisite}'."));
                }
            }

            orderedActionIds.Add(actionId);
        }

        foreach ((string actionId, ContractActionExpectation expectation) in scenario.ActionExpectations)
        {
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

        if (scenario.ExpectedOutcome == AccountContractOutcome.CanBeFullySecured && scenario.ActionExpectations.Values.Any(expectation => expectation.IsInitiallyBlocked || expectation.CreatesUnresolvedRisk))
        {
            diagnostics.Add(new(scenario.Id, "fully-secured-scenario-has-blockers", "A fully secured scenario cannot start with blocked actions or accepted unresolved risks."));
        }
    }
}
