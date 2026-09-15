using Avalonia;
using CubeShelf.Core.Diagnostics;

namespace CubeShelf.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            CrashReporter.WriteFallback(exception);
            Environment.ExitCode = -1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
