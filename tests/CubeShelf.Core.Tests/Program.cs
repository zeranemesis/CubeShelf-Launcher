using System.IO.Compression;
using System.Text.Json;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Security;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Social;

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
Run("in-game switch agrees with the launcher", TestPlayerDisabledMods);
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
Run("displaced updater images are reclaimed, nothing else", TestDisplacedUpdaterCleanup);
Run("a running image resists overwrite but not rename", TestRunningImageCanOnlyBeRenamed);
Run("release checksum file verifies the install", TestReleaseAssetChecksum);
Run("no checksum means unverified, not blocked", TestReleaseAssetWithoutChecksum);
Run("identity persists and both peers agree", TestIdentityPersistsAndAgrees);
Run("friend code survives paste, not corruption", TestFriendCodeRoundTrip);
Run("sealed presence reaches only friends", TestSealedPresenceReachesOnlyFriends);
Run("sealed presence rejects tampering", TestSealedPresenceRejectsTampering);
Run("sealed presence hides the friend count", TestSealedPresenceHidesFriendCount);
Run("friend store rejects self and replays", TestFriendStoreRejectsSelfAndReplays);
Run("presence goes stale rather than lying", TestPresenceFreshness);
Run("reading mod state leaves the game alone", TestReadingModStateLeavesTheGameAlone);
Run("mod state survives hostile input", TestModStateSurvivesHostileInput);
Run("the active list is written atomically", TestActiveListIsWrittenAtomically);
Run("the sequence never goes backwards", TestSequenceNeverGoesBackwards);
Run("the sequence adopts a document ahead of it", TestSequenceAdoptsADocumentAhead);
Run("the composer shares only what was agreed", TestComposerSharesOnlyWhatWasAgreed);
Run("the composer reports mods without touching them", TestComposerReportsModsWithoutTouchingThem);
Run("the composer is deterministic", TestComposerIsDeterministic);
Run("publishing into a synced folder", TestSyncedFolderPublisher);
Run("the publisher refuses what cannot work", TestSyncedFolderPublisherRefusesWhatCannotWork);
Run("documents are addressed to their author too", TestDocumentsAreAddressedToTheirAuthorToo);

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

