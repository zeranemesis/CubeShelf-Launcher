using System.Text;
using System.Buffers.Binary;

namespace CubeShelf.Core.Library;

public sealed record DiscCompatibility(string VersionId, bool Recognized, bool Supported, string Message);

/// <summary>
/// Reads the GameCube disc header and decides whether a runtime accepts that disc.
///
/// The supported set is supplied by the caller rather than baked in here: CubeShelf
/// carries more than one game now, and PartyBoard's Mario Party 4 revisions are not
/// Ring Out's Soulcalibur II ones. Each catalog entry declares its own set.
/// </summary>
public static class DiscImageService
{
    public static DiscCompatibility Inspect(string path, GameCatalogEntry game, bool english = false) =>
        game.RuntimeOwnsSetup
        ? new("", false, true, english ? "Game files are prepared by the native port." : "Les fichiers du jeu se préparent dans le port natif.")
        : Inspect(path, game.SupportedDiscIds, english,
            game.DataPreparation == GameDataPreparation.Runtime ? game.DiscFormats : new[] { ".iso", ".gcm", ".rvz" });

    /// <summary>Accepted by PartyBoard. Used by the legacy WPF launcher, which is single-game.</summary>
    public static readonly IReadOnlyList<string> MarioParty4DiscIds = new[] { "GMPE01_00", "GMPE01_01" };

    /// <summary>
    /// Friendly names for revisions CubeShelf knows about. A disc id missing from here is
    /// still perfectly usable -- the entry's declared set decides, not this table.
    /// </summary>
    private static readonly Dictionary<string, string> KnownLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMPE01_00"] = "Mario Party 4 USA 1.0",
        ["GMPE01_01"] = "Mario Party 4 USA 1.1",
        ["GRSEAF"] = "Soulcalibur II USA",
        ["GRSPAF"] = "Soulcalibur II PAL",
        ["GRSJAF"] = "Soulcalibur II JPN",
        ["GRSEPS"] = "Soulcalibur II « Plus » (disque moddé)"
    };

    public static DiscCompatibility Inspect(
        string path,
        IReadOnlyCollection<string> supportedDiscIds,
        bool english = false,
        IReadOnlyCollection<string>? acceptedFormats = null)
    {
        ArgumentNullException.ThrowIfNull(supportedDiscIds);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new("", false, false, english ? "No disc image selected." : "Aucune image de disque sélectionnée.");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        acceptedFormats ??= new[] { ".iso", ".gcm", ".rvz" };
        if (!acceptedFormats.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return new("", false, false, english ? "File format not supported by this runtime." : "Format de fichier non pris en charge par ce runtime.");

        try
        {
            if (extension is ".rvz" or ".ciso" or ".gcz")
            {
                if (new FileInfo(path).Length < 32)
                    return Unreadable(english);
                return new("", false, true, english
                    ? "Compressed image selected. Game and revision are unverified; the runtime must validate them when opening it."
                    : "Image compressée sélectionnée. Jeu et révision non vérifiés ; le runtime doit les valider à l’ouverture.");
            }

            if (extension is not ".iso" and not ".gcm")
                return new("", false, false, english ? "Unsupported file format." : "Format de fichier non pris en charge.");
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[0x20];
            if (stream.Length < 0x440) return Unreadable(english);
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32BigEndian(header[0x1c..]) != 0xc2339f3d ||
                header[..6].ToArray().Any(value => !char.IsAsciiLetterOrDigit((char)value)))
                return Unreadable(english);

            var gameId = Encoding.ASCII.GetString(header[..6]);
            var versionId = $"{gameId}_{header[7]:00}";
            var supported = IsSupported(supportedDiscIds, gameId, versionId);
            var label = Describe(versionId) ?? Describe(gameId);

            if (!supported)
                return new(versionId, true, false, english
                    ? $"Detected revision {versionId}, not supported by this runtime."
                    : $"Révision {versionId} détectée, non prise en charge par ce runtime.");

            return new(versionId, true, true, label is null
                ? english ? $"Compatible disc revision: {versionId}." : $"Révision compatible : {versionId}."
                : english ? $"Compatible disc revision: {label} ({versionId})." : $"Révision compatible : {label} ({versionId}).");
        }
        catch (IOException)
        {
            return Unreadable(english);
        }
        catch (UnauthorizedAccessException)
        {
            return Unreadable(english);
        }
    }

    /// <summary>
    /// One line naming what the runtime accepts, for the disc panel under the compatibility message.
    /// </summary>
    public static string DescribeSupported(IReadOnlyCollection<string> supportedDiscIds, bool english = false)
    {
        ArgumentNullException.ThrowIfNull(supportedDiscIds);
        if (supportedDiscIds.Count == 0)
            return english
                ? "This runtime does not declare the disc revisions it accepts."
                : "Ce runtime ne déclare pas les révisions de disque qu’il accepte.";

        var described = supportedDiscIds
            .Select(id => Describe(id) is { } label ? $"{label} ({id})" : id)
            .ToArray();

        return (english ? "Accepted by this runtime: " : "Accepté par ce runtime : ") +
            string.Join(", ", described) + ".";
    }

    /// <summary>
    /// A catalog entry may list a full version id (GMPE01_00 -- that revision only) or a bare
    /// six-character disc id (GRSEAF -- every revision of that disc).
    /// </summary>
    private static bool IsSupported(IReadOnlyCollection<string> supported, string gameId, string versionId) =>
        supported.Any(entry =>
            entry.Equals(versionId, StringComparison.OrdinalIgnoreCase) ||
            entry.Equals(gameId, StringComparison.OrdinalIgnoreCase));

    private static string? Describe(string id) =>
        KnownLabels.TryGetValue(id, out var label) ? label : null;

    private static DiscCompatibility Unreadable(bool english) => new("", false, false,
        english ? "Image unreadable, truncated or missing its GameCube header." : "Image illisible, tronquée ou sans en-tête GameCube valide.");
}
