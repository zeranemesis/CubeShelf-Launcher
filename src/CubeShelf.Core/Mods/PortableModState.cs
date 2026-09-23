using System.Text.Json;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Mods;

/// <summary>
/// Reads what <see cref="PortableModManager"/> has written, without becoming one.
///
/// Constructing a manager creates its directory and rewrites active-mods.txt and
/// active-mods.json -- the very files a running game is reading. Anything that only wants to
/// look at mod state, such as composing a presence document for every game in the catalog,
/// comes here instead. Nothing in this type creates, writes or validates: an unknown game
/// simply reads as no mods.
/// </summary>
public static class PortableModState
{
    private const string InstalledFile = "installed.json";
    private const string PlayerDisabledFile = "player-disabled.json";

    /// <summary>
    /// The same rule <see cref="PortableModManager"/>'s constructor enforces, but as a question
    /// rather than an exception: a caller walking the whole catalog must not die on one entry.
    /// </summary>
    public static bool IsValidGameId(string? gameId) =>
        !string.IsNullOrWhiteSpace(gameId) &&
        gameId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>Where a game's mod state lives. Does not create the directory.</summary>
    public static string DirectoryFor(IPlatformPaths paths, string gameId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!IsValidGameId(gameId))
            throw new ArgumentException("Identifiant de jeu invalide.", nameof(gameId));
        return Path.Combine(Path.GetFullPath(paths.DataDirectory), "Mods", gameId);
    }

    public static IReadOnlyList<PortableInstalledMod> ReadInstalled(IPlatformPaths paths, string gameId) =>
        IsValidGameId(gameId)
            ? ReadInstalledFrom(DirectoryFor(paths, gameId))
            : Array.Empty<PortableInstalledMod>();

    /// <summary>Mods the player switched off from inside the game. The game writes this file.</summary>
    public static IReadOnlyCollection<int> ReadPlayerDisabled(IPlatformPaths paths, string gameId) =>
        IsValidGameId(gameId)
            ? ReadPlayerDisabledFrom(DirectoryFor(paths, gameId))
            : Array.Empty<int>();

    /// <summary>
    /// Directory-based overloads so <see cref="PortableModManager"/>, which already holds its own
    /// resolved paths, can delegate here rather than keep a second copy of the file format.
    /// </summary>
    internal static IReadOnlyList<PortableInstalledMod> ReadInstalledFrom(string directory)
    {
        var json = TryReadAllText(Path.Combine(directory, InstalledFile));
        if (json is null) return Array.Empty<PortableInstalledMod>();
        try
        {
            return JsonSerializer.Deserialize<List<PortableInstalledMod>>(json)
                ?? (IReadOnlyList<PortableInstalledMod>)Array.Empty<PortableInstalledMod>();
        }
        catch (JsonException)
        {
            return Array.Empty<PortableInstalledMod>();
        }
    }

    internal static IReadOnlyCollection<int> ReadPlayerDisabledFrom(string directory)
    {
        var json = TryReadAllText(Path.Combine(directory, PlayerDisabledFile));
        if (json is null) return Array.Empty<int>();
        try
        {
            return JsonSerializer.Deserialize<HashSet<int>>(json)
                ?? (IReadOnlyCollection<int>)Array.Empty<int>();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Installed mods with the in-game switch already applied to <c>Enabled</c>, which is what
    /// "this mod will actually load" means. Ordered so repeated reads of unchanged state produce
    /// an identical result, which is what lets a caller detect that nothing has changed.
    /// </summary>
    public static IReadOnlyList<PortableInstalledMod> ReadEffective(IPlatformPaths paths, string gameId)
    {
        var installed = ReadInstalled(paths, gameId);
        if (installed.Count == 0) return installed;

        var playerDisabled = ReadPlayerDisabled(paths, gameId);
        return installed
            .Select(mod => mod with { Enabled = mod.Enabled && !playerDisabled.Contains(mod.Id) })
            .OrderBy(mod => mod.Id)
            .ToArray();
    }

    /// <summary>
    /// Reads a file that another thread -- or the game itself -- may be rewriting.
    ///
    /// <see cref="PortableModManager"/> writes these files with plain WriteAllText, so a reader
    /// can genuinely catch a sharing violation or a half-written file. Returning null for any
    /// of that is right here: mod state is informational to every caller of this type, and no
    /// presence document is worth an exception escaping a background loop.
    /// </summary>
    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
