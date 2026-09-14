using System.Text;

namespace CubeShelf.Core.Library;

public sealed record DiscCompatibility(string VersionId, bool Recognized, bool Supported, string Message);

public static class DiscImageService
{
    private static readonly Dictionary<string, string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMPE01_00"] = "USA 1.0",
        ["GMPE01_01"] = "USA 1.1"
    };

    public static string SupportedSummary =>
        "Build PartyBoard actuel : USA 1.0 (GMPE01_00) et USA 1.1 (GMPE01_01).";

    public static DiscCompatibility Inspect(string path, bool english = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new("", false, false, english ? "No disc image selected." : "Aucune image de disque sélectionnée.");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".rvz")
            return new("", false, true, english
                ? "RVZ selected. Exact revision validation requires Dolphin decoding."
                : "RVZ sélectionné. La validation exacte nécessite le décodage Dolphin.");

        if (extension is not ".iso" and not ".gcm")
            return new("", false, false, english ? "Unsupported file format." : "Format de fichier non pris en charge.");

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) != header.Length) throw new InvalidDataException();

            var gameId = Encoding.ASCII.GetString(header[..6]);
            var versionId = $"{gameId}_{header[7]:00}";
            var supported = Supported.TryGetValue(versionId, out var label);
            return new(versionId, true, supported,
                supported
                    ? english ? $"Compatible disc revision: {label} ({versionId})." : $"Révision compatible : {label} ({versionId})."
                    : english ? $"Detected revision {versionId}, not supported by the current build." : $"Révision {versionId} détectée, non prise en charge par le build actuel.");
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

    private static DiscCompatibility Unreadable(bool english) => new("", false, false,
        english ? "Unable to read the GameCube ISO/GCM header." : "Impossible de lire l’en-tête GameCube de l’ISO/GCM.");
}
