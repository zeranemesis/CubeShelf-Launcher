using SharpCompress.Archives;
using SharpCompress.Common;
using System.Security.Cryptography;

namespace CubeShelf.Launcher.Services;

public sealed record ModProgress(string Stage, double Progress);

public sealed record InstalledMod(
    int Id,
    string Name,
    long Updated,
    bool Enabled,
    int Priority,
    string Path,
    string ContentRoot,
    string Sha256,
    string ArchiveName);

public sealed record InstallResult(
    int Id,
    string Name,
    string Path,
    string ContentRoot,
    string Sha256);

public sealed class ModManager
{
    private readonly GameDefinition _game;
    private readonly GameBananaService _gb;
    private readonly string _root;
    private readonly string _stateFile;
    private readonly string _activeListFile;

    public ModManager(LauncherConfig config, GameDefinition game, GameBananaService gb)
    {
        _game = game;
        _gb = gb;

        var modsBase = Path.IsPathRooted(config.ModsRoot)
            ? config.ModsRoot
            : Path.Combine(AppContext.BaseDirectory, config.ModsRoot);

        _root = Path.GetFullPath(Path.Combine(modsBase, game.Id));
        _stateFile = Path.Combine(_root, "installed.json");
        _activeListFile = Path.Combine(_root, "active-mods.txt");
        Directory.CreateDirectory(_root);
    }

    public string ActiveListFile => _activeListFile;

    public IReadOnlyList<InstalledMod> GetInstalled()
    {
        if (!File.Exists(_stateFile))
            return Array.Empty<InstalledMod>();

        return JsonSerializer.Deserialize<List<InstalledMod>>(
            File.ReadAllText(_stateFile)) ?? new();
    }

    public async Task<InstallResult> InstallAsync(
        int id,
        Action<ModProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mod = await _gb.GetModAsync(id, cancellationToken);
        var ext = Path.GetExtension(mod.ArchiveName);

        if (string.IsNullOrWhiteSpace(ext))
            ext = GuessExtensionFromUrl(mod.ArchiveUrl);

        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";

        var temp = Path.Combine(
            Path.GetTempPath(),
            $"cubeshelf-{_game.Id}-{id}-{Guid.NewGuid():N}{ext}");

        var target = Path.Combine(_root, id.ToString());

        progress?.Invoke(new("download", .12));
        await _gb.DownloadAsync(
            mod,
            temp,
            new Progress<double>(p =>
                progress?.Invoke(new("download", .12 + p * .50))),
            cancellationToken);

        progress?.Invoke(new("hash", .65));

        string hash;
        await using (var fs = File.OpenRead(temp))
            hash = Convert.ToHexString(
                await SHA256.HashDataAsync(fs, cancellationToken)).ToLowerInvariant();

        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);

        progress?.Invoke(new("extract", .72));
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var archive = ArchiveFactory.OpenArchive(temp);
            archive.WriteToDirectory(
                staging,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
        }
        catch
        {
            try { Directory.Delete(staging, true); } catch { }

            throw new InvalidDataException(
                "L'archive n'a pas pu être extraite. ZIP / 7z / RAR sont pris en charge.");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new("normalize", .88));
        var stagingContentRoot = DetectContentRoot(staging);

        if (Directory.Exists(target))
            Directory.Delete(target, true);

        var relativeContent = Path.GetRelativePath(staging, stagingContentRoot);
        Directory.Move(staging, target);

        var contentRoot = Path.GetFullPath(Path.Combine(target, relativeContent));

        var installed = GetInstalled().Where(x => x.Id != id).ToList();
        var old = GetInstalled().FirstOrDefault(x => x.Id == id);

        var priority = old?.Priority ??
            (installed.Count == 0 ? 100 : installed.Max(x => x.Priority) + 10);

        installed.Add(new InstalledMod(
            id,
            mod.Name,
            mod.Updated,
            true,
            priority,
            target,
            contentRoot,
            hash,
            mod.ArchiveName));

        Save(installed);
        WriteActiveList();

        progress?.Invoke(new("done", 1.0));

        return new InstallResult(id, mod.Name, target, contentRoot, hash);
    }

    public void SetEnabled(int id, bool enabled)
    {
        var items = GetInstalled().ToList();
        var index = items.FindIndex(x => x.Id == id);

        if (index < 0)
            return;

        items[index] = items[index] with { Enabled = enabled };
        Save(items);
        WriteActiveList();
    }

    public void SetPriority(int id, int priority)
    {
        var items = GetInstalled().ToList();
        var index = items.FindIndex(x => x.Id == id);

        if (index < 0)
            return;

        items[index] = items[index] with { Priority = priority };
        Save(items);
        WriteActiveList();
    }

    public void Uninstall(int id)
    {
        var items = GetInstalled().ToList();
        var mod = items.FirstOrDefault(x => x.Id == id);

        if (mod is null)
            return;

        if (Directory.Exists(mod.Path))
            Directory.Delete(mod.Path, true);

        Save(items.Where(x => x.Id != id).ToList());
        WriteActiveList();
    }

    public string WriteActiveList()
    {
        var lines = GetInstalled()
            .Where(x => x.Enabled && Directory.Exists(x.ContentRoot))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Id)
            .Select(x => Path.GetFullPath(x.ContentRoot))
            .ToArray();

        File.WriteAllLines(_activeListFile, lines);
        return _activeListFile;
    }

    private string DetectContentRoot(string staging)
    {
        var directFiles = Path.Combine(staging, "files");
        if (Directory.Exists(directFiles))
            return directFiles;

        var gameFiles = Directory
            .EnumerateDirectories(staging, "files", SearchOption.AllDirectories)
            .FirstOrDefault(p =>
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(p)),
                    _game.Id,
                    StringComparison.OrdinalIgnoreCase));

        if (gameFiles is not null)
            return gameFiles;

        var dirs = Directory.GetDirectories(staging);
        var looseFiles = Directory.GetFiles(staging);

        if (dirs.Length == 1 && looseFiles.Length == 0)
        {
            var nestedFiles = Path.Combine(dirs[0], "files");
            return Directory.Exists(nestedFiles) ? nestedFiles : dirs[0];
        }

        return staging;
    }

    private static string GuessExtensionFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "";

        var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return (ext == ".zip" || ext == ".7z" || ext == ".rar") ? ext : "";
    }

    private void Save(IReadOnlyList<InstalledMod> items)
    {
        File.WriteAllText(
            _stateFile,
            JsonSerializer.Serialize(
                items,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
