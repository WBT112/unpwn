using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Unpwn.App.Presentation;

namespace Unpwn.App.Converters;

public sealed class StatusStateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AppVisualState.Normal => new SolidColorBrush(Color.Parse("#4B6B88")),
            AppVisualState.Warning => new SolidColorBrush(Color.Parse("#B26A00")),
            AppVisualState.Blocked => new SolidColorBrush(Color.Parse("#7048A8")),
            AppVisualState.Error => new SolidColorBrush(Color.Parse("#B3261E")),
            AppVisualState.Success => new SolidColorBrush(Color.Parse("#167A3F")),
            AppVisualState.UnresolvedRisk => new SolidColorBrush(Color.Parse("#9A4D00")),
            _ => new SolidColorBrush(Color.Parse("#4B6B88")),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
