using System.Text.Json;
using System.Text.Json.Nodes;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Core.Games;

/// <summary>Uses each supported engine's startup contract; no simulated keys or external window resizing.</summary>
public static class GameFullscreenLaunch
{
    public static void Apply(GameCatalogEntry game, GameLaunchConfiguration launch)
    {
        if (game.GitHubRepo.Equals("sms-port", StringComparison.OrdinalIgnoreCase))
        {
            launch.Environment["SMS_WINDOW_MODE"] = "fullscreen";
            launch.Environment["SMS_FULLSCREEN_MODE"] = "desktop";
        }
        else if (game.GitHubRepo.Equals("strikers", StringComparison.OrdinalIgnoreCase))
            launch.Environment["STRIKERS_FULLSCREEN"] = "1";
        else if (game.GitHubRepo.Equals("Marioparty4", StringComparison.OrdinalIgnoreCase))
        {
            var profile = Environment.GetEnvironmentVariable("PARTYBOARD_TEST_PROFILE");
            var path = Path.Combine(string.IsNullOrWhiteSpace(profile) ? PhoneLinkServer.DefaultSavesDirectory() : profile, "config.json");
            var previous = ReadSettings(path);
            var settings = previous is null ? new JsonObject() : JsonNode.Parse(previous) as JsonObject
                ?? throw new InvalidDataException("Le fichier de réglages PartyBoard n’est pas un objet JSON.");
            if (settings["video.enableFullscreen"]?.ToJsonString() == "true") return;
            settings["video.enableFullscreen"] = true;
            WriteSettings(path, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (game.GitHubRepo.Equals("RingOut", StringComparison.OrdinalIgnoreCase))
        {
            // The distributed RingOut wrapper uses userdata beside RingOut.exe.
            // Its raw runner accepts --user-dir; preserve an explicit caller choice.
            var directory = Path.GetDirectoryName(Path.GetFullPath(game.Executable))!;
            var profile = Path.Combine(directory, "userdata");
            for (var i = 0; i + 1 < launch.Arguments.Count; i++)
                if (launch.Arguments[i] == "--user-dir") profile = Path.GetFullPath(launch.Arguments[i + 1], directory);
            var path = Path.Combine(profile, "Config", "Dolphin.ini");
            var previous = ReadSettings(path) ?? "";
            var lines = previous.Replace("\r\n", "\n").Split('\n').ToList();
            var display = false;
            var found = false;
            var insertion = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith('['))
                {
                    display = line.Equals("[Display]", StringComparison.OrdinalIgnoreCase);
                    if (display) insertion = i + 1;
                }
                else if (display)
                {
                    insertion = i + 1;
                    var equals = line.IndexOf('=');
                    if (equals >= 0 && line[..equals].Trim().Equals("Fullscreen", StringComparison.OrdinalIgnoreCase))
                    { lines[i] = "Fullscreen = True"; found = true; }
                }
            }
            if (!found)
            {
                if (insertion < 0) { lines.Add("[Display]"); lines.Add("Fullscreen = True"); }
                else lines.Insert(insertion, "Fullscreen = True");
            }
            var updated = string.Join(Environment.NewLine, lines);
            if (updated != previous) WriteSettings(path, updated);
        }
    }

    private static string? ReadSettings(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Le fichier de réglages du jeu est trop volumineux.");
        return File.ReadAllText(path);
    }

    private static void WriteSettings(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backup = path + ".cubeshelf-before-fullscreen";
        if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
