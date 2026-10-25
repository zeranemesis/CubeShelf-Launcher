using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CubeShelf.Core.Mods;

namespace CubeShelf.Desktop;

/// <summary>The Mods page as tiles: search, category, state and order.</summary>
public sealed partial class MainWindow
{
    private IReadOnlyList<DesktopModItem> _allModItems = Array.Empty<DesktopModItem>();
    // shortcut: thumbnails stay in memory for the session (a few dozen small JPEGs), never disposed.
    private readonly Dictionary<string, Bitmap> _modThumbnails = new();
    private readonly Dictionary<int, GameBananaMod> _modDetails = new();
    private bool _modFiltersReady;
    private string _remoteModSearch = "";
    private bool _modSearchPending;
    private readonly Dictionary<(string Game, int Tab), int> _modSelectionByTab = new();
    private bool InstalledModsSelected => ModsTabs.SelectedIndex != 1;

    private void UpdateModTabs()
    {
        InstalledModsTab.Header = P7("Mes mods", "My mods");
        ModsBackButton.Content = P7("Retour au jeu", "Back to game");
        ModsGameHeading.Text = (_selectedGame?.Title ?? "CubeShelf") + " · Mods";
        SearchModsButton.Content = P7("Rechercher", "Search");
        RefreshModsButton.IsVisible = !InstalledModsSelected;
        _modFiltersReady = false;
        ModStateFilter.ItemsSource = InstalledModsSelected
            ? new[] { P7("Tous", "All"), P7("Activés", "Enabled"), P7("Désactivés", "Disabled") }
            : new[] { P7("Tous", "All"), P7("Installés", "Installed"), P7("Non installés", "Not installed") };
        ModStateFilter.SelectedIndex = 0;
        ModSearchBox.Watermark = InstalledModsSelected ? P7("Filtrer mes mods…", "Filter my mods…")
            : P7("Rechercher sur GameBanana…", "Search GameBanana…");
        _modFiltersReady = true;
        UpdateModControllerHelp();
    }