// PartyBoard can switch a mod off from inside the game, and the launcher has to
// agree: the panel must not claim a mod is on while the game ignores it, and
// turning it back on in the launcher has to actually win.
void TestPlayerDisabledMods()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        using var client = new HttpClient(new StaticHttpHandler(_ =>
            CreateZipBytes("files/data/board.bin", new byte[] { 5 })));
        var banana = new GameBananaClient(client);
        foreach (var id in new[] { 71, 72 })
            manager.InstallAsync(new GameBananaMod(id, $"Mod {id}", 1, "https://files.gamebanana.com/m.zip", "m.zip"), banana)
                .GetAwaiter().GetResult();

        var details = Path.Combine(paths.DataDirectory, "Mods", "GAME", "active-mods.json");
        var disabled = Path.Combine(paths.DataDirectory, "Mods", "GAME", "player-disabled.json");

        // The game is handed identity, not just paths, so it can name a mod.
        Assert(File.Exists(details));
        Assert(JsonSerializer.Deserialize<List<PortableActiveMod>>(File.ReadAllText(details))!
            .Select(item => item.Id).OrderBy(id => id).SequenceEqual(new[] { 71, 72 }));

        // PartyBoard writes this file; the launcher only reads it.
        File.WriteAllText(disabled, "[72]");
        Assert(manager.PrepareActiveList() is not null);
        Assert(File.ReadAllLines(manager.ActiveListFile).Length == 1);
        Assert(manager.GetPlayerDisabled().SequenceEqual(new[] { 72 }));
        Assert(JsonSerializer.Deserialize<List<PortableActiveMod>>(File.ReadAllText(details))!.Single().Id == 71);

        // Turning it back on in the launcher clears the in-game switch, or the
        // panel would show it enabled while the game went on ignoring it.
        manager.SetEnabled(72, true);
        Assert(manager.GetPlayerDisabled().Count == 0);
        Assert(File.ReadAllLines(manager.ActiveListFile).Length == 2);
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
// The cleanup deletes files from the directory the application runs out of, so the thing
// worth proving is not that it deletes -- it is that it stops. A pattern one character too
// wide here removes part of the install.
void TestDisplacedUpdaterCleanup()
{
    if (!OperatingSystem.IsWindows()) return;

    var dir = AppContext.BaseDirectory;
    var doomed = new[] { "CubeShelf.Updater.exe.old", "CubeShelf.exe.a1b2c3d4.old" };
    var spared = new[] { "CubeShelf.Core.dll", "CubeShelf.Updater.exe", "notes.old", "CubeShelf.runtimeconfig.json" };
    var created = new List<string>();

    try
    {
        foreach (var name in doomed.Concat(spared))
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) continue;
            File.WriteAllText(path, "x");
            created.Add(path);
        }

        LauncherUpdateService.RemoveDisplacedUpdaterImages();

        foreach (var name in doomed) Assert(!File.Exists(Path.Combine(dir, name)));
        foreach (var name in spared) Assert(File.Exists(Path.Combine(dir, name)));
    }
    finally
    {
        foreach (var path in created)
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

// The whole self-update strategy rests on one Windows behaviour: a running executable
// cannot be overwritten, but it can be renamed. If that ever stopped being true the
// updater would be silently wrong again, so it is asserted rather than assumed -- against
// this test's own image, which is the only running one it has.
void TestRunningImageCanOnlyBeRenamed()
{
    if (!OperatingSystem.IsWindows()) return;

    var self = Environment.ProcessPath;
    if (string.IsNullOrEmpty(self) || !File.Exists(self)) return;

    var source = Path.Combine(Path.GetTempPath(), "cubeshelf-image-" + Guid.NewGuid().ToString("N"));
    File.WriteAllText(source, "replacement");
    var displaced = self + ".renametest";

    try
    {
        AssertThrows<IOException>(() => File.Copy(source, self, true));

        File.Move(self, displaced);
        Assert(!File.Exists(self));
        Assert(File.Exists(displaced));
    }
    finally
    {
        // Put the image back under its own name whatever happened above; a test must not
        // leave the binary it runs from renamed.
        try { if (File.Exists(displaced) && !File.Exists(self)) File.Move(displaced, self); } catch { }
        try { if (File.Exists(source)) File.Delete(source); } catch { }
    }
}

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

        // PartyBoard reads the disc image itself, so CubeShelf must not extract it. Flipping
        // this back to CubeShelf costs the user a DolphinTool download, an RVZ to ISO
        // conversion and roughly 900 MB of duplicated disc, and the only visible symptom is
        // a long wait before a Play button that would have worked immediately. Nothing else
        // would report it, so this does.
        var partyBoard = games.Single(game => game.Id == "GMPE01_00");
        Assert(partyBoard.DataPreparation == GameDataPreparation.Runtime);
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

// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
// Publishing into a folder something else synchronises: no account, no secret at rest.
// ---------------------------------------------------------------------------

void TestSyncedFolderPublisher()
{
    WithTempRoot(root =>
    {
        var folder = Path.Combine(root, "synced");
        Directory.CreateDirectory(folder);
        const string url = "https://cloud.example.test/s/token/download";

        var publisher = new SyncedFolderPresencePublisher(
            new SyncedFolderTarget(folder, SyncedFolderTarget.DefaultFileName, url));
        Assert(publisher.IsConfigured && publisher.PresenceUrl == url);

        var result = publisher.PublishAsync("{\"v\":1}").GetAwaiter().GetResult();
        Assert(result.Succeeded && result.PresenceUrl == url && result.Error is null);

        var document = Path.Combine(folder, SyncedFolderTarget.DefaultFileName);
        Assert(File.ReadAllText(document) == "{\"v\":1}");

        // Nothing half-written left for the sync client to upload.
        Assert(Directory.GetFiles(folder, "*.tmp").Length == 0);
        Assert(Directory.GetFiles(folder).Length == 1);

        // Republishing replaces in place rather than accumulating.
        Assert(publisher.PublishAsync("{\"v\":2}").GetAwaiter().GetResult().Succeeded);
        Assert(File.ReadAllText(document) == "{\"v\":2}");
        Assert(Directory.GetFiles(folder).Length == 1);
    });
}

void TestSyncedFolderPublisherRefusesWhatCannotWork()
{
    WithTempRoot(root =>
    {
        const string url = "https://cloud.example.test/s/token/download";

        // A missing folder is far more likely to be a typo than an intention, and creating it
        // would publish into a path nothing synchronises: looks successful, reaches nobody.
        var missing = new SyncedFolderPresencePublisher(
            new SyncedFolderTarget(Path.Combine(root, "not-there"), "p.json", url));
        var result = missing.PublishAsync("{}").GetAwaiter().GetResult();
        Assert(!result.Succeeded && result.Error is { Length: > 0 });
        Assert(!Directory.Exists(Path.Combine(root, "not-there")));

        // The read address goes straight into a friend code, which accepts https only. Finding
        // out here beats finding out when a friend cannot read you.
        foreach (var bad in new[] { "", "   ", "http://cloud.example.test/p.json", "cloud.example.test/p.json" })
        {
            var publisher = new SyncedFolderPresencePublisher(new SyncedFolderTarget(root, "p.json", bad));
            Assert(!publisher.IsConfigured);
            Assert(publisher.PresenceUrl.Length == 0);
            Assert(!publisher.PublishAsync("{}").GetAwaiter().GetResult().Succeeded);
        }

        // A configured publisher is one a friend code can be built from.
        var good = new SyncedFolderPresencePublisher(new SyncedFolderTarget(root, "p.json", url));
        Assert(good.IsConfigured);
        using var identity = PeerIdentity.Create();
        Assert(FriendCode.TryDecode(
            FriendCode.Encode(identity.PublicKey, good.PresenceUrl), out var decoded, out _));
        Assert(decoded!.PresenceUrl == url);
    });
}

void TestDocumentsAreAddressedToTheirAuthorToo()
{
    WithTempRoot(root =>
    {
        using var me = PeerIdentity.Create();
        using var friend = PeerIdentity.Create();
        var friends = new FriendStore(root);
        Assert(friends.TryAdd(new FriendCodePayload(friend.PublicKey, "https://example.test/p.json"),
            "Ami", me.PublicKey, out _));

        var recipients = PresenceRecipients.ForPublication(me, friends);
        Assert(recipients.Count == 2);

        var snapshot = PresenceComposer.Offline("Zera", 1, DateTimeOffset.UtcNow);
        var envelope = SealedPresence.Seal(me, snapshot, recipients);

        // The friend still reads it, and so do we -- which is what lets a self-test prove the
        // published URL really serves a document this identity can open.
        Assert(SealedPresence.TryOpen(friend, me.PublicKey, envelope, out _));
        Assert(SealedPresence.TryOpen(me, me.PublicKey, envelope, out var mine));
        Assert(mine!.Sequence == 1);

        // A paused friend drops out; we never do.
        Assert(friends.Update(Convert.ToBase64String(friend.PublicKey), entry => entry.Paused = true));
        var paused = PresenceRecipients.ForPublication(me, friends);
        Assert(paused.Count == 1);
        var afterPause = SealedPresence.Seal(me, snapshot, paused);
        Assert(!SealedPresence.TryOpen(friend, me.PublicKey, afterPause, out _));
        Assert(SealedPresence.TryOpen(me, me.PublicKey, afterPause, out _));
    });
}

// ---------------------------------------------------------------------------
// Composing the document to publish: what is shared, and nothing written while doing it.
// ---------------------------------------------------------------------------

void TestComposerSharesOnlyWhatWasAgreed()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var composer = new PresenceComposer(paths);
        var now = DateTimeOffset.UtcNow;
        var games = SampleLibrary();

        var everything = composer.Compose("Zera", games, new[] { "GRSEAF" },
            new PresenceSharingOptions(), 1, now);
        Assert(everything.Status == PresenceStatus.InGame);
        Assert(everything.CurrentGameId == "GRSEAF" && everything.CurrentGameTitle == "Soulcalibur II");
        Assert(everything.Library.Count == 2);
        Assert(everything.Library[0].Id == "G4QE01");         // ordered, not catalog order
        Assert(everything.Library[1].PlayCount == 12);
        Assert(everything.DisplayName == "Zera");

        // Each flag removes exactly its own data and nothing else.
        var noLibrary = composer.Compose("Zera", games, new[] { "GRSEAF" },
            new PresenceSharingOptions(ShareLibrary: false), 1, now);
        Assert(noLibrary.Library.Count == 0);
        Assert(noLibrary.CurrentGameId == "GRSEAF");          // still in a game

        var noPlayTime = composer.Compose("Zera", games, Array.Empty<string>(),
            new PresenceSharingOptions(SharePlayTime: false), 1, now);
        Assert(noPlayTime.Library.Count == 2);
        Assert(noPlayTime.Library.All(game => game.PlayCount == 0 && game.TotalPlaySeconds == 0));
        Assert(noPlayTime.Library.All(game => game.LastPlayedAt is null));
        Assert(noPlayTime.Library.Any(game => game.IsFavorite));   // a favourite is not play time

        var noCurrentGame = composer.Compose("Zera", games, new[] { "GRSEAF" },
            new PresenceSharingOptions(ShareCurrentGame: false), 1, now);
        Assert(noCurrentGame.CurrentGameId is null && noCurrentGame.CurrentGameTitle is null);
        Assert(noCurrentGame.Status == PresenceStatus.Online);     // present, just not saying what
        Assert(noCurrentGame.Library.Count == 2);

        // Not playing anything reads as online, never as offline: only staleness means offline,
        // because a peer that stopped publishing cannot publish that it stopped.
        var idle = composer.Compose("Zera", games, Array.Empty<string>(), new PresenceSharingOptions(), 1, now);
        Assert(idle.Status == PresenceStatus.Online && idle.CurrentGameId is null);

        var farewell = PresenceComposer.Offline("Zera", 9, now);
        Assert(farewell.Status == PresenceStatus.Offline);
        Assert(farewell.Library.Count == 0 && farewell.Mods.Count == 0 && farewell.Sequence == 9);
    });
}

