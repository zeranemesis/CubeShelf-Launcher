using System.Net;
using System.Text;
using System.Text.Json;
using CubeShelf.Assistance;

internal static class GameWebGuideChecks
{
    public static async Task Run()
    {
        using var http = new HttpClient(new Pages());
        var guides = new GameWebGuides(http);
        var sources = await guides.FindAsync("Mario Party 4", "SCÈNE: un mini-jeu\nRECHERCHE: Booksquirm", default);
        if (sources.Count != 3 || sources.Any(source => !source.Excerpt.Contains("Booksquirm") || source.Excerpt.Contains("evil")))
            throw new Exception("Guides are missing or script markup leaked into evidence.");
        if (sources.Any(source => new Uri(source.Url).Host is not ("strategywiki.org" or "www.mariowiki.com" or "en.wikibooks.org"))) throw new Exception("Untrusted source URL accepted.");
        if (GameWebGuides.SearchTerms("description without search line", "Mario Party 4") != "") throw new Exception("Unstructured private text became a query.");
        if (GameWebGuides.SearchTerms("SCÈNE: menu ; RECHERCHE: Mario Party 4 memory card", "Mario Party 4") != "memory card") throw new Exception("Inline search marker lost.");
        if (GameWebGuides.MatchesScene("Submissions", "Mario Party 4 memory card submissions", "Mario Party 4", "memory card") ||
            GameWebGuides.MatchesScene("Slow and Steady (Mario Party 6)", "Mario Party 4 memory card", "Mario Party 4", "memory card") ||
            GameWebGuides.MatchesScene("Mario Party 4", "A party game with many minigames and boards.", "Mario Party 4", "memory card") ||
            !GameWebGuides.MatchesScene("Booksquirm", "Booksquirm is a Mario Party 4 minigame.", "Mario Party 4", "Booksquirm"))
            throw new Exception("Unrelated guide accepted or relevant minigame rejected.");
        using var partial = new HttpClient(new Pages(failMario: true));
        if ((await new GameWebGuides(partial).FindAsync("Mario Party 4", "RECHERCHE: Booksquirm", default)).Count != 2)
            throw new Exception("One failed site discarded the other guide.");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { await guides.FindAsync("Mario Party 4", "", stopped.Token); throw new Exception("Web cancellation ignored."); }
        catch (OperationCanceledException) { }
        using var oversized = new HttpClient(new Pages(oversized: true));
        if ((await new GameWebGuides(oversized).FindAsync("Mario Party 4", "", default)).Count != 0) throw new Exception("Oversized guide accepted.");
        Console.WriteLine("PASS web guides: trusted URLs, relevant plain text, partial failure, size limit and cancellation");
    }
    private sealed class Pages(bool failMario = false, bool oversized = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get || request.Content is not null) throw new Exception("A screenshot or request body was uploaded.");
            if (failMario && request.RequestUri!.Host == "www.mariowiki.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json = request.RequestUri!.Query.Contains("action=query")
                ? "{\"query\":{\"search\":[{\"title\":\"Mario Party 4/Booksquirm\"}]}}"
                : JsonSerializer.Serialize(new { parse = new { text = new Dictionary<string, string> { ["*"] = oversized ? new string('x', 1048577) : "<script>evil()</script><p>Booksquirm: move into the matching hole before the page falls.</p><p>Use the control stick to position your character safely.</p>" } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
