using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private string _activeShelfConsole = "gamecube";
    private bool _consoleShelfReady;
    private readonly Dictionary<string, NativePort> _nativeShelfPorts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _shelfMediaArtwork = new();
    private Dictionary<string, string> _selectedPortVersions = new();
    private bool _updatingPortVersions;

    public sealed record PortVersionChoice(string GameId, string Label)
    {
        public override string ToString() => Label;
    }

    private PortVersionChoice[] PortVersionsFor(GameCatalogEntry game) => NativePortCatalog.VersionsOf(_catalogGames, game)
        .Select(candidate => new PortVersionChoice(candidate.Id,
            candidate.RuntimeName + (_nativeShelfPorts.TryGetValue(candidate.Id, out var port) && port.ReleaseTag.Length > 0
                ? " · " + port.ReleaseTag : " · intégré"))).ToArray();

    private void RefreshPortVersionPickers(GameCatalogEntry? game)
    {
        _updatingPortVersions = true;
        try
        {
            var choices = game is null ? [] : PortVersionsFor(game);
            foreach (var picker in new[] { GamePortVersionPicker, BigPicturePortVersionPicker })
            {
                picker.ItemsSource = choices;
                picker.SelectedItem = choices.FirstOrDefault(choice => choice.GameId == game?.Id);
                picker.IsVisible = choices.Length > 1;
            }
        }
        finally { _updatingPortVersions = false; }
    }

    private void CardPortVersionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { Tag: DesktopGameCard card, SelectedItem: PortVersionChoice choice }
            && choice.GameId != card.Game.Id) SelectPortVersion(choice.GameId);
    }

    private void PortVersionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_updatingPortVersions && sender is ComboBox { SelectedItem: PortVersionChoice choice }) SelectPortVersion(choice.GameId);
    }

    private void SelectPortVersion(string id)
    {
        var game = _catalogGames.FirstOrDefault(candidate => candidate.Id == id);
        if (game is null || game.ShelfConsole != _activeShelfConsole
            || _selectedPortVersions.GetValueOrDefault(NativePortCatalog.GameKey(game)) == id) return;
        _selectedPortVersions[NativePortCatalog.GameKey(game)] = id;
        File.WriteAllText(Path.Combine(_paths.ConfigurationDirectory, "selected-port-versions.json"), JsonSerializer.Serialize(_selectedPortVersions));
        var favorite = NativePortCatalog.VersionsOf(_catalogGames, game).Any(candidate => candidate.IsFavorite);
        foreach (var candidate in NativePortCatalog.VersionsOf(_catalogGames, game)) candidate.IsFavorite = favorite;
        SelectCatalogGame(game, showGameView: false);
        PersistCatalog();
        if (_bigPictureActive)
        {
            RebuildBigPictureShelf();
        }
    }

    private string PrepareUnifiedShelfCatalog(IPlatformPaths paths)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var games = JsonSerializer.Deserialize<List<GameCatalogEntry>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "games.json")), options) ?? [];
        foreach (var console in new[] { "gamecube", "wii", "switch" })
        {
            foreach (var port in NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Catalogs", console + ".json")))
            {
                var id = "native-" + console + "-" + port.Id;
                _nativeShelfPorts.Add(id, port);
                games.Add(new GameCatalogEntry
                {
                    Id = id, Title = port.Title, ShelfConsole = console, NativePortId = port.Id,
                    RuntimeOwnsSetup = true, RuntimeName = port.Project, Genre = "Port natif",
                    Region = string.Join(", ", port.Platforms), Players = port.Status,
                    Description = port.Notes + "\nProjet : " + port.Repository,
                    DataPreparation = GameDataPreparation.Runtime, HomepageUrl = port.Homepage,
                    LaunchArguments = [], LaunchEnvironment = [], DiscFormats = []
                });
            }
        }
        var covers = JsonSerializer.Deserialize<List<ShelfCover>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Catalogs", "covers.json"))) ?? [];
        foreach (var group in games.GroupBy(NativePortCatalog.GameKey))
        {
            var existing = group.FirstOrDefault(game => game.Covers.Count > 0)?.Covers;
            var artwork = covers.FirstOrDefault(cover => cover.Console == group.First().ShelfConsole && cover.Title == group.First().Title);
            if(artwork is { Media.Length: >0 })_shelfMediaArtwork[group.Key]=artwork.Media;
            foreach (var game in group.Where(game => game.Covers.Count == 0))
            {
                if (existing is not null) game.Covers = existing.Select(cover => new GameCover
                    { Label = cover.Label, Front = cover.Front, Back = cover.Back, Spine = cover.Spine }).ToList();
                else if (artwork is not null && artwork.Front.Length > 0) game.Covers = [new GameCover
                    { Label = "GameTDB · " + artwork.GameTdbId, Front = artwork.Front, Back = artwork.Back, Spine = artwork.Spine }];
            }
        }
        Directory.CreateDirectory(paths.CacheDirectory);
        var file = Path.Combine(paths.CacheDirectory, "shipped-shelf-suite.json");
        File.WriteAllText(file, JsonSerializer.Serialize(games, options));
        return file;
    }

    private sealed class ShelfCover
    {
        public string Console { get; set; } = "";
        public string Title { get; set; } = "";
        public string GameTdbId { get; set; } = "";
        public string Front { get; set; } = "";
        public string Back { get; set; } = "";
        public string Spine { get; set; } = "";
        public string Media { get; set; } = "";
    }

    private void InitializeConsoleShelfPicker()
    {
        var versionsFile = Path.Combine(_paths.ConfigurationDirectory, "selected-port-versions.json");
        if (File.Exists(versionsFile))
        {
            try { _selectedPortVersions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(versionsFile)) ?? new(); }
            catch (JsonException) { }
        }
        foreach (var group in _catalogGames.Where(game => !GameVariants.IsVariant(game)).GroupBy(NativePortCatalog.GameKey))
        {
            var favorite = group.Any(game => game.IsFavorite);
            foreach (var game in group) game.IsFavorite = favorite;
        }
        _activeShelfConsole = Program.Product.Id;
        var file = Path.Combine(_paths.ConfigurationDirectory, "selected-console.txt");
        if (Program.Product.Id == "gamecube" && File.Exists(file))
        {
            var saved = File.ReadAllText(file).Trim();
            if (saved is "gamecube" or "wii" or "switch") _activeShelfConsole = saved;
        }
        ConsoleShelfPicker.SelectedIndex = _activeShelfConsole switch { "wii" => 1, "switch" => 2, _ => 0 };
        BigPictureConsoleShelfPicker.SelectedIndex = ConsoleShelfPicker.SelectedIndex;
        RefreshShelfBranding();
        _consoleShelfReady = true;
        Closed += (_, _) => ShelfTransition.Dispose();
        Deactivated += (_, _) => ShelfTransition.Stop();
        PropertyChanged += (_, change) =>
        {
            if ((change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                || (change.Property == IsVisibleProperty && !IsVisible)) ShelfTransition.Stop();
        };
    }

    private void ConsoleShelfChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_consoleShelfReady || sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string console } }
            || console == _activeShelfConsole) return;
        var direction = ConsoleIndex(console) > ConsoleIndex(_activeShelfConsole) ? 1 : -1;
        Grid.SetColumn(ShelfTransition, _bigPictureActive ? 0 : 1);
        Grid.SetColumnSpan(ShelfTransition, _bigPictureActive ? 2 : 1);
        ShelfTransition.Capture(ViewHost,ShelfProduct.Resolve(console).Console,direction,_preferences.ReduceShelfMotion);
        _activeShelfConsole = console;
        _consoleShelfReady = false;
        ConsoleShelfPicker.SelectedIndex = console switch { "wii" => 1, "switch" => 2, _ => 0 };
        BigPictureConsoleShelfPicker.SelectedIndex = ConsoleShelfPicker.SelectedIndex;
        _consoleShelfReady = true;
        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        File.WriteAllText(Path.Combine(_paths.ConfigurationDirectory, "selected-console.txt"), console);
        RefreshShelfBranding();
        _favoritesOnly = false;
        _selectedGame = ShelfGames.FirstOrDefault();
        // Clearing search already refreshes the library; suppress that redundant rebuild.
        _changingShelf = true;
        try { LibrarySearchBox.Text = ""; }
        finally { _changingShelf = false; }
        if (_selectedGame is not null) RefreshGameState();
        else RefreshLibraryParity();
        if (_bigPictureActive)
        {
            RebuildBigPictureShelf();
            ShelfTransition.Play();
            return;
        }
        ShowParityView(LibraryView);
        ShelfTransition.Play();
    }

    private void RefreshShelfBranding()
    {
        Title=Program.DisplayName+" · "+ShelfProduct.Resolve(_activeShelfConsole).Console;
        SidebarConsoleLogo.Platform=BigPictureConsoleLogo.Platform=_activeShelfConsole;
    }

    private bool _changingShelf;
    private static int ConsoleIndex(string console) => console switch { "wii"=>1,"switch"=>2,_=>0 };

    private async Task<bool> EnsureNativeShelfPortAsync(GameCatalogEntry game, bool alwaysShow = false)
    {
        if (!_nativeShelfPorts.TryGetValue(game.Id, out var port)) return false;
        var library = new NativePortLibrary(_paths);
        if (alwaysShow || !library.IsConfigured(port))
        {
            await new NativePortsWindow(ShelfProduct.Resolve(game.ShelfConsole), port.Id, _paths).ShowDialog(this);
            library = new NativePortLibrary(_paths);
        }
        if (!library.IsConfigured(port)) return false;
        game.Executable = library.Executable(port)!;
        SavePlayPaths(game);
        RefreshLibraryParity();
        return true;
    }
}