void TestComposerReportsModsWithoutTouchingThem()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var modDirectory = Path.Combine(paths.DataDirectory, "Mods", "GMPE01_00");
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(modDirectory, "installed.json"), System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new PortableInstalledMod(7, "Board pack", 0, true, 1, modDirectory, ""),
            new PortableInstalledMod(8, "Muted in game", 0, true, 1, modDirectory, "")
        }));
        File.WriteAllText(Path.Combine(modDirectory, "player-disabled.json"), "[8]");

        var activeList = Path.Combine(modDirectory, "active-mods.txt");
        File.WriteAllText(activeList, "what the running game is reading\n");
        var before = File.ReadAllBytes(activeList);
        var writtenAt = File.GetLastWriteTimeUtc(activeList);

        // Mods are reported for games in the library, so the game that owns them has to be in it.
        var library = SampleLibrary()
            .Append(new PresenceGame("GMPE01_00", "Mario Party 4", 40, 7200, false, null))
            .ToArray();

        var composer = new PresenceComposer(paths);
        var snapshot = composer.Compose("Zera", library, Array.Empty<string>(),
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow);

        Assert(snapshot.Mods.Count == 2);
        Assert(snapshot.Mods[0] is { GameId: "GMPE01_00", ModId: "7", Enabled: true });
        Assert(snapshot.Mods[1] is { ModId: "8", Enabled: false });   // the player switched it off

        // Composing must leave the mod state exactly as it found it -- one of these games could
        // be running while the background loop publishes.
        Assert(File.ReadAllBytes(activeList).SequenceEqual(before));
        Assert(File.GetLastWriteTimeUtc(activeList) == writtenAt);

        var withoutMods = composer.Compose("Zera", library, Array.Empty<string>(),
            new PresenceSharingOptions(ShareMods: false), 1, DateTimeOffset.UtcNow);
        Assert(withoutMods.Mods.Count == 0);

        // A game that is not in the library contributes no mods, even with state on disk: the
        // catalog decides what a friend can see.
        Assert(composer.Compose("Zera", SampleLibrary(), Array.Empty<string>(),
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow).Mods.Count == 0);
    });
}

