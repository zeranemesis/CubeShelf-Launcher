using System.Diagnostics;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;

namespace CubeShelf.Core.Library;

public sealed class GameDataPreparer
{
    private readonly IPlatformPaths _paths;
    private readonly DolphinToolService _dolphin;

    public GameDataPreparer(IPlatformPaths paths, DolphinToolService? dolphinTool = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _dolphin = dolphinTool ?? new DolphinToolService(paths);
    }

    public DolphinToolStatus DolphinToolStatus => _dolphin.GetStatus();

    public bool IsPrepared(string gameId, string runtimeExecutable) =>
        File.Exists(runtimeExecutable) &&
        Directory.Exists(Path.Combine(Path.GetDirectoryName(runtimeExecutable)!, gameId, "files"));

    public async Task<string> PrepareAsync(
        string gameId,
        string runtimeExecutable,
        string discImage,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowDolphinToolDownload = false)
    {
        ValidateGameId(gameId);
        if (!File.Exists(runtimeExecutable))
            throw new FileNotFoundException("PartyBoard doit être installé avant la préparation.", runtimeExecutable);
        if (!File.Exists(discImage))
            throw new FileNotFoundException("L’image du jeu est introuvable.", discImage);

        var originalSource = Path.GetFullPath(discImage);
        var extension = Path.GetExtension(originalSource).ToLowerInvariant();
        var source = originalSource;
        string? converted = null;
        try
        {
            if (extension == ".rvz")
            {
                var dolphinProgress = new Progress<double>(value => progress?.Report(value * .22));
                var dolphin = await _dolphin.EnsureAvailableAsync(
                    allowDolphinToolDownload, dolphinProgress, cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "converted"));
                converted = Path.Combine(_paths.CacheDirectory, "converted", $"{gameId}-{Guid.NewGuid():N}.iso");
                await ConvertRvzAsync(dolphin, source, converted, cancellationToken).ConfigureAwait(false);
                source = converted;
                progress?.Report(.30);
            }
            else if (extension is not ".iso" and not ".gcm")
            {
                throw new InvalidDataException("Utilise une image ISO, GCM ou RVZ.");
            }

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
                var offset = extension == ".rvz" ? .30 : 0;
                var scale = 1 - offset;
                await GameCubeIsoExtractor.ExtractFilesAsync(
                    source,
                    staging,
                    new Progress<double>(value => progress?.Report(offset + value * scale)),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.EnumerateFileSystemEntries(staging).Any())
                    throw new InvalidDataException("L’image ne contient aucune donnée exploitable.");

                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    targetMoved = true;
                }
                Directory.Move(staging, target);
                newMoved = true;
                committed = true;
                progress?.Report(1);
                try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
                return target;
            }
            catch
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                if (!committed && newMoved && Directory.Exists(target)) Directory.Delete(target, true);
                if (!committed && targetMoved && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            finally
            {
                if (committed)
                    try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
            }
        }
        finally
        {
            if (converted is not null)
                try { if (File.Exists(converted)) File.Delete(converted); } catch { }
        }
    }

    private static async Task ConvertRvzAsync(
        string tool,
        string input,
        string output,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(tool)!
        };
        foreach (var argument in new[] { "convert", "-f", "iso", "-i", input, "-o", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ??
                            throw new InvalidOperationException("Impossible de démarrer DolphinTool.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException("La conversion RVZ vers ISO a échoué.");
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) ||
            gameId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Identifiant de jeu invalide.", nameof(gameId));
    }
}
