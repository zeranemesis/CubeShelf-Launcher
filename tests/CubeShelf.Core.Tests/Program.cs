using System.IO.Compression;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Security;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Downloads;

var failures = new List<string>();
Run("valid archive", TestValidArchive);
Run("path traversal rejected", TestTraversal);
Run("expanded size rejected", TestExpansionLimit);
Run("XDG paths", TestXdgPaths);
Run("strict platform artifact selection", TestManifestSelection);
Run("disc revision detection", TestDiscRevision);
Run("desktop profile persistence", TestProfilePersistence);
Run("verified PartyBoard installation", TestPartyBoardInstallation);
Run("portable mod lifecycle and conflicts", TestPortableMods);
Run("mod load order handed to PartyBoard", TestModLoadOrder);
Run("persistent download activity", TestDownloadActivity);
Run("HTTP range runtime resume", TestRuntimeResume);
Run("transactional mod dependencies", TestModDependencies);
Run("mod dependency cycle rejected", TestModDependencyCycle);
Run("mod transaction rollback", TestModRollback);
Run("GameBanana media cache", TestMediaCache);
Run("preferences persistence", TestPreferences);
Run("non-destructive WPF migration", TestLegacyMigration);
Run("transactional GameCube preparation", TestGameDataPreparation);
Run("verified launcher update", TestLauncherUpdate);

if (failures.Count == 0)
{
    Console.WriteLine("20 CubeShelf.Core tests passed.");
    return 0;
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
return 1;

void Run(string name, Action action)
{
    try { action(); Console.WriteLine($"OK  {name}"); }
    catch (Exception exception) { failures.Add($"FAIL {name}: {exception.Message}"); }
}

void TestValidArchive()
{
    WithTempRoot(root =>
    {
        var zip = CreateZip(root, "valid.zip", "folder/file.txt", new byte[] { 1, 2, 3 });
        var destination = Path.Combine(root, "out");
        SecureArchiveExtractor.Extract(zip, destination, Limits());
        Assert(File.Exists(Path.Combine(destination, "folder", "file.txt")));
    });
}

void TestTraversal()
{
    WithTempRoot(root =>
    {
        var zip = CreateZip(root, "escape.zip", "../escape.txt", new byte[] { 1 });
        AssertThrows<InvalidDataException>(() =>
            SecureArchiveExtractor.Extract(zip, Path.Combine(root, "out"), Limits()));
        Assert(!File.Exists(Path.Combine(root, "escape.txt")));
    });
}

void TestExpansionLimit()
{
    WithTempRoot(root =>
    {
        var zip = CreateZip(root, "large.zip", "large.bin", new byte[64]);
        AssertThrows<InvalidDataException>(() => SecureArchiveExtractor.Extract(
            zip, Path.Combine(root, "out"), new ArchiveExtractionLimits(5, 32, 32)));
    });
}

void TestXdgPaths()
{
    WithTempRoot(root =>
    {
        var paths = new PlatformPaths(environment: new Dictionary<string, string?>
        {
            ["XDG_DATA_HOME"] = Path.Combine(root, "data"),
            ["XDG_CACHE_HOME"] = Path.Combine(root, "cache"),
            ["XDG_CONFIG_HOME"] = Path.Combine(root, "config")
        }, platform: PlatformFamily.Linux);
        Assert(paths.DataDirectory == Path.Combine(root, "data", "CubeShelf"));
        Assert(paths.DownloadHistoryFile.StartsWith(paths.DataDirectory, StringComparison.Ordinal));
    });
}

void TestManifestSelection()
{
    var manifest = new ReleaseManifest(2, "1.0.0", new[]
    {
        new ReleaseArtifact("windows", "x64", "launcher", "https://example.invalid/win", 1, new string('a', 64)),
        new ReleaseArtifact("linux", "x64", "launcher", "https://example.invalid/linux", 1, new string('b', 64))
    });
    Assert(manifest.Select("linux", "x64", "launcher").Url.EndsWith("/linux"));
    AssertThrows<PlatformNotSupportedException>(() => manifest.Select("macos", "arm64", "launcher"));
}

void TestDiscRevision()
{
    WithTempRoot(root =>
    {
        var image = Path.Combine(root, "disc.iso");
        var header = new byte[8];
        System.Text.Encoding.ASCII.GetBytes("GMPE01").CopyTo(header, 0);
        header[7] = 1;
        File.WriteAllBytes(image, header);
        var result = DiscImageService.Inspect(image);
        Assert(result is { Recognized: true, Supported: true, VersionId: "GMPE01_01" });
    });
}

void TestProfilePersistence()
{
    WithTempRoot(root =>
    {
        var store = new GameProfileStore(root);
        store.Save(new GameProfileEntry("game", "/runtime/partyboard", "/games/disc.iso"));
        store.Save(new GameProfileEntry("game", "/runtime/new", "/games/disc.iso"));
        var loaded = store.Load("GAME");
        Assert(loaded is { Executable: "/runtime/new", DiscImage: "/games/disc.iso" });
    });
}

void TestPartyBoardInstallation()
{
    WithTempRoot(root =>
    {
        var launch = OperatingSystem.IsWindows() ? "partyboard.exe" : "partyboard";
        byte[] package;
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry(launch);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("runtime");
            }
            package = memory.ToArray();
        }

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var manifest = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 2,
            version = "test-1",
            artifacts = new Dictionary<string, object>
            {
                [rid] = new { name = "partyboard.zip", kind = "zip", launch, sha256 = sha, size = package.Length }
            }
        }));
        using var client = new HttpClient(new StaticHttpHandler(uri =>
            uri.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) ? manifest : package));
        var installer = new PartyBoardInstaller(new TestPaths(root), client);
        var result = installer.InstallLatestAsync("owner", "repo", "tag", "game").GetAwaiter().GetResult();
        Assert(File.Exists(result.ExecutablePath));
        Assert(File.ReadAllText(result.ExecutablePath) == "runtime");
        var installed = installer.GetStatus("game");
        Assert(installed is { IsInstalled: true, NeedsRepair: false, Version: "test-1" });

        File.Delete(result.ExecutablePath);
        Assert(installer.GetStatus("game").NeedsRepair);
        var repaired = installer.RepairLatestAsync("owner", "repo", "tag", "game").GetAwaiter().GetResult();
        Assert(File.Exists(repaired.ExecutablePath));
        Assert(installer.Uninstall("game"));
        Assert(!installer.GetStatus("game").IsInstalled);
        Assert(!File.Exists(repaired.ExecutablePath));
    });
}

