using System.Text.Json;

namespace CubeShelf.Core.Library;

public sealed record GameProfileEntry(string Id, string Executable, string DiscImage);

public sealed class GameProfileStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public GameProfileStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _path = Path.Combine(Path.GetFullPath(configurationDirectory), "desktop-profile.json");
    }

    public GameProfileEntry? Load(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        return LoadAll().FirstOrDefault(entry => entry.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(GameProfileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        var entries = LoadAll();
        var index = entries.FindIndex(existing => existing.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) entries[index] = entry;
        else entries.Add(entry);

        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, _json));
        File.Move(temporary, _path, true);
    }

    private List<GameProfileEntry> LoadAll()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<GameProfileEntry>>(File.ReadAllText(_path), _json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
