using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CubeShelf.Mobile;

/// <summary>User data only; executable libraries are installed with DecompShelf.</summary>
public sealed class EmbeddedSm64Files(string filesDirectory)
{
    public const string EngineVersion = "ghostship-3.0.0";
    public string Root { get; } = Path.Combine(filesDirectory, "native-games", "sm64");
    public string Current => Path.Combine(Root, "current");
    public string Staging { get; } = Path.Combine(filesDirectory, "native-games", "sm64", "extracting-" + Guid.NewGuid().ToString("N"));
    public string Rom => Path.Combine(Root, "baserom.us.z64");
    private string Marker => Path.Combine(Current, ".prepared.json");
    public bool IsPrepared
    {
        get
        {
            try
            {
                if (!File.Exists(Marker) || new FileInfo(Marker).Length > 4096) return false;
                var record = JsonSerializer.Deserialize<Prepared>(File.ReadAllText(Marker));
                return record?.Engine == EngineVersion && record.GameBytes > 0 && record.PortBytes > 0
                    && File.Exists(Path.Combine(Current, "config.yml"))
                    && new FileInfo(Path.Combine(Current, "sm64.o2r")).Length == record.GameBytes
                    && new FileInfo(Path.Combine(Current, "ghostship.o2r")).Length == record.PortBytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
        }
    }

    public static bool SupportedRomHash(string sha1) => sha1 is
        "9bef1128717f958171a4afac3ed78ee2bb4e86ce" or "8a20a5c83d6ceb0f0506cfc9fa20d8f438cafe51";

    public async Task ImportRomAsync(Stream input, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Root);
        var partial = Rom + ".partial";
        try
        {
            await using (var output = File.Create(partial))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                var buffer = new byte[131072];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += read;
                    if (total > 16 * 1024 * 1024) throw new InvalidDataException("ROM trop volumineuse. Choisis Super Mario 64 US ou japonais au format .z64.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                }
                if (!SupportedRomHash(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()))
                    throw new InvalidDataException("ROM non reconnue. Une copie US ou japonaise de Super Mario 64 au format .z64 est nécessaire.");
                await output.FlushAsync(cancellation);
                output.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(partial, Rom, true);
        }
        finally { File.Delete(partial); }
    }

    public async Task StageAssetsAsync(Stream input, CancellationToken cancellation)
    {
        DiscardStaging();
        Directory.CreateDirectory(Staging);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (archive.Entries.Count > 4096) throw new InvalidDataException("Trop de fichiers de préparation.");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Contains('\\') || name.TrimEnd('/').Split('/').Any(p => p is ".." or "." or "")
                || !(name is "config.yml" or "ghostship.o2r" || name.StartsWith("assets/", StringComparison.Ordinal) || name.StartsWith(".tcc/", StringComparison.Ordinal))
                || !names.Add(name) || entry.Length > 16 * 1024 * 1024
                || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Fichier de préparation invalide.");
            total += entry.Length;
            if (total > 64 * 1024 * 1024) throw new InvalidDataException("Fichiers de préparation trop volumineux.");
        }
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(Staging, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(Staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Chemin de préparation invalide.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = File.Create(target);
            await using var source = entry.Open();
            await source.CopyToAsync(output, cancellation);
        }
    }

    public void CommitPrepared()
    {
        foreach (var file in new[] { "sm64.o2r", "ghostship.o2r" })
        {
            using var archive = ZipFile.OpenRead(Path.Combine(Staging, file));
            if (archive.Entries.Count == 0) throw new InvalidDataException("L’extraction du jeu est incomplète.");
        }
        if (!File.Exists(Path.Combine(Staging, "config.yml"))) throw new InvalidDataException("Configuration du moteur absente.");
        foreach (var file in new[] { "ghostship.cfg.json", "imgui.ini" })
            if (File.Exists(Path.Combine(Current, file))) File.Copy(Path.Combine(Current, file), Path.Combine(Staging, file), true);
        var record = new Prepared(EngineVersion, new FileInfo(Path.Combine(Staging, "sm64.o2r")).Length,
            new FileInfo(Path.Combine(Staging, "ghostship.o2r")).Length);
        File.WriteAllText(Path.Combine(Staging, ".prepared.json"), JsonSerializer.Serialize(record));
        var backup = Path.Combine(Root, "previous");
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        if (Directory.Exists(Current)) Directory.Move(Current, backup);
        try { Directory.Move(Staging, Current); }
        catch
        {
            if (Directory.Exists(backup)) Directory.Move(backup, Current);
            throw;
        }
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
    }

    public void DiscardStaging()
    {
        if (Directory.Exists(Staging)) Directory.Delete(Staging, true);
    }
    public void RecoverInterruptedCommit()
    {
        var backup = Path.Combine(Root, "previous");
        if (!Directory.Exists(Current) && Directory.Exists(backup)) Directory.Move(backup, Current);
    }
    private sealed record Prepared(string Engine, long GameBytes, long PortBytes);
}
