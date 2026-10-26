using Avalonia;
using CubeShelf.Core.Diagnostics;
using CubeShelf.Core.Library;
using System.Text.Json;

namespace CubeShelf.Desktop;

internal static class Program
{
    public const string DisplayName = "DecompShelf";
    public static ShelfProduct Product { get; private set; } = ShelfProduct.Resolve(null);
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var settingsFile = Path.Combine(AppContext.BaseDirectory, "shelf-product.json");
            var id = File.Exists(settingsFile) ? JsonSerializer.Deserialize<ShelfSettings>(File.ReadAllText(settingsFile))?.Console : null;
            var option = Array.IndexOf(args, "--shelf");
            if (option >= 0)
            {
                if (option + 1 >= args.Length) throw new ArgumentException("--shelf attend gamecube, wii ou switch.");
                id = args[option + 1];
            }
            Product = ShelfProduct.Resolve(id);
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

    private sealed record ShelfSettings(string Console);
}
