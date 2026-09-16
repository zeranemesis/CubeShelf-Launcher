using System.IO.Compression;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Security;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Downloads;

var failures = new List<string>();
var executed = 0;
Run("valid archive", TestValidArchive);
Run("path traversal rejected", TestTraversal);
Run("expanded size rejected", TestExpansionLimit);
Run("XDG paths", TestXdgPaths);
Run("strict platform artifact selection", TestManifestSelection);
Run("disc revision detection", TestDiscRevision);
Run("desktop profile persistence", TestProfilePersistence);
Run("verified PartyBoard installation", TestPartyBoardInstallation);
Run("portable mod lifecycle and conflicts", TestPortableMods);
Run("catalog can follow the newest release", TestLatestReleaseTag);
Run("mod load order handed to PartyBoard", TestModLoadOrder);
Run("mod with no disc folder is flagged", TestModLayoutWarning);
Run("disc root found however deep it is buried", TestModContentRootDepth);
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
Run("per-game disc compatibility", TestPerGameDiscCompatibility);
Run("release asset pattern matching", TestReleaseAssetPattern);
Run("catalog keeps runtime acquisition metadata", TestCatalogRuntimeMetadata);
Run("shipped catalog is coherent", TestShippedCatalogIsCoherent);
Run("release checksum file verifies the install", TestReleaseAssetChecksum);
Run("no checksum means unverified, not blocked", TestReleaseAssetWithoutChecksum);

if (failures.Count == 0)
{
    Console.WriteLine($"{executed} CubeShelf.Core tests passed.");
    return 0;
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
return 1;

void Run(string name, Action action)
{
    executed++;
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
        var result = DiscImageService.Inspect(image, DiscImageService.MarioParty4DiscIds);
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
        var rid = CurrentRid();
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

// A catalog that names a fixed tag depends on the publisher keeping that one
// release pointing at the newest build. PartyBoard stopped doing that and the
// launcher went on reinstalling a months-old build over anything newer, so a
// catalog can now say "latest" and be told by GitHub which tag that is.
void TestLatestReleaseTag()
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
        var rid = CurrentRid();
        var manifest = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 2,
            version = "v9.9.9",
            artifacts = new Dictionary<string, object>
            {
                [rid] = new { name = "partyboard.zip", kind = "zip", launch, sha256 = sha, size = package.Length }
            }
        }));
        var releases = System.Text.Encoding.UTF8.GetBytes("""[{"tag_name":"v9.9.9"}]""");

        var asked = new List<string>();
        using var client = new HttpClient(new StaticHttpHandler(uri =>
        {
            asked.Add(uri.AbsoluteUri);
            if (uri.Host == "api.github.com") return releases;
            return uri.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) ? manifest : package;
        }));

        var installer = new PartyBoardInstaller(new TestPaths(root), client);
        var result = installer.InstallLatestAsync("owner", "repo", "latest", "game").GetAwaiter().GetResult();

        // The tag GitHub named is the one the download URLs use, not "latest".
        Assert(asked.Any(url => url.Contains("api.github.com") && url.Contains("/releases")));
        Assert(asked.Any(url => url.Contains("/releases/download/v9.9.9/manifest.json")));
        Assert(!asked.Any(url => url.Contains("/releases/download/latest/")));
        Assert(File.Exists(result.ExecutablePath));
        Assert(installer.GetStatus("game") is { IsInstalled: true, Version: "v9.9.9" });
    });
}

