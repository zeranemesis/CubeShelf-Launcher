namespace CubeShelf.Core.Releases;

/// <summary>
/// Matches a GitHub release asset name against a catalog pattern.
///
/// Publishers put the version in the file name -- RingOut-1.6.1-windows-x64.zip -- so a catalog
/// entry cannot name the asset literally without going stale at every release. One wildcard is
/// enough to express that and keeps the pattern unambiguous.
/// </summary>
public static class ReleaseAssetPattern
{
    public static bool Matches(string assetName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(pattern)) return false;

        var star = pattern.IndexOf('*');
        if (star < 0) return assetName.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];

        // The prefix and suffix must not overlap, or "RingOut-*.zip" would match "RingOut-.zip"
        // and, worse, a shorter name could satisfy both ends at once.
        return assetName.Length >= prefix.Length + suffix.Length &&
            assetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
