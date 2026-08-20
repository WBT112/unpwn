using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Unpwn.App.Services;

internal static class LinuxRecoveryBrowserRuntime
{
    private const string WpeWebKitLibrary = "libWPEWebKit-2.0.so.1";
    private const string WpeBackendLibrary = "libWPEBackend-fdo-1.0.so.1";

    internal static bool IsEmbeddedWpeAvailable() =>
        OperatingSystem.IsLinux() &&
        CanLoad(WpeWebKitLibrary) &&
        CanLoad(WpeBackendLibrary);

    internal static bool ShouldUseDialogFallback(bool isLinux, bool isWpeAvailable) =>
        isLinux && !isWpeAvailable;

    private static bool CanLoad(string libraryName)
    {
        if (!NativeLibrary.TryLoad(libraryName, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }
}

internal interface IRecoveryBrowserControl : IDisposable
{
    event EventHandler<WebViewEnvironmentRequestedEventArgs>? EnvironmentRequested;

    event EventHandler<WebViewAdapterEventArgs>? AdapterCreated;

    event EventHandler<WebViewAdapterEventArgs>? AdapterDestroyed;

    event EventHandler<WebViewNavigationStartingEventArgs>? NavigationStarted;

    event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;

    event EventHandler<WebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    event EventHandler? Closing;

    bool IsEmbedded { get; }

    Control? EmbeddedControl { get; }

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    IPlatformHandle? TryGetPlatformHandle();

    void Show();

    void Hide();

    void Navigate(Uri destination);

    bool GoBack();

    bool GoForward();

    bool Refresh();

    bool Stop();

    Task<string?> InvokeScript(string script);
}

internal sealed class EmbeddedRecoveryBrowserControl(NativeWebView webView)
    : IRecoveryBrowserControl
{
    private readonly NativeWebView _webView = webView ??
        throw new ArgumentNullException(nameof(webView));

    public event EventHandler<WebViewEnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add => _webView.EnvironmentRequested += value;
        remove => _webView.EnvironmentRequested -= value;
    }

    public event EventHandler<WebViewAdapterEventArgs>? AdapterCreated
    {
        add => _webView.AdapterCreated += value;
        remove => _webView.AdapterCreated -= value;
    }

    public event EventHandler<WebViewAdapterEventArgs>? AdapterDestroyed
    {
        add => _webView.AdapterDestroyed += value;
        remove => _webView.AdapterDestroyed -= value;
    }

    public event EventHandler<WebViewNavigationStartingEventArgs>? NavigationStarted
    {
        add => _webView.NavigationStarted += value;
        remove => _webView.NavigationStarted -= value;
    }

    public event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _webView.NavigationCompleted += value;
        remove => _webView.NavigationCompleted -= value;
    }

    public event EventHandler<WebViewNewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _webView.NewWindowRequested += value;
        remove => _webView.NewWindowRequested -= value;
    }

    public event EventHandler? Closing
    {
        add { }
        remove { }
    }

    public bool IsEmbedded => true;

    public Control EmbeddedControl => _webView;

    public bool CanGoBack => _webView.CanGoBack;

    public bool CanGoForward => _webView.CanGoForward;

    public IPlatformHandle? TryGetPlatformHandle() => _webView.TryGetPlatformHandle();

    public void Show() => _webView.IsVisible = true;

    public void Hide() => _webView.IsVisible = false;

    public void Navigate(Uri destination) => _webView.Navigate(destination);

    public bool GoBack() => _webView.GoBack();

    public bool GoForward() => _webView.GoForward();

    public bool Refresh() => _webView.Refresh();

    public bool Stop() => _webView.Stop();

    public Task<string?> InvokeScript(string script) => _webView.InvokeScript(script);

    public void Dispose()
    {
    }
}

internal sealed class DialogRecoveryBrowserControl : IRecoveryBrowserControl
{
    private readonly NativeWebDialog _dialog;
    private readonly TopLevel _owner;
    private bool _isShown;

    internal DialogRecoveryBrowserControl(NativeWebDialog dialog, TopLevel owner)
    {
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dialog.Closing += Dialog_OnClosing;
    }

    public event EventHandler<WebViewEnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add => _dialog.EnvironmentRequested += value;
        remove => _dialog.EnvironmentRequested -= value;
    }

    public event EventHandler<WebViewAdapterEventArgs>? AdapterCreated
    {
        add => _dialog.AdapterCreated += value;
        remove => _dialog.AdapterCreated -= value;
    }

    public event EventHandler<WebViewAdapterEventArgs>? AdapterDestroyed
    {
        add => _dialog.AdapterDestroyed += value;
        remove => _dialog.AdapterDestroyed -= value;
    }

    public event EventHandler<WebViewNavigationStartingEventArgs>? NavigationStarted
    {
        add => _dialog.NavigationStarted += value;
        remove => _dialog.NavigationStarted -= value;
    }

    public event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _dialog.NavigationCompleted += value;
        remove => _dialog.NavigationCompleted -= value;
    }

    public event EventHandler<WebViewNewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _dialog.NewWindowRequested += value;
        remove => _dialog.NewWindowRequested -= value;
    }

    public event EventHandler? Closing;

    public bool IsEmbedded => false;

    public Control? EmbeddedControl => null;

    public bool CanGoBack => _dialog.CanGoBack;

    public bool CanGoForward => _dialog.CanGoForward;

    public IPlatformHandle? TryGetPlatformHandle() => _dialog.TryGetWebViewPlatformHandle();

    public void Show()
    {
        if (_isShown)
        {
            _dialog.Focus();
            return;
        }

        _isShown = true;
        _dialog.Show(_owner);
    }

    public void Hide()
    {
        if (!_isShown)
        {
            return;
        }

        _isShown = false;
        _dialog.Close();
    }

    public void Navigate(Uri destination) => _dialog.Navigate(destination);

    public bool GoBack() => _dialog.GoBack();

    public bool GoForward() => _dialog.GoForward();

    public bool Refresh() => _dialog.Refresh();

    public bool Stop() => _dialog.Stop();

    public Task<string?> InvokeScript(string script) => _dialog.InvokeScript(script);

    public void Dispose()
    {
        _dialog.Closing -= Dialog_OnClosing;
        _dialog.Dispose();
    }

    private void Dialog_OnClosing(object? sender, EventArgs args)
    {
        _isShown = false;
        Closing?.Invoke(this, EventArgs.Empty);
    }
}
