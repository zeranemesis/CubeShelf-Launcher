using System.Net.Http;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CubeShelf.Launcher.Services;

public sealed record UpdateInfo(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string AssetUrl,
    string ChecksumUrl,
    string Message);

public sealed class UpdateService
{
    private readonly LauncherConfig _config;
    private readonly HttpClient _http = new();

    public UpdateService(LauncherConfig config)
    {
        _config = config;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.2");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo> CheckAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0,0,0);
        var api = $"https://api.github.com/repos/{_config.GitHubOwner}/{_config.GitHubRepo}/releases/latest";

        using var response = await _http.GetAsync(api);
        if (!response.IsSuccessStatusCode)
            return new(false, current.ToString(), "", "", "", "",
                $"Aucune release disponible ou GitHub a répondu {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0";
        Version.TryParse(tag.TrimStart('v', 'V'), out var latest);
        latest ??= new Version(0,0,0);

        string assetUrl = "", checksumUrl = "";
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.Equals(_config.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                assetUrl = url;
            if (name.Equals(_config.ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                checksumUrl = url;
        }

        return new(
            latest > current,
            current.ToString(),
            latest.ToString(),
            doc.RootElement.GetProperty("html_url").GetString() ?? "",
            assetUrl,
            checksumUrl,
            latest > current ? "Nouvelle version disponible." : "CubeShelf est à jour.");
    }

    public async Task PrepareAndLaunchUpdateAsync(UpdateInfo info)
    {
        if (!info.UpdateAvailable || string.IsNullOrWhiteSpace(info.AssetUrl))
            throw new InvalidOperationException("Aucune mise à jour installable.");

        var tempDir = Path.Combine(Path.GetTempPath(), "CubeShelfUpdate");
        Directory.CreateDirectory(tempDir);
        var zip = Path.Combine(tempDir, _config.ReleaseAssetName);

        await DownloadFileAsync(info.AssetUrl, zip);

        if (!string.IsNullOrWhiteSpace(info.ChecksumUrl))
        {
            var checksumText = await _http.GetStringAsync(info.ChecksumUrl);
            var expected = ParseChecksum(checksumText, _config.ReleaseAssetName);
            if (expected.Length > 0)
            {
                await using var fs = File.OpenRead(zip);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("SHA-256 de la mise à jour invalide.");
            }
        }

        var updater = Path.Combine(AppContext.BaseDirectory, "CubeShelf.Updater.exe");
        if (!File.Exists(updater))
            throw new FileNotFoundException("CubeShelf.Updater.exe est absent.", updater);

        Process.Start(new ProcessStartInfo(updater)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            ArgumentList =
            {
                Environment.ProcessId.ToString(),
                zip,
                AppContext.BaseDirectory
            }
        });
    }

    private async Task DownloadFileAsync(string url, string destination)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        await input.CopyToAsync(output);
    }

    private static string ParseChecksum(string text, string file)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(file, StringComparison.OrdinalIgnoreCase))
                return parts[0].Trim();
        }
        return "";
    }
}