// PartyBoard reads active-mods.txt highest priority first and lets the first
// root claiming a path win, so this order is the mod load order.
void TestModLoadOrder()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        using var client = new HttpClient(new StaticHttpHandler(request =>
            CreateZipBytes("files/data/board.bin", new byte[] { 7 })));
        var banana = new GameBananaClient(client);
        manager.InstallAsync(new GameBananaMod(11, "Low", 1, "https://files.gamebanana.com/low.zip", "low.zip"), banana)
            .GetAwaiter().GetResult();
        manager.InstallAsync(new GameBananaMod(22, "High", 2, "https://files.gamebanana.com/high.zip", "high.zip"), banana)
            .GetAwaiter().GetResult();
        manager.SetPriority(11, 10);
        manager.SetPriority(22, 900);

        var listFile = manager.PrepareActiveList();
        Assert(listFile == manager.ActiveListFile);
        var lines = File.ReadAllLines(listFile);
        Assert(lines.Length == 2);
        Assert(lines.All(Path.IsPathFullyQualified));
        Assert(lines[0].EndsWith(Path.Combine("22", "files")));
        Assert(lines[1].EndsWith(Path.Combine("11", "files")));

        // A list corrupted outside the launcher must not survive the next launch.
        File.WriteAllText(listFile, "garbage written by something else");
        Assert(File.ReadAllLines(manager.PrepareActiveList()).SequenceEqual(lines));

        // A disabled mod disappears from the list the game reads.
        manager.SetEnabled(22, false);
        var remaining = File.ReadAllLines(manager.PrepareActiveList());
        Assert(remaining.Length == 1 && remaining[0].EndsWith(Path.Combine("11", "files")));
    });
}

