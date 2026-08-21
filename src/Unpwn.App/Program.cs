using Avalonia;

namespace Unpwn.App;

internal static class Program
{
    internal static DesktopE2EConfiguration? DesktopE2E { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        DesktopE2E = DesktopE2EConfiguration.LoadFromArguments(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .LogToTrace();
}
