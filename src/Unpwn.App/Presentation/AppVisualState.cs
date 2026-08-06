using Unpwn.App.Localization;

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
    public static VisualStatusViewModel Create(
        AppVisualState state,
        ILocalizationService localization,
        string titleKey,
        string messageKey)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        var (kindKey, symbol) = state switch
        {
            AppVisualState.Normal => ("Status.Normal", "i"),
            AppVisualState.Warning => ("Status.Warning", "!"),
            AppVisualState.Blocked => ("Status.Blocked", "⛔"),
            AppVisualState.Error => ("Status.Error", "×"),
            AppVisualState.Success => ("Status.Success", "✓"),
            AppVisualState.UnresolvedRisk => ("Status.UnresolvedRisk", "⚠"),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        return new VisualStatusViewModel(
            state,
            localization.GetString(kindKey),
            symbol,
            localization.GetString(titleKey),
            localization.GetString(messageKey));
    }
}
