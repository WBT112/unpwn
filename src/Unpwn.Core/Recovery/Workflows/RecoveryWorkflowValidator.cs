using global::Unpwn.Core;

namespace Unpwn.Core.Recovery.Workflows;

public sealed class RecoveryWorkflowValidator
{
    public static WorkflowValidationResult Validate(
        RecoveryWorkflowDefinition workflow,
        DateOnly? currentDate = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        List<WorkflowValidationDiagnostic> diagnostics = [];
        var workflowId = string.IsNullOrWhiteSpace(workflow.WorkflowId)
            ? "<unknown>"
            : workflow.WorkflowId;
        var validationDate = currentDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        RequireText(workflow.WorkflowId, null, "workflow-id-required", "WorkflowId is required.", diagnostics, workflowId);
        RequireText(workflow.ProviderId, null, "provider-id-required", "ProviderId is required.", diagnostics, workflowId);
        RequireText(workflow.ProviderName, null, "provider-name-required", "ProviderName is required.", diagnostics, workflowId);
        RequireText(workflow.SupportedAccountType, null, "account-type-required", "SupportedAccountType is required.", diagnostics, workflowId);
        RequireText(workflow.WorkflowVersion, null, "workflow-version-required", "WorkflowVersion is required.", diagnostics, workflowId);

        if (!Enum.IsDefined(workflow.TrustLevel))
        {
            diagnostics.Add(new(workflowId, null, "workflow-trust-level-invalid", "TrustLevel is invalid."));
        }

        if (workflow.TrustLevel == RecoveryWorkflowTrustLevel.GeneralManualGuidance &&
            workflow.RecoveryLocations.Count > 0)
        {
            diagnostics.Add(new(
                workflowId,
                null,
                "general-workflow-provider-location-forbidden",
                "General manual guidance cannot declare provider-specific recovery locations or trusted origins."));
        }

        if (workflow.AllowsAccountOriginDiscovery &&
            workflow.TrustLevel != RecoveryWorkflowTrustLevel.GeneralManualGuidance)
        {
            diagnostics.Add(new(
                workflowId,
                null,
                "account-origin-discovery-requires-general-workflow",
                "Account-origin discovery is restricted to explicitly general manual guidance."));
        }

        if (workflow.VerifiedAt == default)
        {
            diagnostics.Add(new(workflowId, null, "verification-date-required", "VerifiedAt is required."));
        }
        else if (workflow.VerifiedAt > validationDate)
        {
            diagnostics.Add(new(workflowId, null, "verification-date-in-future", "VerifiedAt must not be in the future."));
        }

        ValidateLocations(workflow, workflowId, diagnostics);
        ValidateActions(workflow, workflowId, diagnostics);

        return diagnostics.Count == 0
            ? WorkflowValidationResult.Valid
            : WorkflowValidationResult.FromDiagnostics(diagnostics);
    }

    private static void ValidateLocations(
        RecoveryWorkflowDefinition workflow,
        string workflowId,
        List<WorkflowValidationDiagnostic> diagnostics)
    {
        if (workflow.RecoveryLocations.Count == 0 &&
            workflow.TrustLevel != RecoveryWorkflowTrustLevel.GeneralManualGuidance)
        {
            diagnostics.Add(new(workflowId, null, "recovery-location-required", "At least one recovery location is required."));
            return;
        }

        HashSet<string> locationIds = new(StringComparer.Ordinal);
        foreach (var location in workflow.RecoveryLocations)
        {
            if (string.IsNullOrWhiteSpace(location.Id))
            {
                diagnostics.Add(new(workflowId, null, "location-id-required", "Recovery locations require an identifier."));
            }
            else if (!locationIds.Add(location.Id))
            {
                diagnostics.Add(new(workflowId, null, "duplicate-location-id", $"Recovery location '{location.Id}' is duplicated."));
            }

            if (!location.Url.IsAbsoluteUri || location.Url.Scheme != Uri.UriSchemeHttps)
            {
                diagnostics.Add(new(workflowId, null, "location-url-must-use-https", $"Recovery location '{location.Id}' must use an absolute HTTPS URL."));
            }

            if (location.ExpectedOrigins.Count == 0)
            {
                diagnostics.Add(new(workflowId, null, "expected-origin-required", $"Recovery location '{location.Id}' must declare at least one expected origin."));
                continue;
            }

            foreach (var expectedOrigin in location.ExpectedOrigins)
            {
                if (!Uri.TryCreate(expectedOrigin, UriKind.Absolute, out var expectedUri) ||
                    expectedUri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(
                        expectedUri.GetLeftPart(UriPartial.Authority),
                        expectedOrigin.TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new(workflowId, null, "expected-origin-invalid", $"Recovery location '{location.Id}' contains an invalid HTTPS origin."));
                }
            }

            if (location.Url.IsAbsoluteUri)
            {
                var actualOrigin = location.Url.GetLeftPart(UriPartial.Authority);
                if (!location.ExpectedOrigins.Contains(actualOrigin, StringComparer.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new(workflowId, null, "location-origin-mismatch", $"Recovery location '{location.Id}' URL origin must be listed in ExpectedOrigins."));
                }
            }
        }
    }