    private void BackFromMods(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_bigPictureActive) ReturnToBigPicture(sender, args);
        else ParityBackToGame(sender, args);
    }

    private void ModsTabChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_modFiltersReady || !ReferenceEquals(args.Source, ModsTabs)) return;
        ModSearchBox.Text = "";
        UpdateModTabs();
        ApplyModFilters();
        if (!InstalledModsSelected && (_remoteMods.Count == 0 || _remoteModSearch.Length > 0) && RefreshModsButton.IsEnabled)
            RefreshMods(sender, new Avalonia.Interactivity.RoutedEventArgs());
    }

    private void SearchModsTab(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (InstalledModsSelected) ApplyModFilters();
        else RefreshMods(sender, args);
    }

    private void InitializeModFilters()
    {
        ModSortFilter.ItemsSource = new[] { P7("Récents", "Newest"), P7("Plus vus", "Most viewed"), "A → Z" };
        ModSortFilter.SelectedIndex = 0;
        UpdateModTabs();
        _modFiltersReady = true;
    }

    private void ShowModItems(IReadOnlyList<DesktopModItem> items)
    {
        EnsureDesktopModTools();
        if (!_modFiltersReady) InitializeModFilters();
        _allModItems = items;
        var all = P7("Toutes les catégories", "All categories");
        var current = ModCategoryFilter.SelectedItem as string;
        var categories = new[] { all }.Concat(items.Select(item => item.Remote?.Category ?? "")
            .Where(name => name.Length > 0).Distinct().Order()).ToArray();
        _modFiltersReady = false;
        ModCategoryFilter.ItemsSource = categories;
        ModCategoryFilter.SelectedItem = current is not null && categories.Contains(current) ? current : all;
        _modFiltersReady = true;
        ApplyModFilters();
    }

    private void ApplyModFilters()
    {
        var search = ModSearchBox.Text?.Trim() ?? "";
        var category = ModCategoryFilter.SelectedIndex > 0 ? ModCategoryFilter.SelectedItem as string : null;
        var disabled = _selectedGame is { } game ? PortableModState.ReadPlayerDisabled(_paths, game.ModLibraryId) : Array.Empty<int>();
        IEnumerable<DesktopModItem> query = _allModItems.Select(item => item with { Enabled = item.Enabled && !disabled.Contains(item.Id) });
        query = InstalledModsSelected ? query.Where(item => item.Installed) : query.Where(item => item.Remote is not null);
        if (search.Length > 0)
            query = query.Where(item => item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                        (item.Remote?.Author ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase));
        if (category is not null) query = query.Where(item => item.Remote?.Category == category);
        query = (InstalledModsSelected, ModStateFilter.SelectedIndex) switch
        {
            (true, 1) => query.Where(item => item.Enabled),
            (true, 2) => query.Where(item => !item.Enabled),
            (false, 1) => query.Where(item => item.Installed),
            (false, 2) => query.Where(item => !item.Installed),
            _ => query
        };
        query = ModSortFilter.SelectedIndex switch
        {
            1 => query.OrderByDescending(item => item.Remote?.Views ?? 0),
            2 => query.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(item => item.Installed).ThenByDescending(item => item.Remote?.Updated ?? 0)
        };
        var shown = query.ToArray();
        ModsEmptyState.IsVisible = shown.Length == 0;
        ModsEmptyState.Text = InstalledModsSelected
            ? P7("Aucun mod ici. Ouvre GameBanana avec RB pour en trouver, ou modifie tes filtres.",
                "No mods here. Press RB to find mods on GameBanana, or change your filters.")
            : P7("Aucun résultat pour le moment. Appuie sur Y pour rechercher, ou actualise le catalogue.",
                "No results yet. Press Y to search, or refresh the catalog.");
        var selectedId = _selectedGame is { } selectedGame && _modSelectionByTab.TryGetValue((selectedGame.ModLibraryId, ModsTabs.SelectedIndex), out var rememberedId)
            ? rememberedId : _selectedMod?.Id;
        ModsList.ItemsSource = shown;
        ModsList.SelectedItem = shown.FirstOrDefault(item => item.Id == selectedId) ?? shown.FirstOrDefault();
        var total = _allModItems.Count(item => InstalledModsSelected ? item.Installed : item.Remote is not null);
        ModResultCount.Text = P7($"{shown.Length} mod(s) sur {total}", $"{shown.Length} of {total} mod(s)");
        UpdateModControllerHelp();
        _ = LoadModThumbnailsAsync(shown);
    }

    private async Task LoadModThumbnailsAsync(IReadOnlyList<DesktopModItem> items)
    {
        foreach (var item in items)
        {
            var url = item.Remote?.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(url) || item.Thumbnail is not null) continue;
            if (!_modThumbnails.TryGetValue(url, out var bitmap))
            {
                try { bitmap = new Bitmap(await _mediaCache.GetAsync(url, true)); }
                catch { continue; } // the tile keeps its category name instead of a picture
                _modThumbnails[url] = bitmap;
            }
            item.Thumbnail = bitmap;
        }
    }

    private void ModFiltersChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_modFiltersReady) ApplyModFilters();
    }

    private void ModSearchKeyUp(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) SearchModsTab(sender, new Avalonia.Interactivity.RoutedEventArgs());
        else ApplyModFilters();
    }

    /// <summary>A tile only knows its summary; installing needs the mod's page (archive, description).</summary>
    private async Task<GameBananaMod?> EnsureModDetailsAsync(DesktopModItem item)
    {
        if (item.Remote is not { IsSummary: true } summary) return item.Remote;
        if (!_modDetails.TryGetValue(summary.Id, out var full))
        {
            full = (await _gameBanana.GetAsync(summary.Id)) with
            {
                Category = summary.Category, Author = summary.Author, Views = summary.Views,
                ThumbnailUrl = summary.ThumbnailUrl
            };
            _modDetails[summary.Id] = full;
        }
        return full;
    }

    /// <summary>A hint before installing: the archive's contents are only known once downloaded.</summary>
    private static bool LooksLikeDiscPatch(GameBananaMod? mod)
    {
        if (mod is null || mod.IsSummary) return false;
        var text = string.Join(" ", new[] { mod.Description, mod.InstallInstructions }
            .Concat(mod.Files?.Select(file => file.Name) ?? Array.Empty<string>()));
        return new[] { "xdelta", ".bps", ".ips", ".ppf", "gecko", ".gct", "riivolution", "delta patch", "patch your iso", "patch the iso" }
            .Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
