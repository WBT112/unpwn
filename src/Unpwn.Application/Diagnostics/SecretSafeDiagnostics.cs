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

        var diagnosticEvent = CreateEvent(operation, exception);
        _sink.Write(diagnosticEvent);
        return new InvalidOperationException(diagnosticEvent.Message);
    }

    public void ReportFailure(DiagnosticOperation operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _sink.Write(CreateEvent(operation, exception));
    }

    private static DiagnosticEvent CreateEvent(
        DiagnosticOperation operation,
        Exception exception)
    {
        var (eventId, message) = GetDescriptor(operation);

        return new DiagnosticEvent(
            DiagnosticSeverity.Error,
            operation,
            eventId,
            message,
            exception.GetType().Name);
    }

    public static (string EventId, string Message) GetDescriptor(
        DiagnosticOperation operation) => operation switch
        {
            DiagnosticOperation.VaultUnlock => ("UNPWN1001", "Vault unlock failed."),
            DiagnosticOperation.RecoverySessionLoad => ("UNPWN1002", "Recovery session loading failed."),
            DiagnosticOperation.RecoveryAction => ("UNPWN1003", "Recovery action failed."),
            DiagnosticOperation.CredentialExport => ("UNPWN1004", "Credential export failed."),
            DiagnosticOperation.VaultLock => ("UNPWN1005", "Vault lock failed."),
            DiagnosticOperation.VaultPasswordChange => ("UNPWN1006", "Vault password change failed."),
            DiagnosticOperation.WorkspaceSave => ("UNPWN1007", "Recovery workspace saving failed."),
            DiagnosticOperation.WorkspaceLoad => ("UNPWN1008", "Recovery workspace loading failed."),
            DiagnosticOperation.StartupRecovery => ("UNPWN1009", "Previous application run ended unexpectedly."),
            DiagnosticOperation.ApplicationCrash => ("UNPWN1010", "Application failure boundary activated."),
            DiagnosticOperation.DiagnosticExport => ("UNPWN1011", "Diagnostic export failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported diagnostic operation."),
        };
}
