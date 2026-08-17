using System.Runtime.InteropServices;

namespace Unpwn.App.Services;

/// <summary>
/// Temporarily exposes the X11 GDK backend while Avalonia.Controls.WebView initializes
/// WebKitGTK on Linux. The offscreen/compositor host removes the XID-parent dependency,
/// but WebKitGTK 12.1 still initializes through GTK's X11 GDK backend.
/// </summary>
internal sealed class LinuxGtkWebViewInitializationScope : IDisposable
{
    private readonly string? _previousBackend;
    private readonly bool _changed;
    private bool _disposed;

    private LinuxGtkWebViewInitializationScope()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable("GDK_BACKEND");
        if (string.IsNullOrWhiteSpace(current) ||
            string.Equals(current, "x11", StringComparison.Ordinal))
        {
            return;
        }

        // Environment.SetEnvironmentVariable alone is insufficient here: GTK reads the native
        // process environment. Keep managed and native views synchronized for the duration of
        // WebKitGTK initialization, matching the upstream WebView 12.1 ForceX11GdkBackend behavior.
        if (setenv("GDK_BACKEND", "x11", overwrite: 1) != 0)
        {
            return;
        }

        _previousBackend = current;
        _changed = true;
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
    }

    internal static LinuxGtkWebViewInitializationScope Enter() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_changed || _previousBackend is null)
        {
            return;
        }

        try
        {
            _ = setenv("GDK_BACKEND", _previousBackend, overwrite: 1);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        Environment.SetEnvironmentVariable("GDK_BACKEND", _previousBackend);
    }

    [DllImport("libc", EntryPoint = "setenv", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);
}