void TestPortableMods()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        var firstPackage = CreateZipBytes("files/shared.bin", new byte[] { 1 });
        var secondPackage = CreateZipBytes("files/shared.bin", new byte[] { 2 });
        var first = new GameBananaMod(1, "First", 1, "https://files.gamebanana.com/first.zip", "first.zip");
        var second = new GameBananaMod(2, "Second", 2, "https://files.gamebanana.com/second.zip", "second.zip");
        var split = firstPackage.Length / 2;
        var partialDirectory = Path.Combine(paths.DataDirectory, "Mods", "GAME", ".downloads");
        Directory.CreateDirectory(partialDirectory);
        File.WriteAllBytes(Path.Combine(partialDirectory, "1-1.zip.part"), firstPackage[..split]);
        var rangeHandler = new RangeHttpHandler(Array.Empty<byte>(), firstPackage, split);
        using var firstClient = new HttpClient(rangeHandler);
        using var secondClient = new HttpClient(new StaticHttpHandler(_ => secondPackage));
        manager.InstallAsync(first, new GameBananaClient(firstClient)).GetAwaiter().GetResult();
        Assert(rangeHandler.RangeObserved);
        manager.InstallAsync(second, new GameBananaClient(secondClient)).GetAwaiter().GetResult();
        Assert(manager.GetInstalled().Count == 2);
        Assert(manager.AnalyzeConflicts().Single().RelativePath == "shared.bin");
        manager.SetEnabled(2, false);
        Assert(manager.AnalyzeConflicts().Count == 0);
        Assert(File.ReadAllLines(manager.ActiveListFile).Length == 1);
        manager.SetPriority(1, 250);
        Assert(manager.GetInstalled().Single(item => item.Id == 1).Priority == 250);
        manager.Uninstall(1);
        Assert(manager.GetInstalled().Count == 1);
    });
}

byte[] CreateZipBytes(string entryName, byte[] bytes)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
    return memory.ToArray();
}

byte[] CreateZipEntries(IReadOnlyDictionary<string, byte[]> entries)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        foreach (var (entryName, bytes) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
    return memory.ToArray();
}

void TestModDependencies()
{
    WithTempRoot(root =>
    {
        var packages = CreateDependencyPackages(cycle: false);
        using var http = new HttpClient(new ModGraphHttpHandler(packages.Root, packages.Dependency));
        var manager = new PortableModManager(new TestPaths(root), "GAME");
        var rootMod = new GameBananaMod(1, "Root", 10, "https://files.gamebanana.com/root.zip", "root.zip");
        var plan = manager.PlanInstallAsync(rootMod, new GameBananaClient(http)).GetAwaiter().GetResult();
        Assert(plan.Packages.Select(item => item.Mod.Id).SequenceEqual(new[] { 2, 1 }));
        Assert(plan.Summary.Contains("1 dépendance", StringComparison.Ordinal));
        manager.InstallPlanAsync(plan).GetAwaiter().GetResult();
        Assert(manager.GetInstalled().Count == 2);
        Assert(File.Exists(Path.Combine(root, "data", "Mods", "GAME", "1", "files", "root.bin")));
    });
}

void TestModDependencyCycle()
{
    WithTempRoot(root =>
    {
        var packages = CreateDependencyPackages(cycle: true);
        using var http = new HttpClient(new ModGraphHttpHandler(packages.Root, packages.Dependency));
        var manager = new PortableModManager(new TestPaths(root), "GAME");
        var rootMod = new GameBananaMod(1, "Root", 10, "https://files.gamebanana.com/root.zip", "root.zip");
        AssertThrows<InvalidDataException>(() => manager.PlanInstallAsync(rootMod, new GameBananaClient(http)).GetAwaiter().GetResult());
        Assert(manager.GetInstalled().Count == 0);
    });
}

