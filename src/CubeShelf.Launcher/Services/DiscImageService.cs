namespace CubeShelf.Launcher.Services;

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

    public static DiscCompatibility Inspect(string path, bool english)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new("", false, false, english ? "No disc image selected." : "Aucune image de disque sélectionnée.");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".rvz")
            return new("", false, true, english ? "RVZ selected. Exact revision validation still needs Dolphin/RVZ decoding." : "RVZ sélectionné. La validation exacte de la révision nécessite encore le décodage Dolphin/RVZ.");
        if (ext != ".iso" && ext != ".gcm")
            return new("", false, false, english ? "Unsupported file format." : "Format de fichier non pris en charge.");

        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[8];
            if (stream.Read(header, 0, header.Length) != header.Length) throw new InvalidDataException();
            var gameId = Encoding.ASCII.GetString(header, 0, 6);
            var versionId = $"{gameId}_{header[7]:00}";
            var ok = Supported.ContainsKey(versionId);
            return new(versionId, true, ok,
                ok ? (english ? $"Compatible disc revision: {Supported[versionId]} ({versionId})." : $"Révision compatible : {Supported[versionId]} ({versionId}).")
                   : (english ? $"Detected revision {versionId}, not listed by the current decompilation." : $"Révision détectée {versionId}, non listée par la décompilation actuelle."));
        }
        catch
        {
            return new("", false, false, english ? "Unable to read the GameCube ISO/GCM header." : "Impossible de lire l'en-tête GameCube de l'ISO/GCM.");
        }
    }
}
