namespace CubeShelf.Core.Library;

/// <summary>The physical format shown in the universal CubeShelf library.</summary>
public enum MediaFormFactor
{
    Cartridge,
    OpticalDisc,
    MiniDisc,
    GameCard,
    Umd,
    HuCard,
    Cassette,
    Digital
}

/// <summary>Presentation shell, separate from the playable media.</summary>
public enum CaseFormFactor
{
    Cardboard,
    Plastic,
    Jewel,
    Clamshell,
    None
}

/// <summary>
/// Describes visuals only. A profile does not imply that an emulator or native
/// runtime is installed or supported on the current device.
/// </summary>
public sealed record ConsoleMediaProfile(
    string Id,
    string DisplayName,
    MediaFormFactor Media,
    CaseFormFactor Case,
    string TemplateId,
    string InsertAnimationId)
{
    public bool IsPhysical => Media != MediaFormFactor.Digital;
}

/// <summary>
/// Data-only starting registry for all major media families. New systems can
/// be registered without adding branches to the GameCube-specific launcher.
/// Keep the identifiers stable: they are serialized in GameCatalogEntry.
/// </summary>
public static class ConsoleMediaProfiles
{
    public static readonly ConsoleMediaProfile Fallback =
        new("other", "Other platform", MediaFormFactor.Digital, CaseFormFactor.None,
            "generic-digital", "launch-digital");

    private static ConsoleMediaProfile P(
        string id, string displayName, MediaFormFactor media, CaseFormFactor @case,
        string template, string animation) =>
        new(id, displayName, media, @case, template, animation);

