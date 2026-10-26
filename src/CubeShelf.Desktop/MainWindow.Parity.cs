using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Mods;
using System.Runtime.InteropServices;
using System.Text;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly List<DesktopGameCard> _parityCards = new();
    private bool _favoritesOnly;
    private bool _wizardShown;
    private CancellationTokenSource? _toastCancellation;

    private async void OnParityWindowOpened(object? sender, EventArgs args)
    {
        if (LibrarySortCombo.SelectedIndex < 0)
            LibrarySortCombo.SelectedIndex = 1;

        RefreshLibraryParity();
        RefreshParityGameDetails();
        RefreshStorageParity();
        UpdateResponsiveParity();

        if (!_wizardShown && !_preferences.FirstRunCompleted)
        {
            _wizardShown = true;
            await ShowFirstRunWizardAsync();
        }

        // Someone who went through the wizard before its network page existed, or skipped it,
        // hears about it once now -- before CubeShelf ever looks.
        if (!_preferences.MeshDiscoveryNoticeSeen) await ShowMeshDiscoveryNoticeAsync();
        var startupArgs = Environment.GetCommandLineArgs();
        if (!startupArgs.Contains("--desktop", StringComparer.OrdinalIgnoreCase) &&
            (_preferences.StartInBigPicture || startupArgs.Contains("--big-picture", StringComparer.OrdinalIgnoreCase)))
            EnterBigPicture(this, new RoutedEventArgs());
    }

    private async Task ShowMeshDiscoveryNoticeAsync()
    {
        var english = UiLocalization.IsEnglish(_preferences.Language);
        var dialog = CreatePhase7Dialog(MeshDiscoveryNotice.Title(english), 640, 420);
        var box = new CheckBox { Content = MeshDiscoveryNotice.Box(english), IsChecked = _preferences.MeshPublicDiscovery };
        var ok = new Button { Content = "OK", Classes = { "primary" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = MeshDiscoveryNotice.Body(english), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new TextBlock { Text = MeshDiscoveryNotice.Costs(english), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Foreground = Avalonia.Media.Brushes.Gray },
                    ok
                }
            }
        };
        await dialog.ShowDialog(this);

        // Closed any way at all, the message was seen; what the box says is the answer.
        _preferences = _preferences with { MeshDiscoveryNoticeSeen = true, MeshPublicDiscovery = box.IsChecked == true };
        _preferencesStore.Save(_preferences);
        ApplyPreferences();
        EnsureMeshDiscovery();
    }

    private void OnParityWindowSizeChanged(object? sender, SizeChangedEventArgs args) =>
        UpdateResponsiveParity();

    private void UpdateResponsiveParity()
    {
        if (GameCase is null || SelectedTitleText is null) return;
        var width = Bounds.Width;
        if (width < 1120)
        {
            GameCase.Width = 300;
            GameCase.Height = 375;
            SelectedTitleText.FontSize = 36;
        }
        else if (width < 1360)
        {
            GameCase.Width = 380;
            GameCase.Height = 475;
            SelectedTitleText.FontSize = 44;
        }
        else
        {
            GameCase.Width = 465;
            GameCase.Height = 580;
            SelectedTitleText.FontSize = 54;
        }
    }

    private void ParityShowLibrary(object? sender, RoutedEventArgs args)
    {
        _favoritesOnly = false;
        LibrarySectionTitle.Text = UiLocalization.Get(_preferences.Language, "LibraryTitle");
        RefreshLibraryParity();
        ShowParityView(LibraryView);
    }

    private void ParityShowFavorites(object? sender, RoutedEventArgs args)
    {
        _favoritesOnly = true;
        LibrarySectionTitle.Text = UiLocalization.Get(_preferences.Language, "FavoritesTitle");
        RefreshLibraryParity();
        ShowParityView(LibraryView);
    }

    private void ParityOpenMods(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null)
        {
            ParityShowLibrary(sender, args);
            return;
        }
        ShowMods(sender, args);
    }

    private void ParityShowDownloads(object? sender, RoutedEventArgs args)
    {
        ShowDownloads(sender, args);
    }

    private void ParityShowSettings(object? sender, RoutedEventArgs args)
    {
        ShowSettings(sender, args);
        RefreshStorageParity();
        // The network and RetroAchievements cards live here.
        RefreshMeshUi();
        RefreshRetroAchievementsUi();
    }

    private void ParityBackToGame(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null)
        {
            ParityShowLibrary(sender, args);
            return;
        }
        ShowParityView(GameView);
        RefreshParityGameDetails();
    }

    private IReadOnlyList<Control>? _contentViews;

    /// <summary>
    /// The pages that take turns filling the window, read from the tree rather than listed here.
    /// Listing them meant every new page had to be remembered in several places at once, and
    /// forgetting one left two pages drawn on top of each other. A page now joins simply by
    /// carrying Classes="view" in the markup.
    ///
    /// The overlays -- the toast host, the connectivity banner, the theme fade -- deliberately
    /// carry no such class, so they are never hidden by a page change.
    /// </summary>
    private IReadOnlyList<Control> ContentViews => _contentViews ??=
        ViewHost.Children.OfType<Control>().Where(child => child.Classes.Contains("view")).ToArray();

    private void ShowParityView(Control view)
    {
        foreach (var candidate in ContentViews)
            candidate.IsVisible = ReferenceEquals(candidate, view);
        UpdateBigPictureSubnav(view);
    }

    /// <summary>
    /// The marker class is a string, so a typo would silently produce a page that never hides.
    /// Cheap to check once at startup, and only worth the developer's time.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void VerifyContentViewsAreTagged()
    {
        foreach (var child in ViewHost.Children.OfType<Control>())
            if (child.Name?.EndsWith("View", StringComparison.Ordinal) == true &&
                !child.Classes.Contains("view"))
                throw new InvalidOperationException(
                    $"{child.Name} est une vue de contenu mais n’a pas Classes=\"view\".");
    }

    private void LibrarySearchChanged(object? sender, TextChangedEventArgs args)
    {
        if (!_changingShelf) RefreshLibraryParity();
    }
    private void LibrarySortChanged(object? sender, SelectionChangedEventArgs args) => RefreshLibraryParity();

    private void RefreshLibraryParity()
    {
        if (LibraryCards is null) return;
        var previousCards = _parityCards.ToArray();
        _parityCards.Clear();

        // Keep the original game on the shelf; active mods only select its launch configuration.
        IEnumerable<GameCatalogEntry> query = ShelfGames;
        if (_favoritesOnly) query = query.Where(game => game.IsFavorite);

        var search = LibrarySearchBox?.Text?.Trim() ?? "";
        if (search.Length > 0)
        {
            query = query.Where(game =>
                game.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                game.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                game.Genre.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        query = (LibrarySortCombo?.SelectedIndex ?? 1) switch
        {
            0 => query.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => query.OrderByDescending(game => game.TotalPlaySeconds)
                      .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(game => game.LastPlayedAt ?? DateTimeOffset.MinValue)
                      .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        foreach (var game in query)
        {
            var card = BuildCard(game);
            ApplyQuickPlayCard(card);
            _parityCards.Add(card);
        }

        LibraryCards.ItemsSource = _parityCards.ToArray();
        // Acquire the next cards before releasing the old ones, keeping reused art pinned in cache.
        foreach (var card in previousCards) card.Dispose();
        var english = UiLocalization.IsEnglish(_preferences.Language);
        var console = ShelfProduct.Resolve(_activeShelfConsole).Console;
        Resources["BrandSubtitle"] = english ? console + " library" : "Bibliothèque " + console;
        Resources["SelectedGame"] = "NINTENDO " + console.ToUpperInvariant() + (english ? " • SELECTED GAME" : " • JEU SÉLECTIONNÉ");
        LibrarySectionTitle.Text = _favoritesOnly ? UiLocalization.Get(_preferences.Language, "FavoritesTitle")
            : english ? "Your " + console + " games" : "Tes jeux " + console;
        LibraryUpdatesText.Text = _favoritesOnly
            ? english ? $"{_parityCards.Count} favorite(s)" : $"{_parityCards.Count} favori(s)"
            : english ? $"{_parityCards.Count} game(s)" : $"{_parityCards.Count} jeu(x)";
        EmptyLibraryText.IsVisible = _parityCards.Count == 0;
    }

    private DesktopGameCard BuildCard(GameCatalogEntry game)
    {
        var coverPath = ResolveCoverPath(game.Covers.FirstOrDefault()?.Front);
        var runtime = _installer.GetStatus(game.Id);
        var disc = ResolveApplicationPath(game.DiscImage);
        var executable = runtime.IsInstalled ? runtime.ExecutablePath : ResolveApplicationPath(game.Executable);
        var runtimeManagesDisc = game.DataPreparation == GameDataPreparation.Runtime;
        var prepared = !runtimeManagesDisc && File.Exists(executable) && _gameData.IsPrepared(game, executable);
        var english = UiLocalization.IsEnglish(_preferences.Language);
        var last = game.LastPlayedAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? (english ? "Never" : "Jamais");
        var update = _gameUpdateSnapshots.TryGetValue(game.Id, out var snapshot)
            ? snapshot.RuntimeUpdateAvailable
                ? (english ? $"⬇ {game.RuntimeName} update" : $"⬇ Mise à jour {game.RuntimeName}")
                : snapshot.RuntimeUpdatePendingBuild
                    ? (english ? "◷ Build pending" : "◷ Build en attente")
                    : snapshot.SourceUpdateAvailable
                        ? (english ? "↻ Newer source" : "↻ Source plus récente")
                        : (english ? "✓ Up to date" : "✓ À jour")
            : "";
        var card = new DesktopGameCard(
            game,
            LoadCardCover(coverPath),
            game.IsFavorite ? "★" : "",
            game.RuntimeOwnsSetup ? "◆ Port natif" : File.Exists(disc) ? "✓ ISO/RVZ" : "○ ISO/RVZ",
            runtime.IsInstalled || File.Exists(executable)
                ? $"✓ {game.RuntimeName}"
                : $"○ {game.RuntimeName}",
            // A runtime that owns its disc has no CubeShelf data step to report.
            runtimeManagesDisc
                ? (english ? "◆ Runtime-managed" : "◆ Géré par le runtime")
                : prepared
                    ? (english ? "✓ Data" : "✓ Données")
                    : (english ? "○ Data" : "○ Données"),
            english
                ? $"{game.PlayCount} launches • {FormatPlayTime(game.TotalPlaySeconds)} • {last}"
                : $"{game.PlayCount} lancements • {FormatPlayTime(game.TotalPlaySeconds)} • {last}",
            update);
        card.Versions = PortVersionsFor(game);
        card.SelectedVersion = card.Versions.FirstOrDefault(choice => choice.GameId == game.Id);
        return card;
    }

    private void SelectLibraryGame(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DesktopGameCard card }) return;
        SelectCatalogGame(card.Game);
    }

    private void SelectCatalogGame(GameCatalogEntry game, bool showGameView = true)
    {
        game = GameVariants.Resolve(_catalogGames, game, _paths);
        _selectedGame = game;
        var saved = _profileStore.Load(game.Id);
        if (saved is not null)
        {
            game.Executable = saved.Executable;
            game.DiscImage = saved.DiscImage;
        }

        var managedRuntime = _installer.GetStatus(game.Id);
        if (managedRuntime.IsInstalled)
            game.Executable = managedRuntime.ExecutablePath;

        game.Executable = ResolveApplicationPath(game.Executable);
        game.DiscImage = ResolveApplicationPath(game.DiscImage);
        GameTitle.Text = GameVariants.BaseOf(_catalogGames, game).Title;
        _modManager = new PortableModManager(_paths, game.ModLibraryId);
        _remoteMods = Array.Empty<GameBananaMod>();
        _selectedMod = null;
        RefreshGameState();
        RefreshModItems();
        RefreshParityGameDetails();
        if (showGameView) ShowParityView(GameView);
        RefreshLibraryParity();
        _ = RefreshGameUpdatePhase3Async(game, forceUi: true);
    }

    private void RefreshParityGameDetails()
    {
        if (_selectedGame is null || SelectedTitleText is null) return;
        var game = _selectedGame;
        var baseGame = GameVariants.BaseOf(_catalogGames, game);
        RefreshPortVersionPickers(baseGame);
        SelectedTitleText.Text = baseGame.Title;
        SelectedMetaText.Text = string.Join("  •  ", new[]
        {
            game.Year > 0 ? game.Year.ToString() : null,
            string.IsNullOrWhiteSpace(game.Genre) ? null : game.Genre,
            string.IsNullOrWhiteSpace(game.Players) ? null : game.Players,
            string.IsNullOrWhiteSpace(game.Region) ? null : game.Region,
            game.Id
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        SelectedDescriptionText.Text = baseGame.Description +
            (GameVariants.IsVariant(game) ? "\n\n" + P7($"Mod actif : {game.Title}", $"Active mod: {game.Title}") : "");

        var runtime = _installer.GetStatus(game.Id);
        var discPath = ResolveApplicationPath(game.DiscImage);
        var executable = runtime.IsInstalled ? runtime.ExecutablePath : ResolveApplicationPath(game.Executable);
        var hasRuntime = runtime.IsInstalled || File.Exists(executable);
        var hasDisc = File.Exists(discPath);
        var runtimeManagesDisc = game.DataPreparation == GameDataPreparation.Runtime;
        var prepared = !runtimeManagesDisc && hasRuntime && _gameData.IsPrepared(game, executable);

        var english = UiLocalization.IsEnglish(_preferences.Language);
        OriginalGameStateText.Text = hasDisc
            ? (english ? "✓ Original game configured" : "✓ Jeu original configuré")
            : (english ? "○ Original game not configured" : "○ Jeu original non configuré");
        RuntimeStateText.Text = hasRuntime
            ? $"✓ {game.RuntimeName} {(english ? "installed" : "installé")}{(string.IsNullOrWhiteSpace(runtime.Version) ? "" : " • " + runtime.Version)}"
            : $"○ {game.RuntimeName} {(english ? "not installed" : "non installé")}";

        // A runtime that owns the disc has no CubeShelf step to report, so the badge says who
        // handles it rather than claiming there is work pending that will never happen. It says
        // "handled", not "prepared": PartyBoard reads the image as it is and prepares nothing.
        DataStateText.Text = runtimeManagesDisc
            ? (english ? $"◆ Disc handled by {game.RuntimeName}" : $"◆ Disque géré par {game.RuntimeName}")
            : prepared
                ? (english ? "✓ Game data ready" : "✓ Données prêtes")
                : (english ? "○ Game data to prepare" : "○ Données à préparer");
        SetupProgressText.Text = QuickPlayHint(game);

        FavoriteButton.Content = game.IsFavorite
            ? (english ? "★ Favorite" : "★ Favori")
            : (english ? "☆ Add to favorites" : "☆ Ajouter aux favoris");
        var cover = baseGame.Covers.FirstOrDefault();
        GameCase.CaseFormat = Controls.GameCaseControl.FormatForConsole(baseGame.ShelfConsole);
        GameCase.RealisticShell = true;
        GameCase.UseImportedModel = baseGame.ShelfConsole == "gamecube";
        if(GameCase.SetCover(
            ResolveCoverPath(cover?.Front),
            ResolveCoverPath(cover?.Back),
            ResolveCoverPath(cover?.Spine)))
            GameCase.SetShelfPose(baseGame.ShelfConsole == "gamecube" ? 24 : 14,true,animate:false);

        if (_gameUpdateSnapshots.TryGetValue(game.Id, out var updateSnapshot))
            ApplyGameUpdateSnapshotPhase3(updateSnapshot);
        else
        {
            GameUpdateStateText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "○ Update not checked"
                : "○ Mise à jour non vérifiée";
            GitHubUpdateStatusText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "Update not checked."
                : "Mise à jour non vérifiée.";
            GitHubCommitText.Text = "";
            GitHubChangesText.Text = "";
            SourceSafetyText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "Source code is optional: it is not required to play."
                : "La source est optionnelle : elle n’est pas nécessaire pour jouer.";
            DeleteSourceButton.IsVisible = false;
        }
    }

    private void ToggleFavoriteParity(object? sender, RoutedEventArgs args)
    {
        ToggleFavorite(sender, args);
        RefreshParityGameDetails();
        RefreshLibraryParity();
        var english = UiLocalization.IsEnglish(_preferences.Language);
        ShowToastParity(english ? "Favorites" : "Favoris", _selectedGame?.IsFavorite == true
            ? (english ? "Game added to favorites." : "Jeu ajouté aux favoris.")
            : (english ? "Game removed from favorites." : "Jeu retiré des favoris."));
    }

    private void FlipGameCase(object? sender, RoutedEventArgs args) => GameCase.Flip();

    private string ResolveCoverPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        path = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static Bitmap? LoadCardCover(string path)
    {
        try { return File.Exists(path) ? Controls.CoverBitmapCache.Acquire(path, 512) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { return null; }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch { return null; }
    }

    private async void RunFirstStartWizard(object? sender, RoutedEventArgs args) =>
        await ShowFirstRunWizardAsync(force: true);

    private async Task ShowFirstRunWizardAsync(bool force = false)
    {
        if (!force && _preferences.FirstRunCompleted) return;
        string? raUser = null;
        if (_raStore is not null && _raStore.Load(out var rejected) is { } session && !rejected) raUser = session.User;
        var wizard = new FirstRunWizardWindow(_paths, _preferencesStore, _preferences, SignInRetroAchievementsAsync, raUser);
        await wizard.ShowDialog(this);
        // The network page decides on the spot, even in a wizard left half-way.
        _preferences = _preferences with
        {
            MeshPublicDiscovery = wizard.Preferences.MeshPublicDiscovery,
            MeshDiscoveryNoticeSeen = _preferences.MeshDiscoveryNoticeSeen || wizard.Preferences.MeshDiscoveryNoticeSeen
        };
        _preferencesStore.Save(_preferences);
        if (!wizard.Completed)
        {
            ApplyPreferences();
            EnsureMeshDiscovery();
            return;
        }

        _preferences = wizard.Preferences;
        ApplyPreferences();
        RefreshRetroAchievementsUi();
        RefreshLibraryParity();
        if (wizard.OpenGameAfterFinish && _catalogGames.FirstOrDefault() is { } game)
            SelectCatalogGame(game);
    }


    private void RefreshStorageParity()
    {
        if (StorageSummaryText is null) return;
        var data = MeasureDirectory(_paths.DataDirectory);
        var cache = MeasureDirectory(_paths.CacheDirectory);
        StorageSummaryText.Text = UiLocalization.IsEnglish(_preferences.Language) ? $"CubeShelf data: {FormatBytes(data)} • Cache: {FormatBytes(cache)}" : $"Données CubeShelf : {FormatBytes(data)} • Cache : {FormatBytes(cache)}";
    }



    private string BuildDiagnosticsReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("CubeShelf Diagnostic");
        builder.AppendLine("====================");
        builder.AppendLine($"OS             : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture   : {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Dossier app    : {AppContext.BaseDirectory}");
        builder.AppendLine($"Dossier données: {_paths.DataDirectory}");
        builder.AppendLine($"Cache          : {FormatBytes(MeasureDirectory(_paths.CacheDirectory))}");
        builder.AppendLine();
        if (_selectedGame is null)
        {
            builder.AppendLine("Aucun jeu sélectionné.");
            return builder.ToString();
        }

        var game = _selectedGame;
        var runtime = _installer.GetStatus(game.Id);
        var compatibility = DiscImageService.Inspect(game.DiscImage, game, UiLocalization.IsEnglish(_preferences.Language));
        builder.AppendLine($"Jeu            : {game.Title} ({game.Id})");
        builder.AppendLine($"Favori         : {(game.IsFavorite ? "Oui" : "Non")}");
        builder.AppendLine($"Lancements     : {game.PlayCount}");
        builder.AppendLine($"Temps de jeu   : {FormatPlayTime(game.TotalPlaySeconds)}");
        builder.AppendLine($"PartyBoard     : {(runtime.IsInstalled ? "Installé" : runtime.NeedsRepair ? "À réparer" : "Non installé")}");
        builder.AppendLine($"Runtime version: {runtime.Version}");
        builder.AppendLine($"Executable     : {game.Executable}");
        builder.AppendLine($"Image disque   : {game.DiscImage}");
        builder.AppendLine($"Compatibilité  : {compatibility.Message}");
        builder.AppendLine($"Données prêtes : {_gameData.IsPrepared(game, game.Executable)}");
        builder.AppendLine($"Mods installés : {_modManager?.GetInstalled().Count ?? 0}");
        builder.AppendLine($"Queue en attente: {_portableQueue?.PendingCount ?? 0}");
        if (_gameUpdateSnapshots.TryGetValue(game.Id, out var update))
        {
            builder.AppendLine($"Runtime update : {update.RuntimeUpdateAvailable}");
            builder.AppendLine($"Build en attente: {update.RuntimeUpdatePendingBuild}");
            builder.AppendLine($"Source locale  : {(update.LocalSource.Present ? update.LocalSource.Path : "absente")}");
            builder.AppendLine($"Source gérée   : {update.LocalSource.ManagedByCubeShelf}");
            builder.AppendLine($"Commit source  : {update.LatestSource?.Sha ?? ""}");
            builder.AppendLine($"Commit release : {update.RuntimeRelease.Commit}");
        }
        return builder.ToString();
    }

    private async void ShowToastParity(string title, string message)
    {
        _toastCancellation?.Cancel();
        _toastCancellation?.Dispose();
        _toastCancellation = new CancellationTokenSource();
        var token = _toastCancellation.Token;
        ToastTitle.Text = title;
        ToastMessage.Text = message;
        ToastHost.Opacity = 0;
        ToastHost.IsVisible = true;
        ToastHost.Opacity = 1;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            ToastHost.Opacity = 0;
            await Task.Delay(170, token);
            ToastHost.IsVisible = false;
        }
        catch (OperationCanceledException) { }
    }

    private async void HideToastParity(object? sender, RoutedEventArgs args)
    {
        _toastCancellation?.Cancel();
        ToastHost.Opacity = 0;
        await Task.Delay(170);
        ToastHost.IsVisible = false;
    }

    private static long MeasureDirectory(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return total;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public sealed class DesktopGameCard : IDisposable, System.ComponentModel.INotifyPropertyChanged
    {
        public DesktopGameCard(GameCatalogEntry game, Bitmap? cover, string favorite, string disc, string runtime, string data, string stats, string update)
        {
            Game = game;
            Cover = cover;
            Favorite = favorite;
            Disc = disc;
            Runtime = runtime;
            Data = data;
            Stats = stats;
            Update = update;
        }

        public GameCatalogEntry Game { get; }
        public PortVersionChoice[] Versions { get; set; } = [];
        public PortVersionChoice? SelectedVersion { get; set; }
        public bool HasVersionChoice => Versions.Length > 1;
        public Controls.GameCaseFormat CaseFormat => Controls.GameCaseControl.FormatForConsole(Game.ShelfConsole);
        public bool HasFavorite => Favorite.Length > 0;
        public double CoverWidth => Game.ShelfConsole == "switch" ? 202 : 234;
        public Bitmap? Cover { get; }
        public string Title => Game.Title;
        public string Subtitle => string.Join(" • ", new[] { Game.Year > 0 ? Game.Year.ToString() : "", Game.Genre }.Where(value => value.Length > 0));
        public string Favorite { get; }
        public string Disc { get; }
        public string Runtime { get; }
        public string Data { get; }
        public string Stats { get; }
        public string Update { get; }
        public string PlayLabel { get; private set; } = "";
        public string PlayHint { get; private set; } = "";
        public bool CanQuickPlay { get; private set; } = true;
        public bool IsPreparing => !CanQuickPlay;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void UpdatePlay(string label, string hint, bool enabled)
        {
            if (PlayLabel == label && PlayHint == hint && CanQuickPlay == enabled) return;
            PlayLabel = label; PlayHint = hint; CanQuickPlay = enabled;
            foreach (var property in new[] { nameof(PlayLabel), nameof(PlayHint), nameof(CanQuickPlay), nameof(IsPreparing) })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));
        }
        public void Dispose() => Controls.CoverBitmapCache.Release(Cover);
    }
}
