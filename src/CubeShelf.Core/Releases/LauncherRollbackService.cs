using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Releases;

public sealed record PreviousLauncher(string Path, string Label, bool AppImage = false);

public static class LauncherRollbackService
{
    private sealed record BackupManifest(string InstallDirectory, string Version, Dictionary<string, string> Files);

    public static PreviousLauncher? FindPrevious(IPlatformPaths paths)
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(appImage) && File.Exists(appImage + ".previous"))
            return new(appImage + ".previous", "AppImage · " + File.GetLastWriteTime(appImage + ".previous").ToString("g"), true);
        if (!OperatingSystem.IsWindows()) return null;
        var root = Path.Combine(paths.DataDirectory, "UpdaterBackups");
        if (!Directory.Exists(root)) return null;
        foreach (var directory in Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            try
            {
                var manifest = Read(directory);
                if (Path.GetFullPath(manifest.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return new(directory, "CubeShelf " + manifest.Version);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return null;
    }

    public static string Prepare(PreviousLauncher previous, IPlatformPaths paths)
    {
        var destination = Path.Combine(paths.CacheDirectory, "Updates");
        Directory.CreateDirectory(destination);
        if (previous.AppImage)
        {
            if ((File.GetAttributes(previous.Path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La copie précédente ne peut pas être un lien.");
            var image = Path.Combine(destination, "CubeShelf-rollback.AppImage");
            File.Copy(previous.Path, image, true);
            return image;
        }
        var manifest = Read(previous.Path);
        if (!manifest.Files.Keys.Contains("CubeShelf.exe", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("La copie précédente ne contient pas CubeShelf.");
        var archive = Path.Combine(destination, "CubeShelf-rollback.zip");
        var temporary = archive + ".part";
        try
        {
            using (var output = File.Create(temporary))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            foreach (var pair in manifest.Files)
            {
                var file = Safe(previous.Path, pair.Key);
                using (var input = File.OpenRead(file))
                    if (!Convert.ToHexString(SHA256.HashData(input)).Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La copie précédente de CubeShelf a été altérée.");
                zip.CreateEntryFromFile(file, pair.Key.Replace('\\', '/'), CompressionLevel.Fastest);
            }
            File.Move(temporary, archive, true);
            return archive;
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }

    private static BackupManifest Read(string root)
    {
        var path = Safe(root, "backup.complete.json");
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Manifeste de copie invalide.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path))
                       ?? throw new InvalidDataException("Copie précédente vide.");
        if (manifest.Files is null || manifest.Files.Count is 0 or > 4096)
            throw new InvalidDataException("Copie précédente invalide.");
        return manifest;
    }

    private static string Safe(string root, string relative)
    {
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chemin en dehors de la copie de CubeShelf.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Lien interdit dans la copie de CubeShelf.");
            if (current.Equals(basePath, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }
}
