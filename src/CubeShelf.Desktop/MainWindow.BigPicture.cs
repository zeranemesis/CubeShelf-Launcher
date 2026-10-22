using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _bigPictureActive;
    private WindowState _bigPicturePreviousState;
    private double _bigPicturePreviousWidth, _bigPicturePreviousHeight;
    private Control? _bigPicturePreviousView;
    private GameCatalogEntry[] _bigPictureGames = [];
    private int _bigPictureIndex;
    private Bitmap? _bigPictureBitmap;
    private DispatcherTimer? _bigPictureTimer;
    private readonly Controls.BigPictureGamepad _bigPictureGamepad = new();
    private int _bigPictureLastInput;
    private long _bigPictureNextRepeat;

    private void InitializeBigPicture()
    {
        AddHandler(KeyDownEvent, BigPictureKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _bigPictureTimer?.Stop();
            BigPictureCover.Source = null;
            _bigPictureBitmap?.Dispose();
            _bigPictureGamepad.Dispose();
        };
    }

    private GameCatalogEntry? BigPictureGame => _bigPictureGames.Length == 0 ? null
        : GameVariants.Resolve(_catalogGames, _bigPictureGames[_bigPictureIndex], _paths);

    private void EnterBigPicture(object? sender, RoutedEventArgs args)
    {
        if (_bigPictureActive) return;
        _bigPicturePreviousState = WindowState;
        _bigPicturePreviousWidth = Width;
        _bigPicturePreviousHeight = Height;
        _bigPicturePreviousView = ContentViews.FirstOrDefault(view => view.IsVisible);
        _bigPictureGames = ShelfGames.OrderBy(game => game.Title).ToArray();
        _bigPictureIndex = Math.Max(0, Array.FindIndex(_bigPictureGames,
            game => _selectedGame is not null && PlayKey(game) == PlayKey(_selectedGame)));
        _bigPictureActive = true;
        DesktopSidebar.IsVisible = false;
        Grid.SetColumn(ViewHost, 0);
        Grid.SetColumnSpan(ViewHost, 2);
        ShowParityView(BigPictureView);
        WindowState = WindowState.FullScreen;
        RefreshBigPictureGame();
        BigPicturePlayButton.Focus();
        _bigPictureLastInput = -1; // Require a release after entering / returning from a dialog.
        _bigPictureTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Input, (_, _) => PollBigPictureGamepad());
        _bigPictureTimer.Start();
    }

    private void ExitBigPicture(object? sender, RoutedEventArgs args)
    {
        if (!_bigPictureActive) return;
        _bigPictureActive = false;
        _bigPictureTimer?.Stop();
        DesktopSidebar.IsVisible = true;
        Grid.SetColumn(ViewHost, 1);
        Grid.SetColumnSpan(ViewHost, 1);
        ShowParityView(_bigPicturePreviousView ?? LibraryView);
        WindowState = _bigPicturePreviousState;
        Width = _bigPicturePreviousWidth;
        Height = _bigPicturePreviousHeight;
        BigPictureCover.Source = null;
        _bigPictureBitmap?.Dispose();
        _bigPictureBitmap = null;
        BigPictureEntryButton.Focus();
    }

    private void MoveBigPictureGame(int step)
    {
        if (_bigPictureGames.Length == 0) return;
        _bigPictureIndex = (_bigPictureIndex + step + _bigPictureGames.Length) % _bigPictureGames.Length;
        RefreshBigPictureGame();
        BigPicturePlayButton.Focus();
    }

    private void PreviousBigPictureGame(object? sender, RoutedEventArgs args) => MoveBigPictureGame(-1);
    private void NextBigPictureGame(object? sender, RoutedEventArgs args) => MoveBigPictureGame(1);

    private void RefreshBigPictureGame()
    {
        var game = BigPictureGame;
        BigPictureCover.Source = null;
        _bigPictureBitmap?.Dispose();
        _bigPictureBitmap = game is null ? null : LoadBitmap(ResolveCoverPath(game.Covers.FirstOrDefault()?.Front));
        BigPictureCover.Source = _bigPictureBitmap;
        BigPictureTitle.Text = game?.Title ?? P7("Ta bibliothèque est vide", "Your library is empty");
        BigPictureMetadata.Text = game is null ? "" : string.Join("  •  ", new[] { game.Genre, game.Players, game.Year > 0 ? game.Year.ToString() : "" }.Where(value => value.Length > 0));
        BigPictureDescription.Text = game?.Description ?? P7("Ajoute un jeu depuis la bibliothèque pour commencer.", "Add a game from the library to get started.");
        BigPictureCount.Text = P7("Bibliothèque", "Library") + $" · {_bigPictureGames.Length}";
        BigPicturePosition.Text = _bigPictureGames.Length == 0 ? "0 / 0" : $"{_bigPictureIndex + 1} / {_bigPictureGames.Length}";
        BigPictureExitButton.Content = P7("Retour au bureau · Échap", "Back to desktop · Esc");
        BigPictureCancelButton.Content = P7("Annuler le lancement", "Cancel launch");
        BigPicturePreviousButton.IsEnabled = BigPictureNextButton.IsEnabled = _bigPictureGames.Length > 1;
        RefreshBigPictureStatus();
    }

    private void RefreshBigPictureStatus()
    {
        if (!_bigPictureActive) return;
        var game = BigPictureGame;
        if (game is not null) HydrateGamePathsPhase3(game);
        BigPicturePlayButton.Content = game is null ? P7("Aucun jeu", "No games") : QuickPlayLabel(game);
        BigPicturePlayButton.IsEnabled = game is not null && PlayIntentFor(game) is null;
        BigPictureCancelButton.IsVisible = game is not null && PlayIntentFor(game) is not null;
        BigPictureStatus.Text = game is null ? "" : QuickPlayHint(game);
    }

    private async void PlayBigPicture(object? sender, RoutedEventArgs args)
    {
        if (BigPictureGame is { } game && PlayIntentFor(game) is null) await RequestPlayAsync(game);
    }

    private void CancelBigPictureLaunch(object? sender, RoutedEventArgs args)
    {
        if (BigPictureGame is { } game) PlayIntentFor(game)?.Cancellation.Cancel();
    }

    private void BigPictureKeyDown(object? sender, KeyEventArgs args)
    {
        if (OwnedWindows.Any(window => window.IsVisible)) return;
        if (args.Key == Key.F11)
        {
            if (_bigPictureActive) ExitBigPicture(this, new RoutedEventArgs());
            else EnterBigPicture(this, new RoutedEventArgs());
            args.Handled = true;
            return;
        }
        if (!_bigPictureActive) return;
        switch (args.Key)
        {
            case Key.Left: MoveBigPictureGame(-1); break;
            case Key.Right: MoveBigPictureGame(1); break;
            case Key.Up: FocusBigPictureAction(-1); break;
            case Key.Down: FocusBigPictureAction(1); break;
            case Key.Escape: ExitBigPicture(this, new RoutedEventArgs()); break;
            case Key.Enter: ActivateBigPictureAction(); break;
            case Key.Tab: return; // Native focus traversal remains available.
            default: args.Handled = true; return;
        }
        args.Handled = true;
    }

    private Button[] BigPictureActions => new[] { BigPicturePlayButton, BigPictureCancelButton, BigPictureExitButton }
        .Where(button => button.IsVisible && button.IsEnabled).ToArray();

    private void FocusBigPictureAction(int step)
    {
        var actions = BigPictureActions;
        var index = Array.FindIndex(actions, button => button.IsFocused);
        actions[(Math.Max(0, index) + step + actions.Length) % actions.Length].Focus();
    }

    private void ActivateBigPictureAction()
    {
        var action = BigPictureActions.FirstOrDefault(button => button.IsFocused) ?? BigPictureActions.FirstOrDefault();
        action?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void PollBigPictureGamepad()
    {
        var input = _bigPictureGamepad.Read(out var connected);
        BigPictureHelp.Text = connected
            ? P7("← → / stick : choisir   •   ↑ ↓ : actions   •   A : valider   •   B : bureau", "← → / stick: browse   •   ↑ ↓: actions   •   A: select   •   B: desktop")
            : P7("← → Choisir   •   ↑ ↓ Actions   •   Entrée Jouer / valider   •   Échap Bureau   •   F11 Big Picture", "← → Browse   •   ↑ ↓ Actions   •   Enter Play / select   •   Esc Desktop   •   F11 Big Picture");
        if (!IsActive || WindowState == WindowState.Minimized || OwnedWindows.Any(window => window.IsVisible))
        {
            _bigPictureLastInput = -1;
            return;
        }
        if (input == 0) { _bigPictureLastInput = 0; return; }
        if (_bigPictureLastInput == -1) return;
        var now = Environment.TickCount64;
        var direction = input is >= 1 and <= 4;
        if (input == _bigPictureLastInput && (!direction || now < _bigPictureNextRepeat)) return;
        _bigPictureNextRepeat = now + (input == _bigPictureLastInput ? 180 : 450);
        _bigPictureLastInput = input;
        switch (input)
        {
            case 1: MoveBigPictureGame(-1); break;
            case 2: MoveBigPictureGame(1); break;
            case 3: FocusBigPictureAction(-1); break;
            case 4: FocusBigPictureAction(1); break;
            case 5: ActivateBigPictureAction(); break;
            case 6: ExitBigPicture(this, new RoutedEventArgs()); break;
        }
    }
}
