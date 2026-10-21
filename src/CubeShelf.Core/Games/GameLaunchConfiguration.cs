using CubeShelf.Core.Library;

namespace CubeShelf.Core.Games;

public sealed record GameLaunchConfiguration(
    IReadOnlyList<string> Arguments,
    Dictionary<string, string?> Environment)
{
    /// <summary>Expand catalog values without shell parsing or modifying the parent environment.</summary>
    public static GameLaunchConfiguration Create(GameCatalogEntry game, string? modList = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        string Expand(string value) => System.Text.RegularExpressions.Regex.Replace(value,
            "\\{(disc|mods)\\}", match => match.Groups[1].Value == "disc" ? game.DiscImage : modList ?? "");

        // Remove inherited values belonging to the other runtimes before applying this game's contract.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PARTYBOARD_DISC_IMAGE"] = null,
            ["PARTYBOARD_MOD_LIST"] = null,
            ["STRIKERS_DATA"] = null,
            ["SMS_DISC_ROOT"] = null,
            ["SMS_DISC_IMAGE"] = null,
            ["SMS_CUBESHELF_MOD_LIST"] = null,
            ["SMS_CUBESHELF"] = null
        };
        foreach (var pair in game.LaunchEnvironment)
            environment[pair.Key] = Expand(pair.Value);
        if (game.ReadsDiscDirectly) environment[game.DirectDiscVariable] = game.DiscImage;
        return new(game.LaunchArguments.Select(Expand).ToArray(), environment);
    }
}
