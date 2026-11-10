using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CubeShelf.Assistance;

public sealed record GameWebSource(string Title, string Url, string Excerpt);
public sealed record GameAdvice(string Answer, IReadOnlyList<GameWebSource> Sources, string WebStatus);

/// <summary>Public guide APIs only. No screenshot, profile, cookie or API key leaves the device.</summary>
public sealed class GameWebGuides(HttpClient http)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly (string Api, string Pages)[] Sites =
    [ ("https://strategywiki.org/w/api.php", "https://strategywiki.org/wiki/"), ("https://www.mariowiki.com/api.php", "https://www.mariowiki.com/"),
      ("https://en.wikibooks.org/w/api.php", "https://en.wikibooks.org/wiki/") ];

    public async Task<IReadOnlyList<GameWebSource>> FindAsync(string game, string search, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        game = game[..Math.Min(game.Length, 160)]; search = SearchTerms(search, game);
        var results = await Task.WhenAll(Sites.Select(site => FindSiteAsync(site.Api, site.Pages, game, search, cancellation)));
        cancellation.ThrowIfCancellationRequested();
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(term => term.Length >= 4).ToArray();
        return results.SelectMany(items => items).DistinctBy(item => item.Url)
            .OrderByDescending(source => terms.Sum(term => source.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : source.Excerpt.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            .Take(3).ToArray();
    }

    private async Task<IReadOnlyList<GameWebSource>> FindSiteAsync(string api, string pages, string game, string search, CancellationToken cancellation)
    {
        var result = new List<GameWebSource>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation); budget.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var titles = search.Length == 0 ? [] : await SearchAsync(api, $"\"{game}\" {search}", budget.Token);
            if (titles.Length == 0) titles = [game, game + "/Controls"];
            foreach (var title in titles.Take(4))
            {
                using var page = await JsonAsync(api + "?action=parse&prop=text&format=json&page=" + Uri.EscapeDataString(title), budget.Token);
                if (!page.RootElement.TryGetProperty("parse", out var parsed) || !parsed.TryGetProperty("text", out var text) || !text.TryGetProperty("*", out var html)) continue;
                var excerpt = RelevantText(html.GetString() ?? "", search);
                if (excerpt.Length < 60 || !MatchesScene(title, excerpt, game, search)) continue;
                result.Add(new GameWebSource(title, pages + Uri.EscapeDataString(title.Replace(' ', '_')), excerpt));
            }
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return result; }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidDataException or RegexMatchTimeoutException) { return result; }
    }

    private async Task<string[]> SearchAsync(string api, string query, CancellationToken cancellation)
    {
        using var json = await JsonAsync(api + "?action=query&list=search&srnamespace=0&srlimit=4&format=json&srsearch=" + Uri.EscapeDataString(query), cancellation);
        if (!json.RootElement.TryGetProperty("query", out var result) || !result.TryGetProperty("search", out var hits)) return [];
        return hits.EnumerateArray().Select(hit => hit.GetProperty("title").GetString()).Where(title => !string.IsNullOrWhiteSpace(title)).Cast<string>().ToArray();
    }

    private async Task<JsonDocument> JsonAsync(string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DecompShelf/0.18.1 (+https://github.com/zeranemesis/CubeShelf-Launcher)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var data = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (data.Length + count > 1048576) throw new InvalidDataException("Guide trop volumineux.");
            data.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(data.ToArray());
    }

    public static string SearchTerms(string observation, string game)
    {
        var match = Regex.Match(observation, @"RECHERCHE\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase, RegexTimeout);
        var terms = match.Success ? match.Groups[1].Value.Replace(game, "", StringComparison.OrdinalIgnoreCase) : "";
        var cleaned = Regex.Replace(terms[..Math.Min(terms.Length, 160)], @"[^\p{L}\p{N}\s'’-]", " ", RegexOptions.None, RegexTimeout);
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.ToLowerInvariant() is not ("game" or "screen" or "capture" or "guide" or "help" or "visible")));
    }

    public static bool MatchesScene(string title, string excerpt, string game, string search)
    {
        var gamePage = title.Equals(game, StringComparison.OrdinalIgnoreCase) || title.StartsWith(game + "/", StringComparison.OrdinalIgnoreCase);
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(term => term.Length >= 4).ToArray();
        // A full-text mention of the game alone also matches unrelated games and wiki maintenance pages.
        return gamePage ? terms.Length == 0 || terms.Any(term => excerpt.Contains(term, StringComparison.OrdinalIgnoreCase)) :
            terms.Length > 0 && terms.All(term => title.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
            excerpt.Contains(game, StringComparison.OrdinalIgnoreCase);
    }

    public static string RelevantText(string html, string search)
    {
        html = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexTimeout);
        html = Regex.Replace(html, @"</(?:p|li|h[1-6]|tr|div)>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ", RegexOptions.None, RegexTimeout));
        var terms = Regex.Matches(search.ToLowerInvariant(), @"[\p{L}\p{N}]{4,}", RegexOptions.None, RegexTimeout).Select(match => match.Value).Distinct().ToArray();
        var paragraphs = text.Split('\n').Select((value, index) => (Text: Regex.Replace(value, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim(), Index: index))
            .Where(item => item.Text.Length >= 30).ToArray();
        var selected = paragraphs.OrderByDescending(item => terms.Count(term => item.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(item => item.Index).Take(4).OrderBy(item => item.Index);
        var excerpt = string.Join("\n", selected.Select(item => item.Text));
        return excerpt[..Math.Min(excerpt.Length, 1500)];
    }
}
