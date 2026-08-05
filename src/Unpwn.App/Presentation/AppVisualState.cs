namespace Unpwn.App.Presentation;

public enum AppVisualState
{
    Normal,
    Warning,
    Blocked,
    Error,
    Success,
    UnresolvedRisk,
}

public sealed record VisualStatusViewModel(
    AppVisualState State,
    string KindLabel,
    string Symbol,
    string Title,
    string Message)
{
    public static VisualStatusViewModel Create(AppVisualState state, string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var (kindLabel, symbol) = state switch
        {
            AppVisualState.Normal => ("Status", "i"),
            AppVisualState.Warning => ("Warning", "!"),
            AppVisualState.Blocked => ("Blocked", "⛔"),
            AppVisualState.Error => ("Failed", "×"),
            AppVisualState.Success => ("Success", "✓"),
            AppVisualState.UnresolvedRisk => ("Unresolved risk", "⚠"),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        return new VisualStatusViewModel(state, kindLabel, symbol, title, message);
    }
}
