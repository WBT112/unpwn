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

    public abstract Task ClearBrowsingDataAsync(CancellationToken cancellationToken);

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

    public override Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
        _webView?.Profile.ClearBrowsingDataAsync().WaitAsync(cancellationToken) ??
        Task.CompletedTask;

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
    private const uint AllWebsiteDataTypes = 0x7FFF;
    private static readonly AsyncReadyCallback WebsiteDataClearedCallback =
        CompleteWebsiteDataClear;
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

    public override async Task ClearBrowsingDataAsync(CancellationToken cancellationToken)
    {
        if (_webView == IntPtr.Zero)
        {
            return;
        }

        var manager = webkit_web_view_get_website_data_manager(_webView);
        if (manager == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "The WPE WebKit website-data manager is unavailable.");
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        try
        {
            webkit_website_data_manager_clear(
                manager,
                AllWebsiteDataTypes,
                timespan: 0,
                IntPtr.Zero,
                Marshal.GetFunctionPointerForDelegate(WebsiteDataClearedCallback),
                GCHandle.ToIntPtr(handle));
        }
        catch
        {
            handle.Free();
            throw;
        }

        await completion.Task.WaitAsync(cancellationToken);
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

    private static void CompleteWebsiteDataClear(
        IntPtr sourceObject,
        IntPtr result,
        IntPtr userData)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var completion = (TaskCompletionSource)handle.Target!;
        handle.Free();
        IntPtr error = IntPtr.Zero;
        if (webkit_website_data_manager_clear_finish(
                sourceObject,
                result,
                ref error))
        {
            completion.TrySetResult();
            return;
        }

        if (error != IntPtr.Zero)
        {
            g_error_free(error);
        }

        completion.TrySetException(new IOException(
            "WPE WebKit did not clear the Recovery Browser profile data."));
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AsyncReadyCallback(
        IntPtr sourceObject,
        IntPtr result,
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

    [LibraryImport("libglib-2.0.so.0")]
    private static partial void g_error_free(IntPtr error);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial IntPtr webkit_web_view_get_network_session(IntPtr webView);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial IntPtr webkit_web_view_get_website_data_manager(IntPtr webView);

    [LibraryImport(WpeWebKitLibrary)]
    private static partial void webkit_website_data_manager_clear(
        IntPtr manager,
        uint types,
        long timespan,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(WpeWebKitLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool webkit_website_data_manager_clear_finish(
        IntPtr manager,
        IntPtr result,
        ref IntPtr error);

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

    public override Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public override void Dispose()
    {
    }
}
