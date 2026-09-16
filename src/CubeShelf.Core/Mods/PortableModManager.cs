using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Mods;

public sealed record PortableInstalledMod(int Id, string Name, long Updated, bool Enabled, int Priority,
    string ContentRoot, string Sha256);
public sealed record PortableModConflict(string RelativePath, IReadOnlyList<int> ModIds);
public sealed record PortableModLayoutWarning(int Id, string Name, IReadOnlyList<string> TopLevelEntries);

public sealed class PortableModManager
{
    private static readonly ArchiveExtractionLimits Limits = new(20_000, 4L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024);
    private readonly string _root;
    private readonly string _state;
    private readonly string _active;

    public PortableModManager(IPlatformPaths paths, string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Identifiant de jeu invalide.", nameof(gameId));
        _gameId = gameId;
        _root = Path.Combine(Path.GetFullPath(paths.DataDirectory), "Mods", gameId);
        _state = Path.Combine(_root, "installed.json");
        _active = Path.Combine(_root, "active-mods.txt");
        Directory.CreateDirectory(_root);
        WriteActiveList();
    }

    public string ActiveListFile => _active;

    /// <summary>
    /// Rewrites active-mods.txt from the current state and returns its path.
    /// Called right before launching so PartyBoard never reads a list left
    /// stale by an interrupted install or an edit made outside the launcher.
    /// </summary>
    public string PrepareActiveList()
    {
        WriteActiveList();
        return _active;
    }

    public IReadOnlyList<PortableInstalledMod> GetInstalled()
    {
        if (!File.Exists(_state)) return Array.Empty<PortableInstalledMod>();
        try { return JsonSerializer.Deserialize<List<PortableInstalledMod>>(File.ReadAllText(_state)) ?? new(); }
        catch (JsonException) { return Array.Empty<PortableInstalledMod>(); }
    }

    public async Task<PortableInstalledMod> InstallAsync(GameBananaMod mod, GameBananaClient client,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var plan = await PlanInstallAsync(mod, client, progress, cancellationToken);
        var installed = await InstallPlanAsync(plan, false, progress, cancellationToken);
        return installed.Single(item => item.Id == mod.Id);
    }

    public async Task<ModInstallPlan> PlanInstallAsync(GameBananaMod rootMod, GameBananaClient client,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        const int maximumNodes = 32;
        const int maximumDepth = 8;
        var packages = new List<PreparedModPackage>();
        var warnings = new List<string>();
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();

        async Task Visit(GameBananaMod mod, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > maximumDepth) throw new InvalidDataException("La chaîne de dépendances dépasse 8 niveaux.");
            if (visited.Contains(mod.Id)) return;
            if (!visiting.Add(mod.Id)) throw new InvalidDataException($"Cycle de dépendances détecté autour du mod {mod.Id}.");
            if (visiting.Count + visited.Count > maximumNodes)
                throw new InvalidDataException("Le plan dépasse la limite de 32 mods.");

            var package = await PrepareAsync(mod, client, progress, cancellationToken);
            ValidateManifest(package.Manifest, mod);
            if (package.Manifest is null)
                warnings.Add($"{mod.Name} ne contient pas cubeshelf-mod.json : il sera traité comme un mod autonome.");
            else
            {
                foreach (var dependency in (package.Manifest.Dependencies ?? Array.Empty<ModDependency>()).DistinctBy(item => item.Id))
                {
                    if (dependency.Id <= 0 || dependency.Id == mod.Id)
                        throw new InvalidDataException($"Dépendance invalide déclarée par {mod.Name}.");
                    try
                    {
                        var dependencyMod = await client.GetAsync(dependency.Id, cancellationToken);
                        if (dependency.MinimumUpdated > 0 && dependencyMod.Updated < dependency.MinimumUpdated)
                            throw new InvalidDataException($"La dépendance {dependency.Id} est trop ancienne.");
                        await Visit(dependencyMod, depth + 1);
                    }
                    catch (Exception exception) when (!dependency.Required && exception is not OperationCanceledException)
                    {
                        warnings.Add($"Dépendance optionnelle {dependency.Id} ignorée : {exception.Message}");
                    }
                }
            }
            visiting.Remove(mod.Id);
            visited.Add(mod.Id);
            packages.Add(package);
        }

