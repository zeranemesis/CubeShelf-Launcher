namespace CubeShelf.Launcher.Services;

public sealed record GameBananaMod(
    int Id,
    string Name,
    long Updated,
    string ProfileUrl,
    string DownloadPageUrl,
    string ArchiveUrl,
    string ArchiveName,
    string InstallInstructions,
    string Description,
    string VersionLabel,
    string LatestUpdateSummary,
    string ThumbnailUrl,
    IReadOnlyList<string> ImageUrls);

public sealed class GameBananaService
{
    private readonly HttpClient _http = new();
    private readonly int _gameBananaGameId;

    public GameBananaService(int gameBananaGameId)
    {
        _gameBananaGameId = gameBananaGameId;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.5");
    }

    public async Task<IReadOnlyList<int>> GetLatestModIdsAsync(int pages = 3)
    {
        if (_gameBananaGameId <= 0)
            return Array.Empty<int>();

        var ids = new HashSet<int>();

        for (var page = 1; page <= pages; page++)
        {
            var url =
                $"https://api.gamebanana.com/Core/List/New?page={page}" +
                $"&itemtype=Mod&gameid={_gameBananaGameId}" +
                $"&include_updated=true&format=json_min";

            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url));

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2)
                    continue;

                if (!string.Equals(
                        row[0].GetString(),
                        "Mod",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (row[1].TryGetInt32(out var id))
                    ids.Add(id);
            }
        }

        return ids.ToList();
    }

    public async Task<IReadOnlyList<GameBananaMod>> GetLatestModsAsync()
    {
        var ids = await GetLatestModIdsAsync();
        var result = new List<GameBananaMod>();

        foreach (var id in ids.Take(80))
        {
            try
            {
                result.Add(await GetModAsync(id));
            }
            catch
            {
                // One withheld or malformed submission must not block the list.
            }
        }

        return result
            .OrderByDescending(x => x.Updated)
            .ToList();
    }

    public async Task<GameBananaMod> GetModAsync(int id, CancellationToken cancellationToken = default)
    {
        var fields = Uri.EscapeDataString(
            "name,udate,date,description,text,screenshots," +
            "Files().aFiles(),install_instructions," +
            "Preview().sStructuredDataFullsizeUrl()," +
            "Preview().sSubFeedImageUrl()," +
            "Updates().aGetLatestUpdates()," +
            "Updates().aLatestUpdates()," +
            "Updates().bSubmissionHasUpdates()," +
            "Updates().nUpdatesCount()," +
            "Url().sProfileUrl()," +
            "Url().sDownloadUrl()");

        var url =
            $"https://api.gamebanana.com/Core/Item/Data" +
            $"?itemtype=Mod&itemid={id}" +
            $"&fields={fields}&return_keys=true&format=json_min";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
        var r = doc.RootElement;

        string GetString(string key)
            => r.TryGetProperty(key, out var v) &&
               v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";

        var files = r.TryGetProperty("Files().aFiles()", out var f)
            ? f
            : default;

        var screenshots = r.TryGetProperty("screenshots", out var ss)
            ? ss
            : default;

        var updates = r.TryGetProperty("Updates().aGetLatestUpdates()", out var up)
            ? up
            : r.TryGetProperty("Updates().aLatestUpdates()", out var up2)
                ? up2
                : default;

        var previewFull = GetString("Preview().sStructuredDataFullsizeUrl()");
        var previewThumb = GetString("Preview().sSubFeedImageUrl()");

        var images = new List<string>();

        AddImage(images, previewFull);
        foreach (var image in ExtractImageUrls(screenshots))
            AddImage(images, image);
        AddImage(images, previewThumb);

        var description =
            FirstNonEmpty(
                GetString("description"),
                GetString("text"),
                "Aucune description fournie.");

        description = StripMarkup(description);

        var updateSummary = ExtractLatestUpdateSummary(updates);
        var updated = r.TryGetProperty("udate", out var u) &&
                      u.TryGetInt64(out var updatedTs)
            ? updatedTs
            : 0;

        var versionLabel =
            !string.IsNullOrWhiteSpace(updateSummary)
                ? updateSummary
                : updated > 0
                    ? "Mise à jour " +
                      DateTimeOffset.FromUnixTimeSeconds(updated)
                          .ToLocalTime()
                          .ToString("dd/MM/yyyy")
                    : "Version GameBanana";

        return new GameBananaMod(
            id,
            FirstNonEmpty(GetString("name"), $"Mod {id}"),
            updated,
            GetString("Url().sProfileUrl()"),
            GetString("Url().sDownloadUrl()"),
            FindDownloadUrl(files),
            FindArchiveName(files),
            StripMarkup(GetString("install_instructions")),
            description,
            versionLabel,
            updateSummary,
            FirstNonEmpty(previewThumb, images.FirstOrDefault() ?? ""),
            images);
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private static void AddImage(List<string> list, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return;

        if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
            list.Add(url);
    }

    private static IEnumerable<string> ExtractImageUrls(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            yield break;

        foreach (var pair in Walk(element))
        {
            if (pair.Value.ValueKind != JsonValueKind.String)
                continue;

            var value = pair.Value.GetString() ?? "";
            if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            var lower = value.ToLowerInvariant();

            if (lower.Contains(".jpg") ||
                lower.Contains(".jpeg") ||
                lower.Contains(".png") ||
                lower.Contains(".webp") ||
                lower.Contains(".gif") ||
                lower.Contains("/img/"))
            {
                yield return value;
            }
        }
    }

    private static string ExtractLatestUpdateSummary(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            element.ValueKind == JsonValueKind.Null)
            return "";

        var candidateKeys = new[]
        {
            "version", "title", "name", "_sName", "_sTitle", "sName", "sTitle"
        };

        foreach (var pair in Walk(element))
        {
            if (pair.Value.ValueKind != JsonValueKind.String)
                continue;

            if (!candidateKeys.Any(k =>
                    pair.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                continue;

            var text = StripMarkup(pair.Value.GetString() ?? "");
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return "";
    }

    private static string StripMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        withoutTags = Regex.Replace(withoutTags, "\\s+", " ").Trim();
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string FindDownloadUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return "";

        foreach (var pair in Walk(element))
        {
            if (pair.Value.ValueKind != JsonValueKind.String)
                continue;

            var s = pair.Value.GetString() ?? "";

            if (pair.Key.Contains("download", StringComparison.OrdinalIgnoreCase) &&
                s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        foreach (var pair in Walk(element))
        {
            if (pair.Value.ValueKind != JsonValueKind.String)
                continue;

            var s = pair.Value.GetString() ?? "";
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return s;
        }

        return "";
    }

    private static string FindArchiveName(JsonElement element)
    {
        foreach (var pair in Walk(element))
        {
            if (pair.Value.ValueKind != JsonValueKind.String)
                continue;

            var s = pair.Value.GetString() ?? "";
            var ext = Path.GetExtension(s).ToLowerInvariant();

            if (ext == ".zip" || ext == ".7z" || ext == ".rar")
                return Path.GetFileName(s);
        }

        return "";
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> Walk(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                yield return new(p.Name, p.Value);

                foreach (var nested in Walk(p.Value))
                    yield return nested;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                foreach (var nested in Walk(item))
                    yield return nested;
        }
    }

    public async Task<string> DownloadAsync(
        GameBananaMod mod,
        string destination,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mod.ArchiveUrl))
        {
            throw new InvalidOperationException(
                "Aucune archive GameBanana exploitable n'a été trouvée.");
        }

        using var response = await _http.GetAsync(
            mod.ArchiveUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);

        var buffer = new byte[128 * 1024];
        long done = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            done += read;

            if (total is > 0)
                progress?.Report((double)done / total.Value);
        }

        return destination;
    }
}
