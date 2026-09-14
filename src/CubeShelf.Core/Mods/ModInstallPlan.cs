using System.Text.Json;
using SharpCompress.Archives;

namespace CubeShelf.Core.Mods;

public sealed record ModDependency(int Id, long MinimumUpdated = 0, bool Required = true);

public sealed record CubeShelfModManifest(
    int Schema,
    string GameId,
    int ModId,
    string Version,
    IReadOnlyList<ModDependency>? Dependencies = null,
    IReadOnlyList<int>? IncompatibleWith = null);

public sealed record PreparedModPackage(
    GameBananaMod Mod,
    string ArchivePath,
    CubeShelfModManifest? Manifest,
    string Sha256);

public sealed record ModInstallPlan(
    int RootModId,
    string GameId,
    IReadOnlyList<PreparedModPackage> Packages,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<int> IncompatibleInstalledModIds)
{
    public string Summary => Packages.Count == 1
        ? $"Installer {Packages[0].Mod.Name}"
        : $"Installer {Packages[^1].Mod.Name} et {Packages.Count - 1} dépendance(s)";
}

internal static class ModManifestReader
{
    private const int MaximumManifestBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static CubeShelfModManifest? Read(string archivePath)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var candidates = archive.Entries.Where(entry => !entry.IsDirectory &&
            string.Equals(Path.GetFileName(entry.Key), "cubeshelf-mod.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1) throw new InvalidDataException("L’archive contient plusieurs manifestes CubeShelf.");
        var entry = candidates[0];
        if (entry.Size < 2 || entry.Size > MaximumManifestBytes)
            throw new InvalidDataException("Le manifeste CubeShelf a une taille invalide.");
        using var input = entry.OpenEntryStream();
        using var limited = new MemoryStream((int)entry.Size);
        input.CopyTo(limited);
        if (limited.Length > MaximumManifestBytes)
            throw new InvalidDataException("Le manifeste CubeShelf dépasse 128 Kio.");
        try
        {
            return JsonSerializer.Deserialize<CubeShelfModManifest>(limited.ToArray(), JsonOptions)
                   ?? throw new InvalidDataException("Le manifeste CubeShelf est vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Le manifeste CubeShelf n’est pas un JSON valide.", exception);
        }
    }
}
