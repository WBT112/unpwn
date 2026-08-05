namespace Unpwn.Core.Recovery.Workflows;

public sealed record WorkflowValidationDiagnostic(
    string WorkflowId,
    string? ActionId,
    string Rule,
    string Message);
