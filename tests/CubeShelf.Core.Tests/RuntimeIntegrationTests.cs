using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Games;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;

internal static class RuntimeIntegrationTests
{
    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Runtime integration assertion failed.");
    }

    private static string CatalogPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VERSION"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "CubeShelf.Desktop", "games.json");
    }

    private static void WithRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }

    public static void LaunchContracts() => WithRoot(root =>
    {
        var games = new GameCatalogService(CatalogPath(), root).Load();
        var disc = Path.Combine(root, "jeux & images", "{mods} été.iso");
        foreach (var game in games)
        {
            game.DiscImage = disc;
            var launch = GameLaunchConfiguration.Create(game, "mods with spaces.txt");
            switch (game.RuntimeName)
            {
                case "PartyBoard":
                    Check(launch.Arguments.Count == 0 && launch.Environment["PARTYBOARD_DISC_IMAGE"] == disc);
                    Check(launch.Environment["PARTYBOARD_MOD_LIST"] == "mods with spaces.txt" && launch.Environment["STRIKERS_DATA"] is null);
                    break;
                case "Ring Out":
                    Check(launch.Arguments.SequenceEqual(new[] { disc }));
                    Check(launch.Environment.Values.All(value => value is null));
                    break;
                case "Strikers":
                    Check(launch.Arguments.Count == 0 && launch.Environment["STRIKERS_DATA"] == disc);
                    Check(launch.Environment["PARTYBOARD_DISC_IMAGE"] is null && launch.Environment["PARTYBOARD_MOD_LIST"] is null);
                    break;
                case "Sunshine PAL":
                    // CubeShelf extracts the disc beside sms.exe; the game reads that
                    // folder, relative to its working directory, and the mod list.
                    Check(launch.Arguments.Count == 0 && launch.Environment["SMS_DISC_ROOT"] == "GMSP01/files");
                    Check(launch.Environment["SMS_CUBESHELF_MOD_LIST"] == "mods with spaces.txt" && launch.Environment["SMS_CUBESHELF"] == "1");
                    Check(launch.Environment["PARTYBOARD_DISC_IMAGE"] is null && launch.Environment["STRIKERS_DATA"] is null);
                    break;
                default: throw new InvalidOperationException("Add a launch contract test for the new runtime.");
            }
        }
    });

    public static void CatalogMigration() => WithRoot(root =>
    {
        var service = new GameCatalogService(CatalogPath(), root);
        var games = service.Load();
        var game = games.Single(item => item.RuntimeName == "Strikers");
        game.DiscImage = "my-disc.iso";
        game.Executable = "my-runtime";
        game.IsFavorite = true;
        game.TotalPlaySeconds = 1234;
        service.Save(games);
        // Simulate the user catalog written before the new metadata existed.
        var path = service.UserCatalogPath;
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        foreach (var node in json)
        {
            node!.AsObject().Remove("LaunchArguments");
            node.AsObject().Remove("LaunchEnvironment");
            node.AsObject().Remove("DiscFormats");
        }
        File.WriteAllText(path, json.ToJsonString());
        game = service.Load().Single(item => item.RuntimeName == "Strikers");
        Check(game.LaunchEnvironment["STRIKERS_DATA"] == "{disc}" && !game.DiscFormats.Contains(".rvz"));
        Check(game.DiscImage == "my-disc.iso" && game.Executable == "my-runtime" && game.IsFavorite && game.TotalPlaySeconds == 1234);
    });

    public static void DiscValidation() => WithRoot(root =>
    {
        var games = new GameCatalogService(CatalogPath(), root).Load();
        var strikers = games.Single(item => item.RuntimeName == "Strikers");
        var path = Path.Combine(root, "game.iso");
        var header = new byte[0x440];
        Encoding.ASCII.GetBytes("G4QE01").CopyTo(header, 0);
        File.WriteAllBytes(path, header[..8]);
        Check(!DiscImageService.Inspect(path, strikers).Supported);
        File.WriteAllBytes(path, header);
        Check(!DiscImageService.Inspect(path, strikers).Recognized);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x1c), 0xc2339f3d);
        File.WriteAllBytes(path, header);
        Check(DiscImageService.Inspect(path, strikers) is { Recognized: true, Supported: true });
        Encoding.ASCII.GetBytes("GMPE01").CopyTo(header, 0);
        File.WriteAllBytes(path, header);
        Check(DiscImageService.Inspect(path, strikers) is { Recognized: true, Supported: false });
        foreach (var format in new[] { ".rvz", ".ciso", ".gcz" })
        {
            path = Path.Combine(root, "game" + format);
            File.WriteAllBytes(path, new byte[64]);
            var result = DiscImageService.Inspect(path, strikers);
            Check(!result.Recognized && result.Supported == (format != ".rvz"));
            if (result.Supported) Check(result.Message.Contains("non vérifiés"));
        }
    });

    public static void ModResumeHistory() => WithRoot(root =>
    {
        var store = new DownloadActivityStore(new TestPaths(root));
        var pending = store.Create("MOD", "Board", "GMPE01_00:42");
        store.Update(pending.Id, DownloadActivityState.Running, .5, "Downloading");
        var completed = store.Create("MOD", "Finished", "GMPE01_00:43");
        store.Update(completed.Id, DownloadActivityState.Completed, 1, "Done");
        store.Create("MOD", "Malformed", "../escape:-1");
        using var queue = new PortableDownloadQueue(store);
        var resumed = queue.Items.Single(item => item.Title == "Board");
        Check(resumed.State == DownloadActivityState.Interrupted && resumed.CanResume);
        Check(!queue.Items.Single(item => item.Title == "Finished").CanResume);
        Check(!queue.Items.Single(item => item.Title == "Malformed").CanResume);
        Check(ModDownloadReference.TryParse(resumed.ReferenceId, out var reference) && reference == new ModDownloadReference("GMPE01_00", 42));
        foreach (var invalid in new[] { "GAME:0", "GAME:42:more", "GAME:2147483648", "GAME:+42", ":42" })
            Check(!ModDownloadReference.TryParse(invalid, out _));
    });

    public static void ModHttpResume() => WithRoot(root =>
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("files/board.txt").Open())) writer.Write("board data");
        var bytes = buffer.ToArray();
        var mod = new GameBananaMod(42, "Board", 123, "https://gamebanana.com/dl/42", "board.zip");
        var paths = new TestPaths(root);
        var manager = new PortableModManager(paths, "GAME");
        var downloads = Path.Combine(paths.DataDirectory, "Mods", "GAME", ".downloads");
        Directory.CreateDirectory(downloads);
        var partial = Path.Combine(downloads, "42-123.zip.part");
        File.WriteAllBytes(partial, bytes[..(bytes.Length / 2)]);
        using var handler = new ResumeHandler(bytes);
        using var http = new HttpClient(handler);
        var client = new GameBananaClient(http);
        var plan = manager.PlanInstallAsync(mod, client).GetAwaiter().GetResult();
        Check(handler.SawRange && File.ReadAllBytes(partial).SequenceEqual(bytes));
        // Replanning after an interrupted install must also recover an end-of-file Range (416).
        plan = manager.PlanInstallAsync(mod, client).GetAwaiter().GetResult();
        Check(handler.SawEndOfFile && handler.SawFullDownload);
        var installed = manager.InstallPlanAsync(plan, false).GetAwaiter().GetResult().Single();
        Check(File.ReadAllText(Path.Combine(installed.ContentRoot, "board.txt")) == "board data");
    });

    private sealed class ResumeHandler(byte[] bytes) : HttpMessageHandler
    {
        public bool SawRange { get; private set; }
        public bool SawEndOfFile { get; private set; }
        public bool SawFullDownload { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var offset = (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
            if (offset == bytes.Length)
            {
                SawEndOfFile = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { RequestMessage = request });
            }
            SawRange |= offset > 0;
            SawFullDownload |= offset == 0;
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes[offset..])
            };
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return Task.FromResult(response);
        }
    }
}
