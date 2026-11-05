using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Text.Json;

namespace CubeShelf.Desktop.Controls;

/// <summary>Bundled title artwork, with a readable fallback for games without a logo.</summary>
public sealed class GameTitleControl : UserControl
{
    private sealed record LogoAsset(string Console, string Title, string File, int Width, int Height);
    private static readonly Lazy<Dictionary<string, LogoAsset>> Catalog = new(() =>
    {
        using var stream = AssetLoader.Open(AssetUri("catalog.json"));
        return (JsonSerializer.Deserialize<LogoAsset[]>(stream) ?? [])
            .ToDictionary(logo => logo.Console + "\0" + logo.Title, StringComparer.Ordinal);
    });
    private static Uri AssetUri(string file) => new($"avares://{typeof(GameTitleControl).Assembly.GetName().Name}/Assets/Logos/{file}");
    private Bitmap? _bitmap;
    private string? _loadedFile, _gameId, _console;
    private string _gameTitle = "";
    private readonly Image _logo = new() { Stretch = Stretch.Uniform, MaxWidth = 430, MaxHeight = 130,
        HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _text = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    public bool HasArtwork => _logo.IsVisible;

    public GameTitleControl()
    {
        FontSize = 30;
        var grid = new Grid(); grid.Children.Add(_logo); grid.Children.Add(_text); Content = grid;
        DetachedFromVisualTree += (_, _) => ReleaseLogo();
        AttachedToVisualTree += (_, _) => SetGame(_gameId, _gameTitle, _console);
    }

    private void ReleaseLogo()
    {
        _logo.Source = null;
        _bitmap?.Dispose(); _bitmap = null; _loadedFile = null;
    }

    public void SetGame(string? gameId, string title, string? console = null)
    {
        _gameId = gameId; _gameTitle = title; _console = console;
        Catalog.Value.TryGetValue(console + "\0" + title, out var asset);
        var file = asset?.File ?? (console is null or "gamecube" ? gameId switch
        {
            "GMSP01" => "sunshine.png",
            "GMPE01_00" or "GMPE01_01" => "mario-party-4.png",
            "GRSEAF" => "soulcalibur-ii.png",
            "G4QE01" => "mario-smash-football.png",
            _ => null
        } : null);
        if (file != _loadedFile)
        {
            ReleaseLogo();
            if (file is not null) try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Logos", file);
                using var stream = asset is not null && File.Exists(path) ? File.OpenRead(path) : AssetLoader.Open(AssetUri(file));
                _bitmap = asset is { Width: > 0, Height: > 0 } && asset.Height * 640L > asset.Width * 260L
                    ? Bitmap.DecodeToHeight(stream, Math.Min(260, asset.Height), BitmapInterpolationMode.HighQuality)
                    : Bitmap.DecodeToWidth(stream, Math.Min(640, asset?.Width ?? 640), BitmapInterpolationMode.HighQuality);
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { }
            _loadedFile = file;
        }
        _logo.Source = _bitmap; _logo.IsVisible = _bitmap is not null;
        _text.Text = title; _text.IsVisible = _bitmap is null;
        _text.Foreground = Foreground; _text.FontSize = FontSize;
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetName(_logo, title);
    }
}
