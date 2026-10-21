using System.Diagnostics;
using System.Security.Cryptography;
using CubeShelf.Core.Mods;
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

    /// <summary>An ISO or GCM the runtime opens itself needs nothing beyond the runtime.</summary>
    public bool IsPrepared(GameCatalogEntry game, string runtimeExecutable) =>
        game.ReadsDiscDirectly ? File.Exists(runtimeExecutable) : IsPrepared(game.Id, runtimeExecutable);

    public bool IsPrepared(string gameId, string runtimeExecutable) =>
        File.Exists(runtimeExecutable) &&
        (Directory.Exists(Path.Combine(Path.GetDirectoryName(runtimeExecutable)!, gameId, "files")) ||
         (File.Exists(PatchedDiscPath(gameId, runtimeExecutable)) &&
          File.Exists(PatchedDiscMarker(gameId, runtimeExecutable))));

    /// <summary>Where a disc patch's verified result goes (<see cref="GameDataPreparation.DiscPatch"/>).</summary>
    public static string PatchedDiscPath(string gameId, string runtimeExecutable) =>
        Path.Combine(Path.GetDirectoryName(runtimeExecutable)!, gameId, "disc.iso");

    private static string PatchedDiscMarker(string gameId, string runtimeExecutable) =>
        PatchedDiscPath(gameId, runtimeExecutable) + ".verified";

    /// <summary>
    /// Builds a mod's disc from the user's (<see cref="GameDataPreparation.DiscPatch"/>): only the
    /// exact image the patch was made against is accepted, and the result must be the exact
    /// image the mod's authors made. The user's image is never modified.
    /// </summary>
    public async Task<string> PrepareDiscPatchAsync(
        string gameId,
        string runtimeExecutable,
        string discImage,
        GameDiscPatch patch,
        string deltaPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowDolphinToolDownload = false)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(patch);
        if (!File.Exists(runtimeExecutable))
            throw new FileNotFoundException("Le runtime doit être installé avant la préparation.", runtimeExecutable);
        if (!File.Exists(discImage))
            throw new FileNotFoundException("L’image du jeu est introuvable.", discImage);
        if (!File.Exists(deltaPath))
            throw new FileNotFoundException("Le patch du mod est introuvable.", deltaPath);
        var extension = Path.GetExtension(discImage).ToLowerInvariant();
        if (extension is not ".iso" and not ".gcm" and not ".rvz")
            throw new InvalidDataException(
                $"{patch.ModName} se fabrique depuis une image ISO, GCM ou RVZ ({patch.SourceDescription}) : " +
                "les images CISO, NKit ou réduites ne conviennent pas.");

        // An RVZ (Dolphin's lossless format) goes back to the exact ISO first; the MD5 check
        // below is then on that image.
        string? converted = null;
        try
        {
            var source = discImage;
            var start = 0.0;
            if (extension == ".rvz")
            {
                var dolphin = await _dolphin.EnsureAvailableAsync(allowDolphinToolDownload,
                    new Progress<double>(value => progress?.Report(value * .05)), cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.Combine(_paths.CacheDirectory, "converted"));
                converted = Path.Combine(_paths.CacheDirectory, "converted", $"{gameId}-{Guid.NewGuid():N}.iso");
                await ConvertRvzAsync(dolphin, discImage, converted, cancellationToken).ConfigureAwait(false);
                source = converted;
                start = .15;
                progress?.Report(start);
            }
            return await PatchDiscAsync(gameId, runtimeExecutable, source, patch, deltaPath, start, progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { if (converted is not null && File.Exists(converted)) File.Delete(converted); } catch { }
        }
    }

    private static async Task<string> PatchDiscAsync(string gameId, string runtimeExecutable, string source,
        GameDiscPatch patch, string deltaPath, double start, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        double At(double fraction) => start + (1 - start) * fraction;
        var sourceMd5 = await Md5Async(source, new Progress<double>(value => progress?.Report(At(value * .15))),
            cancellationToken).ConfigureAwait(false);
        if (!sourceMd5.Equals(patch.SourceMd5, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"{patch.ModName} se fabrique uniquement depuis {patch.SourceDescription} (MD5 {patch.SourceMd5}). " +
                $"Cette image a le MD5 {sourceMd5} : une image tronquée, compressée, modifiée ou d’une autre " +
                "région ne convient pas. Choisis une copie complète de ton disque.");

        var output = PatchedDiscPath(gameId, runtimeExecutable);
        var marker = PatchedDiscMarker(gameId, runtimeExecutable);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var staging = Path.Combine(Path.GetDirectoryName(output)!, ".disc-staging-" + Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            await VcdiffDecoder.ApplyAsync(source, deltaPath, staging,
                new Progress<double>(value => progress?.Report(At(.15 + value * .7))), cancellationToken).ConfigureAwait(false);
            var resultMd5 = await Md5Async(staging, new Progress<double>(value => progress?.Report(At(.85 + value * .15))),
                cancellationToken).ConfigureAwait(false);
            if (!resultMd5.Equals(patch.ResultMd5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"L’image fabriquée n’est pas celle de {patch.ModName} (MD5 {resultMd5}, attendu {patch.ResultMd5}).");
            if (File.Exists(marker)) File.Delete(marker);
            File.Move(staging, output, true);
            await File.WriteAllTextAsync(marker, resultMd5 + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            progress?.Report(1);
            return output;
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch { }
        }
    }

    private static async Task<string> Md5Async(string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[4 << 20];
        long done = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            md5.AppendData(buffer, 0, read);
            done += read;
            progress?.Report(stream.Length == 0 ? 1 : (double)done / stream.Length);
        }
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    public async Task<string> PrepareAsync(
        string gameId,
        string runtimeExecutable,
        string discImage,
        IReadOnlyCollection<string> supportedDiscIds,
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

            var compatibility = DiscImageService.Inspect(source, supportedDiscIds);
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