void TestModRollback()
{
    WithTempRoot(root =>
    {
        var packages = CreateDependencyPackages(cycle: false);
        using var http = new HttpClient(new ModGraphHttpHandler(packages.Root, packages.Dependency));
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        var rootMod = new GameBananaMod(1, "Root", 10, "https://files.gamebanana.com/root.zip", "root.zip");
        var plan = manager.PlanInstallAsync(rootMod, new GameBananaClient(http)).GetAwaiter().GetResult();
        var collision = Path.Combine(paths.DataDirectory, "Mods", "GAME", "1");
        File.WriteAllText(collision, "keep");
        AssertThrows<IOException>(() => manager.InstallPlanAsync(plan).GetAwaiter().GetResult());
        Assert(File.ReadAllText(collision) == "keep");
        Assert(!Directory.Exists(Path.Combine(paths.DataDirectory, "Mods", "GAME", "2")));
        Assert(manager.GetInstalled().Count == 0);
    });
}

void TestMediaCache()
{
    WithTempRoot(root =>
    {
        var handler = new ImageHttpHandler();
        using var http = new HttpClient(handler);
        var cache = new MediaCacheService(new TestPaths(root), http);
        var url = "https://images.gamebanana.com/test.png";
        var first = cache.GetAsync(url, true).GetAwaiter().GetResult();
        var second = cache.GetAsync(url, true).GetAwaiter().GetResult();
        Assert(first == second && File.Exists(first));
        Assert(handler.Requests == 1);
        AssertThrows<InvalidDataException>(() => cache.GetAsync("https://example.com/test.png", true).GetAwaiter().GetResult());
        Assert(cache.Clear() > 0 && !File.Exists(first));
    });
}

void TestPreferences()
{
    WithTempRoot(root =>
    {
        var store = new UserPreferencesStore(new TestPaths(root));
        store.Save(new UserPreferences(Language: "en", Theme: "light", RefreshModsOnOpen: false));
        var loaded = store.Load();
        Assert(loaded.Language == "en" && loaded.Theme == "light" && !loaded.RefreshModsOnOpen);
        store.Reset();
        Assert(store.Load().Language == "fr");
    });
}

void TestLegacyMigration()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var legacyApp = Path.Combine(root, "legacy-app");
        var legacyGame = Path.Combine(legacyApp, "Mods", "GAME");
        var legacyContent = Path.Combine(legacyGame, "7", "files");
        Directory.CreateDirectory(legacyContent);
        File.WriteAllBytes(Path.Combine(legacyContent, "mod.bin"), new byte[] { 7 });
        File.WriteAllText(Path.Combine(legacyGame, "installed.json"), System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { Id = 7, Name = "Legacy", Updated = 4L, Enabled = true, Priority = 120,
                Path = Path.Combine(legacyGame, "7"), ContentRoot = legacyContent, Sha256 = "abc", ArchiveName = "legacy.zip" }
        }));
        var oldRuntime = Path.Combine(paths.DataDirectory, "Games", "GAME", "Runtime", "current");
        Directory.CreateDirectory(Path.Combine(oldRuntime, "res"));
        File.WriteAllText(Path.Combine(oldRuntime, OperatingSystem.IsWindows() ? "PartyBoard.exe" : "partyboard"), "runtime");

        var migrator = new LegacyDataMigrator(paths, legacyApp);
        var report = migrator.Run();
        Assert(report.RuntimesCopied == 1 && report.ModLibrariesCopied == 1);
        Assert(File.Exists(Path.Combine(legacyContent, "mod.bin")));
        var manager = new PortableModManager(paths, "GAME");
        var migrated = manager.GetInstalled().Single();
        Assert(File.Exists(Path.Combine(migrated.ContentRoot, "mod.bin")));
        Assert(new PartyBoardInstaller(paths).GetStatus("GAME").IsInstalled);
        Assert(migrator.Run().AlreadyCompleted);
    });
}