    private static void ValidateActions(
        RecoveryWorkflowDefinition workflow,
        string workflowId,
        List<WorkflowValidationDiagnostic> diagnostics)
    {
        if (workflow.Actions.Count == 0)
        {
            diagnostics.Add(new(workflowId, null, "action-required", "At least one recovery action is required."));
            return;
        }

        HashSet<string> actionIds = new(StringComparer.Ordinal);
        var locationIds = workflow.RecoveryLocations
            .Where(location => !string.IsNullOrWhiteSpace(location.Id))
            .Select(location => location.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var action in workflow.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
            {
                diagnostics.Add(new(workflowId, null, "action-id-required", "Recovery actions require an identifier."));
            }
            else if (!actionIds.Add(action.Id))
            {
                diagnostics.Add(new(workflowId, action.Id, "duplicate-action-id", $"Recovery action '{action.Id}' is duplicated."));
            }

            if (action.RecoveryPaths.Count == 0)
            {
                diagnostics.Add(new(workflowId, action.Id, "recovery-path-required", "Recovery actions must support at least one recovery path."));
            }
            else if (action.RecoveryPaths.Distinct().Count() != action.RecoveryPaths.Count)
            {
                diagnostics.Add(new(workflowId, action.Id, "duplicate-recovery-path", "Recovery action paths must be unique."));
            }

            if (action.Requirement == RecoveryActionRequirement.Required &&
                (action.CompletionCriteria.Count == 0 || action.CompletionCriteria.Any(string.IsNullOrWhiteSpace)))
            {
                diagnostics.Add(new(workflowId, action.Id, "required-action-completion-criteria", "Required actions must declare non-empty completion criteria."));
            }

            if (action.CompletionCriteria.Any(criterion =>
                    !RecoveryActionGuidanceKeys.IsResourceKey(criterion)))
            {
                diagnostics.Add(new(
                    workflowId,
                    action.Id,
                    "completion-criterion-resource-key-required",
                    "Recovery action completion criteria must be stable presentation resource keys."));
            }

            try
            {
                action.Guidance.Validate();
                if (!action.Guidance.CompletionCriteriaKeys.SequenceEqual(
                        action.CompletionCriteria,
                        StringComparer.Ordinal))
                {
                    diagnostics.Add(new(
                        workflowId,
                        action.Id,
                        "guidance-criteria-mismatch",
                        "Recovery action guidance criteria must match the canonical completion criteria keys."));
                }
            }
            catch (InvalidOperationException)
            {
                diagnostics.Add(new(
                    workflowId,
                    action.Id,
                    "workflow-guidance-invalid",
                    "Recovery action guidance must contain stable resource keys."));
            }

            if (action.AutomationSupport == AutomationSupport.Automated)
            {
                diagnostics.Add(new(workflowId, action.Id, "automation-support-too-high", "Repository workflows cannot claim fully automated recovery support."));
            }

            if (action.AutomationSupport == AutomationSupport.Navigation &&
                action.RecoveryLocationId is null &&
                !(workflow.AllowsAccountOriginDiscovery &&
                  action.Type == RecoveryActionType.ChangePassword))
            {
                diagnostics.Add(new(
                    workflowId,
                    action.Id,
                    "navigation-source-required",
                    "Navigation requires a reviewed location or explicitly allowed account-origin password discovery."));
            }

            if (action.RecoveryLocationId is { } locationId &&
                !locationIds.Contains(locationId))
            {
                diagnostics.Add(new(
                    workflowId,
                    action.Id,
                    "missing-action-recovery-location",
                    $"Recovery action '{action.Id}' references an unknown recovery location."));
            }
        }

        foreach (var action in workflow.Actions)
        {
            foreach (var prerequisite in action.Prerequisites)
            {
                if (!actionIds.Contains(prerequisite))
                {
                    diagnostics.Add(new(workflowId, action.Id, "missing-prerequisite", $"Prerequisite '{prerequisite}' does not reference a defined action."));
                }
            }
        }

        AddCycleDiagnostics(workflow, workflowId, diagnostics);
    }

    private static void AddCycleDiagnostics(
        RecoveryWorkflowDefinition workflow,
        string workflowId,
        List<WorkflowValidationDiagnostic> diagnostics)
    {
        var actions = workflow.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Id))
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);

        foreach (var action in workflow.Actions)
        {
            Visit(action.Id);
        }

        void Visit(string actionId)
        {
            if (visited.Contains(actionId) || !actions.TryGetValue(actionId, out var action))
            {
                return;
            }

            if (!visiting.Add(actionId))
            {
                diagnostics.Add(new(workflowId, actionId, "cyclic-prerequisite", $"Action '{actionId}' participates in a prerequisite cycle."));
                return;
            }

            foreach (var prerequisite in action.Prerequisites)
            {
                Visit(prerequisite);
            }

            visiting.Remove(actionId);
            visited.Add(actionId);
        }
    }

    private static void RequireText(
        string value,
        string? actionId,
        string rule,
        string message,
        List<WorkflowValidationDiagnostic> diagnostics,
        string workflowId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(new(workflowId, actionId, rule, message));
        }
    }
}
