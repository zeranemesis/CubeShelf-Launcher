using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using CubeShelf.Core.Releases;

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
    IReadOnlyList<string>? ImageUrls = null,
    IReadOnlyList<GameBananaFile>? Files = null,
    string Category = "",
    string Author = "",
    long Views = 0)
{
    /// <summary>A catalogue entry (<see cref="GameBananaClient.BrowseAsync"/>): no archive until its page is read.</summary>
    public bool IsSummary => string.IsNullOrEmpty(ArchiveUrl);

    /// <summary>
    /// This mod with the archive of its file whose name matches <paramref name="pattern"/>
    /// (one * wildcard), or as it is when none does. A mod page can carry several files, and
    /// the first is not always the one wanted: Super Mario Eclipse's is its players guide.
    /// </summary>
    public GameBananaMod WithArchive(string pattern)
    {
        var file = Files?.FirstOrDefault(candidate => ReleaseAssetPattern.Matches(candidate.Name, pattern));
        return file is null ? this : this with { ArchiveUrl = file.Url, ArchiveName = file.Name };
    }
}

/// <summary>One downloadable file of a GameBanana mod.</summary>
public sealed record GameBananaFile(string Name, string Url, long Size);

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

    /// <summary>
    /// The game's mods as tiles: name, picture, category, author and views, 15 per page, newest
    /// first, those whose name contains <paramref name="search"/> when one is given. Installing
    /// needs <see cref="GetAsync"/> for the archive.
    /// </summary>
    public async Task<IReadOnlyList<GameBananaMod>> BrowseAsync(int gameBananaGameId, string search = "",
        int maxPages = 10, CancellationToken cancellationToken = default)
    {
        if (gameBananaGameId <= 0) return Array.Empty<GameBananaMod>();
        var mods = new List<GameBananaMod>();
        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"https://gamebanana.com/apiv11/Game/{gameBananaGameId}/Subfeed?_sSort=new&_nPage={page}&_csvModelInclusions=Mod" +
                      (string.IsNullOrWhiteSpace(search) ? "" : "&_sName=" + Uri.EscapeDataString(search.Trim()));
            using var document = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
            var (records, complete) = ParseSubfeed(document.RootElement);
            mods.AddRange(records.Where(mod => mods.All(known => known.Id != mod.Id)));
            if (complete || records.Count == 0) break;
        }
        return mods;
    }

    /// <summary>One page of apiv11's Subfeed, and whether it was the last.</summary>
    public static (IReadOnlyList<GameBananaMod> Mods, bool Complete) ParseSubfeed(JsonElement root)
    {
        string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        long Number(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
        var complete = root.TryGetProperty("_aMetadata", out var metadata) &&
                       metadata.TryGetProperty("_bIsComplete", out var flag) && flag.ValueKind == JsonValueKind.True;
        var mods = new List<GameBananaMod>();
        if (!root.TryGetProperty("_aRecords", out var records) || records.ValueKind != JsonValueKind.Array)
            return (mods, true);
        foreach (var record in records.EnumerateArray())
        {
            var id = Number(record, "_idRow");
            if (Text(record, "_sModelName") != "Mod" || id is <= 0 or > int.MaxValue) continue;
            var images = new List<string>();
            var thumbnail = "";
            if (record.TryGetProperty("_aPreviewMedia", out var media) && media.ValueKind == JsonValueKind.Object &&
                media.TryGetProperty("_aImages", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var image in list.EnumerateArray())
                {
                    var baseUrl = Text(image, "_sBaseUrl");
                    if (!IsTrustedImage(baseUrl)) continue;
                    if (Text(image, "_sFile").Length > 0) AddImage(images, baseUrl + "/" + Text(image, "_sFile"));
                    if (thumbnail.Length == 0)
                    {
                        var small = FirstNonEmpty(Text(image, "_sFile530"), Text(image, "_sFile220"), Text(image, "_sFile"));
                        if (small.Length > 0) thumbnail = baseUrl + "/" + small;
                    }
                }
            var category = record.TryGetProperty("_aRootCategory", out var root2) ? Text(root2, "_sName") : "";
            var author = record.TryGetProperty("_aSubmitter", out var submitter) ? Text(submitter, "_sName") : "";
            var updated = Math.Max(Number(record, "_tsDateModified"), Number(record, "_tsDateAdded"));
            mods.Add(new GameBananaMod((int)id, FirstNonEmpty(Text(record, "_sName"), $"Mod {id}"), updated, "", "",
                VersionLabel: Text(record, "_sVersion"), ProfileUrl: Text(record, "_sProfileUrl"),
                ThumbnailUrl: thumbnail, ImageUrls: images, Category: category, Author: author,
                Views: Number(record, "_nViewCount")));
        }
        return (mods, complete);
    }

    private static bool IsTrustedImage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("images.gamebanana.com", StringComparison.OrdinalIgnoreCase);

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
        var fileList = new List<GameBananaFile>();
        if (files.ValueKind == JsonValueKind.Object)
            foreach (var entry in files.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var fileName = entry.Value.TryGetProperty("_sFile", out var n) ? n.GetString() ?? "" : "";
                var fileUrl = entry.Value.TryGetProperty("_sDownloadUrl", out var u) ? u.GetString() ?? "" : "";
                var fileSize = entry.Value.TryGetProperty("_nFilesize", out var z) && z.TryGetInt64(out var size) ? size : 0;
                if (fileName.Length > 0 && IsHttps(fileUrl))
                {
                    EnsureTrusted(fileUrl);
                    fileList.Add(new(fileName, fileUrl, fileSize));
                }
            }
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
            FirstNonEmpty(thumbnail, images.FirstOrDefault() ?? ""), images, fileList);
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
        EnsureTrusted(response.RequestMessage?.RequestUri?.ToString() ?? "");
        // An interrupted install can leave an already complete archive. Some servers reject
        // its end-of-file Range with 416; restart once rather than making Retry fail forever.
        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(destination);
            await DownloadAsync(mod, destination, progress, cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange?.From == existing;
        if (!append) existing = 0;
        var expected = response.Content.Headers.ContentRange?.Length ??
            (response.Content.Headers.ContentLength is long length ? existing + length : null);
        if (expected > MaximumBytes) throw new InvalidDataException("L’archive du mod dépasse 2 Gio.");
        Storage.DiskSpaceGuard.Require(destination, Math.Max(0, (expected ?? existing) - existing));
        DownloadTransfer.Current?.BeginDownload(expected, existing);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 128 * 1024, true);
        var buffer = new byte[128 * 1024];
        long total = existing;
        while (true)
        {
            if (DownloadTransfer.Current is { } transfer) await transfer.WaitAsync(cancellationToken);
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (DownloadTransfer.Current is { } network) await network.RecordNetworkAsync(read, cancellationToken);
            total += read;
            if (total > MaximumBytes) throw new InvalidDataException("L’archive du mod dépasse 2 Gio.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            DownloadTransfer.Current?.RecordDisk(read);
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