    private static readonly IReadOnlyDictionary<string, ConsoleMediaProfile> Registry =
        new[]
        {
            // Nintendo
            P("nes", "NES", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "nes-cartridge", "insert-top-loader"),
            P("famicom", "Famicom", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "famicom-cartridge", "insert-top-loader"),
            P("snes", "Super Nintendo", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "snes-cartridge", "insert-top-loader"),
            P("super-famicom", "Super Famicom", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "sfc-cartridge", "insert-top-loader"),
            P("n64", "Nintendo 64", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "n64-cartridge", "insert-top-loader"),
            P("gamecube", "Nintendo GameCube", MediaFormFactor.MiniDisc, CaseFormFactor.Plastic, "gamecube-minidisc", "insert-tray"),
            P("wii", "Nintendo Wii", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "wii-disc", "insert-slot"),
            P("wii-u", "Nintendo Wii U", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "wii-u-disc", "insert-slot"),
            P("switch", "Nintendo Switch", MediaFormFactor.GameCard, CaseFormFactor.Plastic, "switch-card", "insert-card"),
            P("switch-2", "Nintendo Switch 2", MediaFormFactor.GameCard, CaseFormFactor.Plastic, "switch-2-card", "insert-card"),
            P("game-boy", "Game Boy", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "gb-cartridge", "insert-card"),
            P("game-boy-color", "Game Boy Color", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "gbc-cartridge", "insert-card"),
            P("game-boy-advance", "Game Boy Advance", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "gba-cartridge", "insert-card"),
            P("ds", "Nintendo DS", MediaFormFactor.GameCard, CaseFormFactor.Plastic, "ds-card", "insert-card"),
            P("3ds", "Nintendo 3DS", MediaFormFactor.GameCard, CaseFormFactor.Plastic, "3ds-card", "insert-card"),
            P("virtual-boy", "Virtual Boy", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "virtual-boy-cartridge", "insert-card"),

            // Sega
            P("master-system", "Master System", MediaFormFactor.Cartridge, CaseFormFactor.Plastic, "master-system-cartridge", "insert-card"),
            P("mega-drive", "Mega Drive / Genesis", MediaFormFactor.Cartridge, CaseFormFactor.Plastic, "mega-drive-cartridge", "insert-top-loader"),
            P("32x", "Sega 32X", MediaFormFactor.Cartridge, CaseFormFactor.Plastic, "32x-cartridge", "insert-top-loader"),
            P("game-gear", "Game Gear", MediaFormFactor.Cartridge, CaseFormFactor.Plastic, "game-gear-cartridge", "insert-card"),
            P("sega-cd", "Sega CD", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "sega-cd-disc", "insert-tray"),
            P("saturn", "Sega Saturn", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "saturn-disc", "insert-tray"),
            P("dreamcast", "Dreamcast", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "dreamcast-disc", "insert-tray"),

            // Sony
            P("ps1", "PlayStation", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "ps1-disc", "insert-tray"),
            P("ps2", "PlayStation 2", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "ps2-disc", "insert-tray"),
            P("ps3", "PlayStation 3", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "ps3-disc", "insert-slot"),
            P("ps4", "PlayStation 4", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "ps4-disc", "insert-slot"),
            P("ps5", "PlayStation 5", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "ps5-disc", "insert-slot"),
            P("psp", "PlayStation Portable", MediaFormFactor.Umd, CaseFormFactor.Plastic, "psp-umd", "insert-umd"),
            P("ps-vita", "PlayStation Vita", MediaFormFactor.GameCard, CaseFormFactor.Plastic, "vita-card", "insert-card"),

            // Microsoft
            P("xbox", "Xbox", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "xbox-disc", "insert-tray"),
            P("xbox-360", "Xbox 360", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "xbox-360-disc", "insert-tray"),
            P("xbox-one", "Xbox One", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "xbox-one-disc", "insert-slot"),
            P("xbox-series", "Xbox Series X|S", MediaFormFactor.OpticalDisc, CaseFormFactor.Plastic, "xbox-series-disc", "insert-slot"),

            // Other cartridge/disc families
            P("pc-engine", "PC Engine / TurboGrafx-16", MediaFormFactor.HuCard, CaseFormFactor.Plastic, "pc-engine-hucard", "insert-card"),
            P("pc-engine-cd", "PC Engine CD", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "pc-engine-cd", "insert-tray"),
            P("neo-geo-aes", "Neo Geo AES", MediaFormFactor.Cartridge, CaseFormFactor.Plastic, "neo-geo-cartridge", "insert-top-loader"),
            P("neo-geo-cd", "Neo Geo CD", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "neo-geo-cd", "insert-tray"),
            P("atari-2600", "Atari 2600", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "atari-2600-cartridge", "insert-top-loader"),
            P("atari-7800", "Atari 7800", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "atari-7800-cartridge", "insert-top-loader"),
            P("atari-lynx", "Atari Lynx", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "atari-lynx-cartridge", "insert-card"),
            P("atari-jaguar", "Atari Jaguar", MediaFormFactor.Cartridge, CaseFormFactor.Cardboard, "jaguar-cartridge", "insert-top-loader"),
            P("3do", "3DO", MediaFormFactor.OpticalDisc, CaseFormFactor.Jewel, "3do-disc", "insert-tray"),
            P("commodore-64", "Commodore 64", MediaFormFactor.Cassette, CaseFormFactor.None, "c64-cassette", "insert-tape"),

            // ROMs, arcade and native projects are not necessarily physical media.
            P("arcade", "Arcade", MediaFormFactor.Digital, CaseFormFactor.None, "arcade-board", "launch-digital"),
            P("dos", "DOS", MediaFormFactor.Digital, CaseFormFactor.None, "generic-digital", "launch-digital"),
            P("windows", "Windows PC", MediaFormFactor.Digital, CaseFormFactor.None, "pc-native", "launch-digital"),
            P("linux", "Linux PC", MediaFormFactor.Digital, CaseFormFactor.None, "pc-native", "launch-digital"),
            P("macos", "macOS", MediaFormFactor.Digital, CaseFormFactor.None, "pc-native", "launch-digital")
        }.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<ConsoleMediaProfile> All => Registry.Values.ToArray();

    public static bool TryGet(string? platformId, out ConsoleMediaProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(platformId) &&
            Registry.TryGetValue(platformId.Trim(), out var found))
        {
            profile = found;
            return true;
        }

        profile = Fallback;
        return false;
    }

    public static ConsoleMediaProfile Resolve(string? platformId) =>
        TryGet(platformId, out var profile) ? profile : Fallback;
}
