using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Unpwn.App.Services;

namespace Unpwn.App.Views;

public partial class RecoveryBrowserView : UserControl, IDisposable
{
    private readonly Func<string, IRecoveryBrowserPlatformAdapter> _platformAdapterFactory;
    private AvaloniaRecoveryBrowserHost? _host;

    public RecoveryBrowserView()
        : this(RecoveryBrowserPlatformAdapter.Create)
    {
    }

    internal RecoveryBrowserView(
        Func<string, IRecoveryBrowserPlatformAdapter> platformAdapterFactory)
    {
        _platformAdapterFactory = platformAdapterFactory ??
            throw new ArgumentNullException(nameof(platformAdapterFactory));
        InitializeComponent();
        UpdateSnapshot(null);
    }

    public RecoveryBrowserHostSnapshot? Snapshot => _host?.Snapshot;

    public bool Start(RecoveryBrowserHostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_host is not null)
        {
            return false;
        }

        var webView = new NativeWebView();
        var host = new AvaloniaRecoveryBrowserHost(webView, _platformAdapterFactory);
        host.SnapshotChanged += Host_OnSnapshotChanged;
        if (!host.Start(request))
        {
            host.SnapshotChanged -= Host_OnSnapshotChanged;
            host.Dispose();
            return false;
        }

        _host = host;
        BrowserContent.Content = webView;
        UpdateSnapshot(host.Snapshot);
        return true;
    }

    public void Dispose()
    {
        if (_host is not null)
        {
            _host.SnapshotChanged -= Host_OnSnapshotChanged;
            _host.Dispose();
            _host = null;
        }

        BrowserContent.Content = null;
        UpdateSnapshot(null);
        GC.SuppressFinalize(this);
    }

    private void Host_OnSnapshotChanged(
        object? sender,
        RecoveryBrowserHostSnapshot snapshot) => UpdateSnapshot(snapshot);

    private void UpdateSnapshot(RecoveryBrowserHostSnapshot? snapshot)
    {
        BackButton.IsEnabled = snapshot?.CanGoBack == true;
        ForwardButton.IsEnabled = snapshot?.CanGoForward == true;
        if (snapshot?.VisibleOrigin is { } origin)
        {
            OriginText.Text = origin;
        }
        else
        {
            BindDynamicResource(OriginText, "RecoveryBrowser.OriginUnavailable");
        }

        var securityResourceKey = snapshot?.LastSecurityEvent switch
        {
            RecoveryBrowserSecurityEventCode.None or null => null,
            _ => $"RecoveryBrowser.Security.{snapshot.LastSecurityEvent}",
        };
        if (securityResourceKey is null)
        {
            SecurityStatusText.Text = string.Empty;
        }
        else
        {
            BindDynamicResource(SecurityStatusText, securityResourceKey);
        }
    }

    private static void BindDynamicResource(TextBlock textBlock, string key) =>
        textBlock.Bind(
            TextBlock.TextProperty,
            new DynamicResourceExtension(key).ProvideValue(null!));

    private void Back_OnClick(object? sender, RoutedEventArgs args) => _host?.GoBack();

    private void Forward_OnClick(object? sender, RoutedEventArgs args) => _host?.GoForward();

    private void Reload_OnClick(object? sender, RoutedEventArgs args) => _host?.Reload();

    private void Stop_OnClick(object? sender, RoutedEventArgs args) => _host?.StopLoading();

    private void Close_OnClick(object? sender, RoutedEventArgs args) => Dispose();
}
