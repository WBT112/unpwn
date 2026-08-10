
namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Retains only already-sanitized diagnostic events in memory for explicit local export.
/// </summary>
public sealed class BoundedSecretSafeDiagnosticStore :
    ISecretSafeDiagnosticSink,
    ISecretSafeDiagnosticSource
{
    private readonly Lock _gate = new();
    private readonly Queue<DiagnosticEvent> _events = new();
    private readonly int _capacity;

    public BoundedSecretSafeDiagnosticStore(int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        lock (_gate)
        {
            _events.Enqueue(diagnosticEvent);
            while (_events.Count > _capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }
}
