using Avalonia.Automation;
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

public enum StatusPresentation
{
    ScreenInstruction,
    GlobalContext,
    GlobalWarning,
    TransientResult,
}

public sealed record VisualStatusViewModel(
    AppVisualState State,
    string KindLabel,
    string Symbol,
    string Title,
    string Message,
    StatusPresentation Presentation = StatusPresentation.ScreenInstruction)
{
    public AutomationLiveSetting LiveSetting => Presentation == StatusPresentation.GlobalWarning
        ? AutomationLiveSetting.Assertive
        : AutomationLiveSetting.Polite;

    public bool IsTransientResult => Presentation == StatusPresentation.TransientResult;

    public static VisualStatusViewModel Create(
        AppVisualState state,
        ILocalizationService localization,
        string titleKey,
        string messageKey,
        StatusPresentation presentation = StatusPresentation.ScreenInstruction,
        params object?[] messageArguments)
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
            messageArguments.Length == 0
                ? localization.GetString(messageKey)
                : localization.Format(messageKey, messageArguments),
            presentation);
    }
}
