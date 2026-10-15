using CubeShelf.Core.Mods;

namespace CubeShelf.Core.Online;

public enum RoomModStatus
{
    /// <summary>Every mod the room plays with is installed here, in the same version.</summary>
    Compatible,
    /// <summary>Some are missing or in another version; CubeShelf can fetch them from GameBanana.</summary>
    NeedsInstall
}

/// <summary>
/// What joining a room asks of this PC's mods. <see cref="SessionMods"/> is the list to hand the
/// companion (PARTYBOARD_ONLINE_MODS): the room's mods in the room's order, for that session only.
/// The player's own choice of mods is never changed to join a room.
/// </summary>
public sealed record RoomModReport(
    RoomModStatus Status,
    IReadOnlyList<RoomMod> Missing,
    IReadOnlyList<RoomMod> Outdated,
    string SessionMods);

/// <summary>
/// Compares a room's mods with what is installed here, the way the companion does: by GameBanana
/// id and by the first eight bytes of the archive's SHA-256. A mod installed before CubeShelf
/// recorded hashes is fingerprinted by its id alone, exactly as the companion falls back, so it
/// matches only another copy in the same state.
/// </summary>
public static class RoomModCheck
{
    public static RoomModReport Compare(IReadOnlyList<RoomMod> required, IReadOnlyList<PortableInstalledMod> installed)
    {
        var missing = new List<RoomMod>();
        var outdated = new List<RoomMod>();
        foreach (var mod in required)
        {
            var mine = installed.FirstOrDefault(m => m.Id == mod.Id);
            if (mine is null) missing.Add(mod);
            else if (!string.Equals(Fingerprint(mine), mod.Fingerprint, StringComparison.Ordinal)) outdated.Add(mod);
        }
        return new RoomModReport(
            missing.Count == 0 && outdated.Count == 0 ? RoomModStatus.Compatible : RoomModStatus.NeedsInstall,
            missing, outdated, string.Join(",", required.Select(m => m.Id)));
    }

    /// <summary>The companion's fingerprint of an installed mod (Mods.cs, Fingerprint).</summary>
    public static string Fingerprint(PortableInstalledMod mod)
    {
        var sha = mod.Sha256 ?? "";
        if (sha.Length >= 16 && sha.Take(16).All(Uri.IsHexDigit)) return sha[..16].ToLowerInvariant();
        return Convert.ToHexString(BitConverter.GetBytes((long)mod.Id)).ToLowerInvariant();
    }
}
