using System.Text.Json;
using CubeShelf.Core.Mods;

namespace CubeShelf.Core.Platform;

public sealed record MigrationReport(bool AlreadyCompleted, int RuntimesCopied, int ModLibrariesCopied, IReadOnlyList<string> Messages);

public sealed class LegacyDataMigrator
{
    private readonly IPlatformPaths _paths;
    private readonly string _legacyApplicationDirectory;
    private readonly string _marker;

    public LegacyDataMigrator(IPlatformPaths paths, string legacyApplicationDirectory)
    {
        _paths = paths;
        _legacyApplicationDirectory = Path.GetFullPath(legacyApplicationDirectory);
        _marker = Path.Combine(Path.GetFullPath(paths.DataDirectory), "migration-v0.8.json");
    }

    public MigrationReport Run()
    {
        if (File.Exists(_marker)) return new(true, 0, 0, new[] { "Migration v0.8 déjà effectuée." });
        var messages = new List<string>();
        var runtimes = MigrateRuntimes(messages);
        var mods = MigrateMods(messages);
        Directory.CreateDirectory(Path.GetDirectoryName(_marker)!);
        File.WriteAllText(_marker, JsonSerializer.Serialize(new
        {
            schema = 1, completedAt = DateTimeOffset.UtcNow, runtimesCopied = runtimes,
            modLibrariesCopied = mods, sourcesPreserved = true, messages
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new(false, runtimes, mods, messages);
    }

    private int MigrateRuntimes(List<string> messages)
    {
        var gamesRoot = Path.Combine(_paths.DataDirectory, "Games");
        if (!Directory.Exists(gamesRoot)) return 0;
        var copied = 0;
        foreach (var gameDirectory in Directory.EnumerateDirectories(gamesRoot))
        {
            var gameId = Path.GetFileName(gameDirectory);
            var source = Path.Combine(gameDirectory, "Runtime", "current");
            if (!Directory.Exists(source)) continue;
            var runtimeRoot = Path.Combine(_paths.DataDirectory, "Runtimes", gameId);
            var destination = Path.Combine(runtimeRoot, "versions", "legacy-wpf");
            if (Directory.Exists(destination)) continue;
            CopyTree(source, destination);
            var executable = Directory.EnumerateFiles(destination, OperatingSystem.IsWindows() ? "*.exe" : "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("partyboard", StringComparison.OrdinalIgnoreCase));
            if (executable is null)
            {
                Directory.Delete(destination, true);
                messages.Add($"Runtime {gameId} ignoré : exécutable PartyBoard introuvable.");
                continue;
            }
            Directory.CreateDirectory(runtimeRoot);
            File.WriteAllText(Path.Combine(runtimeRoot, "runtime-state.json"), JsonSerializer.Serialize(new
            {
                Version = "legacy-wpf",
                RelativeExecutable = Path.GetRelativePath(destination, executable).Replace(Path.DirectorySeparatorChar, '/')
            }, new JsonSerializerOptions { WriteIndented = true }));
            copied++;
            messages.Add($"Runtime {gameId} copié depuis l’installation WPF.");
        }
        return copied;
    }

    private int MigrateMods(List<string> messages)
    {
        var sourceRoot = Path.Combine(_legacyApplicationDirectory, "Mods");
        if (!Directory.Exists(sourceRoot)) return 0;
        var destinationRoot = Path.Combine(_paths.DataDirectory, "Mods");
        var copied = 0;
        foreach (var sourceGame in Directory.EnumerateDirectories(sourceRoot))
        {
            var gameId = Path.GetFileName(sourceGame);
            var destinationGame = Path.Combine(destinationRoot, gameId);
            if (Directory.Exists(destinationGame)) continue;
            CopyTree(sourceGame, destinationGame);
            RewriteModState(sourceGame, destinationGame);
            copied++;
            messages.Add($"Bibliothèque de mods {gameId} copiée; la source WPF est conservée.");
        }
        return copied;
    }

    private static void RewriteModState(string sourceGame, string destinationGame)
    {
        var state = Path.Combine(destinationGame, "installed.json");
        if (!File.Exists(state)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(state));
            var migrated = new List<PortableInstalledMod>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("Id").GetInt32();
                var oldContent = item.TryGetProperty("ContentRoot", out var content) ? content.GetString() ?? "" : "";
                var relative = string.IsNullOrWhiteSpace(oldContent) ? "" : Path.GetRelativePath(sourceGame, oldContent);
                var safe = !string.IsNullOrWhiteSpace(relative) && !relative.StartsWith("..", StringComparison.Ordinal);
                var fallback = Path.Combine(destinationGame, id.ToString());
                var contentRoot = safe ? Path.GetFullPath(Path.Combine(destinationGame, relative)) : DetectContentRoot(fallback);
                migrated.Add(new PortableInstalledMod(id,
                    item.TryGetProperty("Name", out var name) ? name.GetString() ?? $"Mod {id}" : $"Mod {id}",
                    item.TryGetProperty("Updated", out var updated) ? updated.GetInt64() : 0,
                    !item.TryGetProperty("Enabled", out var enabled) || enabled.GetBoolean(),
                    item.TryGetProperty("Priority", out var priority) ? priority.GetInt32() : 100,
                    contentRoot,
                    item.TryGetProperty("Sha256", out var hash) ? hash.GetString() ?? "" : ""));
            }
            File.WriteAllText(state, JsonSerializer.Serialize(migrated, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            File.Move(state, state + ".legacy", true);
        }
    }

    private static string DetectContentRoot(string root)
    {
        var files = Path.Combine(root, "files");
        return Directory.Exists(files) ? files : root;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }
}
