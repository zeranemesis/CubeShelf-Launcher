using System.IO.Compression;
using System.Text;
using CubeShelf.Core.Security;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Library;

var failures = new List<string>();
string currentRoot = "";

Run("Disc USA 1.1 reconnu", () =>
{
    var path = Path.Combine(TestRoot(), "disc.iso");
    var header = new byte[8];
    Encoding.ASCII.GetBytes("GMPE01").CopyTo(header, 0);
    header[7] = 1;
    File.WriteAllBytes(path, header);

    var result = DiscImageService.Inspect(path, DiscImageService.MarioParty4DiscIds, english: false);
    Assert(result.Recognized && result.Supported && result.VersionId == "GMPE01_01");
});

Run("Archive valide extraite", () =>
{
    var root = TestRoot();
    var zip = Path.Combine(root, "valid.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("res/config.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("ok");
    }

    var destination = Path.Combine(root, "out");
    SecureArchiveExtractor.Extract(zip, destination, Limits());
    Assert(File.ReadAllText(Path.Combine(destination, "res", "config.txt")) == "ok");
});

Run("Traversée de chemin refusée", () =>
{
    var root = TestRoot();
    var zip = Path.Combine(root, "traversal.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("../escape.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("blocked");
    }

    AssertThrows<InvalidDataException>(() =>
        SecureArchiveExtractor.Extract(zip, Path.Combine(root, "out"), Limits()));
    Assert(!File.Exists(Path.Combine(root, "escape.txt")));
});

Run("Limite décompressée appliquée", () =>
{
    var root = TestRoot();
    var zip = Path.Combine(root, "large.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("large.bin");
        using var stream = entry.Open();
        stream.Write(new byte[32]);
    }

    AssertThrows<InvalidDataException>(() =>
        SecureArchiveExtractor.Extract(
            zip,
            Path.Combine(root, "out"),
            new ArchiveExtractionLimits(10, 16, 16)));
});

Run("Chemins XDG Linux respectés", () =>
{
    var root = TestRoot();
    var paths = new PlatformPaths(
        environment: new Dictionary<string, string?>
        {
            ["XDG_DATA_HOME"] = Path.Combine(root, "data"),
            ["XDG_CACHE_HOME"] = Path.Combine(root, "cache"),
            ["XDG_CONFIG_HOME"] = Path.Combine(root, "config")
        },
        platform: PlatformFamily.Linux);

    Assert(paths.DataDirectory == Path.Combine(root, "data", "CubeShelf"));
    Assert(paths.CacheDirectory == Path.Combine(root, "cache", "CubeShelf"));
    Assert(paths.ConfigurationDirectory == Path.Combine(root, "config", "CubeShelf"));
});

Run("Manifeste sélectionne strictement la plateforme", () =>
{
    var manifest = new ReleaseManifest(
        ReleaseManifest.CurrentSchemaVersion,
        "1.0.0",
        new[]
        {
            new ReleaseArtifact("windows", "x64", "launcher", "https://example.invalid/win", 10, new string('a', 64)),
            new ReleaseArtifact("linux", "x64", "launcher", "https://example.invalid/linux", 10, new string('b', 64))
        });

    Assert(manifest.Select("linux", "x64", "launcher").Sha256 == new string('b', 64));
    AssertThrows<PlatformNotSupportedException>(() => manifest.Select("macos", "arm64", "launcher"));
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) en échec :");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("6 tests CubeShelf réussis.");
return 0;

void Run(string name, Action test)
{
    var root = Path.Combine(Path.GetTempPath(), "CubeShelfTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    currentRoot = root;

    try
    {
        test();
        Console.WriteLine($"OK  {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

string TestRoot() => currentRoot;

ArchiveExtractionLimits Limits() => new(100, 1024 * 1024, 1024 * 1024);

void Assert(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Assertion non satisfaite.");
}

void AssertThrows<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Exception {typeof(T).Name} attendue.");
}
