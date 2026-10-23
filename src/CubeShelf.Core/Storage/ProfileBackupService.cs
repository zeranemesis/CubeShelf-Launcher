using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Storage;

public sealed record ProfileBackupInfo(string Id, DateTimeOffset CreatedAt, string Reason, int FileCount);

/// <summary>Local snapshots of launcher preferences, library and mod profiles; no game files or identity keys.</summary>
public sealed class ProfileBackupService(IPlatformPaths paths)
{
    private sealed record Entry(string Bucket, string Name, string Sha256);
    private sealed record Manifest(int Schema, DateTimeOffset CreatedAt, string Reason, Entry[] Files);
    private string Root => Path.Combine(paths.DataDirectory, "ProfileBackups");
    private string Pending => Path.Combine(paths.ConfigurationDirectory, "pending-profile-restore.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<ProfileBackupInfo> List()
    {
        if (!Directory.Exists(Root)) return [];
        var result = new List<ProfileBackupInfo>();
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var manifest = Read(Path.GetFileName(folder));
                result.Add(new(Path.GetFileName(folder), manifest.CreatedAt, manifest.Reason, manifest.Files.Length));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public ProfileBackupInfo Create(string reason)
    {
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        var folder = Safe(Root, id);
        Directory.CreateDirectory(folder);
        var entries = new List<Entry>();
        long totalBytes = 0;
        Add("config", "settings.json");
        Add("config", "desktop-profile.json");
        Add("data", "games.json");
        var profiles = Safe(paths.ConfigurationDirectory, "mod-profiles");
        if (Directory.Exists(profiles))
            foreach (var file in Directory.EnumerateFiles(profiles, "*.json"))
                Add("config", "mod-profiles/" + Path.GetFileName(file));
        var manifest = new Manifest(1, DateTimeOffset.UtcNow, reason, entries.ToArray());
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        return new(id, manifest.CreatedAt, reason, entries.Count);

        void Add(string bucket, string name)
        {
            if (entries.Count >= 512) throw new IOException("Trop de fichiers dans le profil à sauvegarder.");
            var source = Target(bucket, name);
            if (!File.Exists(source)) return;
            var length = new FileInfo(source).Length;
            if (length > 4 * 1024 * 1024 || totalBytes + length > 32 * 1024 * 1024)
                throw new IOException("Le profil dépasse la limite de sauvegarde (32 Mo).");
            var bytes = File.ReadAllBytes(source);
            ValidateJson(bytes);
            totalBytes += bytes.Length;
            var target = Safe(folder, bucket + "/" + name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
            entries.Add(new(bucket, name, Convert.ToHexString(SHA256.HashData(bytes))));
        }
    }

    // Restore before stores are opened on the next launch, so closing the current
    // window cannot overwrite restored files with its old in-memory preferences.
    public void RequestRestore(string id)
    {
        _ = Validate(id);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var temporary = Pending + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(id));
        File.Move(temporary, Pending, true);
    }

    public bool ApplyPendingRestore()
    {
        if (!File.Exists(Pending)) return false;
        if (new FileInfo(Pending).Length > 4096) throw new InvalidDataException("Demande de restauration invalide.");
        var id = JsonSerializer.Deserialize<string>(File.ReadAllText(Pending))
                 ?? throw new InvalidDataException("Point de restauration manquant.");
        var files = Validate(id);
        var before = Create("Avant restauration");
        var changed = new List<(string Path, byte[]? Bytes)>();
        try
        {
            foreach (var (entry, bytes) in files)
            {
                var target = Target(entry.Bucket, entry.Name);
                changed.Add((target, File.Exists(target) ? File.ReadAllBytes(target) : null));
                Write(target, bytes);
            }
            File.Delete(Pending);
            return true;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var (target, bytes) in changed.AsEnumerable().Reverse())
                try
                {
                    if (bytes is not null) Write(target, bytes);
                    else if (File.Exists(target)) File.Delete(target);
                }
                catch (Exception rollbackError) { failures.Add(rollbackError); }
            throw new AggregateException($"Restauration annulée. Le profil précédent est conservé dans {before.Id}.", failures);
        }
    }

    public void DiscardPendingRestore()
    {
        if (File.Exists(Pending))
            File.Move(Pending, Pending + ".failed-" + DateTime.UtcNow.Ticks, false);
    }

    private List<(Entry Entry, byte[] Bytes)> Validate(string id)
    {
        var manifest = Read(id);
        var result = new List<(Entry, byte[])>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in manifest.Files)
        {
            if (entry is null) throw new InvalidDataException("Entrée de sauvegarde vide.");
            _ = Target(entry.Bucket, entry.Name);
            if (!seen.Add(entry.Bucket + "/" + entry.Name)) throw new InvalidDataException("Fichier dupliqué dans la sauvegarde.");
            var file = Safe(Safe(Root, id), entry.Bucket + "/" + entry.Name);
            var length = new FileInfo(file).Length;
            if (length > 4 * 1024 * 1024 || totalBytes + length > 32 * 1024 * 1024)
                throw new InvalidDataException("Profil trop volumineux.");
            var bytes = File.ReadAllBytes(file);
            totalBytes += bytes.Length;
            ValidateJson(bytes);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La sauvegarde du profil a été altérée.");
            result.Add((entry, bytes));
        }
        return result;
    }

    private Manifest Read(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id || id.Contains('/') || id.Contains('\\'))
            throw new InvalidDataException("Identifiant de sauvegarde invalide.");
        var path = Safe(Safe(Root, id), "manifest.json");
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Manifeste trop volumineux.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))
                       ?? throw new InvalidDataException("Sauvegarde vide.");
        if (manifest.Schema != 1 || manifest.Files is null || manifest.Files.Length > 512)
            throw new InvalidDataException("Sauvegarde incompatible.");
        return manifest;
    }

    private string Target(string bucket, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':'))
            throw new InvalidDataException("Nom de fichier de profil invalide.");
        var allowed = bucket == "data" && name == "games.json" || bucket == "config" &&
            (name is "settings.json" or "desktop-profile.json" ||
             name.StartsWith("mod-profiles/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) &&
             name.Count(c => c == '/') == 1);
        if (!allowed) throw new InvalidDataException("Fichier absent du périmètre de restauration.");
        return Safe(bucket == "data" ? paths.DataDirectory : paths.ConfigurationDirectory, name);
    }

    private static void ValidateJson(byte[] bytes)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Profil trop volumineux.");
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("Fichier de profil invalide.");
    }

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path + ".restore", bytes);
        File.Move(path + ".restore", path, true);
    }

    private static string Safe(string root, string relative)
    {
        var basePath = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(basePath, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Chemin en dehors du profil.");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Les liens ne sont pas admis dans les points de restauration.");
            if (current.Equals(basePath, comparison)) break;
        }
        return full;
    }
}
