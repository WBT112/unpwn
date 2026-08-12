using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;

namespace Unpwn.App.Services;

internal abstract class RecoveryBrowserPlatformAdapter(string profileDataPath)
    : IRecoveryBrowserPlatformAdapter
{
    protected string ProfileDataPath { get; } = profileDataPath;

    public event EventHandler<RecoveryBrowserSecurityEventCode>? SecurityEvent;

    public abstract bool IsConfigured { get; }

    public abstract void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args);

    public abstract void Attach(IPlatformHandle? platformHandle);

    public abstract void Dispose();

    protected void PublishSecurityEvent(RecoveryBrowserSecurityEventCode code) =>
        SecurityEvent?.Invoke(this, code);

    public static IRecoveryBrowserPlatformAdapter Create(string profileDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDataPath);
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRecoveryBrowserPlatformAdapter(profileDataPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxRecoveryBrowserPlatformAdapter(profileDataPath);
        }

        return new UnsupportedRecoveryBrowserPlatformAdapter(profileDataPath);
    }
}

internal sealed class WindowsRecoveryBrowserPlatformAdapter(string profileDataPath)
    : RecoveryBrowserPlatformAdapter(profileDataPath)
{
    private CoreWebView2? _webView;

    private bool _isConfigured;

    public override bool IsConfigured => _isConfigured;

    public override void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is not WindowsWebView2EnvironmentRequestedEventArgs webView2)
        {
            return;
        }

        Directory.CreateDirectory(ProfileDataPath);
        webView2.UserDataFolder = ProfileDataPath;
        webView2.ProfileName = "Recovery";
        webView2.AllowSingleSignOnUsingOSPrimaryAccount = false;
        webView2.IsInPrivateModeEnabled = false;
    }

    public override void Attach(IPlatformHandle? platformHandle)
    {
        Detach();
        _isConfigured = false;
        if (platformHandle is not IWindowsWebView2PlatformHandle webView2Handle ||
            webView2Handle.CoreWebView2 == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _webView = CoreWebView2.CreateFromComICoreWebView2(webView2Handle.CoreWebView2);
            HardenSettings(_webView.Settings);
            _webView.PermissionRequested += WebView_OnPermissionRequested;
            _webView.DownloadStarting += WebView_OnDownloadStarting;
            _webView.LaunchingExternalUriScheme += WebView_OnLaunchingExternalUriScheme;
            _webView.ServerCertificateErrorDetected += WebView_OnServerCertificateErrorDetected;
            _webView.ClientCertificateRequested += WebView_OnClientCertificateRequested;
            _isConfigured = true;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            Detach();
        }
    }

    public override void Dispose()
    {
        Detach();
        _isConfigured = false;
    }

    internal static void HardenSettings(CoreWebView2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
    }

    private void Detach()
    {
        if (_webView is null)
        {
            return;
        }

        _webView.PermissionRequested -= WebView_OnPermissionRequested;
        _webView.DownloadStarting -= WebView_OnDownloadStarting;
        _webView.LaunchingExternalUriScheme -= WebView_OnLaunchingExternalUriScheme;
        _webView.ServerCertificateErrorDetected -= WebView_OnServerCertificateErrorDetected;
        _webView.ClientCertificateRequested -= WebView_OnClientCertificateRequested;
        _webView = null;
    }

    private void WebView_OnPermissionRequested(
        object? sender,
        CoreWebView2PermissionRequestedEventArgs args)
    {
        args.SavesInProfile = false;
        args.State = CoreWebView2PermissionState.Deny;
        args.Handled = true;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.PermissionBlocked);
    }

    private void WebView_OnDownloadStarting(
        object? sender,
        CoreWebView2DownloadStartingEventArgs args)
    {
        args.Cancel = true;
        args.Handled = true;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.DownloadBlocked);
    }

    private void WebView_OnLaunchingExternalUriScheme(
        object? sender,
        CoreWebView2LaunchingExternalUriSchemeEventArgs args)
    {
        args.Cancel = true;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.ExternalProtocolBlocked);
    }

    private void WebView_OnServerCertificateErrorDetected(
        object? sender,
        CoreWebView2ServerCertificateErrorDetectedEventArgs args)
    {
        args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.TlsErrorBlocked);
    }

    private void WebView_OnClientCertificateRequested(
        object? sender,
        CoreWebView2ClientCertificateRequestedEventArgs args)
    {
        args.Cancel = true;
        args.Handled = true;
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.ClientCertificateBlocked);
    }
}

