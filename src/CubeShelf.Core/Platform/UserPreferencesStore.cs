using System.Text.Json;

namespace CubeShelf.Core.Platform;

public sealed record UserPreferences(
    string Language = "fr",
    string Theme = "dark",
    bool CheckGamesOnStartup = true,
    bool ShowGameUpdatePopup = true,
    bool RefreshModsOnOpen = true,
    bool AutoFitWindowToScreen = true,
    double WindowWidth = 1180,
    double WindowHeight = 760,
    bool FirstRunCompleted = true);

public sealed class UserPreferencesStore
{
    private readonly string _file;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public UserPreferencesStore(IPlatformPaths paths)
    {
        _file = Path.Combine(Path.GetFullPath(paths.DataDirectory), "settings.json");
    }

    public UserPreferences Load()
    {
        if (!File.Exists(_file)) return new();
        try { return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_file), _json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, _json));
        File.Move(temporary, _file, true);
    }

    public void Reset() { if (File.Exists(_file)) File.Delete(_file); }
}
