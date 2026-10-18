using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Mods;

/// <summary>Finds a game's disc patch (<see cref="GameDiscPatch"/>) among its installed mods.</summary>
public static class DiscPatchSource
{
    /// <summary>
    /// The patch file of the enabled mod that carries it, or null when that mod is not installed
    /// (or switched off). The mod is matched by its GameBanana id, else by the patch's file name.
    /// </summary>
    public static string? FindDelta(IPlatformPaths paths, string gameId, GameDiscPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var installed = new PortableModManager(paths, gameId).GetInstalled()
            .Where(mod => mod.Enabled && Directory.Exists(mod.ContentRoot))
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
