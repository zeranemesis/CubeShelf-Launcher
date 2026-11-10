using Avalonia;

namespace CubeShelf.Desktop.Controls;

internal static class GameHelpPlacement
{
    public static Point Anchor(PixelRect bounds, double scale, PixelPoint position)
    {
        var margin = Math.Min((int)(12 * scale), Math.Min(bounds.Width, bounds.Height) / 4);
        return new Point(Math.Clamp((position.X - bounds.X - margin) / Math.Max(1d, bounds.Width - 64 * scale - margin * 2), 0, 1),
            Math.Clamp((position.Y - bounds.Y - margin) / Math.Max(1d, bounds.Height - 80 * scale - margin * 2), 0, 1));
    }

    public static (PixelRect Buddy, PixelRect Panel) Place(PixelRect bounds, double scale, Point anchor, bool expanded, double? preferredHeight = null)
    {
        var margin = Math.Min((int)(12 * scale), Math.Min(bounds.Width, bounds.Height) / 4);
        var gap = (int)(8 * scale);
        var width = Math.Min((int)(64 * scale), bounds.Width - margin * 2);
        var height = Math.Min((int)(80 * scale), bounds.Height - margin * 2);
        var buddy = new PixelRect(bounds.X + margin + (int)(Math.Clamp(anchor.X, 0, 1) * (bounds.Width - width - margin * 2)),
            bounds.Y + margin + (int)(Math.Clamp(anchor.Y, 0, 1) * (bounds.Height - height - margin * 2)), width, height);
        var left = buddy.X - gap - bounds.X - margin;
        var right = bounds.Right - margin - buddy.Right - gap;
        var panelWidth = Math.Min((int)(320 * scale), bounds.Width - margin * 2);
        var panelHeight = Math.Min((int)((preferredHeight ?? (expanded ? 320 : 142)) * scale), bounds.Height - margin * 2);
        int x, y;
        if (Math.Max(left, right) >= 180 * scale)
        {
            panelWidth = Math.Min(panelWidth, Math.Max(left, right));
            x = left >= right ? buddy.X - gap - panelWidth : buddy.Right + gap;
            y = buddy.Y;
        }
        else
        {
            // shortcut: tiny game windows can overlap Bip; add a stacked compact layout if these sizes are needed.
            x = buddy.X;
            y = buddy.Y - bounds.Y > bounds.Bottom - buddy.Bottom ? buddy.Y - gap - panelHeight : buddy.Bottom + gap;
        }
        x = Math.Clamp(x, bounds.X + margin, bounds.Right - margin - panelWidth);
        y = Math.Clamp(y, bounds.Y + margin, bounds.Bottom - margin - panelHeight);
        return (buddy, new PixelRect(x, y, panelWidth, panelHeight));
    }
}
