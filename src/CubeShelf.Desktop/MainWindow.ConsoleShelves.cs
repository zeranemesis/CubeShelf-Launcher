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
        Directory.CreateDirectory(paths.CacheDirectory);
        var file = Path.Combine(paths.CacheDirectory, "shipped-shelf-suite.json");
        File.WriteAllText(file, JsonSerializer.Serialize(games, options));
        return file;
    }

    private void InitializeConsoleShelfPicker()
    {
        _activeShelfConsole = Program.Product.Id;
        var file = Path.Combine(_paths.ConfigurationDirectory, "selected-console.txt");
        if (Program.Product.Id == "gamecube" && File.Exists(file))
        {
            var saved = File.ReadAllText(file).Trim();
            if (saved is "gamecube" or "wii" or "switch") _activeShelfConsole = saved;
        }
        ConsoleShelfPicker.SelectedIndex = _activeShelfConsole switch { "wii" => 1, "switch" => 2, _ => 0 };
        BigPictureConsoleShelfPicker.SelectedIndex = ConsoleShelfPicker.SelectedIndex;
        Title = "CubeShelf · " + ShelfProduct.Resolve(_activeShelfConsole).Console;
        _consoleShelfReady = true;
    }

    private void ConsoleShelfChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_consoleShelfReady || sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string console } }
            || console == _activeShelfConsole) return;
        _activeShelfConsole = console;
        _consoleShelfReady = false;
        ConsoleShelfPicker.SelectedIndex = console switch { "wii" => 1, "switch" => 2, _ => 0 };
        BigPictureConsoleShelfPicker.SelectedIndex = ConsoleShelfPicker.SelectedIndex;
        _consoleShelfReady = true;
        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        File.WriteAllText(Path.Combine(_paths.ConfigurationDirectory, "selected-console.txt"), console);
        Title = "CubeShelf · " + ShelfProduct.Resolve(console).Console;
        _favoritesOnly = false;
        _selectedGame = ShelfGames.FirstOrDefault();
        LibrarySearchBox.Text = "";
        RefreshLibraryParity();
        if (_selectedGame is not null) RefreshGameState();
        if (_bigPictureActive)
        {
            ExitBigPicture(this, new RoutedEventArgs());
            EnterBigPicture(this, new RoutedEventArgs());
            return;
        }
        ShowParityView(LibraryView);
    }

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
