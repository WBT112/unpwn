namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Converts failures into bounded diagnostics without retaining exception messages or stack traces.
/// </summary>
public sealed class SecretSafeDiagnostics(ISecretSafeDiagnosticSink sink)
{
    private readonly ISecretSafeDiagnosticSink _sink =
        sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>
    /// Reports a failure and returns an exception that is safe to propagate to presentation code.
    /// </summary>
    public InvalidOperationException ReportFailureAndCreateSafeException(
        DiagnosticOperation operation,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (eventId, message) = operation switch
        {
            DiagnosticOperation.VaultUnlock => ("UNPWN1001", "Vault unlock failed."),
            DiagnosticOperation.RecoverySessionLoad => ("UNPWN1002", "Recovery session loading failed."),
            DiagnosticOperation.RecoveryAction => ("UNPWN1003", "Recovery action failed."),
            DiagnosticOperation.CredentialExport => ("UNPWN1004", "Credential export failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported diagnostic operation."),
        };

        _sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                operation,
                eventId,
                message,
                exception.GetType().Name));

        return new InvalidOperationException(message);
    }
}