void TestComposerIsDeterministic()
{
    WithTempRoot(root =>
    {
        var composer = new PresenceComposer(new TestPaths(root));
        var games = SampleLibrary();

        // Two publishes of an unchanged shelf differ only by when and which number, so the
        // fingerprint lets the loop skip a needless write.
        var first = composer.Compose("Zera", games, Array.Empty<string>(),
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow);
        var second = composer.Compose("Zera", games, Array.Empty<string>(),
            new PresenceSharingOptions(), 99, DateTimeOffset.UtcNow.AddHours(3));
        Assert(PresenceComposer.ContentFingerprint(first) == PresenceComposer.ContentFingerprint(second));

        // Anything a friend would actually see does change it.
        var playing = composer.Compose("Zera", games, new[] { "GRSEAF" },
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow);
        Assert(PresenceComposer.ContentFingerprint(playing) != PresenceComposer.ContentFingerprint(first));

        var renamed = composer.Compose("Someone else", games, Array.Empty<string>(),
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow);
        Assert(PresenceComposer.ContentFingerprint(renamed) != PresenceComposer.ContentFingerprint(first));

        // Catalog order must not leak into the document, or the fingerprint would change for no
        // reason a friend could see.
        var reordered = composer.Compose("Zera", games.Reverse().ToArray(), Array.Empty<string>(),
            new PresenceSharingOptions(), 1, DateTimeOffset.UtcNow);
        Assert(PresenceComposer.ContentFingerprint(reordered) == PresenceComposer.ContentFingerprint(first));
    });
}

