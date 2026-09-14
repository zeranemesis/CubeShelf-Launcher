using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace CubeShelf.Core.Mods;

public sealed record GameBananaMod(
    int Id,
    string Name,
    long Updated,
    string ArchiveUrl,
    string ArchiveName,
    string Description = "",
    string VersionLabel = "",
    string ProfileUrl = "",
    string InstallInstructions = "",
    string ThumbnailUrl = "",
    IReadOnlyList<string>? ImageUrls = null);

public sealed class GameBananaClient
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private readonly HttpClient _http;

    public GameBananaClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-preview");
    }

    public async Task<IReadOnlyList<GameBananaMod>> GetLatestAsync(int gameBananaGameId,
        CancellationToken cancellationToken = default)
    {
        if (gameBananaGameId <= 0) return Array.Empty<GameBananaMod>();
        var listUrl = $"https://api.gamebanana.com/Core/List/New?page=1&itemtype=Mod&gameid={gameBananaGameId}&include_updated=true&format=json_min";
        using var list = JsonDocument.Parse(await _http.GetStringAsync(listUrl, cancellationToken));
        var ids = list.RootElement.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 2 && row[1].TryGetInt32(out _))
            .Select(row => row[1].GetInt32()).Distinct().Take(40).ToArray();
        using var gate = new SemaphoreSlim(6);
        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await GetAsync(id, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(tasks)).OfType<GameBananaMod>()
            .OrderByDescending(mod => mod.Updated).ToArray();
    }

    public async Task<GameBananaMod> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var fields = Uri.EscapeDataString(
            "name,udate,date,description,text,screenshots,Files().aFiles(),install_instructions," +
            "Preview().sStructuredDataFullsizeUrl(),Preview().sSubFeedImageUrl()," +
            "Updates().aGetLatestUpdates(),Updates().aLatestUpdates(),Updates().bSubmissionHasUpdates()," +
            "Updates().nUpdatesCount(),Url().sProfileUrl(),Url().sDownloadUrl()");
        var url = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={id}&fields={fields}&return_keys=true&format=json_min";
        using var document = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? $"Mod {id}" : $"Mod {id}";
        var updated = root.TryGetProperty("udate", out var updatedValue) && updatedValue.TryGetInt64(out var timestamp) ? timestamp : 0;
        var files = root.TryGetProperty("Files().aFiles()", out var fileValue) ? fileValue : default;
        var values = Walk(files).Where(pair => pair.Value.ValueKind == JsonValueKind.String)
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.GetString() ?? "")).ToArray();
        var archiveUrl = values.FirstOrDefault(pair =>
            pair.Key.Contains("download", StringComparison.OrdinalIgnoreCase) && IsHttps(pair.Value)).Value;
        if (string.IsNullOrWhiteSpace(archiveUrl))
            archiveUrl = values.FirstOrDefault(pair => IsHttps(pair.Value)).Value;
        if (string.IsNullOrWhiteSpace(archiveUrl) && root.TryGetProperty("Url().sDownloadUrl()", out var directDownload))
            archiveUrl = directDownload.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(archiveUrl))
            throw new InvalidDataException("GameBanana ne fournit aucune archive exploitable.");
        EnsureTrusted(archiveUrl);
        var archiveName = values.Select(pair => Path.GetFileName(pair.Value))
            .FirstOrDefault(value => IsArchiveName(value));
        archiveName ??= Path.GetFileName(new Uri(archiveUrl).AbsolutePath);
        string GetString(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        var images = new List<string>();
        AddImage(images, GetString("Preview().sStructuredDataFullsizeUrl()"));
        if (root.TryGetProperty("screenshots", out var screenshots))
            foreach (var value in Walk(screenshots).Select(pair => pair.Value)
                         .Where(value => value.ValueKind == JsonValueKind.String)
                         .Select(value => value.GetString() ?? ""))
                AddImage(images, value);
        var thumbnail = GetString("Preview().sSubFeedImageUrl()");
        AddImage(images, thumbnail);
        var description = CleanMarkup(FirstNonEmpty(GetString("description"), GetString("text"), "Aucune description fournie."));
        var updates = root.TryGetProperty("Updates().aGetLatestUpdates()", out var latestUpdates)
            ? latestUpdates
            : root.TryGetProperty("Updates().aLatestUpdates()", out var alternateUpdates) ? alternateUpdates : default;
        var updateLabel = ExtractUpdateLabel(updates);
        var version = !string.IsNullOrWhiteSpace(updateLabel) ? updateLabel : updated > 0
            ? "Mise à jour " + DateTimeOffset.FromUnixTimeSeconds(updated).ToLocalTime().ToString("dd/MM/yyyy")
            : "Version GameBanana";
        return new(id, name, updated, archiveUrl, archiveName, description, version,
            GetString("Url().sProfileUrl()"), CleanMarkup(GetString("install_instructions")),
            FirstNonEmpty(thumbnail, images.FirstOrDefault() ?? ""), images);
    }

    public async Task DownloadAsync(GameBananaMod mod, string destination, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTrusted(mod.ArchiveUrl);
        var existing = File.Exists(destination) ? new FileInfo(destination).Length : 0L;
        if (existing > MaximumBytes)
        {
            File.Delete(destination);
            existing = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, mod.ArchiveUrl);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureTrusted(response.RequestMessage?.RequestUri?.ToString() ?? "");
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange?.From == existing;
        if (!append) existing = 0;
        var expected = response.Content.Headers.ContentRange?.Length ??
            (response.Content.Headers.ContentLength is long length ? existing + length : null);
        if (expected > MaximumBytes) throw new InvalidDataException("L’archive du mod dépasse 2 Gio.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 128 * 1024, true);
        var buffer = new byte[128 * 1024];
        long total = existing;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumBytes) throw new InvalidDataException("L’archive du mod dépasse 2 Gio.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (expected is > 0) progress?.Report(Math.Clamp((double)total / expected.Value, 0, 1));
        }
    }

    private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsArchiveName(string value) =>
        new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string CleanMarkup(string value)
    {
        var text = Regex.Replace(value ?? "", "<[^>]+>", " ");
        return WebUtility.HtmlDecode(Regex.Replace(text, "\\s+", " ").Trim());
    }

    private static void AddImage(List<string> images, string value)
    {
        if (!IsHttps(value)) return;
        if (!images.Contains(value, StringComparer.OrdinalIgnoreCase)) images.Add(value);
    }

    private static string ExtractUpdateLabel(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return "";
        var keys = new[] { "version", "title", "name", "_sName", "_sTitle", "sName", "sTitle" };
        foreach (var pair in Walk(element))
            if (pair.Value.ValueKind == JsonValueKind.String && keys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = CleanMarkup(pair.Value.GetString() ?? "");
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        return "";
    }

    private static void EnsureTrusted(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Adresse de téléchargement GameBanana non approuvée.");
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                yield return new(property.Name, property.Value);
                foreach (var nested in Walk(property.Value)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var nested in Walk(item)) yield return nested;
    }
}
