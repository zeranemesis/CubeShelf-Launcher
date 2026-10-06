using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CubeShelf.Desktop;

/// <summary>
/// A notification in the corner of the screen, for when CubeShelf is out of sight: reduced to the
/// notification area, minimised, or behind another window. Drawn here rather than through each
/// system's notification service, which would mean one integration per platform for the same
/// three lines of text.
///
/// It never takes the focus -- whatever the user is typing into keeps the keyboard -- and goes
/// away on its own. A click brings CubeShelf back where the notification is about.
/// </summary>
internal sealed class NotificationWindow : Window
{
    private const double CardWidth = 380;
    private readonly int _slot;

    public NotificationWindow(string title, string body, Action onClick, int slot)
    {
        _slot = slot;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = CardWidth;
        SizeToContent = SizeToContent.Height;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Title = title;

        Bitmap? logo = null;
        try
        {
            logo = new Bitmap(AssetLoader.Open(new Uri("avares://CubeShelf/Assets/Brand/gamecube_logo.png")));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
        }

        Content = new Border
        {
            Background = Resource("Panel", Color.FromRgb(0x1b, 0x1f, 0x2a)),
            BorderBrush = Resource("Accent", Color.FromRgb(0x7c, 0x5c, 0xff)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Image { Source = logo, Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) },
                    new StackPanel
                    {
                        [Grid.ColumnProperty] = 1,
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = title, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Resource("Text", Colors.White) },
                            new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Foreground = Resource("Muted", Color.FromRgb(0xb8, 0xbd, 0xca)), FontSize = 12 }
                        }
                    }
                }
            }
        };

        PointerPressed += (_, _) =>
        {
            onClick();
            Close();
        };

        Opened += (_, _) => PlaceInCorner();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    /// <summary>Bottom right of the screen's working area -- above the taskbar -- stacked by slot.</summary>
    private void PlaceInCorner()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;

        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Math.Max(Bounds.Height, 80) * scale);
        var margin = (int)(16 * scale);
        var step = height + (int)(10 * scale);
        Position = new PixelPoint(area.Right - width - margin, area.Bottom - margin - height - _slot * step);
    }

    private static IBrush Resource(string key, Color fallback) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
