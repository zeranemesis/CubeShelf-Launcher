using Avalonia.Interactivity;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _personalLibrary;
    private bool _personalLibraryReturnToConsoles;
    private HashSet<string> _personalGameIds = [];

    private void OpenPersonalLibrary(object? sender, RoutedEventArgs args)
    {
        if (OwnedWindows.Any(window => window.IsVisible)) return;
        LaunchTransition.Stop();
        if (!_personalLibrary || !LibraryView.IsVisible)
            _personalLibraryReturnToConsoles = PlatformHome.IsVisible;
        _personalLibrary = true;
        _favoritesOnly = false;
        CloseBigPictureMenu();
        _changingShelf = true;
        try { LibrarySearchBox.Text = ""; }
        finally { _changingShelf = false; }
        RefreshLibraryParity();
        ShowParityView(LibraryView);
        if (_bigPictureActive) BigPictureReturnButton.Focus();
    }

    private IEnumerable<GameCatalogEntry> PersonalLibraryGames()
    {
        var native = new NativePortLibrary(_paths);
        var profiles = _profileStore.LoadAll().GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var added = _catalogGames.Where(game =>
        {
            if (_nativeShelfPorts.TryGetValue(game.Id, out var configuredPort) && native.Executable(configuredPort) is { Length: > 0 } executable)
            {
                game.Executable = executable;
                return true;
            }
            var saved = profiles.GetValueOrDefault(game.Id);
            // An explicitly added game stays accessible even if its drive is temporarily missing.
            if (saved is not null && (!string.IsNullOrWhiteSpace(saved.Executable) || !string.IsNullOrWhiteSpace(saved.DiscImage)))
            {
                game.Executable = saved.Executable;
                game.DiscImage = saved.DiscImage;
                return true;
            }
            if (game.Id.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(ResolveApplicationPath(game.Executable)) || File.Exists(ResolveApplicationPath(game.DiscImage))) return true;
            return !game.RuntimeOwnsSetup && _installer.GetStatus(game.Id).IsInstalled;
        }).Select(game => GameVariants.BaseOf(_catalogGames, game)).DistinctBy(game => game.Id).ToArray();
        _personalGameIds = added.Select(game => game.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ShelfProducts.SelectMany(product => NativePortCatalog.ForConsole(added, product.Id, _selectedPortVersions));
    }
}
