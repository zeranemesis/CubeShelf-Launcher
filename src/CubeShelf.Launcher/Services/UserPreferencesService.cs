using System.Windows;

namespace CubeShelf.Launcher.Services;

public sealed class UserPreferences
{
    public string Language { get; set; } = "fr";
    public string Theme { get; set; } = "dark";
    public bool CheckGamesOnStartup { get; set; } = true;
    public bool ShowGameUpdatePopup { get; set; } = true;
    public bool RefreshModsOnOpen { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
}

public sealed class UserPreferencesService
{
    private readonly string _dataDir;
    private readonly string _file;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public UserPreferencesService()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf");

        Directory.CreateDirectory(_dataDir);
        _file = Path.Combine(_dataDir, "settings.json");
    }

    public string DataDirectory => _dataDir;
    public bool SettingsFileExists => File.Exists(_file);

    public UserPreferences Load()
    {
        if (!File.Exists(_file))
            return new UserPreferences();

        try
        {
            var raw = File.ReadAllText(_file);
            var preferences =
                JsonSerializer.Deserialize<UserPreferences>(raw) ??
                new UserPreferences();

            // Existing CubeShelf users should not suddenly be interrupted by
            // the new onboarding wizard after upgrading.
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty(
                    "FirstRunCompleted",
                    out _) &&
                !doc.RootElement.TryGetProperty(
                    "firstRunCompleted",
                    out _))
            {
                preferences.FirstRunCompleted = true;
                Save(preferences);
            }

            return preferences;
        }
        catch
        {
            return new UserPreferences
            {
                FirstRunCompleted = true
            };
        }
    }

    public void Save(UserPreferences preferences)
        => File.WriteAllText(
            _file,
            JsonSerializer.Serialize(preferences, _json));

    public void Reset()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }

    public void ApplyLanguage(string language)
    {
        var app = Application.Current;

        var old = app.Resources.MergedDictionaries.FirstOrDefault(
            x => x.Source?.OriginalString.Contains(
                     "Strings.",
                     StringComparison.OrdinalIgnoreCase) == true);

        if (old is not null)
            app.Resources.MergedDictionaries.Remove(old);

        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    language.Equals(
                        "en",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Resources/Strings.en.xaml"
                        : "Resources/Strings.fr.xaml",
                    UriKind.Relative)
            });
    }

    public void ApplyTheme(string theme)
    {
        var app = Application.Current;

        var old = app.Resources.MergedDictionaries.FirstOrDefault(
            x => x.Source?.OriginalString.Contains(
                     "Themes/",
                     StringComparison.OrdinalIgnoreCase) == true);

        if (old is not null)
            app.Resources.MergedDictionaries.Remove(old);

        app.Resources.MergedDictionaries.Insert(
            0,
            new ResourceDictionary
            {
                Source = new Uri(
                    theme.Equals(
                        "light",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Themes/Light.xaml"
                        : "Themes/Dark.xaml",
                    UriKind.Relative)
            });
    }
}
