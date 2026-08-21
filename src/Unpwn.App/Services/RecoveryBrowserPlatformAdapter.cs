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

    public virtual Task WaitForProfileReleaseAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public abstract void Dispose();

    protected void PublishSecurityEvent(RecoveryBrowserSecurityEventCode code) =>
        SecurityEvent?.Invoke(this, code);

    public static IRecoveryBrowserPlatformAdapter Create(string profileDataPath)
        => Create(profileDataPath, useGtkOffscreen: true);

    public static IRecoveryBrowserPlatformAdapter CreateDialog(string profileDataPath)
        => Create(profileDataPath, useGtkOffscreen: false);

    private static IRecoveryBrowserPlatformAdapter Create(
        string profileDataPath,
        bool useGtkOffscreen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDataPath);
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRecoveryBrowserPlatformAdapter(profileDataPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxRecoveryBrowserPlatformAdapter(profileDataPath, useGtkOffscreen);
        }

        return new UnsupportedRecoveryBrowserPlatformAdapter(profileDataPath);
    }
}

internal sealed class WindowsRecoveryBrowserPlatformAdapter(string profileDataPath)
    : RecoveryBrowserPlatformAdapter(profileDataPath)
{
    private static readonly TimeSpan BrowserProcessExitTimeout = TimeSpan.FromSeconds(15);
    private CoreWebView2? _webView;
    private CoreWebView2Environment? _environment;
    private uint _browserProcessId;
    private TaskCompletionSource _browserProcessExited = CompletedBrowserProcessExit();

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
        DetachWebView();
        _isConfigured = false;
        if (platformHandle is not IWindowsWebView2PlatformHandle webView2Handle ||
            webView2Handle.CoreWebView2 == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ResetEnvironmentTracking();
            _webView = CoreWebView2.CreateFromComICoreWebView2(webView2Handle.CoreWebView2);
            _environment = _webView.Environment;
            _browserProcessId = _webView.BrowserProcessId;
            _browserProcessExited = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _environment.BrowserProcessExited += Environment_OnBrowserProcessExited;
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
            DetachWebView();
            ResetEnvironmentTracking();
        }
    }

    public override void Dispose()
    {
        DetachWebView();
        ResetEnvironmentTracking();
        _isConfigured = false;
    }

    public override Task ClearBrowsingDataAsync(CancellationToken cancellationToken) =>
        _webView?.Profile.ClearBrowsingDataAsync().WaitAsync(cancellationToken) ??
        Task.CompletedTask;

    public override Task WaitForProfileReleaseAsync(CancellationToken cancellationToken) =>
        _environment is null
            ? Task.CompletedTask
            : _browserProcessExited.Task.WaitAsync(BrowserProcessExitTimeout, cancellationToken);

    internal static void HardenSettings(CoreWebView2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
    }

    private void DetachWebView()
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

    private void ResetEnvironmentTracking()
    {
        if (_environment is not null)
        {
            _environment.BrowserProcessExited -= Environment_OnBrowserProcessExited;
        }

        _environment = null;
        _browserProcessId = 0;
        _browserProcessExited.TrySetResult();
    }

    private void Environment_OnBrowserProcessExited(
        object? sender,
        CoreWebView2BrowserProcessExitedEventArgs args)
    {
        if (args.BrowserProcessId == _browserProcessId)
        {
            _browserProcessExited.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedBrowserProcessExit()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
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
    private const string GtkWebKitLibrary = "libwebkit2gtk-4.1.so.0";
    private const uint AllWebsiteDataTypes = 0x7FFF;
    private static readonly AsyncReadyCallback WpeWebsiteDataClearedCallback =
        CompleteWpeWebsiteDataClear;
    private static readonly AsyncReadyCallback GtkWebsiteDataClearedCallback =
        CompleteGtkWebsiteDataClear;
    private static readonly RecoveryBrowserNativeAsyncOperationRegistry WebsiteDataClearOperations =
        new();
    private readonly PermissionSignalCallback _permissionCallback;
    private readonly DownloadSignalCallback _downloadCallback;
    private readonly TlsSignalCallback _tlsCallback;
    private ulong _permissionHandler;
    private ulong _downloadHandler;
    private ulong _tlsHandler;
    private IntPtr _webView;
    private IntPtr _downloadSignalOwner;
    private IntPtr _websiteDataManager;
    private LinuxRecoveryBrowserBackend _backend;
    private bool _storageHardeningFailed;
    private readonly bool _useGtkOffscreen;

    private bool _isConfigured;

    public LinuxRecoveryBrowserPlatformAdapter(
        string profileDataPath,
        bool useGtkOffscreen = true)
        : base(profileDataPath)
    {
        _useGtkOffscreen = useGtkOffscreen;
        _permissionCallback = DenyPermission;
        _downloadCallback = CancelDownload;
        _tlsCallback = RejectTlsError;
    }

    public override bool IsConfigured => _isConfigured;

    internal LinuxRecoveryBrowserBackend Backend => _backend;

    public override void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (_storageHardeningFailed)
        {
            return;
        }

        try
        {
            RecoveryBrowserFilePermissions.EnsurePrivateDirectory(ProfileDataPath);
            switch (args)
            {
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                    {
                        var dataDirectory = Path.Combine(ProfileDataPath, "data");
                        var cacheDirectory = Path.Combine(ProfileDataPath, "cache");
                        RecoveryBrowserFilePermissions.EnsurePrivateDirectory(dataDirectory);
                        RecoveryBrowserFilePermissions.EnsurePrivateDirectory(cacheDirectory);
                        wpe.DataDirectory = dataDirectory;
                        wpe.CacheDirectory = cacheDirectory;
                        // Prefer WPE when it is available. Avalonia falls through to WebKitGTK when
                        // WPE is unavailable, and the GTK path below is hardened separately.
                        wpe.PreferWebKitGtkInstead = false;
                        break;
                    }
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    // Keep all provider website state in memory. The Recovery Browser owns one
                    // web view for the account-bound session, so cookies remain usable within the
                    // session without writing browser state into a normal or persistent GTK profile.
                    gtk.EphemeralDataManager = true;
                    gtk.DisableCache = true;
                    // Avalonia's normal GTK host is X11/XID-only. Use the compositor-backed GTK
                    // adapter so the Recovery Browser also works when the Avalonia window is Wayland.
                    gtk.ExperimentalOffscreen = _useGtkOffscreen;
                    break;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _storageHardeningFailed = true;
            PublishSecurityEvent(RecoveryBrowserSecurityEventCode.PlatformHardeningUnavailable);
        }
    }

    public override void Attach(IPlatformHandle? platformHandle)
    {
        Detach();
        _isConfigured = false;
        if (_storageHardeningFailed)
        {
            return;
        }

        try
        {
            switch (platformHandle)
            {
                case ILinuxWpePlatformHandle wpeHandle when wpeHandle.WebKitWebView != IntPtr.Zero:
                    AttachWpe(wpeHandle.WebKitWebView);
                    break;
                case IGtkWebViewPlatformHandle gtkHandle when gtkHandle.WebKitWebView != IntPtr.Zero:
                    AttachGtk(gtkHandle.WebKitWebView);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Detach();
        }
    }

    public override void Dispose()
    {
        Detach();
        _isConfigured = false;
    }

    public override Task ClearBrowsingDataAsync(CancellationToken cancellationToken)
    {
        if (_websiteDataManager == IntPtr.Zero || _backend == LinuxRecoveryBrowserBackend.None)
        {
            return Task.CompletedTask;
        }

        var manager = _websiteDataManager;
        var backend = _backend;
        var callback = backend == LinuxRecoveryBrowserBackend.Wpe
            ? WpeWebsiteDataClearedCallback
            : GtkWebsiteDataClearedCallback;
        var callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);

        return WebsiteDataClearOperations.RunAsync(
            userData =>
            {
                if (backend == LinuxRecoveryBrowserBackend.Wpe)
                {
                    wpe_webkit_website_data_manager_clear(
                        manager,
                        AllWebsiteDataTypes,
                        timespan: 0,
                        IntPtr.Zero,
                        callbackPointer,
                        userData);
                }
                else
                {
                    gtk_webkit_website_data_manager_clear(
                        manager,
                        AllWebsiteDataTypes,
                        timespan: 0,
                        IntPtr.Zero,
                        callbackPointer,
                        userData);
                }
            },
            cancellationToken);
    }

    private void AttachWpe(IntPtr webView)
    {
        _webView = webView;
        _backend = LinuxRecoveryBrowserBackend.Wpe;
        var session = wpe_webkit_web_view_get_network_session(_webView);
        _websiteDataManager = wpe_webkit_web_view_get_website_data_manager(_webView);
        if (session == IntPtr.Zero || _websiteDataManager == IntPtr.Zero)
        {
            Detach();
            return;
        }

        wpe_webkit_network_session_set_persistent_credential_storage_enabled(session, false);
        _downloadSignalOwner = session;
        AttachSecuritySignals();
    }

    private void AttachGtk(IntPtr webView)
    {
        _webView = webView;
        _backend = LinuxRecoveryBrowserBackend.Gtk;
        var context = gtk_webkit_web_view_get_context(_webView);
        _websiteDataManager = context == IntPtr.Zero
            ? IntPtr.Zero
            : gtk_webkit_web_context_get_website_data_manager(context);
        if (context == IntPtr.Zero ||
            _websiteDataManager == IntPtr.Zero ||
            !gtk_webkit_website_data_manager_is_ephemeral(_websiteDataManager))
        {
            Detach();
            return;
        }

        gtk_webkit_website_data_manager_set_persistent_credential_storage_enabled(
            _websiteDataManager,
            false);
        _downloadSignalOwner = context;
        AttachSecuritySignals();
    }

    private void AttachSecuritySignals()
    {
        _permissionHandler = Connect(_webView, "permission-request", _permissionCallback);
        _tlsHandler = Connect(_webView, "load-failed-with-tls-errors", _tlsCallback);
        _downloadHandler = Connect(_downloadSignalOwner, "download-started", _downloadCallback);
        _isConfigured = _permissionHandler != 0 && _tlsHandler != 0 && _downloadHandler != 0;
        if (!_isConfigured)
        {
            Detach();
        }
    }

    private static ulong Connect(IntPtr instance, string signal, Delegate callback) =>
        instance == IntPtr.Zero
            ? 0
            : g_signal_connect_data(
                instance,
                signal,
                Marshal.GetFunctionPointerForDelegate(callback),
                IntPtr.Zero,
                IntPtr.Zero,
                0);

    private void Detach()
    {
        Disconnect(_webView, _permissionHandler);
        Disconnect(_webView, _tlsHandler);
        Disconnect(_downloadSignalOwner, _downloadHandler);

        _permissionHandler = 0;
        _downloadHandler = 0;
        _tlsHandler = 0;
        _webView = IntPtr.Zero;
        _downloadSignalOwner = IntPtr.Zero;
        _websiteDataManager = IntPtr.Zero;
        _backend = LinuxRecoveryBrowserBackend.None;
        _isConfigured = false;
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
        if (_backend == LinuxRecoveryBrowserBackend.Wpe)
        {
            wpe_webkit_permission_request_deny(request);
        }
        else if (_backend == LinuxRecoveryBrowserBackend.Gtk)
        {
            gtk_webkit_permission_request_deny(request);
        }
        else
        {
            return 1;
        }

        PublishSecurityEvent(RecoveryBrowserSecurityEventCode.PermissionBlocked);
        return 1;
    }

    private void CancelDownload(IntPtr sender, IntPtr download, IntPtr userData)
    {
        if (_backend == LinuxRecoveryBrowserBackend.Wpe)
        {
            wpe_webkit_download_cancel(download);
        }
        else if (_backend == LinuxRecoveryBrowserBackend.Gtk)
        {
            gtk_webkit_download_cancel(download);
        }

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
        // Returning false preserves WebKit's default TLS failure behavior. unpwn never creates
        // a certificate exception for the Recovery Browser.
        return 0;
    }

    private static void CompleteWpeWebsiteDataClear(
        IntPtr sourceObject,
        IntPtr result,
        IntPtr userData) =>
        CompleteWebsiteDataClear(
            sourceObject,
            result,
            userData,
            wpe_webkit_website_data_manager_clear_finish,
            "WPE WebKit did not clear the Recovery Browser profile data.");

    private static void CompleteGtkWebsiteDataClear(
        IntPtr sourceObject,
        IntPtr result,
        IntPtr userData) =>
        CompleteWebsiteDataClear(
            sourceObject,
            result,
            userData,
            gtk_webkit_website_data_manager_clear_finish,
            "WebKitGTK did not clear the Recovery Browser session data.");

    private static void CompleteWebsiteDataClear(
        IntPtr sourceObject,
        IntPtr result,
        IntPtr userData,
        WebsiteDataClearFinish finish,
        string failureMessage)
    {
        IntPtr error = IntPtr.Zero;
        var succeeded = finish(sourceObject, result, ref error);
        if (error != IntPtr.Zero)
        {
            g_error_free(error);
        }

        WebsiteDataClearOperations.Complete(
            userData,
            succeeded ? null : new IOException(failureMessage));
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

    private delegate bool WebsiteDataClearFinish(
        IntPtr manager,
        IntPtr result,
        ref IntPtr error);

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

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_web_view_get_network_session")]
    private static partial IntPtr wpe_webkit_web_view_get_network_session(IntPtr webView);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_web_view_get_website_data_manager")]
    private static partial IntPtr wpe_webkit_web_view_get_website_data_manager(IntPtr webView);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_website_data_manager_clear")]
    private static partial void wpe_webkit_website_data_manager_clear(
        IntPtr manager,
        uint types,
        long timespan,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_website_data_manager_clear_finish")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool wpe_webkit_website_data_manager_clear_finish(
        IntPtr manager,
        IntPtr result,
        ref IntPtr error);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_network_session_set_persistent_credential_storage_enabled")]
    private static partial void wpe_webkit_network_session_set_persistent_credential_storage_enabled(
        IntPtr session,
        [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_permission_request_deny")]
    private static partial void wpe_webkit_permission_request_deny(IntPtr request);

    [LibraryImport(WpeWebKitLibrary, EntryPoint = "webkit_download_cancel")]
    private static partial void wpe_webkit_download_cancel(IntPtr download);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_web_view_get_context")]
    private static partial IntPtr gtk_webkit_web_view_get_context(IntPtr webView);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_web_context_get_website_data_manager")]
    private static partial IntPtr gtk_webkit_web_context_get_website_data_manager(IntPtr context);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_website_data_manager_is_ephemeral")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gtk_webkit_website_data_manager_is_ephemeral(IntPtr manager);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_website_data_manager_set_persistent_credential_storage_enabled")]
    private static partial void gtk_webkit_website_data_manager_set_persistent_credential_storage_enabled(
        IntPtr manager,
        [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_website_data_manager_clear")]
    private static partial void gtk_webkit_website_data_manager_clear(
        IntPtr manager,
        uint types,
        long timespan,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_website_data_manager_clear_finish")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gtk_webkit_website_data_manager_clear_finish(
        IntPtr manager,
        IntPtr result,
        ref IntPtr error);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_permission_request_deny")]
    private static partial void gtk_webkit_permission_request_deny(IntPtr request);

    [LibraryImport(GtkWebKitLibrary, EntryPoint = "webkit_download_cancel")]
    private static partial void gtk_webkit_download_cancel(IntPtr download);
}

internal enum LinuxRecoveryBrowserBackend
{
    None,
    Wpe,
    Gtk,
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