void TestGameDataPreparation()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var runtimeDirectory = Path.Combine(root, "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        var executable = Path.Combine(runtimeDirectory, OperatingSystem.IsWindows() ? "PartyBoard.exe" : "partyboard");
        File.WriteAllText(executable, "runtime");
        var iso = Path.Combine(root, "game.iso");
        var bytes = new byte[0x500];
        System.Text.Encoding.ASCII.GetBytes("GMPE01").CopyTo(bytes, 0);
        bytes[7] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x424, 4), 0x440);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x428, 4), 33);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x440, 4), 0x01000000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x448, 4), 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x450, 4), 0x480);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x454, 4), 3);
        System.Text.Encoding.UTF8.GetBytes("file.bin\0").CopyTo(bytes, 0x458);
        new byte[] { 4, 5, 6 }.CopyTo(bytes, 0x480);
        File.WriteAllBytes(iso, bytes);
        var preparer = new GameDataPreparer(paths);
        var result = preparer.PrepareAsync("GAME", executable, iso).GetAwaiter().GetResult();
        Assert(File.ReadAllBytes(Path.Combine(result, "file.bin")).SequenceEqual(new byte[] { 4, 5, 6 }));
        Assert(preparer.IsPrepared("GAME", executable));
        Assert(File.Exists(iso));
    });
}

void TestLauncherUpdate()
{
    WithTempRoot(root =>
    {
        var package = CreateZipBytes("CubeShelf", new byte[] { 1, 2, 3 });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos";
        var manifest = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 2, version = "9.0.0",
            artifacts = new[] { new { os, architecture = "x64", type = "launcher", url = "https://downloads.example.test/update.zip", size = package.Length, sha256 = hash } }
        }));
        using var http = new HttpClient(new UpdateHttpHandler(manifest, package));
        var service = new LauncherUpdateService(new TestPaths(root), http, new Uri("https://updates.example.test/manifest.json"));
        var update = service.CheckAsync("9.0.0-preview.2").GetAwaiter().GetResult();
        Assert(update.Available && update.LatestVersion == "9.0.0");
        var downloaded = service.DownloadAsync(update).GetAwaiter().GetResult();
        Assert(File.ReadAllBytes(downloaded).SequenceEqual(package));
    });
}

(byte[] Root, byte[] Dependency) CreateDependencyPackages(bool cycle)
{
    byte[] Manifest(int id, int[] dependencies) => System.Text.Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 1, gameId = "GAME", modId = id, version = "1.0.0",
            dependencies = dependencies.Select(value => new { id = value, minimumUpdated = 0, required = true }).ToArray(),
            incompatibleWith = Array.Empty<int>()
        }));
    return (
        CreateZipEntries(new Dictionary<string, byte[]> { ["cubeshelf-mod.json"] = Manifest(1, new[] { 2 }), ["files/root.bin"] = new byte[] { 1 } }),
        CreateZipEntries(new Dictionary<string, byte[]> { ["cubeshelf-mod.json"] = Manifest(2, cycle ? new[] { 1 } : Array.Empty<int>()), ["files/dep.bin"] = new byte[] { 2 } })
    );
}

void TestDownloadActivity()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var store = new DownloadActivityStore(paths);
        var active = store.Create("runtime", "PartyBoard", "GAME");
        store.Update(active.Id, DownloadActivityState.Running, .42, "Téléchargement");
        var reloaded = new DownloadActivityStore(paths);
        Assert(reloaded.Load().Single().Progress == .42);
        Assert(reloaded.Load().Single().ReferenceId == "GAME");
        Assert(reloaded.MarkInterruptedOperations() == 1);
        Assert(reloaded.Load().Single().State == DownloadActivityState.Interrupted);
        reloaded.ClearFinished();
        Assert(reloaded.Load().Count == 0);
    });
}

