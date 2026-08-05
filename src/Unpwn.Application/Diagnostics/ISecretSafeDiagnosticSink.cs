namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Receives structured diagnostic events that contain no secret-bearing values.
/// </summary>
public interface ISecretSafeDiagnosticSink
{
    void Write(DiagnosticEvent diagnosticEvent);
}
