namespace Unpwn.Core.Recovery.Workflows;

public sealed class RecoveryWorkflowValidator
{
    private static readonly DateOnly CurrentDate = new(2026, 8, 5);

    public static WorkflowValidationResult Validate(RecoveryWorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        List<WorkflowValidationDiagnostic> diagnostics = [];
        string workflowId = string.IsNullOrWhiteSpace(workflow.WorkflowId) ? "<unknown>" : workflow.WorkflowId;

        RequireText(workflow.WorkflowId, null, "workflow-id-required", "WorkflowId is required.", diagnostics, workflowId);
        RequireText(workflow.ProviderId, null, "provider-id-required", "ProviderId is required.", diagnostics, workflowId);
        RequireText(workflow.ProviderName, null, "provider-name-required", "ProviderName is required.", diagnostics, workflowId);
        RequireText(workflow.SupportedAccountType, null, "account-type-required", "SupportedAccountType is required.", diagnostics, workflowId);
        RequireText(workflow.WorkflowVersion, null, "workflow-version-required", "WorkflowVersion is required.", diagnostics, workflowId);

        if (workflow.VerifiedAt > CurrentDate)
        {
            diagnostics.Add(new(workflowId, null, "verification-date-in-future", "VerifiedAt must not be in the future."));
        }

        ValidateLocations(workflow, workflowId, diagnostics);
        ValidateActions(workflow, workflowId, diagnostics);

        return diagnostics.Count == 0 ? WorkflowValidationResult.Valid : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }

    private static void ValidateLocations(RecoveryWorkflowDefinition workflow, string workflowId, List<WorkflowValidationDiagnostic> diagnostics)
    {
        HashSet<string> locationIds = new(StringComparer.Ordinal);
        foreach (RecoveryLocationDefinition location in workflow.RecoveryLocations)
        {
            if (!locationIds.Add(location.Id))
            {
                diagnostics.Add(new(workflowId, null, "duplicate-location-id", $"Recovery location '{location.Id}' is duplicated."));
            }

            if (location.Url.Scheme != Uri.UriSchemeHttps)
            {
                diagnostics.Add(new(workflowId, null, "location-url-must-use-https", $"Recovery location '{location.Id}' must use HTTPS."));
            }

            if (location.ExpectedOrigins.Count == 0)
            {
                diagnostics.Add(new(workflowId, null, "expected-origin-required", $"Recovery location '{location.Id}' must declare at least one expected origin."));
            }

            string actualOrigin = location.Url.GetLeftPart(UriPartial.Authority);
            if (!location.ExpectedOrigins.Contains(actualOrigin, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new(workflowId, null, "location-origin-mismatch", $"Recovery location '{location.Id}' URL origin must be listed in ExpectedOrigins."));
            }
        }
    }

    private static void ValidateActions(RecoveryWorkflowDefinition workflow, string workflowId, List<WorkflowValidationDiagnostic> diagnostics)
    {
        if (workflow.Actions.Count == 0)
        {
            diagnostics.Add(new(workflowId, null, "action-required", "At least one recovery action is required."));
            return;
        }

        HashSet<string> actionIds = new(StringComparer.Ordinal);
        foreach (RecoveryActionDefinition action in workflow.Actions)
        {
            if (!actionIds.Add(action.Id))
            {
                diagnostics.Add(new(workflowId, action.Id, "duplicate-action-id", $"Recovery action '{action.Id}' is duplicated."));
            }

            if (action.Requirement == RecoveryActionRequirement.Required && action.CompletionCriteria.Count == 0)
            {
                diagnostics.Add(new(workflowId, action.Id, "required-action-completion-criteria", "Required actions must declare completion criteria."));
            }

            if (action.AutomationSupport == AutomationSupport.Automated)
            {
                diagnostics.Add(new(workflowId, action.Id, "automation-support-too-high", "Repository workflows cannot claim fully automated recovery support."));
            }
        }

        foreach (RecoveryActionDefinition action in workflow.Actions)
        {
            foreach (string prerequisite in action.Prerequisites)
            {
                if (!actionIds.Contains(prerequisite))
                {
                    diagnostics.Add(new(workflowId, action.Id, "missing-prerequisite", $"Prerequisite '{prerequisite}' does not reference a defined action."));
                }
            }
        }

        AddCycleDiagnostics(workflow, workflowId, diagnostics);
    }

    private static void AddCycleDiagnostics(RecoveryWorkflowDefinition workflow, string workflowId, List<WorkflowValidationDiagnostic> diagnostics)
    {
        Dictionary<string, RecoveryActionDefinition> actions = workflow.Actions
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);

        foreach (RecoveryActionDefinition action in workflow.Actions)
        {
            Visit(action.Id);
        }

        void Visit(string actionId)
        {
            if (visited.Contains(actionId) || !actions.TryGetValue(actionId, out RecoveryActionDefinition? action))
            {
                return;
            }

            if (!visiting.Add(actionId))
            {
                diagnostics.Add(new(workflowId, actionId, "cyclic-prerequisite", $"Action '{actionId}' participates in a prerequisite cycle."));
                return;
            }

            foreach (string prerequisite in action.Prerequisites)
            {
                Visit(prerequisite);
            }

            visiting.Remove(actionId);
            visited.Add(actionId);
        }
    }

    private static void RequireText(string value, string? actionId, string rule, string message, List<WorkflowValidationDiagnostic> diagnostics, string workflowId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(new(workflowId, actionId, rule, message));
        }
    }
}
