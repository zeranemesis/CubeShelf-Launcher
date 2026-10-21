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

    private void InitializeModFilters()
    {
        ModStateFilter.ItemsSource = new[] { P7("Tous", "All"), P7("Installés", "Installed"), P7("Activés", "Enabled"), P7("Non installés", "Not installed") };
        ModSortFilter.ItemsSource = new[] { P7("Récents", "Newest"), P7("Plus vus", "Most viewed"), "A → Z" };
        ModStateFilter.SelectedIndex = 0;
        ModSortFilter.SelectedIndex = 0;
        ModSearchBox.Watermark = P7("Rechercher un mod… (Entrée : chercher sur GameBanana)", "Search mods… (Enter: search GameBanana)");
        _modFiltersReady = true;
    }

    private void ShowModItems(IReadOnlyList<DesktopModItem> items)
    {
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
        IEnumerable<DesktopModItem> query = _allModItems;
        if (search.Length > 0)
            query = query.Where(item => item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                        (item.Remote?.Author ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase));
        if (category is not null) query = query.Where(item => item.Remote?.Category == category);
        query = ModStateFilter.SelectedIndex switch
        {
            1 => query.Where(item => item.Installed),
            2 => query.Where(item => item.Installed && item.Enabled),
            3 => query.Where(item => !item.Installed),
            _ => query
        };
        query = ModSortFilter.SelectedIndex switch
        {
            1 => query.OrderByDescending(item => item.Remote?.Views ?? 0),
            2 => query.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(item => item.Installed).ThenByDescending(item => item.Remote?.Updated ?? 0)
        };
        var shown = query.ToArray();
        var selectedId = _selectedMod?.Id;
        ModsList.ItemsSource = shown;
        ModsList.SelectedItem = shown.FirstOrDefault(item => item.Id == selectedId);
        ModResultCount.Text = P7($"{shown.Length} mod(s) sur {_allModItems.Count}", $"{shown.Length} of {_allModItems.Count} mod(s)");
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
        if (args.Key == Key.Enter) RefreshMods(sender, new Avalonia.Interactivity.RoutedEventArgs());
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
