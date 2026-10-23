using System.Text;
using System.Text.Json;

namespace CubeShelf.Core.Mods;

public sealed record ModLoadout(string Name, int[] EnabledIds, Dictionary<int, int>? Priorities = null);

/// <summary>Named mod selections, scoped to a game's existing mod library.</summary>
public sealed class ModLoadoutStore
{
    private readonly string _file;
    public ModLoadoutStore(string configurationDirectory, string libraryId)
    {
        if (string.IsNullOrWhiteSpace(libraryId) || libraryId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid mod library.", nameof(libraryId));
        _file = Path.Combine(configurationDirectory, "mod-profiles", libraryId + ".json");
    }

    public IReadOnlyList<ModLoadout> Load()
    {
        if (!File.Exists(_file)) return Array.Empty<ModLoadout>();
        if (new FileInfo(_file).Length > 256 * 1024) throw new InvalidDataException("Mod profile file is too large.");
        var profiles = JsonSerializer.Deserialize<ModLoadout[]>(File.ReadAllText(_file)) ?? [];
        return profiles.Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name)
            && profile.Name.Length <= 80 && profile.EnabledIds is not null)
            .Select(profile => profile with { Name = profile.Name.Trim(), EnabledIds = profile.EnabledIds.Where(id => id > 0).Distinct().ToArray(),
                Priorities = profile.Priorities?.Where(pair => profile.EnabledIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value) })
            .DistinctBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
    }

    public void Save(string name, IEnumerable<int> enabledIds, IReadOnlyDictionary<int, int>? priorities = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(enabledIds);
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Profile name must contain 1–80 characters.", nameof(name));
        var profiles = Load().Where(profile => !profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (profiles.Count >= 100) throw new InvalidOperationException("Too many mod profiles.");
        var ids = enabledIds.Where(id => id > 0).Distinct().ToArray();
        profiles.Add(new ModLoadout(name, ids, priorities?.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)));
        Write(profiles);
    }

    /// <summary>Capture the effective selection and its actual load order; old ID-only profiles remain readable.</summary>
    public void SaveSelection(string name, IEnumerable<PortableInstalledMod> installed, IReadOnlyCollection<int> playerDisabled)
    {
        var selected = installed.Where(mod => mod.Enabled && !playerDisabled.Contains(mod.Id)).ToArray();
        Save(name, selected.Select(mod => mod.Id), selected.ToDictionary(mod => mod.Id, mod => mod.Priority));
    }

    public void Delete(string name) => Write(Load().Where(profile => !profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray());

    private void Write(IEnumerable<ModLoadout> profiles)
    {
        var contents = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(contents) > 256 * 1024)
            throw new InvalidDataException("Mod profile file is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, _file, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
