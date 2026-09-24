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

    // Decentralised friends. Publishing is off until the user has both named themselves and
    // proved an address works, so none of this does anything by accident.

    /// <summary>
    /// How friends see us. Never defaulted from the account name: that would publish the
    /// Windows login to everyone the user adds.
    /// </summary>
    string FriendsDisplayName = "",

    /// <summary>A folder something else already synchronises and serves.</summary>
    string PresenceFolder = "",

    /// <summary>The public address that folder is served from, proven by the self-test.</summary>
    string PresenceUrl = "",

    bool PresencePublishEnabled = false,
    bool ShareLibrary = true,
    bool SharePlayTime = true,
    bool ShareCurrentGame = true,
    bool ShareMods = true,
    bool ShareProfile = true,

    /// <summary>One line friends read beside our name. Free text, capped when composed.</summary>
    string ProfileStatus = "",

    /// <summary>The one game we chose to put forward, by catalog id.</summary>
    string ProfilePinnedGameId = "",

    /// <summary>
    /// When this profile started publishing, set once on the first successful publish. It is
    /// ours to state because nobody else can: a friend only ever sees us from the day they
    /// added us.
    /// </summary>
    DateTimeOffset? ProfileFirstSeenAt = null,

    /// <summary>
    /// The address the last successful round trip proved, so the friend code survives a restart.
    /// It counts only while it equals <see cref="PresenceUrl"/>: change the address, or the folder
    /// behind it, and the code is withheld until a new test proves the new pair.
    /// </summary>
    string PresenceVerifiedUrl = "");

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
