namespace Unpwn.Core.Recovery.Workflows;

public sealed class WorkflowValidationResult
{
    private WorkflowValidationResult(IReadOnlyList<WorkflowValidationDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public bool IsValid => Diagnostics.Count == 0;

    public IReadOnlyList<WorkflowValidationDiagnostic> Diagnostics { get; }

    public static WorkflowValidationResult Valid { get; } = new([]);

    public static WorkflowValidationResult FromDiagnostics(IReadOnlyList<WorkflowValidationDiagnostic> diagnostics) => new(diagnostics);
}
