using System.Text.Json;

namespace CubeShelf.Core.Platform;

public sealed record UserPreferences(
    string Language = "fr",
    string Theme = "dark",
    bool CheckGamesOnStartup = true,
    bool ShowGameUpdatePopup = true,
    bool RefreshModsOnOpen = true,
    bool AutoFitWindowToScreen = true,
    double WindowWidth = 1530,
    double WindowHeight = 930,
    bool FirstRunCompleted = false,
    /// <summary>
    /// Allows installing a runtime whose publisher ships no manifest and no checksum, so the
    /// download cannot be verified against anything. Off by default, and deliberately so.
    /// </summary>
    bool AllowUnverifiedRuntimes = false);

public sealed class UserPreferencesStore
{
    private readonly string _file;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public UserPreferencesStore(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = Path.Combine(Path.GetFullPath(paths.ConfigurationDirectory), "settings.json");
    }

    public string SettingsFile => _file;
    public bool Exists => File.Exists(_file);

    public UserPreferences Load()
    {
        if (!File.Exists(_file)) return new();

        try
        {
            var raw = File.ReadAllText(_file);
            var preferences = JsonSerializer.Deserialize<UserPreferences>(raw, _json) ?? new();

            // Legacy CubeShelf settings predate the onboarding flag. Existing
            // users must not suddenly see the first-run assistant after update.
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var hasFirstRunFlag = root.EnumerateObject().Any(property =>
                property.Name.Equals(nameof(UserPreferences.FirstRunCompleted), StringComparison.OrdinalIgnoreCase));

            if (!hasFirstRunFlag)
            {
                preferences = preferences with { FirstRunCompleted = true };
                Save(preferences);
            }

            return preferences;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt legacy file should not trap an existing installation in
            // an onboarding loop. Defaults remain usable and onboarding stays completed.
            return new UserPreferences(FirstRunCompleted: true);
        }
    }

    public void Save(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, _json));
        File.Move(temporary, _file, true);
    }

    public void Reset()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }
}
