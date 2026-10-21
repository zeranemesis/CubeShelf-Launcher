using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Mods;

/// <summary>Finds a game's disc patch (<see cref="GameDiscPatch"/>) among its installed mods.</summary>
public static class DiscPatchSource
{
    /// <summary>
    /// The patch file of the enabled mod that carries it, or null when that mod is not installed
    /// (or switched off). The mod is matched by its GameBanana id when one is specified.
    /// </summary>
    public static string? FindDelta(IPlatformPaths paths, string gameId, GameDiscPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var manager = new PortableModManager(paths, gameId);
        var disabled = manager.GetPlayerDisabled();
        var installed = manager.GetInstalled()
            .Where(mod => mod.Enabled && !disabled.Contains(mod.Id) && Directory.Exists(mod.ContentRoot))
            .Where(mod => patch.GameBananaModId <= 0 || mod.Id == patch.GameBananaModId)
            .OrderByDescending(mod => mod.Id == patch.GameBananaModId)
            .ToList();
        foreach (var mod in installed)
        {
            var match = Directory.EnumerateFiles(mod.ContentRoot, "*", SearchOption.AllDirectories)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(patch.PatchFile, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }
}