void TestRuntimeResume()
{
    WithTempRoot(root =>
    {
        var launch = OperatingSystem.IsWindows() ? "partyboard.exe" : "partyboard";
        var package = CreateZipBytes(launch, System.Text.Encoding.UTF8.GetBytes("resumed-runtime"));
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var manifest = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 2,
            version = "resume-1",
            artifacts = new Dictionary<string, object>
            {
                [rid] = new { name = "resume.zip", kind = "zip", launch, sha256 = sha, size = package.Length }
            }
        }));
        var paths = new TestPaths(root);
        var downloadDirectory = Path.Combine(paths.CacheDirectory, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var split = package.Length / 2;
        File.WriteAllBytes(Path.Combine(downloadDirectory, "resume.zip.part"), package[..split]);
        var handler = new RangeHttpHandler(manifest, package, split);
        using var client = new HttpClient(handler);
        var installer = new PartyBoardInstaller(paths, client);
        var result = installer.InstallLatestAsync("owner", "repo", "tag", "game").GetAwaiter().GetResult();
        Assert(handler.RangeObserved);
        Assert(File.Exists(result.ExecutablePath));
    });
}

string CreateZip(string root, string name, string entryName, byte[] bytes)
{
    var zip = Path.Combine(root, name);
    using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
    var entry = archive.CreateEntry(entryName);
    using var stream = entry.Open();
    stream.Write(bytes);
    return zip;
}

void WithTempRoot(Action<string> action)
{
    var root = Path.Combine(Path.GetTempPath(), "CubeShelfCoreTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try { action(root); }
    finally { try { Directory.Delete(root, true); } catch { } }
}

ArchiveExtractionLimits Limits() => new(100, 1024 * 1024, 1024 * 1024);
void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
void AssertThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class StaticHttpHandler(Func<Uri, byte[]> content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content(request.RequestUri!)),
            RequestMessage = request
        };
        return Task.FromResult(response);
    }
}

sealed class TestPaths(string root) : IPlatformPaths
{
    public string DataDirectory { get; } = Path.Combine(root, "data");
    public string CacheDirectory { get; } = Path.Combine(root, "cache");
    public string ConfigurationDirectory { get; } = Path.Combine(root, "config");
    public string DownloadHistoryFile { get; } = Path.Combine(root, "data", "downloads.json");
}

sealed class RangeHttpHandler(byte[] manifest, byte[] package, int expectedStart) : HttpMessageHandler
{
    public bool RangeObserved { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(manifest), RequestMessage = request
            });

        var start = request.Headers.Range?.Ranges.Single().From;
        if (start != expectedStart) throw new InvalidOperationException($"Expected Range {expectedStart}, received {start}.");
        RangeObserved = true;
        var content = new ByteArrayContent(package[expectedStart..]);
        content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(expectedStart,
            package.Length - 1, package.Length);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = content, RequestMessage = request
        });
    }
}

sealed class ModGraphHttpHandler(byte[] rootPackage, byte[] dependencyPackage) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        byte[] content;
        if (uri.Host.Equals("api.gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            var id = uri.Query.Contains("itemid=1", StringComparison.Ordinal) ? 1 : 2;
            var name = id == 1 ? "Root" : "Dependency";
            content = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["name"] = name,
                ["udate"] = id == 1 ? 10 : 20,
                ["Files().aFiles()"] = new Dictionary<string, object>
                {
                    ["download"] = $"https://files.gamebanana.com/{(id == 1 ? "root" : "dependency")}.zip"
                },
                ["Url().sProfileUrl()"] = $"https://gamebanana.com/mods/{id}"
            }));
        }
        else content = uri.AbsolutePath.EndsWith("root.zip", StringComparison.Ordinal) ? rootPackage : dependencyPackage;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content), RequestMessage = request
        });
    }
}

sealed class ImageHttpHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        var content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = content, RequestMessage = request
        });
    }
}

sealed class UpdateHttpHandler(byte[] manifest, byte[] package) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) ? manifest : package);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content, RequestMessage = request });
    }
}
