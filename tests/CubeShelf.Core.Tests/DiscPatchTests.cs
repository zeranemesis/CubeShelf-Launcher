using System.Security.Cryptography;
using System.Text;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;

/// <summary>Disc patches (Super Mario Eclipse): the xdelta decoder and the preparation built on it.</summary>
internal static class DiscPatchTests
{
    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("Disc patch assertion failed: " + what);
    }

    private static void WithRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-discpatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }

    private static readonly byte[] Source = Encoding.ASCII.GetBytes("GMSE01 Super Mario Sunshine, a disc for the test");

    // What the delta below makes from Source: a source copy, added bytes, a run, and a copy
    // from the target that overlaps what it writes (repeating "ab").
    private static readonly byte[] Expected =
        Encoding.ASCII.GetBytes("GMSE04 Eclipse").Concat(Enumerable.Repeat((byte)'z', 5))
            .Concat(Encoding.ASCII.GetBytes("ababababab")).ToArray();

    private static void Varint(List<byte> into, long value)
    {
        var groups = new List<byte> { (byte)(value & 0x7F) };
        for (value >>= 7; value > 0; value >>= 7) groups.Add((byte)(0x80 | (value & 0x7F)));
        groups.Reverse();
        into.AddRange(groups);
    }

    /// <summary>A one-window VCDIFF delta from Source to Expected, as xdelta3 lays one out.</summary>
    private static byte[] MakeDelta()
    {
        var data = new List<byte>();
        var instructions = new List<byte>();
        var addresses = new List<byte>();
        // COPY 4 bytes "GMSE" from the source (code 19: COPY, size follows, mode VCD_SELF).
        instructions.Add(19); Varint(instructions, 4); Varint(addresses, 0);
        // ADD "04 Eclipse" (code 1: ADD, size follows).
        var added = Encoding.ASCII.GetBytes("04 Eclipse");
        instructions.Add(1); Varint(instructions, added.Length); data.AddRange(added);
        // RUN of five 'z' (code 0).
        instructions.Add(0); Varint(instructions, 5); data.Add((byte)'z');
        // ADD "ab", then COPY 8 from the target 2 bytes back: an overlapping copy.
        instructions.Add(1); Varint(instructions, 2); data.AddRange("ab"u8.ToArray());
        instructions.Add(19); Varint(instructions, 8); Varint(addresses, Source.Length + 19);

        var window = new List<byte>();
        Varint(window, Expected.Length);
        window.Add(0); // no compressed sections
        Varint(window, data.Count);
        Varint(window, instructions.Count);
        Varint(window, addresses.Count);
        var adler = VcdiffDecoder.Adler32(Expected);
        window.AddRange(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler });
        window.AddRange(data);
        window.AddRange(instructions);
        window.AddRange(addresses);

        var delta = new List<byte> { 0xD6, 0xC3, 0xC4, 0x00, 0x00 };
        delta.Add(1 | 4); // VCD_SOURCE, with xdelta3's Adler-32
        Varint(delta, Source.Length);
        Varint(delta, 0);
        Varint(delta, window.Count);
        delta.AddRange(window);
        return delta.ToArray();
    }

    private static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

    public static void XdeltaAppliesExactly() => WithRoot(root =>
    {
        var source = Path.Combine(root, "source.iso");
        var delta = Path.Combine(root, "mod.xdelta");
        var output = Path.Combine(root, "out.iso");
        File.WriteAllBytes(source, Source);
        File.WriteAllBytes(delta, MakeDelta());
        VcdiffDecoder.ApplyAsync(source, delta, output).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(output).SequenceEqual(Expected), "patched bytes");

        // Another disc: the window's checksum fails, as it would on a wrong or damaged image.
        var other = (byte[])Source.Clone();
        other[1] ^= 0xFF;
        File.WriteAllBytes(source, other);
        var refused = false;
        try { VcdiffDecoder.ApplyAsync(source, delta, output).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "a source the delta was not made from is refused");

        File.WriteAllBytes(delta, Encoding.ASCII.GetBytes("not a delta at all"));
        refused = false;
        try { VcdiffDecoder.ApplyAsync(source, delta, output).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { refused = true; }
        Check(refused, "a file that is no VCDIFF delta is refused");
    });

    public static void PreparationChecksBothImages() => WithRoot(root =>
    {
        var runtime = Path.Combine(root, "runtime", "sms.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(runtime)!);
        File.WriteAllText(runtime, "");
        var disc = Path.Combine(root, "my disc", "Super Mario Sunshine (USA).iso");
        Directory.CreateDirectory(Path.GetDirectoryName(disc)!);
        File.WriteAllBytes(disc, Source);
        var delta = Path.Combine(root, "v1.1.0.xdelta");
        File.WriteAllBytes(delta, MakeDelta());
        var patch = new GameDiscPatch
        {
            ModName = "Test Eclipse", PatchFile = "v1.1.0.xdelta", SourceMd5 = Md5(Source), ResultMd5 = Md5(Expected),
            SourceDescription = "the test disc", OutputImage = "GMSE04/disc.iso"
        };
        var preparer = new GameDataPreparer(new TestPaths(root));
        Check(!preparer.IsPrepared("GMSE04", runtime), "not prepared before");

        // A different image (a truncated copy, say) is refused before anything is written.
        File.WriteAllBytes(disc, Source[..^4]);
        var message = "";
        try { preparer.PrepareDiscPatchAsync("GMSE04", runtime, disc, patch, delta).GetAwaiter().GetResult(); }
        catch (InvalidDataException exception) { message = exception.Message; }
        Check(message.Contains(patch.SourceMd5) && message.Contains("the test disc"), "the refusal names the expected image");
        Check(!File.Exists(GameDataPreparer.PatchedDiscPath("GMSE04", runtime)), "nothing written for a refused image");

        File.WriteAllBytes(disc, Source);
        var made = preparer.PrepareDiscPatchAsync("GMSE04", runtime, disc, patch, delta).GetAwaiter().GetResult();
        Check(made == GameDataPreparer.PatchedDiscPath("GMSE04", runtime), "output beside the runtime");
        Check(File.ReadAllBytes(made).SequenceEqual(Expected), "patched disc");
        Check(File.ReadAllBytes(disc).SequenceEqual(Source), "the user's disc is untouched");
        Check(preparer.IsPrepared("GMSE04", runtime), "prepared after");
        Check(Directory.GetFiles(Path.GetDirectoryName(made)!).All(file => !Path.GetFileName(file).StartsWith(".disc-staging")),
            "no staging file left");

        // A patch whose result is not the expected image is refused, and the last good one kept.
        patch.ResultMd5 = new string('0', 32);
        message = "";
        try { preparer.PrepareDiscPatchAsync("GMSE04", runtime, disc, patch, delta).GetAwaiter().GetResult(); }
        catch (InvalidDataException exception) { message = exception.Message; }
        Check(message.Contains("Test Eclipse"), "a wrong result is refused");
        Check(File.ReadAllBytes(made).SequenceEqual(Expected), "the verified disc is kept");
    });

    public static void ModArchiveIsPicked()
    {
        var mod = new GameBananaMod(536309, "Super Mario Eclipse", 1, "https://gamebanana.com/dl/1717133",
            "super_mario_eclipse_official_players_guide_922a3.7z", Files: new[]
            {
                new GameBananaFile("super_mario_eclipse_official_players_guide_922a3.7z", "https://gamebanana.com/dl/1717133", 224416707),
                new GameBananaFile("super_mario_eclipse_v110_ac483.7z", "https://gamebanana.com/dl/1729332", 892257115),
            });
        var picked = mod.WithArchive("super_mario_eclipse_v*.7z");
        Check(picked.ArchiveUrl == "https://gamebanana.com/dl/1729332" && picked.ArchiveName == "super_mario_eclipse_v110_ac483.7z",
            "the patch's archive, not the first file");
        Check(mod.WithArchive("nothing_*.zip") == mod, "no match keeps the mod as it is");
    }

    public static void VariantFollowsItsMod()
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-variant-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestPaths(root);
            var sunshine = new GameCatalogEntry { Id = "GMSP01", Title = "Super Mario Sunshine" };
            var eclipse = new GameCatalogEntry { Id = "GMSE04", Title = "Super Mario Eclipse", VariantOf = "GMSP01", ActivatedByModId = 536309 };
            var catalog = new[] { sunshine, eclipse };
            Check(GameVariants.Resolve(catalog, sunshine, paths) == sunshine, "no mod: Sunshine");
            Check(eclipse.ModLibraryId == "GMSP01" && sunshine.ModLibraryId == "GMSP01", "one mod library for both");

            var mods = new PortableModManager(paths, "GMSP01");
            void Write(bool enabled) => File.WriteAllText(Path.Combine(paths.DataDirectory, "Mods", "GMSP01", "installed.json"),
                System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new PortableInstalledMod(536309, "Super Mario Eclipse", 1, enabled, 0, Path.Combine(root, "eclipse"), ""),
                    new PortableInstalledMod(248171, "Skin", 1, true, 1, Path.Combine(root, "skin"), ""),
                }));
            Write(true);
            Check(GameVariants.Resolve(catalog, sunshine, paths) == eclipse, "Eclipse enabled: Eclipse");
            Check(GameVariants.Resolve(catalog, eclipse, paths) == eclipse, "from the variant too");
            File.WriteAllText(Path.Combine(paths.DataDirectory, "Mods", "GMSP01", "player-disabled.json"), "[536309]");
            Check(GameVariants.Resolve(catalog, eclipse, paths) == sunshine, "switched off in game: Sunshine");
            mods.ClearPlayerDisabled(536309);
            Write(false);
            Check(GameVariants.Resolve(catalog, eclipse, paths) == sunshine, "Eclipse disabled: Sunshine");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class TestPaths : CubeShelf.Core.Platform.IPlatformPaths
    {
        public TestPaths(string root) { DataDirectory = Path.Combine(root, "data"); CacheDirectory = Path.Combine(root, "cache"); }
        public string DataDirectory { get; }
        public string CacheDirectory { get; }
        public string ConfigurationDirectory => DataDirectory;
        public string DownloadHistoryFile => Path.Combine(DataDirectory, "downloads.json");
    }
}
