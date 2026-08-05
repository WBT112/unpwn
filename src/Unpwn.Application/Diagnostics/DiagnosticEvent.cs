namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Represents a diagnostic event whose fields are safe to expose in logs and test artifacts.
/// </summary>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Operation">The bounded operation that failed.</param>
/// <param name="EventId">A stable, non-sensitive event identifier.</param>
/// <param name="Message">A static, non-sensitive description.</param>
/// <param name="ExceptionType">The exception type without its message or stack trace.</param>
public sealed record DiagnosticEvent(
    DiagnosticSeverity Severity,
    DiagnosticOperation Operation,
    string EventId,
    string Message,
    string ExceptionType);