// The five shapes real GameBanana packs for this game actually ship. Only the
// first was handled before, so the rest installed with their own folder names
// standing in for disc paths and changed nothing in game.
void TestModContentRootDepth()
{
    var shapes = new (string Label, string Entry, string Expected)[]
    {
        ("files at the top",      "files/data/board.bin",                          "files"),
        ("one folder deep",       "Pack/files/data/board.bin",                     "Pack/files"),
        ("two folders deep",      "Toad Mod/MP4 DX Toad/files/data/board.bin",     "Toad Mod/MP4 DX Toad/files"),
        ("beside a sys folder",   "UI Mod/store/files/mess/board_e.dat",           "UI Mod/store/files"),
        ("disc folder unwrapped", "Pack/mess/board_e.dat",                         "Pack"),
    };

    foreach (var shape in shapes)
        WithTempRoot(root =>
        {
            var paths = new TestPaths(root);
            var manager = new PortableModManager(paths, "GAME");
            using var client = new HttpClient(new StaticHttpHandler(_ => CreateZipBytes(shape.Entry, new byte[] { 9 })));
            var installed = manager.InstallAsync(
                new GameBananaMod(41, shape.Label, 1, "https://files.gamebanana.com/m.zip", "m.zip"),
                new GameBananaClient(client)).GetAwaiter().GetResult();

            var expected = Path.Combine(paths.DataDirectory, "Mods", "GAME", "41",
                shape.Expected.Replace('/', Path.DirectorySeparatorChar));
            Assert(installed.ContentRoot == expected);
            // Whatever the wrapping, the disc sees the same path.
            var file = Directory.EnumerateFiles(installed.ContentRoot, "*", SearchOption.AllDirectories).Single();
            var virtualPath = "/" + Path.GetRelativePath(installed.ContentRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            Assert(virtualPath is "/data/board.bin" or "/mess/board_e.dat");
            Assert(manager.AnalyzeLayout().Count == 0);
        });
}

// A mod whose content root holds none of the disc's folders overlays nothing.
// It installs and enables like any other, so without this the only symptom is
// that the game looks untouched.
void TestModLayoutWarning()
{
    // The three shapes the fourteen live packs fall into when they carry no
    // disc folder. Each needs a different answer from the player, so each is
    // classified rather than lumped under "unexpected layout".
    var cases = new (string Entry, PortableModLayoutKind Kind)[]
    {
        ("Pack/GMPE01/tex1_128x128_3e7936174a209c24_14.png", PortableModLayoutKind.DolphinTextures),
        ("Pack/board_e.dat",                                 PortableModLayoutKind.LooseFiles),
        ("Pack/High res/bbattle.bin",                        PortableModLayoutKind.SeveralVariants),
    };

    var id = 50;
    foreach (var shape in cases)
        WithTempRoot(root =>
        {
            var paths = new TestPaths(root);
            var manager = new PortableModManager(paths, "GAME");
            var entries = new Dictionary<string, byte[]> { [shape.Entry] = new byte[] { 3 } };
            if (shape.Kind == PortableModLayoutKind.SeveralVariants)
                entries["Pack/Low res/bbattle.bin"] = new byte[] { 4 };
            using var client = new HttpClient(new StaticHttpHandler(_ => CreateZipEntries(entries)));
            manager.InstallAsync(new GameBananaMod(++id, shape.Entry, 1, "https://files.gamebanana.com/m.zip", "m.zip"),
                new GameBananaClient(client)).GetAwaiter().GetResult();

            var flagged = manager.AnalyzeLayout();
            Assert(flagged.Count == 1);
            Assert(flagged[0].Kind == shape.Kind);

            // Disabling it takes it out of what the game is told to load, so it
            // is no longer something to warn about.
            manager.SetEnabled(id, false);
            Assert(manager.AnalyzeLayout().Count == 0);
        });

    // A pack laid out like the disc is never flagged.
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        using var client = new HttpClient(new StaticHttpHandler(_ =>
            CreateZipBytes("files/data/board.bin", new byte[] { 1 })));
        manager.InstallAsync(new GameBananaMod(60, "Disc layout", 1, "https://files.gamebanana.com/d.zip", "d.zip"),
            new GameBananaClient(client)).GetAwaiter().GetResult();
        Assert(manager.AnalyzeLayout().Count == 0);
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
        var result = preparer.PrepareAsync("GAME", executable, iso, DiscImageService.MarioParty4DiscIds).GetAwaiter().GetResult();
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
            artifacts = new[] { new { os, architecture = CurrentArchitecture(), type = "launcher", url = "https://downloads.example.test/update.zip", size = package.Length, sha256 = hash } }
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
        var rid = CurrentRid();
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

// Mario Party 4 revisions used to be compiled into DiscImageService, so every game in the
// catalog was judged against PartyBoard's list. These cover the per-entry replacement.
void TestPerGameDiscCompatibility()
{
    WithTempRoot(root =>
    {
        var soulcalibur = WriteDiscHeader(root, "sc2.iso", "GRSPAF", revision: 0);
        var marioParty = WriteDiscHeader(root, "mp4.iso", "GMPE01", revision: 1);

        var ringOutDiscs = new[] { "GRSEAF", "GRSPAF", "GRSJAF", "GRSEPS" };

        // A bare disc id accepts every revision of that disc.
        var accepted = DiscImageService.Inspect(soulcalibur, ringOutDiscs);
        Assert(accepted is { Recognized: true, Supported: true, VersionId: "GRSPAF_00" });

        // The same disc must be refused by a runtime that does not claim it.
        var refused = DiscImageService.Inspect(soulcalibur, DiscImageService.MarioParty4DiscIds);
        Assert(refused is { Recognized: true, Supported: false });

        // And the converse, so the two sets are genuinely independent.
        Assert(!DiscImageService.Inspect(marioParty, ringOutDiscs).Supported);
        Assert(DiscImageService.Inspect(marioParty, DiscImageService.MarioParty4DiscIds).Supported);

        // A full version id stays exact: GMPE01_02 is not one of the two accepted revisions.
        var otherRevision = WriteDiscHeader(root, "mp4-rev2.iso", "GMPE01", revision: 2);
        Assert(!DiscImageService.Inspect(otherRevision, DiscImageService.MarioParty4DiscIds).Supported);

        Assert(DiscImageService.DescribeSupported(ringOutDiscs).Contains("GRSEAF", StringComparison.Ordinal));
    });
}

void TestReleaseAssetPattern()
{
    // Publishers put the version in the file name, which is the whole reason for the wildcard.
    Assert(ReleaseAssetPattern.Matches("RingOut-1.6.1-windows-x64.zip", "RingOut-*-windows-x64.zip"));
    Assert(ReleaseAssetPattern.Matches("RingOut-1.7-windows-x64.zip", "RingOut-*-windows-x64.zip"));

    // A neighbouring artifact of the same release must not win.
    Assert(!ReleaseAssetPattern.Matches("RingOut-1.6.1-linux-x86_64.zip", "RingOut-*-windows-x64.zip"));
    Assert(!ReleaseAssetPattern.Matches("RingOut-1.6.1-windows-x64-setup.exe", "RingOut-*-windows-x64.zip"));

    // Prefix and suffix must not overlap into a false match on a too-short name.
    Assert(!ReleaseAssetPattern.Matches("RingOut-windows-x64.zip", "RingOut-*-windows-x64.zip"));

    Assert(ReleaseAssetPattern.Matches("PartyBoard-win-x64.zip", "PartyBoard-win-x64.zip"));
    Assert(!ReleaseAssetPattern.Matches("", "RingOut-*.zip"));
}

void TestCatalogRuntimeMetadata()
{
    WithTempRoot(root =>
    {
        var shipped = Path.Combine(root, "shipped.json");
        File.WriteAllText(shipped, """
        [
          {
            "Id": "GRSEAF",
            "Title": "Soulcalibur II",
            "RuntimeName": "Ring Out",
            "SupportedDiscIds": [ "GRSEAF", "GRSPAF" ],
            "RuntimeSource": "GitHubReleaseAsset",
            "RuntimeAssets": { "win-x64": "RingOut-*-windows-x64.zip" },
            "RuntimeLaunchPaths": { "win-x64": "RingOut-{version}/RingOut.exe" },
            "DataPreparation": "Runtime"
          }
        ]
        """);

        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        var service = new GameCatalogService(shipped, data);

        var loaded = service.Load();
        Assert(loaded.Count == 1);
        var entry = loaded[0];
        Assert(entry.RuntimeSource == RuntimeSourceKind.GitHubReleaseAsset);
        Assert(entry.DataPreparation == GameDataPreparation.Runtime);
        Assert(entry.RuntimeName == "Ring Out");
        Assert(entry.SupportedDiscIds.Count == 2);
        Assert(entry.RuntimeAssets["win-x64"] == "RingOut-*-windows-x64.zip");
        Assert(entry.RuntimeLaunchPaths["win-x64"] == "RingOut-{version}/RingOut.exe");

        // User-owned state survives a catalog refresh; acquisition metadata is re-applied from
        // the shipped catalog, which is what lets an existing install pick these fields up.
        entry.DiscImage = "/games/sc2.rvz";
        entry.PlayCount = 7;
        entry.RuntimeSource = RuntimeSourceKind.Manifest;
        entry.SupportedDiscIds.Clear();
        service.Save(loaded);

        var reloaded = new GameCatalogService(shipped, data).Load()[0];
        Assert(reloaded.DiscImage == "/games/sc2.rvz");
        Assert(reloaded.PlayCount == 7);
        Assert(reloaded.RuntimeSource == RuntimeSourceKind.GitHubReleaseAsset);
        Assert(reloaded.SupportedDiscIds.Count == 2);
    });
}

// The catalog CubeShelf actually ships is data, and a typo in it is invisible until a user
// hits it. Check the invariants each entry has to satisfy for its runtime to install at all.
void TestShippedCatalogIsCoherent()
{
    var catalog = FindRepositoryFile(Path.Combine("src", "CubeShelf.Launcher", "games.json"));
    var data = Path.Combine(Path.GetTempPath(), "cubeshelf-catalog-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(data);
    try
    {
        var games = new GameCatalogService(catalog, data).Load();
        Assert(games.Count >= 2);

        foreach (var game in games)
        {
            Assert(!string.IsNullOrWhiteSpace(game.Id));
            Assert(!string.IsNullOrWhiteSpace(game.GitHubOwner));
            Assert(!string.IsNullOrWhiteSpace(game.GitHubRepo));
            Assert(game.SupportedDiscIds.Count > 0);

            if (game.RuntimeSource != RuntimeSourceKind.GitHubReleaseAsset) continue;

            // Every platform this entry declares an artifact for needs a launch path too,
            // or the install resolves an archive it cannot then start.
            Assert(game.RuntimeAssets.Count > 0);
            foreach (var runtimeId in game.RuntimeAssets.Keys)
                Assert(game.RuntimeLaunchPaths.ContainsKey(runtimeId));
        }

        var ringOut = games.Single(game => game.Id == "GRSEAF");
        Assert(ringOut.DataPreparation == GameDataPreparation.Runtime);
        Assert(ringOut.SupportedDiscIds.Contains("GRSEAF"));
        Assert(ringOut.RuntimeAssets.ContainsKey("win-x64"));
    }
    finally
    {
        if (Directory.Exists(data)) Directory.Delete(data, true);
    }
}

// Strikers publishes SHA256SUMS beside its assets. Preferring it over a catalog-pinned hash is
// what makes such an install verified without the pin going stale at the next release.
void TestReleaseAssetChecksum()
{
    WithTempRoot(root =>
    {
        var launch = OperatingSystem.IsWindows() ? "strikers.exe" : "strikers";
        var package = CreateZipBytes(launch, System.Text.Encoding.UTF8.GetBytes("native-port"));
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant();
        const string assetName = "strikers-package.zip";
        var sums = System.Text.Encoding.UTF8.GetBytes(
            $"0000000000000000000000000000000000000000000000000000000000000000  other.tar.gz\n{sha}  {assetName}\n");

        var release = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = "v2.3.4",
            assets = new object[]
            {
                new { name = assetName, size = package.Length, browser_download_url = "https://dl.example.test/pkg.zip" },
                new { name = "SHA256SUMS", size = sums.Length, browser_download_url = "https://dl.example.test/SHA256SUMS" }
            }
        }));

        var handler = new StaticHttpHandler(uri =>
            uri.AbsolutePath.EndsWith("releases/latest", StringComparison.Ordinal) ? release
            : uri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal) ? sums
            : package);
        using var client = new HttpClient(handler);
        var installer = new PartyBoardInstaller(new TestPaths(root), client);

        var source = new GameRuntimeSource(
            "owner", "repo", "latest", RuntimeSourceKind.GitHubReleaseAsset,
            new Dictionary<string, string> { [CurrentRid()] = "strikers-*.zip" },
            new Dictionary<string, string> { [CurrentRid()] = launch },
            PinnedSha256: "",
            ChecksumAsset: "SHA256SUMS");

        var result = installer.InstallLatestAsync(source, "G4QE01").GetAwaiter().GetResult();
        Assert(result.Verified);
        Assert(result.Version == "2.3.4");
        Assert(File.Exists(result.ExecutablePath));

        // The state on disk has to remember it was verified, since the game page reads it back.
        Assert(installer.GetStatus("G4QE01") is { IsInstalled: true, Verified: true });
    });
}