IReadOnlyList<PresenceGame> SampleLibrary() => new[]
{
    new PresenceGame("GRSEAF", "Soulcalibur II", 12, 3600, true, DateTimeOffset.UnixEpoch.AddDays(1)),
    new PresenceGame("G4QE01", "Super Mario Strikers", 3, 900, false, null)
};

// The published sequence. A friend that has seen a higher one rejects everything below it
// for good, so going backwards is the one failure this must never have.
// ---------------------------------------------------------------------------

void TestSequenceNeverGoesBackwards()
{
    WithTempRoot(root =>
    {
        var start = DateTimeOffset.UtcNow;

        // Publishes are spaced by at least MinimumPublishGap in the real loop, so model that
        // rather than forty calls inside one second.
        var clock = start;
        var seen = new List<long>();
        var sequence = new PresenceSequence(root);
        for (var index = 0; index < 40; index++)
        {
            seen.Add(sequence.Next(clock));
            clock += PresencePolicy.MinimumPublishGap;
        }
        var last = seen[^1];

        // Strictly increasing, and no number handed out twice.
        Assert(seen.Zip(seen.Skip(1)).All(pair => pair.Second > pair.First));
        Assert(seen.Distinct().Count() == seen.Count);

        // A restart must not reuse anything: the reservation is persisted before the numbers go
        // out, so the next run resumes above the batch, not above the last number issued.
        var reopened = new PresenceSequence(root);
        Assert(reopened.Next(clock) > last);
        Assert(reopened.InstanceId == sequence.InstanceId);

        // Losing the state file is survivable because the clock is a floor -- the profile
        // restored from an old backup lands above everything it published, since time moved on
        // meanwhile. That is the guarantee, and it holds exactly while the counter is not ahead
        // of wall clock, which MinimumPublishGap is what ensures.
        File.Delete(Path.Combine(root, "presence-state.json"));
        var afterLoss = new PresenceSequence(root);
        Assert(afterLoss.Next(clock) > last);

        // A burst inside one second still hands out distinct, increasing numbers; it is only the
        // rescue-from-a-lost-file property that needs the clock to have moved.
        var burst = new PresenceSequence(root);
        var sameInstant = Enumerable.Range(0, 5).Select(_ => burst.Next(clock)).ToArray();
        Assert(sameInstant.Zip(sameInstant.Skip(1)).All(pair => pair.Second > pair.First));

        // And a clock dragged backwards does not pull the counter with it.
        var backwards = new PresenceSequence(root);
        var beforeJump = backwards.Next(start);
        Assert(backwards.Next(start - TimeSpan.FromDays(365)) > beforeJump);
    });
}

void TestSequenceAdoptsADocumentAhead()
{
    WithTempRoot(root =>
    {
        var now = DateTimeOffset.UtcNow;
        var sequence = new PresenceSequence(root);
        var mine = sequence.Next(now);

        // Our own last document, or an older one: nothing to do.
        Assert(sequence.Reconcile(mine) == PresenceSequenceReconciliation.Consistent);
        Assert(sequence.Reconcile(mine - 100) == PresenceSequenceReconciliation.Consistent);
        Assert(sequence.Current == mine);

        // A document at our own address carrying a number we never issued means a second
        // installation is publishing under this identity -- a copied profile.
        var foreign = mine + 5_000;
        Assert(sequence.Reconcile(foreign) == PresenceSequenceReconciliation.RemoteAhead);

        // Having seen it, we must climb above it. Publishing below would produce documents every
        // friend rejects, which is the unrecoverable failure.
        Assert(sequence.Next(now) > foreign);

        // The adoption is persisted, so a restart does not fall back under it.
        Assert(new PresenceSequence(root).Next(now) > foreign);
    });
}

// Reading mod state must never disturb it: PortableModManager's constructor rewrites
// active-mods.txt, which is the file a running game reads.
// ---------------------------------------------------------------------------

