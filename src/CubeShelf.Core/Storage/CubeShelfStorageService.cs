using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Storage;

public sealed record CubeShelfStorageSummary(
    long RuntimeBytes,
    long GameDataBytes,
    long PreservedGameDataBytes,
    long DolphinBytes,
    long CacheBytes,
    long UpdaterBackupBytes)
{
    public long TotalBytes => RuntimeBytes + GameDataBytes + PreservedGameDataBytes + DolphinBytes + CacheBytes + UpdaterBackupBytes;
}

/// <summary>
/// Portable storage accounting and conservative cache cleanup. Original ISO/GCM/RVZ
/// files are never discovered or deleted by this service.
/// </summary>
public sealed class CubeShelfStorageService
{
    private readonly IPlatformPaths _paths;

    public CubeShelfStorageService(IPlatformPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public CubeShelfStorageSummary Measure(IEnumerable<GameCatalogEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        var runtimeRoot = Path.Combine(_paths.DataDirectory, "Runtimes");
        var runtimeTotal = DirectorySize(runtimeRoot);
        long gameData = 0;

        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.Id)) continue;
            var versionsRoot = Path.Combine(runtimeRoot, game.Id, "versions");
            if (!Directory.Exists(versionsRoot)) continue;
            try
            {
                foreach (var version in Directory.EnumerateDirectories(versionsRoot))
                    gameData += DirectorySize(Path.Combine(version, game.Id));
            }
            catch { }
        }

        var preserved = DirectorySize(Path.Combine(_paths.DataDirectory, "PreparedGameData"));
        var dolphin = DirectorySize(Path.Combine(_paths.DataDirectory, "Tools", "Dolphin"));
        var cache = DirectorySize(_paths.CacheDirectory);
        var backups = DirectorySize(Path.Combine(_paths.DataDirectory, "UpdaterBackups"));

        return new CubeShelfStorageSummary(
            Math.Max(0, runtimeTotal - gameData),
            gameData,
            preserved,
            dolphin,
            cache,
            backups);
    }

    public long CleanTransientCaches()
    {
        var targets = new[]
        {
            _paths.CacheDirectory,
            Path.Combine(_paths.DataDirectory, "UpdaterBackups")
        };
        var before = targets.Sum(DirectorySize);
        foreach (var target in targets)
        {
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            }
            catch
            {
                // Best effort: a cache currently in use is left intact.
            }
        }
        return Math.Max(0, before - targets.Sum(DirectorySize));
    }

    public static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return total;
    }
}

/// <summary>
/// Keeps prepared GameCube data across a "PartyBoard only" uninstall. The vault
/// lives inside CubeShelf's data directory, so moves are normally same-volume and
/// atomic. The original disc image is never part of the vault.
/// </summary>
public sealed class PreparedGameDataVault
{
    private readonly string _root;

    public PreparedGameDataVault(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _root = Path.Combine(paths.DataDirectory, "PreparedGameData");
    }

    public bool HasData(string gameId) => Directory.Exists(GameRoot(gameId));
    public string GetPath(string gameId) => GameRoot(gameId);

    public bool DetachFromRuntime(string gameId, string runtimeExecutable)
    {
        ValidateGameId(gameId);
        if (string.IsNullOrWhiteSpace(runtimeExecutable) || !File.Exists(runtimeExecutable)) return false;
        var source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(runtimeExecutable))!, gameId);
        if (!Directory.Exists(Path.Combine(source, "files"))) return false;

        Directory.CreateDirectory(_root);
        var target = GameRoot(gameId);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        MoveDirectory(source, target);
        return Directory.Exists(Path.Combine(target, "files"));
    }

    public bool RestoreToRuntime(string gameId, string runtimeExecutable)
    {
        ValidateGameId(gameId);
        var source = GameRoot(gameId);
        if (!Directory.Exists(Path.Combine(source, "files")) || !File.Exists(runtimeExecutable)) return false;
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(runtimeExecutable))!, gameId);
        if (Directory.Exists(target)) return false;
        MoveDirectory(source, target);
        return Directory.Exists(Path.Combine(target, "files"));
    }

    public bool Delete(string gameId)
    {
        ValidateGameId(gameId);
        var path = GameRoot(gameId);
        if (!Directory.Exists(path)) return false;
        Directory.Delete(path, recursive: true);
        return true;
    }

    private string GameRoot(string gameId)
    {
        ValidateGameId(gameId);
        return Path.Combine(_root, gameId);
    }

    private static void MoveDirectory(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            CopyDirectory(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Identifiant de jeu invalide.", nameof(gameId));
    }
}