// Without a checksum file and without a pinned hash there is nothing to compare against. The
// install still runs -- it is simply recorded as unverified.
void TestReleaseAssetWithoutChecksum()
{
    WithTempRoot(root =>
    {
        var launch = OperatingSystem.IsWindows() ? "game.exe" : "game";
        var package = CreateZipBytes(launch, System.Text.Encoding.UTF8.GetBytes("unverified"));
        var release = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = "v1.0",
            assets = new object[]
            {
                new { name = "game-win.zip", size = package.Length, browser_download_url = "https://dl.example.test/g.zip" }
            }
        }));
        var handler = new StaticHttpHandler(uri =>
            uri.AbsolutePath.EndsWith("releases/latest", StringComparison.Ordinal) ? release : package);
        using var client = new HttpClient(handler);
        var installer = new PartyBoardInstaller(new TestPaths(root), client);

        var source = new GameRuntimeSource(
            "owner", "repo", "latest", RuntimeSourceKind.GitHubReleaseAsset,
            new Dictionary<string, string> { [CurrentRid()] = "game-*.zip" },
            new Dictionary<string, string> { [CurrentRid()] = launch },
            PinnedSha256: "",
            ChecksumAsset: "SHA256SUMS");

        var result = installer.InstallLatestAsync(source, "NOSUMS").GetAwaiter().GetResult();
        Assert(!result.Verified);
        Assert(File.Exists(result.ExecutablePath));
        Assert(installer.GetStatus("NOSUMS") is { IsInstalled: true, Verified: false });
    });
}

// GitHub's macos runners are Apple Silicon, so a fixture hardcoding x64 describes a platform
// the test process is not running on: the product resolves osx-arm64 and finds no artifact.
// Build the identifiers from the real architecture, as CurrentRuntimeId does in the product.
string CurrentArchitecture() =>
    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
        System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

string CurrentRid() =>
    (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsLinux() ? "linux-" : "osx-") + CurrentArchitecture();

string WriteDiscHeader(string root, string name, string discId, byte revision)
{
    var path = Path.Combine(root, name);
    var header = new byte[8];
    System.Text.Encoding.ASCII.GetBytes(discId).CopyTo(header, 0);
    header[7] = revision;
    File.WriteAllBytes(path, header);
    return path;
}

// Walks up from the test binary to the repository root, identified by the VERSION file, so the
// test does not depend on the working directory the runner happened to use.
string FindRepositoryFile(string relativePath)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "VERSION")))
            return Path.Combine(directory.FullName, relativePath);
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
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
