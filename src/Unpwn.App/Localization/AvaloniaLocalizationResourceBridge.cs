using Avalonia;
using Avalonia.Threading;

namespace Unpwn.App.Localization;

public sealed class AvaloniaLocalizationResourceBridge : IDisposable
{
    private readonly ILocalizationService _localization;
    private bool _isDisposed;

    public AvaloniaLocalizationResourceBridge(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;
        _localization.CultureChanged += Localization_OnCultureChanged;
        ApplyResources();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _localization.CultureChanged -= Localization_OnCultureChanged;
        _isDisposed = true;
    }

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyResources();
            return;
        }

        Dispatcher.UIThread.Post(ApplyResources);
    }

    private void ApplyResources()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Avalonia application resources are unavailable.");
        foreach (var key in _localization.GetResourceKeys(ResourceLocalizationService.DefaultLanguageCode))
        {
            application.Resources[key] = _localization.GetString(key);
        }
    }
}