void TestReadingModStateLeavesTheGameAlone()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var modDirectory = Path.Combine(paths.DataDirectory, "Mods", "GRSEAF");
        Directory.CreateDirectory(modDirectory);

        var content = Path.Combine(modDirectory, "packs", "42");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(modDirectory, "installed.json"), System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new PortableInstalledMod(42, "Board pack", 0, true, 10, content, ""),
            new PortableInstalledMod(43, "Muted by the player", 0, true, 5, content, ""),
            new PortableInstalledMod(44, "Switched off in the launcher", 0, false, 1, content, "")
        }));
        File.WriteAllText(Path.Combine(modDirectory, "player-disabled.json"), "[43]");

        // Stand in for what a running game is reading, and prove we do not touch it.
        var activeList = Path.Combine(modDirectory, "active-mods.txt");
        File.WriteAllText(activeList, "whatever the game is currently using\n");
        var before = File.ReadAllBytes(activeList);
        var writtenAt = File.GetLastWriteTimeUtc(activeList);

        var effective = PortableModState.ReadEffective(paths, "GRSEAF");

        Assert(effective.Count == 3);
        Assert(effective[0].Id == 42 && effective[0].Enabled);
        Assert(effective[1].Id == 43 && !effective[1].Enabled);   // the player switched it off in game
        Assert(effective[2].Id == 44 && !effective[2].Enabled);   // switched off in the launcher

        Assert(File.ReadAllBytes(activeList).SequenceEqual(before));
        Assert(File.GetLastWriteTimeUtc(activeList) == writtenAt);

        // Reading a game that has never had a mod must not bring its directory into existence.
        var untouched = Path.Combine(paths.DataDirectory, "Mods", "GMPE01_00");
        Assert(PortableModState.ReadEffective(paths, "GMPE01_00").Count == 0);
        Assert(!Directory.Exists(untouched));
    });
}

void TestModStateSurvivesHostileInput()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);

        // A catalog walk must not die on one entry, and must not be steered out of Mods/.
        foreach (var hostile in new[] { "..", "../escape", "a/b", "a\\b", "with space", "", "   " })
        {
            Assert(!PortableModState.IsValidGameId(hostile));
            Assert(PortableModState.ReadEffective(paths, hostile).Count == 0);
            Assert(PortableModState.ReadInstalled(paths, hostile).Count == 0);
            Assert(PortableModState.ReadPlayerDisabled(paths, hostile).Count == 0);
        }
        Assert(PortableModState.IsValidGameId("GMPE01_00") && PortableModState.IsValidGameId("CUSTOM-1a2b"));

        var modDirectory = Path.Combine(paths.DataDirectory, "Mods", "BROKEN");
        Directory.CreateDirectory(modDirectory);
        var installed = Path.Combine(modDirectory, "installed.json");

        File.WriteAllText(installed, "[{\"Id\": 1, \"Name\": \"trunc");
        Assert(PortableModState.ReadEffective(paths, "BROKEN").Count == 0);

        File.WriteAllBytes(installed, new byte[] { 0xFF, 0x00, 0x13, 0x37 });
        Assert(PortableModState.ReadEffective(paths, "BROKEN").Count == 0);

        // The launcher writes these from the UI thread while this reader runs in the background,
        // so a sharing violation has to read as "no mods", not as an exception escaping a loop.
        File.WriteAllText(installed, "[]");
        using (File.Open(installed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert(PortableModState.ReadEffective(paths, "BROKEN").Count == 0);
    });
}

void TestActiveListIsWrittenAtomically()
{
    WithTempRoot(root =>
    {
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GRSEAF");
        var modDirectory = Path.Combine(paths.DataDirectory, "Mods", "GRSEAF");

        var content = Path.Combine(modDirectory, "packs", "7");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(modDirectory, "installed.json"), System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new PortableInstalledMod(7, "Pack", 0, true, 1, content, "")
        }));
        manager.SetEnabled(7, true);

        var activeList = Path.Combine(modDirectory, "active-mods.txt");
        Assert(File.Exists(activeList));

        // The format the game parses has not changed: one path per line, last line terminated.
        var text = File.ReadAllText(activeList);
        Assert(text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert(File.ReadAllLines(activeList).Length == 1);

        // No temporary file is left behind for a sync client or the game to trip over.
        Assert(Directory.GetFiles(modDirectory, "*.tmp").Length == 0);
    });
}

// ---------------------------------------------------------------------------
// Decentralised friends: identity, friend codes, and the sealed presence document.
// ---------------------------------------------------------------------------

