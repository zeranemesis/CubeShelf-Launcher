using System.IO.Compression;
using CubeShelf.Mobile;

internal static class Sm64Tests
{
    internal static async Task Run(string root)
    {
        var files = new EmbeddedSm64Files(Path.Combine(root, "sm64-tests"));
        Directory.CreateDirectory(files.Root);
        await File.WriteAllTextAsync(files.Rom, "previous ROM");
        Directory.CreateDirectory(Path.Combine(files.Root, "saves"));
        var save = Path.Combine(files.Root, "saves", "save_0.json");
        await File.WriteAllTextAsync(save, "player save");
        try
        {
            await files.ImportRomAsync(new MemoryStream("invalid ROM"u8.ToArray()), default);
            throw new Exception("Invalid ROM accepted");
        }
        catch (InvalidDataException) { }
        Check(await File.ReadAllTextAsync(files.Rom) == "previous ROM" && !File.Exists(files.Rom + ".partial"),
            "unrecognised ROM cannot replace the previous import");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await files.ImportRomAsync(new MemoryStream(new byte[8]), cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Check(!File.Exists(files.Rom + ".partial"), "cancelled ROM import removes the partial file");
        try { await files.ImportRomAsync(new MemoryStream(new byte[17 * 1024 * 1024]), default); throw new Exception("Oversized ROM accepted"); }
        catch (InvalidDataException) { }
        Check(await File.ReadAllTextAsync(save) == "player save", "failed imports preserve save data");
        foreach (var name in new[] { "assets/../../escape", "/absolute", "assets\\escape", ".tcc/../escape" })
        {
            using var unsafeZip = new MemoryStream(Zip((name, new byte[1])));
            try { await files.StageAssetsAsync(unsafeZip, default); throw new Exception("Unsafe archive accepted"); }
            catch (InvalidDataException) { }
        }
        Check(!File.Exists(Path.Combine(files.Root, "escape")), "bundled asset traversal is refused before writing files");
        var port = Zip(("version", "engine fixture"u8.ToArray()));
        using (var valid = new MemoryStream(Zip(("config.yml", "config"u8.ToArray()), ("ghostship.o2r", port))))
            await files.StageAssetsAsync(valid, default);
        Check(!files.IsPrepared, "bundled engine assets alone never enable Play");
        try { files.CommitPrepared(); throw new Exception("Missing game archive accepted"); }
        catch (IOException) { }
        Check(!files.IsPrepared, "failed extraction has no ready marker");
        await File.WriteAllBytesAsync(Path.Combine(files.Staging, "sm64.o2r"), Zip(("game", "user data fixture"u8.ToArray())));
        files.CommitPrepared();
        Check(files.IsPrepared && await File.ReadAllTextAsync(save) == "player save", "successful extraction commits data separately from saves");
        var settings = Path.Combine(files.Current, "ghostship.cfg.json");
        await File.WriteAllTextAsync(settings, "player settings");
        var retry = new EmbeddedSm64Files(Path.Combine(root, "sm64-tests"));
        Check(retry.Staging != files.Staging, "cancelled workers cannot write into the next extraction attempt");
        using (var valid = new MemoryStream(Zip(("config.yml", "config"u8.ToArray()), ("ghostship.o2r", port))))
            await retry.StageAssetsAsync(valid, default);
        await File.WriteAllTextAsync(Path.Combine(retry.Staging, "sm64.o2r"), "broken archive");
        try { retry.CommitPrepared(); throw new Exception("Corrupt game archive accepted"); }
        catch (InvalidDataException) { }
        Check(retry.IsPrepared && await File.ReadAllTextAsync(save) == "player save", "failed replacement preserves the playable installation and saves");
        await File.WriteAllBytesAsync(Path.Combine(retry.Staging, "sm64.o2r"), Zip(("game", "new user data fixture"u8.ToArray())));
        retry.CommitPrepared();
        Check(await File.ReadAllTextAsync(settings) == "player settings", "a successful replacement keeps the player's engine settings");
        Directory.Move(files.Current, Path.Combine(files.Root, "previous"));
        files.RecoverInterruptedCommit();
        Check(files.IsPrepared && await File.ReadAllTextAsync(settings) == "player settings", "an interrupted directory commit recovers the previous installation");
        File.Delete(Path.Combine(files.Current, "sm64.o2r"));
        Check(!files.IsPrepared, "a missing game archive disables Play after restart");
        if (Environment.GetEnvironmentVariable("CUBESHELF_SM64_ASSET_ZIP") is { } assetZip)
        {
            var bundled = new EmbeddedSm64Files(Path.Combine(root, "real-sm64-assets"));
            await using var input = File.OpenRead(assetZip);
            await bundled.StageAssetsAsync(input, default);
            Check(File.Exists(Path.Combine(bundled.Staging, "config.yml"))
                && File.Exists(Path.Combine(bundled.Staging, "ghostship.o2r"))
                && Directory.GetFiles(Path.Combine(bundled.Staging, "assets"), "*", SearchOption.AllDirectories).Length > 100,
                "the pinned real asset bundle stages successfully without a game ROM");
        }
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var output = zip.CreateEntry(entry.Name).Open(); output.Write(entry.Data); }
        return stream.ToArray();
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("OK  embedded SM64: " + message);
    }
}
