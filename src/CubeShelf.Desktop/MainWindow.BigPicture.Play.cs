using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<Control, Thickness> _bigPicturePageMargins = new();
    private GameCatalogEntry? _bigPictureModsGame;

    private void UpdateBigPictureSubnav(Control view)
    {
        if (BigPictureSubnav is null) return;
        var show = _bigPictureActive && view != BigPictureView;
        BigPictureSubnav.IsVisible = show;
        foreach (var (page, margin) in _bigPicturePageMargins) page.Margin = margin;
        _bigPicturePageMargins.Clear();
        if (!show) return;
        _bigPicturePageMargins[view] = view.Margin;
        view.Margin = new Thickness(view.Margin.Left, view.Margin.Top + 64, view.Margin.Right, view.Margin.Bottom);
        BigPictureSubnavTitle.Text = "CubeShelf · " + P7("Big Picture", "Big Picture");
        BigPictureReturnButton.Content = P7("← Retour à la collection · Échap / B", "← Back to collection · Esc / B");
    }

    private void ReturnToBigPicture(object? sender, RoutedEventArgs args)
    {
        if (!_bigPictureActive) return;
        RefreshBigPictureGame();
        ShowParityView(BigPictureView);
        BigPicturePlayButton.Focus();
        _bigPictureLastInput = -1;
    }

    private void OpenBigPictureMods(object? sender, RoutedEventArgs args)
    {
        if (BigPictureGame is not { } game || PlayIntentFor(game) is not null || RunningGameFor(game) is not null) return;
        _bigPictureModsGame = game;
        RefreshBigPictureModChoices();
        ShowParityView(BigPictureModsView);
        (BigPictureModChoices.Children.OfType<Button>().FirstOrDefault() ?? BigPictureBrowseModsButton).Focus();
    }

    private void RefreshBigPictureModChoices()
    {
        if (_bigPictureModsGame is not { } game) return;
        var manager = new PortableModManager(_paths, game.ModLibraryId);
        var disabled = manager.GetPlayerDisabled().ToHashSet();
        var unreachable = manager.AnalyzeLayout().Select(item => item.Id).ToHashSet();
        var mods = manager.GetInstalled().OrderByDescending(mod => mod.Priority).ThenBy(mod => mod.Name).ToArray();
        BigPictureModsTitle.Text = game.Title + " · Mods";
        BigPictureBrowseModsButton.Content = P7("Trouver et installer des mods", "Find and install mods");
        BigPictureModsSummary.Text = P7("Choisis les mods actifs pour tes prochaines parties. La création d’un salon propose aussi sa propre sélection.",
            "Choose active mods for your next games. Room creation also offers its own selection.");
        BigPictureModChoices.Children.Clear();
        foreach (var mod in mods)
        {
            var active = mod.Enabled && !disabled.Contains(mod.Id);
            var row = new Button
            {
                Tag = mod.Id, FontSize = 19, Padding = new Thickness(22, 16),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                IsEnabled = active || !unreachable.Contains(mod.Id),
                Content = new TextBlock
                {
                    Text = (active ? "☑ " : "☐ ") + mod.Name + (unreachable.Contains(mod.Id)
                        ? P7(" · Format incompatible", " · Incompatible format") : ""),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            row.Click += (_, _) =>
            {
                try
                {
                    manager.SetEnabled(mod.Id, !active);
                    RefreshBigPictureModChoices();
                    BigPictureModChoices.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, mod.Id))?.Focus();
                    RefreshBigPictureStatus();
                }
                catch (Exception exception) { ShowToastParity("Mods", exception.Message); }
            };
            BigPictureModChoices.Children.Add(row);
        }
        if (mods.Length == 0) BigPictureModChoices.Children.Add(new TextBlock
        {
            Text = P7("Aucun mod installé pour ce jeu.", "No mods installed for this game."), FontSize = 21
        });
        var conflicts = manager.AnalyzeConflicts();
        if (conflicts.Count > 0) BigPictureModsSummary.Text += P7(
            $" {conflicts.Count} conflit(s) : la priorité des mods détermine les fichiers utilisés.",
            $" {conflicts.Count} conflict(s): mod priority determines which files are used.");
    }

    private void BrowseBigPictureMods(object? sender, RoutedEventArgs args)
    {
        if (_bigPictureModsGame is not { } game) return;
        SelectCatalogGame(game, showGameView: false);
        ParityOpenMods(sender, args);
    }

    private async void OpenBigPictureOnline(object? sender, RoutedEventArgs args)
    {
        if (BigPictureGame is not { } game || !OnlineCompanion.IsSupported(game)) return;
        await PrepareForPlayAsync(game, () =>
        {
            // Reuse the actual room directory, host/join flow and room mod checks.
            // Preparing for online must not launch a local game first.
            _onlineGameId = game.Id;
            _roomTags = null;
            _setupMods = null;
            ParityShowOnline(sender, args);
            CreateRoomButton.Focus();
            return Task.CompletedTask;
        });
    }

    private void HandleBigPicturePageInput(int input)
    {
        if (input == 6) { ReturnToBigPicture(this, new RoutedEventArgs()); return; }
        var page = ContentViews.FirstOrDefault(view => view.IsVisible);
        if (page is null) return;
        var controls = page.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control.IsEnabled && control.Focusable
                && control is Button or TextBox or ComboBox).Append(BigPictureReturnButton).ToArray();
        var focused = Array.FindIndex(controls, control => control.IsFocused || control.IsKeyboardFocusWithin);
        if (input is >= 1 and <= 4 && controls.Length > 0)
        {
            var step = input is 1 or 3 ? -1 : 1;
            controls[(Math.Max(0, focused) + step + controls.Length) % controls.Length].Focus();
        }
        else if (input == 5 && focused >= 0)
        {
            if (controls[focused] is CheckBox check) check.IsChecked = check.IsChecked != true;
            else if (controls[focused] is RadioButton radio) radio.IsChecked = true;
            else if (controls[focused] is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (controls[focused] is ComboBox combo && combo.ItemCount > 0)
                combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.ItemCount;
        }
    }

    private bool BigPicturePageHasTextFocus() => ContentViews.Where(view => view.IsVisible)
        .SelectMany(view => view.GetVisualDescendants().OfType<Control>())
        .Any(control => control is TextBox or ComboBox && control.IsKeyboardFocusWithin);
}
