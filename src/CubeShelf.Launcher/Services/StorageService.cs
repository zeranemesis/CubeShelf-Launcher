namespace CubeShelf.Launcher.Services;

public sealed record StorageSummary(
    long RuntimeBytes,
    long GameDataBytes,
    long DolphinBytes,
    long CacheBytes,
    long UpdaterBackupBytes)
{
    public long TotalBytes =>
        RuntimeBytes +
        GameDataBytes +
        DolphinBytes +
        CacheBytes +
        UpdaterBackupBytes;
}

public static class StorageService
{
    private static string LocalDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf");

    public static StorageSummary Measure(IEnumerable<GameDefinition> games)
    {
        long runtime = 0;
        long gameData = 0;

        foreach (var game in games)
        {
            var current = Path.Combine(
                LocalDataRoot,
                "Games",
                game.Id,
                "Runtime",
                "current");

            var data = Path.Combine(current, game.Id);
            var totalCurrent = DirectorySize(current);
            var totalData = DirectorySize(data);

            gameData += totalData;
            runtime += Math.Max(0, totalCurrent - totalData);
        }

        var dolphin = DirectorySize(
            Path.Combine(LocalDataRoot, "Tools", "Dolphin"));

        var cache = DirectorySize(
            Path.Combine(LocalDataRoot, "Cache"));

        var backups = DirectorySize(
            Path.Combine(LocalDataRoot, "UpdaterBackups"));

        return new StorageSummary(
            runtime,
            gameData,
            dolphin,
            cache,
            backups);
    }

    public static long CleanTransientCaches()
    {
        var targets = new[]
        {
            Path.Combine(LocalDataRoot, "Cache"),
            Path.Combine(LocalDataRoot, "UpdaterBackups")
        };

        var before = targets.Sum(DirectorySize);

        foreach (var target in targets)
        {
            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
            }
            catch
            {
                // Cleanup is best-effort. A currently used cache is left intact.
            }
        }

        var after = targets.Sum(DirectorySize);
        return Math.Max(0, before - after);
    }

    public static long DirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }
}
