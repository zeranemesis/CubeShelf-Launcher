using System.Reflection;

namespace CubeShelf.Launcher.Services;

public sealed record UpdateInfo(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string AssetUrl,
    string ChecksumUrl,
    string Message);

public sealed record PreparedLauncherUpdate(
    string Version,
    string ZipPath,
    string UpdaterPath,
    string UpdaterWorkingDirectory);

public sealed class UpdateService
{
    private readonly LauncherConfig _config;
    private readonly HttpClient _http = new();

    public UpdateService(LauncherConfig config)
    {
        _config = config;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.6.15");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current =
            Assembly.GetExecutingAssembly().GetName().Version ??
            new Version(0, 0, 0);

        var api =
            $"https://api.github.com/repos/{_config.GitHubOwner}/{_config.GitHubRepo}/releases/latest";

        using var response =
            await _http.GetAsync(api, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new(
                false,
                current.ToString(),
                "",
                "",
                "",
                "",
                $"Aucune release disponible ou GitHub a répondu {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        var tag =
            doc.RootElement.GetProperty("tag_name").GetString() ??
            "0.0.0";

        Version.TryParse(
            tag.TrimStart('v', 'V'),
            out var latest);

        latest ??= new Version(0, 0, 0);

        string assetUrl = "";
        string checksumUrl = "";

        foreach (var asset in
                 doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name =
                asset.GetProperty("name").GetString() ?? "";

            var url =
                asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.Equals(
                    _config.ReleaseAssetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = url;
            }

            if (name.Equals(
                    _config.ChecksumAssetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                checksumUrl = url;
            }
        }

        return new(
            latest > current,
            current.ToString(),
            latest.ToString(),
            doc.RootElement.GetProperty("html_url").GetString() ?? "",
            assetUrl,
            checksumUrl,
            latest > current
                ? "Nouvelle version disponible."
                : "CubeShelf est à jour.");
    }

    public async Task<PreparedLauncherUpdate> PrepareUpdateAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!info.UpdateAvailable ||
            string.IsNullOrWhiteSpace(info.AssetUrl))
        {
            throw new InvalidOperationException(
                "Aucune mise à jour installable.");
        }

        if (string.IsNullOrWhiteSpace(info.ChecksumUrl))
        {
            throw new CryptographicException(
                "La release CubeShelf ne contient pas checksums.txt.");
        }

        var safeVersion = string.Join(
            "_",
            info.LatestVersion.Split(
                Path.GetInvalidFileNameChars(),
                StringSplitOptions.RemoveEmptyEntries));

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "CubeShelf",
            "Updates",
            safeVersion);

        Directory.CreateDirectory(tempDir);

        var zip =
            Path.Combine(tempDir, _config.ReleaseAssetName);

        var checksumText =
            await _http.GetStringAsync(
                info.ChecksumUrl,
                cancellationToken);

        var expected =
            ParseChecksum(
                checksumText,
                _config.ReleaseAssetName);

        if (expected.Length == 0)
        {
            throw new CryptographicException(
                "checksums.txt ne contient pas le hash attendu.");
        }

        // Reuse a previously completed background download when possible.
        var validCachedFile =
            File.Exists(zip) &&
            await VerifySha256Async(
                zip,
                expected,
                cancellationToken);

        if (!validCachedFile)
        {
            try
            {
                if (File.Exists(zip))
                    File.Delete(zip);
            }
            catch
            {
            }

            progress?.Report(0.01);

            await DownloadFileResumableAsync(
                info.AssetUrl,
                zip,
                p => progress?.Report(0.01 + p * 0.89),
                cancellationToken);

            progress?.Report(0.92);

            if (!await VerifySha256Async(
                    zip,
                    expected,
                    cancellationToken))
            {
                try { File.Delete(zip); } catch { }

                throw new CryptographicException(
                    "SHA-256 de la mise à jour invalide. " +
                    "Le téléchargement a été refusé.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.96);

        var updaterSource = Path.Combine(
            AppContext.BaseDirectory,
            "CubeShelf.Updater.exe");

        if (!File.Exists(updaterSource))
        {
            throw new FileNotFoundException(
                "CubeShelf.Updater.exe est absent.",
                updaterSource);
        }

        var updaterTempDir = Path.Combine(
            Path.GetTempPath(),
            "CubeShelf",
            "Updater",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(updaterTempDir);

        var updaterTemp = Path.Combine(
            updaterTempDir,
            "CubeShelf.Updater.exe");

        File.Copy(
            updaterSource,
            updaterTemp,
            true);

        progress?.Report(1.0);

        return new PreparedLauncherUpdate(
            info.LatestVersion,
            zip,
            updaterTemp,
            updaterTempDir);
    }

    public Process LaunchPreparedUpdate(
        PreparedLauncherUpdate prepared)
    {
        if (!File.Exists(prepared.ZipPath))
        {
            throw new FileNotFoundException(
                "Archive de mise à jour introuvable.",
                prepared.ZipPath);
        }

        if (!File.Exists(prepared.UpdaterPath))
        {
            throw new FileNotFoundException(
                "CubeShelf.Updater.exe est introuvable.",
                prepared.UpdaterPath);
        }

        return Process.Start(
                   new ProcessStartInfo(prepared.UpdaterPath)
                   {
                       WorkingDirectory =
                           prepared.UpdaterWorkingDirectory,
                       UseShellExecute = false,
                       ArgumentList =
                       {
                           Environment.ProcessId.ToString(),
                           prepared.ZipPath,
                           AppContext.BaseDirectory,
                           prepared.Version
                       }
                   }) ??
               throw new InvalidOperationException(
                   "Impossible de démarrer CubeShelf.Updater.");
    }

    private static async Task<bool> VerifySha256Async(
        string file,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);

        var actual =
            Convert
                .ToHexString(
                    await SHA256.HashDataAsync(
                        fs,
                        cancellationToken))
                .ToLowerInvariant();

        return actual.Equals(
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadFileResumableAsync(
        string url,
        string destination,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".part";
        var existing = File.Exists(partial)
            ? new FileInfo(partial).Length
            : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (existing > 0 &&
            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Move(partial, destination, true);
            progress?.Invoke(1);
            return;
        }

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
            existing = 0;

        response.EnsureSuccessStatusCode();

        var responseLength = response.Content.Headers.ContentLength;
        var total = response.Content.Headers.ContentRange?.Length ??
                    (responseLength is > 0
                        ? existing + responseLength.Value
                        : (long?)null);

        var buffer = new byte[256 * 1024];
        var done = existing;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            partial,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            useAsync: true))
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read <= 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;

                if (total is > 0)
                    progress?.Invoke(Math.Clamp((double)done / total.Value, 0, 1));
            }

            await output.FlushAsync(cancellationToken);
        }

        File.Move(partial, destination, true);
        progress?.Invoke(1);
    }

    private static string ParseChecksum(
        string text,
        string file)
    {
        foreach (var line in text.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2 &&
                parts[^1]
                    .TrimStart('*')
                    .Equals(
                        file,
                        StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].Trim();
            }
        }

        return "";
    }
}