void TestIdentityPersistsAndAgrees()
{
    WithTempRoot(root =>
    {
        var path = Path.Combine(root, "identity.key");
        byte[] publicKey;
        using (var created = PeerIdentity.LoadOrCreate(path))
        {
            publicKey = created.PublicKey;
            Assert(publicKey.Length == PeerIdentity.PublicKeyLength && publicKey[0] == 0x04);
        }

        // The identity has to survive a restart: it is what friends added.
        using var reloaded = PeerIdentity.LoadOrCreate(path);
        Assert(reloaded.PublicKey.SequenceEqual(publicKey));

        // Both peers reach the same pairwise key from opposite directions, which is the whole
        // basis for the scheme -- no exchange, no server.
        using var alice = PeerIdentity.Create();
        using var bob = PeerIdentity.Create();
        using var mallory = PeerIdentity.Create();

        var aliceView = alice.DeriveSharedKey(bob.PublicKey);
        var bobView = bob.DeriveSharedKey(alice.PublicKey);
        Assert(aliceView.SequenceEqual(bobView));
        Assert(!alice.DeriveSharedKey(mallory.PublicKey).SequenceEqual(aliceView));
    });
}

void TestFriendCodeRoundTrip()
{
    using var identity = PeerIdentity.Create();
    const string url = "https://gist.githubusercontent.com/someone/abc123/raw/presence.json";
    var code = FriendCode.Encode(identity.PublicKey, url);

    Assert(FriendCode.TryDecode(code, out var payload, out _));
    Assert(payload!.PublicKey.SequenceEqual(identity.PublicKey));
    Assert(payload.PresenceUrl == url);

    // Codes travel through chat windows, which add line breaks.
    Assert(FriendCode.TryDecode("  " + code[..20] + "\n" + code[20..] + " ", out _, out _));

    // A single altered character must not silently yield a different key: that is what the
    // checksum is for.
    //
    // Corrupt a character in the middle, never the last one. The blob is not a multiple of three
    // bytes, so the final base64 character carries only two significant bits and the other four
    // are padding -- editing it can decode to the very same bytes, which is a property of base64
    // rather than a hole in the checksum. An earlier version of this test flipped the last
    // character and failed one run in four.
    var index = code.Length / 2;
    var replacement = code[index] == 'A' ? 'B' : 'A';
    var corrupted = code[..index] + replacement + code[(index + 1)..];
    Assert(!FriendCode.TryDecode(corrupted, out _, out var corruptError));
    Assert(corruptError.Length > 0);

    Assert(!FriendCode.TryDecode(code[..^6], out _, out _));
    Assert(!FriendCode.TryDecode("hello", out _, out _));

    // The URL is pasted from elsewhere, so a plaintext scheme is someone else choosing a
    // downgrade on the user behalf. Refuse it at the door.
    AssertThrows<ArgumentException>(() =>
        FriendCode.Encode(identity.PublicKey, "http://example.test/presence.json"));
}

void TestSealedPresenceReachesOnlyFriends()
{
    using var author = PeerIdentity.Create();
    using var friend = PeerIdentity.Create();
    using var other = PeerIdentity.Create();
    using var stranger = PeerIdentity.Create();

    var snapshot = SampleSnapshot(7);
    var envelope = SealedPresence.Seal(author, snapshot,
        new[] { friend.PublicKey, other.PublicKey });

    Assert(SealedPresence.TryOpen(friend, author.PublicKey, envelope, out var opened));
    Assert(opened!.DisplayName == snapshot.DisplayName);
    Assert(opened.Sequence == 7);
    Assert(opened.CurrentGameId == "GRSEAF");
    Assert(opened.Library.Count == 1 && opened.Library[0].Title == "Soulcalibur II");
    Assert(opened.Mods.Count == 1 && opened.Mods[0].Enabled);

    // Addressed to someone else: no box opens.
    Assert(!SealedPresence.TryOpen(stranger, author.PublicKey, envelope, out _));

    // Right reader, wrong claimed author: the pairwise key differs, so nothing opens. This is
    // what stands in for a signature.
    Assert(!SealedPresence.TryOpen(friend, stranger.PublicKey, envelope, out _));

    // The document survives the trip through its published form.
    var reparsed = SealedPresence.FromJson(SealedPresence.ToJson(envelope));
    Assert(SealedPresence.TryOpen(other, author.PublicKey, reparsed, out var alsoOpened));
    Assert(alsoOpened!.Sequence == 7);
}

void TestSealedPresenceRejectsTampering()
{
    using var author = PeerIdentity.Create();
    using var friend = PeerIdentity.Create();
    var envelope = SealedPresence.Seal(author, SampleSnapshot(1), new[] { friend.PublicKey });

    var payload = Convert.FromBase64String(envelope.Payload);
    payload[0] ^= 0xFF;
    var tampered = SealedPresence.FromJson(SealedPresence.ToJson(envelope))!;
    tampered.Payload = Convert.ToBase64String(payload);
    Assert(!SealedPresence.TryOpen(friend, author.PublicKey, tampered, out _));

    var reTagged = SealedPresence.FromJson(SealedPresence.ToJson(envelope))!;
    reTagged.Tag = Convert.ToBase64String(new byte[16]);
    Assert(!SealedPresence.TryOpen(friend, author.PublicKey, reTagged, out _));

    Assert(!SealedPresence.TryOpen(friend, author.PublicKey, null, out _));
}