internal sealed partial class LinuxRecoveryBrowserPlatformAdapter
    : RecoveryBrowserPlatformAdapter
{
    private const string WpeWebKitLibrary = "libWPEWebKit-2.0.so.1";
    private readonly PermissionSignalCallback _permissionCallback;
    private readonly DownloadSignalCallback _downloadCallback;
    private readonly TlsSignalCallback _tlsCallback;
    private ulong _permissionHandler;
    private ulong _downloadHandler;
    private ulong _tlsHandler;
    private IntPtr _webView;

    private bool _isConfigured;

    public LinuxRecoveryBrowserPlatformAdapter(string profileDataPath)
        : base(profileDataPath)
    {
        _permissionCallback = DenyPermission;
        _downloadCallback = CancelDownload;
        _tlsCallback = RejectTlsError;
    }

    public override bool IsConfigured => _isConfigured;

    public override void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is not LinuxWpeWebViewEnvironmentRequestedEventArgs wpe)
        {
            return;
        }

        var dataDirectory = Path.Combine(ProfileDataPath, "data");
        var cacheDirectory = Path.Combine(ProfileDataPath, "cache");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(cacheDirectory);
        wpe.DataDirectory = dataDirectory;
        wpe.CacheDirectory = cacheDirectory;
        wpe.PreferWebKitGtkInstead = false;
    }

    public override void Attach(IPlatformHandle? platformHandle)
    {
        Detach();
        _isConfigured = false;
        if (platformHandle is not ILinuxWpePlatformHandle wpeHandle ||
            wpeHandle.WebKitWebView == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _webView = wpeHandle.WebKitWebView;
            var session = webkit_web_view_get_network_session(_webView);
            if (session == IntPtr.Zero)
            {
                Detach();
                return;
            }

            webkit_network_session_set_persistent_credential_storage_enabled(session, false);
            _permissionHandler = Connect(_webView, "permission-request", _permissionCallback);
            _tlsHandler = Connect(_webView, "load-failed-with-tls-errors", _tlsCallback);
            _downloadHandler = Connect(session, "download-started", _downloadCallback);
            _isConfigured = _permissionHandler != 0 && _tlsHandler != 0 && _downloadHandler != 0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            Detach();
        }
    }

    public override void Dispose()
    {
        Detach();
        _isConfigured = false;
    }

    private static ulong Connect(IntPtr instance, string signal, Delegate callback) =>
        g_signal_connect_data(
            instance,
            signal,
            Marshal.GetFunctionPointerForDelegate(callback),
            IntPtr.Zero,
            IntPtr.Zero,
            0);

    private void Detach()
    {
        if (_webView == IntPtr.Zero)
        {
            return;
        }

        Disconnect(_webView, _permissionHandler);
        Disconnect(_webView, _tlsHandler);
        var session = webkit_web_view_get_network_session(_webView);
        if (session != IntPtr.Zero)
        {
            Disconnect(session, _downloadHandler);
        }

        _permissionHandler = 0;
        _downloadHandler = 0;
        _tlsHandler = 0;
        _webView = IntPtr.Zero;
    }

    private static void Disconnect(IntPtr instance, ulong handlerId)
    {
        if (instance != IntPtr.Zero && handlerId != 0)
        {
            g_signal_handler_disconnect(instance, handlerId);
        }
    }

    private int DenyPermission(IntPtr sender, IntPtr request, IntPtr userData)
    {
        webkit_permission_request_deny(request);
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.PermissionBlocked);
        return 1;
    }

    private void CancelDownload(IntPtr sender, IntPtr download, IntPtr userData)
    {
        webkit_download_cancel(download);
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.DownloadBlocked);
    }

    private int RejectTlsError(
        IntPtr sender,
        IntPtr failingUri,
        IntPtr certificate,
        uint errors,
        IntPtr userData)
    {
        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.TlsErrorBlocked);
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PermissionSignalCallback(IntPtr sender, IntPtr request, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DownloadSignalCallback(IntPtr sender, IntPtr download, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TlsSignalCallback(
        IntPtr sender,
        IntPtr failingUri,
        IntPtr certificate,
        uint errors,
        IntPtr userData);

    [LibraryImport("libgobject-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    private static partial ulong g_signal_connect_data(
        IntPtr instance,
        string signal,
        IntPtr handler,
        IntPtr data,
        IntPtr destroyData,
        int connectFlags);

    [LibraryImport("libgobject-2.0.so.0")]
    private static partial void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial IntPtr webkit_web_view_get_network_session(IntPtr webView);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial void webkit_network_session_set_persistent_credential_storage_enabled(
        IntPtr session,
        [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial void webkit_permission_request_deny(IntPtr request);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial void webkit_download_cancel(IntPtr download);
}

internal sealed class UnsupportedRecoveryBrowserPlatformAdapter(string profileDataPath)
    : RecoveryBrowserPlatformAdapter(profileDataPath)
{
    public override bool IsConfigured => false;

    public override void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args) =>
        args.EnableDevTools = false;

    public override void Attach(IPlatformHandle? platformHandle)
    {
    }

    public override void Dispose()
    {
    }
}
