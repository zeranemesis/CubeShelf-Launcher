using CubeShelf.Core.Mods;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Library;

/// <summary>
/// Games played through a mod of another (Super Mario Eclipse, a mod of Super Mario Sunshine):
/// a catalogue entry with <see cref="GameCatalogEntry.VariantOf"/> is not shown in the library;
/// its base game becomes it while the mod named by <see cref="GameCatalogEntry.ActivatedByModId"/>
/// is installed and enabled in the base game's mods.
/// </summary>
public static class GameVariants
{
    public static bool IsVariant(GameCatalogEntry game) => !string.IsNullOrWhiteSpace(game.VariantOf);

    /// <summary>The base game of a variant, or the game itself.</summary>
    public static GameCatalogEntry BaseOf(IEnumerable<GameCatalogEntry> catalog, GameCatalogEntry game) =>
        IsVariant(game)
            ? catalog.FirstOrDefault(candidate => candidate.Id.Equals(game.VariantOf, StringComparison.OrdinalIgnoreCase)) ?? game
            : game;

    /// <summary>
    /// What the base game plays as now: the variant whose mod is active in the base game's
    /// mods (enabled here and not switched off in game), else the base game.
    /// </summary>
    public static GameCatalogEntry Resolve(IEnumerable<GameCatalogEntry> catalog, GameCatalogEntry game, IPlatformPaths paths)
    {
        var list = catalog as IReadOnlyCollection<GameCatalogEntry> ?? catalog.ToList();
        var baseGame = BaseOf(list, game);
        var variants = list.Where(candidate => IsVariant(candidate) && candidate.ActivatedByModId > 0 &&
                                               candidate.VariantOf.Equals(baseGame.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (variants.Count == 0) return baseGame;
        var mods = new PortableModManager(paths, baseGame.Id);
        var playerDisabled = mods.GetPlayerDisabled();
        var active = mods.GetInstalled().Where(mod => mod.Enabled && !playerDisabled.Contains(mod.Id))
            .Select(mod => mod.Id).ToHashSet();
        return variants.FirstOrDefault(variant => active.Contains(variant.ActivatedByModId)) ?? baseGame;
    }
}