void TestSealedPresenceHidesFriendCount()
{
    using var author = PeerIdentity.Create();

    // One friend and seven friends have to look the same from outside, or the file leaks the
    // size of the circle on every publish.
    var one = SealedPresence.Seal(author, SampleSnapshot(1),
        new[] { PeerIdentity.Create().PublicKey });
    var several = SealedPresence.Seal(author, SampleSnapshot(1),
        Enumerable.Range(0, 7).Select(_ => PeerIdentity.Create().PublicKey).ToArray());

    Assert(one.Boxes.Count == 8 && several.Boxes.Count == 8);
    Assert(SealedPresence.Seal(author, SampleSnapshot(1),
        Enumerable.Range(0, 9).Select(_ => PeerIdentity.Create().PublicKey).ToArray()).Boxes.Count == 16);

    // Nobody is addressed at all, and the document still looks ordinary.
    Assert(SealedPresence.Seal(author, SampleSnapshot(1), Array.Empty<byte[]>()).Boxes.Count == 8);
}

void TestFriendStoreRejectsSelfAndReplays()
{
    WithTempRoot(root =>
    {
        using var me = PeerIdentity.Create();
        using var friend = PeerIdentity.Create();
        var store = new FriendStore(root);
        const string url = "https://example.test/presence.json";

        Assert(!store.TryAdd(new FriendCodePayload(me.PublicKey, url), "moi", me.PublicKey, out var selfError));
        Assert(selfError.Length > 0);

        var payload = new FriendCodePayload(friend.PublicKey, url);
        Assert(store.TryAdd(payload, "Zera", me.PublicKey, out _));
        Assert(!store.TryAdd(payload, "Zera encore", me.PublicKey, out _));
        Assert(store.Load().Count == 1);

        var key = Convert.ToBase64String(friend.PublicKey);
        var now = DateTimeOffset.UtcNow;
        Assert(store.TryAcceptSequence(key, 5, now));

        // A replayed document carries a sequence already seen, and must not move the state back.
        Assert(!store.TryAcceptSequence(key, 5, now));
        Assert(!store.TryAcceptSequence(key, 4, now));
        Assert(store.TryAcceptSequence(key, 6, now));
        Assert(store.Load()[0].LastSequence == 6);

        // Pausing takes a friend out of the next document without forgetting them.
        Assert(store.ActiveRecipients().Count == 1);
        Assert(store.Update(key, entry => entry.Paused = true));
        Assert(store.ActiveRecipients().Count == 0);
        Assert(store.Load().Count == 1);

        Assert(store.Remove(key));
        Assert(store.Load().Count == 0);
    });
}

void TestPresenceFreshness()
{
    var now = DateTimeOffset.UtcNow;
    var window = TimeSpan.FromMinutes(15);

    var fresh = SampleSnapshot(1) with { PublishedAt = now };
    Assert(fresh.IsFresh(window, now));
    Assert(fresh.EffectiveStatus(window, now) == PresenceStatus.InGame);

    // Publishing is periodic, so a peer that stopped publishing reads as offline rather than
    // staying frozen in whatever game it was last seen playing.
    var stale = SampleSnapshot(1) with { PublishedAt = now - TimeSpan.FromHours(2) };
    Assert(!stale.IsFresh(window, now));
    Assert(stale.EffectiveStatus(window, now) == PresenceStatus.Offline);

    // A clock far in the future is not evidence of presence either.
    var future = SampleSnapshot(1) with { PublishedAt = now + TimeSpan.FromDays(1) };
    Assert(future.EffectiveStatus(window, now) == PresenceStatus.Offline);
}

PresenceSnapshot SampleSnapshot(long sequence) => new(
    PresenceSnapshot.CurrentVersion,
    "Zera",
    DateTimeOffset.UtcNow,
    sequence,
    PresenceStatus.InGame,
    "GRSEAF",
    "Soulcalibur II",
    new[] { new SharedGame("GRSEAF", "Soulcalibur II", 12, 3600, true, DateTimeOffset.UtcNow) },
    new[] { new SharedMod("GMPE01_00", "4242", "Board pack", true) });

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
