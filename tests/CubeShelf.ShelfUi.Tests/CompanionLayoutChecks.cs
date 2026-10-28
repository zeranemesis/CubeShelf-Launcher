using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CubeShelf.Desktop.Controls;

internal static class CompanionLayoutChecks
{
    public static void Run()
    {
        foreach (var bounds in new[] { new PixelRect(0, 0, 1280, 720), new PixelRect(-1600, 60, 800, 600), new PixelRect(20, 20, 320, 240) })
        foreach (var scale in new[] { 1d, 1.25, 2 })
        foreach (var anchor in new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1), new Point(.5, .5) })
        foreach (var expanded in new[] { false, true })
        {
            var (buddy, panel) = GameHelpPlacement.Place(bounds, scale, anchor, expanded);
            if (!bounds.Contains(buddy) || !bounds.Contains(panel) || panel.Width <= 0 || panel.Height <= 0)
                throw new Exception("Companion or panel escaped the game after movement, resize or DPI change.");
            if (bounds.Width / scale >= 640 && buddy.Intersects(panel)) throw new Exception("Help covered its companion despite available space.");
            var restored = GameHelpPlacement.Anchor(bounds, scale, buddy.Position);
            if (Math.Abs(restored.X - anchor.X) > .03 || Math.Abs(restored.Y - anchor.Y) > .03)
                throw new Exception("Dropping the companion changed its relative position.");
        }
        var topLeft = GameHelpPlacement.Place(new PixelRect(0, 0, 1280, 720), 1, new Point(0, 0), false);
        var bottomRight = GameHelpPlacement.Place(new PixelRect(0, 0, 1280, 720), 1, new Point(1, 1), true);
        if (topLeft.Panel.X <= topLeft.Buddy.Right || bottomRight.Panel.Right >= bottomRight.Buddy.X)
            throw new Exception("Panel failed to switch sides with its companion.");
        var clamped = GameHelpPlacement.Anchor(new PixelRect(0, 0, 1280, 720), 1, new PixelPoint(-500, 5000));
        if (clamped != new Point(0, 1)) throw new Exception("Dragging past the game did not clamp to its edges.");

        var button = new CompanionButton { Width = 64, Height = 80, Background = Brushes.Transparent, Content = "Bip" };
        var window = new Window { Width = 160, Height = 120, Content = button };
        int clicks = 0, moves = 0;
        button.Click += (_, _) => clicks++;
        button.Dragged += _ => moves++;
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        var start = button.TranslatePoint(new Point(20, 30), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(2, 1), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(2, 1), MouseButton.Left);
        if (clicks != 1 || moves != 0) throw new Exception($"A simple click or small pointer jitter failed to open help once: clicks={clicks}, moves={moves}, bounds={button.Bounds}, start={start}, hit={window.InputHitTest(start)?.GetType().Name}.");
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(30, 20), MouseButton.Left);
        if (clicks != 1 || moves == 0) throw new Exception("Dragging opened help or failed to move the companion.");
        var hides = 0; button.HideRequested += () => hides++;
        window.MouseDown(start, MouseButton.Right); window.MouseUp(start, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        if (!button.ContextMenu!.IsOpen || clicks != 1) throw new Exception("Right click did not open Bip's menu or opened help instead.");
        var hide = button.ContextMenu.Items.OfType<MenuItem>().Single();
        if (hide.Header?.ToString() != "Masquer Bip") throw new Exception("The hide action is missing.");
        hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); button.ContextMenu.Close();
        if (hides != 1) throw new Exception("The context menu failed to hide Bip once.");
        window.MouseDown(start, MouseButton.Left);
        window.Close();
        if (clicks != 1) throw new Exception("Losing capture triggered help.");
        Console.WriteLine("OK  companion click vs drag, right-click hide menu, edge clamping, adaptive panel sides, small windows, multiple monitors and DPI.");
    }
}
