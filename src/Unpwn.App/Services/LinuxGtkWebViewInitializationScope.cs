using System.Runtime.InteropServices;

namespace Unpwn.App.Services;

/// <summary>
/// Applies process-wide GTK/WebKit compatibility settings only while the Linux browser adapter
/// initializes. GTK requires its X11 backend alongside Avalonia's X11 window, and disabling the
/// DMABUF renderer avoids blank WebKitGTK surfaces on unsupported GBM graphics stacks.
/// </summary>
internal sealed partial class LinuxGtkWebViewInitializationScope : IDisposable
{
    private readonly string? _previousBackend;
    private readonly string? _previousDmaBufRenderer;
    private readonly bool _backendChanged;
    private readonly bool _dmaBufRendererChanged;
    private bool _disposed;

    private LinuxGtkWebViewInitializationScope()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable("GDK_BACKEND");
        if (!string.Equals(current, "x11", StringComparison.Ordinal) &&
            setenv("GDK_BACKEND", "x11", overwrite: 1) == 0)
        {
            _previousBackend = current;
            _backendChanged = true;
            Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        }

        var dmaBufRenderer = Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER");
        if (!string.Equals(dmaBufRenderer, "1", StringComparison.Ordinal) &&
            setenv("WEBKIT_DISABLE_DMABUF_RENDERER", "1", overwrite: 1) == 0)
        {
            _previousDmaBufRenderer = dmaBufRenderer;
            _dmaBufRendererChanged = true;
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        }
    }

    internal static LinuxGtkWebViewInitializationScope Enter() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            Restore("GDK_BACKEND", _previousBackend, _backendChanged);
            Restore(
                "WEBKIT_DISABLE_DMABUF_RENDERER",
                _previousDmaBufRenderer,
                _dmaBufRendererChanged);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void Restore(string name, string? previousValue, bool changed)
    {
        if (!changed)
        {
            return;
        }

        if (previousValue is null)
        {
            _ = unsetenv(name);
        }
        else
        {
            _ = setenv(name, previousValue, overwrite: 1);
        }

        Environment.SetEnvironmentVariable(name, previousValue);
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int unsetenv(string name);
}
