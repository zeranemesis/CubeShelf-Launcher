using System.Globalization;
using CubeShelf.Core.Mods;

namespace CubeShelf.Core.Downloads;

public sealed record ModDownloadReference(string GameId, int ModId)
{
    public override string ToString() => $"{GameId}:{ModId.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string value, out ModDownloadReference? reference)
    {
        reference = null;
        var parts = value.Split(':');
        if (parts.Length != 2 || !PortableModState.IsValidGameId(parts[0]) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return false;
        reference = new(parts[0], id);
        return true;
    }
}
