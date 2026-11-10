using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Styling;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private Window? _helpBubble, _helpWindow;
    private WindowsHelpHotkey? _helpHotkey;
    private CompanionControl? _helpCompanion;
    private string _helpGame = "Jeu sélectionné";
    private bool _helpTakingFrame, _helpClosingForFocus;
    private DispatcherTimer? _helpFocusFallback;
    private bool _helpExpanded;
    private IntPtr _helpGameWindow;
    private Point _helpAnchor = new(1, 0);
    private bool _helpHiddenByUser;
    private ContextMenu? _helpMenu;

    private void InitializeGameHelp()
    {
        if (!OperatingSystem.IsWindows()) return;
        _sessions.Started += id => Dispatcher.UIThread.Post(() =>
        {
            _helpGame = _catalogGames.FirstOrDefault(game => game.Id == id)?.Title ?? id;
            _helpHiddenByUser = false;
            ShowHelpBubble();
        });
        _sessions.Ended += _ => Dispatcher.UIThread.Post(() =>
        {
            if (_sessions.RunningGameIds.Count == 0) DisposeGameHelp();
        });
        Closed += (_, _) => DisposeGameHelp();
    }

    private void ShowHelpBubble()
    {
        if (_helpBubble is not null) return;
        var companion = new CompanionControl { Width = 60, Height = 76, ReduceMotion = _preferences.ReduceShelfMotion };
        _helpCompanion = companion;
        var button = new CompanionButton { Content = companion, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        AutomationProperties.SetName(button, "Compagnon Bip · aide locale");
        ToolTip.SetTip(button, "Cliquer pour l’aide · Maintenir et glisser pour déplacer · Ctrl+Maj+H");
        button.Click += (_, _) => ShowGameHelp();
        button.Dragged += MoveHelpBuddy;
        button.HideRequested += HideGameHelp;
        _helpMenu = button.ContextMenu;
        _helpBubble = new Window
        {
            Width = 64, Height = 80, CanResize = false, Topmost = true, ShowInTaskbar = false,
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            ShowActivated = false, SystemDecorations = SystemDecorations.None, Title = "Aide DecompShelf", Content = button
        };
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
            _helpBubble.Position = new PixelPoint(screen.WorkingArea.Right - (int)(76 * screen.Scaling), screen.WorkingArea.Y + (int)(16 * screen.Scaling));
        _helpBubble.Show();
        _helpBubble.Closed += (_, _) => companion.Dispose();
        _helpHotkey = new WindowsHelpHotkey(() => Dispatcher.UIThread.Post(ShowGameHelp), () => Dispatcher.UIThread.Post(UpdateGameHelpVisibility));
        if (!_helpHotkey.MonitorsFocus)
        {
            _helpFocusFallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _helpFocusFallback.Tick += (_, _) => UpdateGameHelpVisibility(); _helpFocusFallback.Start();
        }
        UpdateGameHelpVisibility();
    }

    private void UpdateGameHelpVisibility()
    {
        if (_helpBubble is null) return;
        var foreground = WindowsHelpHotkey.ForegroundWindow;
        if (_helpHiddenByUser) { SuspendGameHelp(); return; }
        if (_helpMenu?.IsOpen == true && WindowsGameCapture.ProcessId(foreground) == Environment.ProcessId &&
            foreground != (TryGetPlatformHandle()?.Handle ?? IntPtr.Zero)) return;
        var panelHandle = _helpWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var buddyHandle = _helpBubble.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var game = WindowsHelpHotkey.GameWindowForHelp(foreground, buddyHandle, panelHandle, _helpGameWindow);
        var id = _sessions.GameIdForProcess((int)WindowsGameCapture.ProcessId(game));
        if (id is null || WindowsGameCapture.ClientBounds(game) is null)
        {
            SuspendGameHelp(); return;
        }
        if (_helpWindow is not null && _helpWindow.Tag?.ToString() != id)
        {
            _helpClosingForFocus = true;
            try { _helpWindow.Close(); } finally { _helpClosingForFocus = false; }
        }
        _helpGameWindow = game;
        PlaceGameHelp(game);
        if (!_helpTakingFrame) _helpBubble.Show();
    }

    private void SuspendGameHelp()
    {
        _helpMenu?.Close();
        _helpWindow?.Hide(); _helpBubble?.Hide();
    }

    private void HideGameHelp()
    {
        _helpHiddenByUser = true;
        (_helpWindow?.Content as GameHelpPanel)?.Cancel();
        SuspendGameHelp();
        WindowsHelpHotkey.RestoreForeground(_helpGameWindow);
    }

    private void RevealGameHelp()
    {
        _helpHiddenByUser = false;
        _helpBubble?.Show();
        _helpWindow?.Show(); _helpWindow?.Activate();
    }

    private void PlaceGameHelp(IntPtr game)
    {
        if (WindowsGameCapture.ClientBounds(game) is not { } bounds) return;
        var scale = (Screens.ScreenFromPoint(new PixelPoint(bounds.X, bounds.Y)) ?? Screens.Primary)?.Scaling ?? 1;
        var placement = GameHelpPlacement.Place(new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), scale, _helpAnchor, _helpExpanded);
        if (_helpBubble is not null) _helpBubble.Position = placement.Buddy.Position;
        if (_helpWindow is null) return;
        if (_helpWindow.Content is GameHelpPanel panel)
            placement = GameHelpPlacement.Place(new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), scale, _helpAnchor, _helpExpanded,
                panel.HeightForWidth(placement.Panel.Width / scale));
        _helpWindow.Width = placement.Panel.Width / scale;
        _helpWindow.Height = placement.Panel.Height / scale;
        _helpWindow.Position = placement.Panel.Position;
    }

    private void MoveHelpBuddy(PixelPoint position)
    {
        if (WindowsGameCapture.ClientBounds(_helpGameWindow) is not { } bounds) return;
        var scale = (Screens.ScreenFromPoint(new PixelPoint(bounds.X, bounds.Y)) ?? Screens.Primary)?.Scaling ?? 1;
        _helpAnchor = GameHelpPlacement.Anchor(new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), scale, position);
        PlaceGameHelp(_helpGameWindow);
    }

    private void ShowGameHelp()
    {
        var foreground = OperatingSystem.IsWindows() ? WindowsHelpHotkey.ForegroundWindow : IntPtr.Zero;
        // The companion takes focus when clicked; capture and positioning still target its game.
        var previous = WindowsHelpHotkey.GameWindowForHelp(foreground,
            _helpBubble?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
            _helpWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, _helpGameWindow);
        var gameId = OperatingSystem.IsWindows() ? _sessions.GameIdForProcess((int)WindowsGameCapture.ProcessId(previous)) : null;
        if (gameId is null || WindowsGameCapture.ClientBounds(previous) is null) return;
        if (_helpWindow is not null && _helpWindow.Tag?.ToString() == gameId)
        {
            PlaceGameHelp(previous); RevealGameHelp();
            return;
        }
        if (_helpWindow is not null)
        {
            _helpClosingForFocus = true;
            try { _helpWindow.Close(); } finally { _helpClosingForFocus = false; }
        }
        _helpGameWindow = previous;
        _helpGame = _catalogGames.FirstOrDefault(game => game.Id == gameId)?.Title ?? gameId;
        ShowHelpBubble();
        _helpExpanded = false;
        _helpWindow = new Window { Title = "Aide · " + _helpGame, Tag = gameId, Width = 340, Height = 150, CanResize = false,
            SystemDecorations = SystemDecorations.None, ShowInTaskbar = false,
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Topmost = true, RequestedThemeVariant = ThemeVariant.Dark };
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
            _helpWindow.Position = new PixelPoint(screen.WorkingArea.Right - (int)(352 * screen.Scaling), screen.WorkingArea.Y + (int)(104 * screen.Scaling));
        var panel = new GameHelpPanel(_helpGame, Path.Combine(_paths.DataDirectory, "Assistance"), () => _helpWindow?.Close(),
            OperatingSystem.IsWindows() ? new WindowsGameHelpVoice() : null, _preferences.ReduceShelfMotion,
            compact: true, stateChanged: state => { if (_helpCompanion is not null) _helpCompanion.State = state; },
            capture: gameId is null ? null : cancellation => CaptureGameAsync(previous, cancellation),
            expanded: () => { _helpExpanded = true; PlaceGameHelp(previous); }, resized: () => PlaceGameHelp(previous));
        _helpWindow.Content = panel;
        var window = _helpWindow;
        _helpWindow.Closed += (_, _) =>
        {
            panel.Cancel();
            if (ReferenceEquals(_helpWindow, window)) _helpWindow = null;
            if (_helpCompanion is not null) _helpCompanion.State = CubeShelf.Assistance.CompanionState.Idle;
            if (OperatingSystem.IsWindows() && !_helpClosingForFocus) WindowsHelpHotkey.RestoreForeground(previous);
        };
        PlaceGameHelp(previous); RevealGameHelp();
    }

    private async Task<CubeShelf.Assistance.GameFrame> CaptureGameAsync(IntPtr game, CancellationToken cancellation)
    {
        if (_sessions.GameIdForProcess((int)WindowsGameCapture.ProcessId(game)) is null)
            throw new InvalidOperationException("Le jeu suivi est fermé. Relance-le depuis DecompShelf.");
        var panel = _helpWindow;
        _helpTakingFrame = true;
        panel?.Hide(); _helpBubble?.Hide();
        try
        {
            WindowsHelpHotkey.RestoreForeground(game);
            await Task.Delay(180, cancellation);
            if (_sessions.GameIdForProcess((int)WindowsGameCapture.ProcessId(game)) is null)
                throw new InvalidOperationException("La fenêtre du jeu a changé. Relance la demande.");
            return WindowsGameCapture.Capture(game, cancellation);
        }
        finally
        {
            _helpTakingFrame = false;
            var current = WindowsHelpHotkey.ForegroundWindow;
            if (!_helpHiddenByUser && _sessions.GameIdForProcess((int)WindowsGameCapture.ProcessId(current)) is not null)
            {
                _helpBubble?.Show();
                if (panel is not null && ReferenceEquals(panel, _helpWindow)) { panel.Show(); panel.Activate(); }
            }
        }
    }

    private void DisposeGameHelp()
    {
        _helpWindow?.Close(); _helpBubble?.Close(); _helpBubble = null;
        _helpCompanion = null;
        _helpMenu = null; _helpHiddenByUser = false;
        _helpGameWindow = IntPtr.Zero;
        _helpHotkey?.Dispose(); _helpHotkey = null;
        _helpFocusFallback?.Stop(); _helpFocusFallback = null;
    }
}