        await Visit(rootMod, 0);
        var plannedIds = packages.Select(item => item.Mod.Id).ToHashSet();
        var incompatible = packages.SelectMany(item => item.Manifest?.IncompatibleWith ?? Array.Empty<int>())
            .Where(id => id > 0 && !plannedIds.Contains(id) && GetInstalled().Any(item => item.Id == id && item.Enabled))
            .Distinct().Order().ToArray();
        foreach (var package in packages)
        {
            var declared = (package.Manifest?.IncompatibleWith ?? Array.Empty<int>()).Where(plannedIds.Contains).ToArray();
            if (declared.Length > 0)
                throw new InvalidDataException($"Le plan contient des mods incompatibles ({package.Mod.Id} / {string.Join(", ", declared)}).");
        }
        return new ModInstallPlan(rootMod.Id, _gameId, packages, warnings, incompatible);
    }

    public Task<IReadOnlyList<PortableInstalledMod>> InstallPlanAsync(ModInstallPlan plan,
        bool allowIncompatible = false, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(plan.GameId, _gameId, StringComparison.OrdinalIgnoreCase) || plan.Packages.Count == 0)
            throw new InvalidDataException("Le plan d’installation ne correspond pas au jeu sélectionné.");
        if (plan.IncompatibleInstalledModIds.Count > 0 && !allowIncompatible)
            throw new InvalidOperationException("Le plan entre en conflit avec des mods actifs et requiert une confirmation explicite.");

        var transaction = Path.Combine(_root, ".transaction-" + Guid.NewGuid().ToString("N"));
        var stagedRoot = Path.Combine(transaction, "staged");
        var backupRoot = Path.Combine(transaction, "backups");
        var priorState = File.Exists(_state) ? File.ReadAllBytes(_state) : null;
        var movedTargets = new List<string>();
        var backups = new List<(string Backup, string Target)>();
        try
        {
            Directory.CreateDirectory(stagedRoot);
            for (var index = 0; index < plan.Packages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = plan.Packages[index];
                var staged = Path.Combine(stagedRoot, package.Mod.Id.ToString());
                Directory.CreateDirectory(staged);
                SecureArchiveExtractor.Extract(package.ArchivePath, staged, Limits);
                progress?.Report(.1 + .55 * (index + 1d) / plan.Packages.Count);
            }

            Directory.CreateDirectory(backupRoot);
            var items = GetInstalled().Where(item => plan.Packages.All(package => package.Mod.Id != item.Id)).ToList();
            var installed = new List<PortableInstalledMod>();
            foreach (var package in plan.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(_root, package.Mod.Id.ToString());
                if (Directory.Exists(target))
                {
                    var backup = Path.Combine(backupRoot, package.Mod.Id.ToString());
                    Directory.Move(target, backup);
                    backups.Add((backup, target));
                }
                var staged = Path.Combine(stagedRoot, package.Mod.Id.ToString());
                var relativeContent = Path.GetRelativePath(staged, DetectContentRoot(staged));
                Directory.Move(staged, target);
                movedTargets.Add(target);
                var previous = GetInstalled().FirstOrDefault(item => item.Id == package.Mod.Id);
                var result = new PortableInstalledMod(package.Mod.Id, package.Mod.Name, package.Mod.Updated, true,
                    previous?.Priority ?? (items.Count == 0 ? 100 : items.Max(item => item.Priority) + 10),
                    Path.GetFullPath(Path.Combine(target, relativeContent)), package.Sha256);
                items.Add(result);
                installed.Add(result);
            }
            Save(items);
            WriteActiveList();
            foreach (var package in plan.Packages)
                if (File.Exists(package.ArchivePath)) File.Delete(package.ArchivePath);
            progress?.Report(1);
            return Task.FromResult<IReadOnlyList<PortableInstalledMod>>(installed);
        }
        catch
        {
            foreach (var target in movedTargets.AsEnumerable().Reverse())
                if (Directory.Exists(target)) Directory.Delete(target, true);
            foreach (var (backup, target) in backups.AsEnumerable().Reverse())
                if (Directory.Exists(backup)) Directory.Move(backup, target);
            if (priorState is null) { if (File.Exists(_state)) File.Delete(_state); }
            else File.WriteAllBytes(_state, priorState);
            WriteActiveList();
            throw;
        }
        finally
        {
            if (Directory.Exists(transaction)) Directory.Delete(transaction, true);
        }
    }

    private readonly string _gameId;

    private async Task<PreparedModPackage> PrepareAsync(GameBananaMod mod, GameBananaClient client,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var extension = GetArchiveExtension(mod);
        var downloads = Path.Combine(_root, ".downloads");
        Directory.CreateDirectory(downloads);
        var temporary = Path.Combine(downloads, $"{mod.Id}-{mod.Updated}{extension}.part");
        try
        {
            await client.DownloadAsync(mod, temporary, progress, cancellationToken);
            string sha;
            await using (var stream = File.OpenRead(temporary))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            return new PreparedModPackage(mod, temporary, ModManifestReader.Read(temporary), sha);
        }
        catch (InvalidDataException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static string GetArchiveExtension(GameBananaMod mod)
    {
        var extension = Path.GetExtension(mod.ArchiveName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension)) extension = Path.GetExtension(new Uri(mod.ArchiveUrl).AbsolutePath).ToLowerInvariant();
        if (extension is not ".zip" and not ".7z" and not ".rar")
            throw new InvalidDataException("Format d’archive de mod non pris en charge.");
        return extension;
    }

    private void ValidateManifest(CubeShelfModManifest? manifest, GameBananaMod mod)
    {
        if (manifest is null) return;
        if (manifest.Schema != 1) throw new InvalidDataException("Version de manifeste CubeShelf non prise en charge.");
        if (!string.Equals(manifest.GameId, _gameId, StringComparison.OrdinalIgnoreCase) || manifest.ModId != mod.Id)
            throw new InvalidDataException($"Le manifeste du mod {mod.Id} ne correspond pas au paquet téléchargé.");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("Le manifeste CubeShelf ne précise aucune version.");
        var dependencies = manifest.Dependencies ?? Array.Empty<ModDependency>();
        if (dependencies.Select(item => item.Id).Distinct().Count() != dependencies.Count)
            throw new InvalidDataException("Le manifeste CubeShelf contient des dépendances en double.");
    }

    public void SetEnabled(int id, bool enabled) => Update(id, item => item with { Enabled = enabled });
    public void SetPriority(int id, int priority) => Update(id, item => item with { Priority = priority });

    public void Uninstall(int id)
    {
        var items = GetInstalled().ToList();
        var item = items.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null) return;
        var target = Path.Combine(_root, id.ToString());
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Save(items.Where(candidate => candidate.Id != id).ToList());
        WriteActiveList();
    }

    public IReadOnlyList<PortableModConflict> AnalyzeConflicts()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var owners = new Dictionary<string, HashSet<int>>(comparer);
        foreach (var mod in GetInstalled().Where(item => item.Enabled && Directory.Exists(item.ContentRoot)))
            foreach (var file in Directory.EnumerateFiles(mod.ContentRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(mod.ContentRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!owners.TryGetValue(relative, out var ids)) owners[relative] = ids = new();
                ids.Add(mod.Id);
            }
        return owners.Where(pair => pair.Value.Count > 1)
            .Select(pair => new PortableModConflict(pair.Key, pair.Value.Order().ToArray())).OrderBy(item => item.RelativePath).ToArray();
    }

    // PartyBoard overlays a mod's content root onto the root of the disc, so a
    // pack whose root holds none of the disc's own folders replaces nothing: it
    // installs, enables and does exactly nothing in game, with no other symptom.
    // Dolphin-style texture packs land here, and so does an archive with one
    // level too many. Reported, never blocked - the disc may gain new files, and
    // only the player knows what they meant to install.
    public IReadOnlyList<PortableModLayoutWarning> AnalyzeLayout()
    {
        var warnings = new List<PortableModLayoutWarning>();
        foreach (var mod in GetInstalled().Where(item => item.Enabled && Directory.Exists(item.ContentRoot)))
        {
            var entries = Directory.EnumerateFileSystemEntries(mod.ContentRoot)
                .Select(Path.GetFileName).OfType<string>().ToArray();
            if (entries.Any(entry => DiscRootEntries.Contains(entry))) continue;
            warnings.Add(new PortableModLayoutWarning(mod.Id, mod.Name,
                entries.Order(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()));
        }
        return warnings;
    }

    private void Update(int id, Func<PortableInstalledMod, PortableInstalledMod> update)
    {
        var items = GetInstalled().ToList();
        var index = items.FindIndex(item => item.Id == id);
        if (index < 0) return;
        items[index] = update(items[index]);
        Save(items);
        WriteActiveList();
    }

    private void Save(IReadOnlyList<PortableInstalledMod> items)
    {
        Directory.CreateDirectory(_root);
        var temporary = _state + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _state, true);
    }

    // PartyBoard reads this list highest priority first and lets the first
    // root claiming a path win, so the order here is the mod load order.
    private void WriteActiveList() => File.WriteAllLines(_active, GetInstalled()
        .Where(item => item.Enabled && Directory.Exists(item.ContentRoot)).OrderByDescending(item => item.Priority)
        .ThenBy(item => item.Id).Select(item => Path.GetFullPath(item.ContentRoot)));

    private static readonly HashSet<string> DiscRootEntries =
        new(new[] { "data", "dll", "mess", "movie", "sound", "opening.bnr" }, StringComparer.OrdinalIgnoreCase);

    // Mod archives bury the disc root under whatever folder names the author
    // felt like: seen in the wild are "files/" at the top, "<mod>/<variant>/files/"
    // two deep, and "<mod>/store/files/" beside a sys/ folder that must never be
    // overlaid. Only the first shape used to be found, so most packs installed
    // with their own folder names as disc paths and changed nothing in game.
    // Search breadth-first for the shallowest directory that looks like the root
    // of the disc's file partition.
    private static string DetectContentRoot(string staging)
    {
        var found = FindDiscRoot(staging);
        if (found is not null) return found;

        // Nothing recognisable. Keep unwrapping a lone folder as before so a mod
        // that only adds files still installs; AnalyzeLayout reports the rest.
        var directories = Directory.GetDirectories(staging);
        return directories.Length == 1 && Directory.GetFiles(staging).Length == 0 ? directories[0] : staging;
    }

    private static string? FindDiscRoot(string staging)
    {
        const int maximumDepth = 6;
        const int maximumDirectories = 4096;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((staging, 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < maximumDirectories)
        {
            var (current, depth) = queue.Dequeue();
            string[] children;
            try { children = Directory.GetDirectories(current); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            // A "files" folder is the disc partition by name, so it wins over a
            // folder that merely happens to hold one of the disc's own entries.
            var files = children.FirstOrDefault(child =>
                Path.GetFileName(child).Equals("files", StringComparison.OrdinalIgnoreCase));
            if (files is not null) return files;

            if (HoldsDiscEntry(current)) return current;

            if (depth < maximumDepth)
                foreach (var child in children) queue.Enqueue((child, depth + 1));
        }
        return null;
    }

    private static bool HoldsDiscEntry(string directory) =>
        Directory.EnumerateFileSystemEntries(directory)
            .Select(Path.GetFileName).OfType<string>().Any(DiscRootEntries.Contains);
}
