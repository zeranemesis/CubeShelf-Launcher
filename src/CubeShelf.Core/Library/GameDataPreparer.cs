using System.Diagnostics;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Library;

public sealed class GameDataPreparer
{
    private readonly IPlatformPaths _paths;

    public GameDataPreparer(IPlatformPaths paths) => _paths = paths;

    public bool IsPrepared(string gameId, string runtimeExecutable) =>
        File.Exists(runtimeExecutable) && Directory.Exists(Path.Combine(Path.GetDirectoryName(runtimeExecutable)!, gameId, "files"));

    public async Task<string> PrepareAsync(string gameId, string runtimeExecutable, string discImage,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        if (!File.Exists(runtimeExecutable)) throw new FileNotFoundException("PartyBoard doit être installé avant la préparation.", runtimeExecutable);
        if (!File.Exists(discImage)) throw new FileNotFoundException("L’image du jeu est introuvable.", discImage);
        var source = Path.GetFullPath(discImage);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        string? converted = null;
        try
        {
            if (extension == ".rvz")
            {
                var dolphin = FindDolphinTool() ?? throw new InvalidOperationException(
                    "DolphinTool est requis pour convertir un RVZ. Installe Dolphin Emulator puis relance la préparation.");
                Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "converted"));
                converted = Path.Combine(_paths.CacheDirectory, "converted", $"{gameId}-{Guid.NewGuid():N}.iso");
                await ConvertRvzAsync(dolphin, source, converted, cancellationToken);
                source = converted;
                progress?.Report(.3);
            }
            else if (extension is not ".iso" and not ".gcm")
                throw new InvalidDataException("Utilise une image ISO, GCM ou RVZ.");
            var compatibility = DiscImageService.Inspect(source);
            if (!compatibility.Recognized || !compatibility.Supported)
                throw new InvalidDataException("Image incompatible : " + compatibility.Message);

            var gameRoot = Path.Combine(Path.GetDirectoryName(runtimeExecutable)!, gameId);
            var target = Path.Combine(gameRoot, "files");
            var staging = Path.Combine(gameRoot, ".files-staging-" + Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(gameRoot, ".files-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var targetMoved = false;
            var newMoved = false;
            var committed = false;
            try
            {
                var offset = extension == ".rvz" ? .3 : 0;
                await GameCubeIsoExtractor.ExtractFilesAsync(source, staging,
                    new Progress<double>(value => progress?.Report(offset + value * (1 - offset))), cancellationToken);
                if (!Directory.EnumerateFileSystemEntries(staging).Any()) throw new InvalidDataException("L’image ne contient aucune donnée exploitable.");
                if (Directory.Exists(target)) { Directory.Move(target, backup); targetMoved = true; }
                Directory.Move(staging, target);
                newMoved = true;
                committed = true;
                progress?.Report(1);
                try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
                return target;
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (!committed && newMoved && Directory.Exists(target)) Directory.Delete(target, true);
                if (!committed && targetMoved && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            finally { if (committed) try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { } }
        }
        finally { if (converted is not null && File.Exists(converted)) File.Delete(converted); }
    }

    private static async Task ConvertRvzAsync(string tool, string input, string output, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(tool)! };
        foreach (var argument in new[] { "convert", "-f", "iso", "-i", input, "-o", output }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossible de démarrer DolphinTool.");
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); } catch { } throw; }
        if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException("La conversion RVZ vers ISO a échoué.");
    }

    private string? FindDolphinTool()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "DolphinTool.exe", "dolphin-tool.exe" } : new[] { "DolphinTool", "dolphin-tool" };
        var directories = new List<string> { AppContext.BaseDirectory, Path.Combine(_paths.DataDirectory, "Tools", "Dolphin") };
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            { directories.Add(Path.Combine(root, "Dolphin Emulator")); directories.Add(Path.Combine(root, "Dolphin")); }
        }
        else directories.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/app/bin" });
        foreach (var directory in directories.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            foreach (var name in names)
            {
                var direct = Path.Combine(directory, name);
                if (File.Exists(direct)) return direct;
                if (!Directory.Exists(directory)) continue;
                try { var nested = Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault(); if (nested is not null) return nested; }
                catch (UnauthorizedAccessException) { }
            }
        return null;
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Identifiant de jeu invalide.", nameof(gameId));
    }
}
