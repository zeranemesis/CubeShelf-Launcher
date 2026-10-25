using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CubeShelf.Desktop.Controls;

/// <summary>Bundled title artwork, with a readable fallback for games without a logo.</summary>
public sealed class GameTitleControl : UserControl
{
    // Four small, immutable images shared by the library and launch transition.
    private static readonly Dictionary<string, Bitmap?> Logos = new(StringComparer.Ordinal);
    private readonly Image _logo = new() { Stretch = Stretch.Uniform, MaxWidth = 430, MaxHeight = 130,
        HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _text = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };

    public GameTitleControl()
    {
        FontSize = 30;
        var grid = new Grid(); grid.Children.Add(_logo); grid.Children.Add(_text); Content = grid;
    }

    public void SetGame(string? gameId, string title)
    {
        var file = gameId switch
        {
            "GMSP01" => "sunshine.png",
            "GMPE01_00" or "GMPE01_01" => "mario-party-4.png",
            "GRSEAF" => "soulcalibur-ii.png",
            "G4QE01" => "mario-smash-football.png",
            _ => null
        };
        Bitmap? bitmap = null;
        if (file is not null && !Logos.TryGetValue(file, out bitmap))
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://{typeof(GameTitleControl).Assembly.GetName().Name}/Assets/Logos/{file}"));
                bitmap = Bitmap.DecodeToWidth(stream, 640, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { }
            Logos[file] = bitmap;
        }
        _logo.Source = bitmap; _logo.IsVisible = bitmap is not null;
        _text.Text = title; _text.IsVisible = bitmap is null;
        _text.Foreground = Foreground; _text.FontSize = FontSize;
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetName(_logo, title);
    }
}
