using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly List<Bitmap> _bigPictureHomeImages = new();
    private readonly List<Button> _bigPictureResumeButtons = new();

    private void OpenBigPictureHome(object? sender, RoutedEventArgs args)
    {
        if (!_bigPictureActive) return;
        BigPictureHomeTitle.Text = P7("CubeShelf · À toi de jouer", "CubeShelf · Time to play");
        BigPictureCollectionButton.Content = P7("▦ Bibliothèque", "▦ Library");
        BigPictureHomeFriendsButton.Content = P7("Amis et invitations", "Friends and invites");
        BigPictureHomeSettingsButton.Content = P7("Paramètres", "Settings");
        BigPictureHomeSearch.Watermark = P7("Rechercher un jeu…", "Search games…");
        ShowParityView(BigPictureHomeView);
        RefreshBigPictureResume();
        RefreshFriendsView();
        RefreshBigPictureHomeFriends();
        (_bigPictureResumeButtons.FirstOrDefault() ?? BigPictureCollectionButton).Focus();
    }

    private void OpenBigPictureCollection(object? sender, RoutedEventArgs args)
    {
        RefreshBigPictureGame(); ShowParityView(BigPictureView); BigPicturePlayButton.Focus();
    }

    private void OpenBigPictureFriends(object? sender, RoutedEventArgs args) => ParityShowFriends(sender, args);
    private void OpenBigPictureSettings(object? sender, RoutedEventArgs args) => ParityShowSettings(sender, args);

    private void BigPictureHomeSearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (_bigPictureActive && BigPictureHomeView.IsVisible) RefreshBigPictureResume();
    }

    private void RefreshBigPictureResume()
    {
        if (!_bigPictureActive || !BigPictureHomeView.IsVisible) return;
        DisposeBigPictureHomeImages();
        var search = (BigPictureHomeSearch.Text ?? "").Trim();
        var games = ShelfGames.Where(game => search.Length == 0 || game.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(game => _catalogGames.Where(item => PlayKey(item) == game.Id).Max(item => item.LastPlayedAt))
            .ThenBy(game => game.Title).Take(search.Length == 0 ? 4 : 8).ToArray();
        BigPictureResumeTitle.Text = search.Length == 0 ? P7("Reprendre", "Jump back in") : P7("Résultats", "Results");
        BigPictureResumeHint.Text = games.Length == 0 ? P7("Aucun jeu trouvé.", "No games found.")
            : P7("Lance directement un jeu, ou ouvre sa fiche pour choisir tes mods et jouer en ligne.", "Play directly, or open the game to choose mods and play online.");
        foreach (var shelfGame in games)
        {
            var game = GameVariants.Resolve(_catalogGames, shelfGame, _paths);
            HydrateGamePathsPhase3(game);
            var content = new StackPanel { Spacing = 12 };
            var cover = LoadBitmap(ResolveCoverPath(game.Covers.FirstOrDefault()?.Front));
            if (cover is not null) _bigPictureHomeImages.Add(cover);
            content.Children.Add(new Border
            {
                Height = 220, CornerRadius = new CornerRadius(10), ClipToBounds = true,
                Child = new Image { Source = cover, Stretch = Stretch.Uniform }
            });
            content.Children.Add(new TextBlock { Text = game.Title, FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
            var play = new Button { Tag = game, Content = QuickPlayLabel(game), FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                IsEnabled = PlayIntentFor(game) is null };
            play.Classes.Add("primary");
            play.Click += async (_, _) => await RequestPlayAsync(GameVariants.Resolve(_catalogGames, shelfGame, _paths));
            _bigPictureResumeButtons.Add(play); content.Children.Add(play);
            var details = new Button { Content = OnlineCompanion.IsSupported(game) ? P7("Mods et jeu en ligne", "Mods and online play") : P7("Mods et options", "Mods and options"), FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            details.Click += (_, _) =>
            {
                var index = Array.FindIndex(_bigPictureGames, item => item.Id == shelfGame.Id);
                if (index >= 0) SelectBigPictureGame(index);
                OpenBigPictureCollection(this, new RoutedEventArgs());
            };
            content.Children.Add(details);
            BigPictureResumeCards.Children.Add(new Border
            {
                Width = 280, Margin = new Thickness(0, 0, 16, 16), Padding = new Thickness(16), CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.Parse(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? "#F8F9FC" : "#1A2538")), Child = content
            });
        }
    }

    private void RefreshBigPictureResumeStatus()
    {
        foreach (var button in _bigPictureResumeButtons)
            if (button.Tag is GameCatalogEntry stored)
            {
                var game = GameVariants.Resolve(_catalogGames, stored, _paths);
                button.Content = QuickPlayLabel(game); button.IsEnabled = PlayIntentFor(game) is null;
            }
    }

    private void RefreshBigPictureHomeFriends()
    {
        if (!_bigPictureActive || !BigPictureHomeView.IsVisible) return;
        BigPictureHomeInvitesTitle.Text = P7("Tes amis t’attendent", "Friends are waiting");
        BigPictureHomeInvites.Children.Clear();
        var invites = HasIdentity && FriendsList.ItemsSource is IEnumerable<FriendRow> rows
            ? rows.Where(row => row.CanJoin && row.JoinPayload.Length > 0).Take(8).ToArray() : [];
        foreach (var friend in invites)
        {
            var button = new Button
            {
                Tag = friend, Content = P7($"Rejoindre {friend.Name} · {friend.InviteGameTitle}", $"Join {friend.Name} · {friend.InviteGameTitle}"),
                FontSize = 19, Padding = new Thickness(20, 14), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Click += JoinFriendInvite;
            BigPictureHomeInvites.Children.Add(button);
        }
        if (invites.Length == 0) BigPictureHomeInvites.Children.Add(new TextBlock
        {
            Text = P7("Aucune invitation disponible. Retrouve tes amis depuis « Amis et invitations ».", "No invitations available. Find friends under “Friends and invites”."),
            FontSize = 16, TextWrapping = TextWrapping.Wrap
        });
    }

    private void DisposeBigPictureHomeImages()
    {
        BigPictureResumeCards.Children.Clear(); _bigPictureResumeButtons.Clear();
        foreach (var bitmap in _bigPictureHomeImages) bitmap.Dispose();
        _bigPictureHomeImages.Clear();
    }
}
