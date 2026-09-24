using System.Net;
using System.Net.Http.Headers;

namespace CubeShelf.Core.Social;

/// <summary>What one HTTP read of a presence document produced.</summary>
internal sealed record PresenceReadResult(
    string? Json,
    string? ETag,
    bool NotModified = false,
    bool NotPublished = false,
    bool IsConnectivityFailure = false,
    string? Error = null)
{
    public bool Ok => Json is not null;
}

/// <summary>
/// One capped, conditional GET of a presence document.
///
/// Shared by the friends poll and by the self-test that checks our own published address,
/// because they are the same operation pointed at different URLs, and the parts that are easy
/// to get wrong -- 304 before any success check, counting the body rather than believing
/// Content-Length -- should only exist once.
/// </summary>
internal static class PresenceDocumentReader
{
    public static async Task<PresenceReadResult> ReadAsync(
        HttpClient http,
        Uri address,
        string? knownETag,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);

            // A hand-edited ETag would make ParseAdd throw. Omitting the header costs one full
            // download; failing the read would cost the friend entirely.
            if (!string.IsNullOrWhiteSpace(knownETag) &&
                EntityTagHeaderValue.TryParse(knownETag, out var tag))
                request.Headers.IfNoneMatch.Add(tag);

            using var response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // Before any success check: 304 is not 2xx, so EnsureSuccessStatusCode would treat
            // the cheapest possible answer as a failure.
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new(null, knownETag, NotModified: true);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return new(null, null, NotPublished: true,
                    Error: "Aucun document de présence à cette adresse.");

            if (!response.IsSuccessStatusCode)
                return new(null, null,
                    IsConnectivityFailure: (int)response.StatusCode >= 500,
                    Error: $"L’adresse a répondu {(int)response.StatusCode}.");

            if (response.Content.Headers.ContentLength > PresencePolicy.MaximumDocumentBytes)
                return new(null, null, Error: "Le document de présence est trop volumineux.");

            var json = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
            return json is null
                ? new(null, null, Error: "Le document de présence est trop volumineux.")
                : new(json, response.Headers.ETag?.ToString());
        }
        catch (HttpRequestException exception)
        {
            return new(null, null, IsConnectivityFailure: true, Error: exception.Message);
        }
    }

    /// <summary>
    /// Counts the body as it arrives, because Content-Length may be absent or simply untrue.
    /// Returns null once the cap is passed.
    /// </summary>
    private static async Task<string?> ReadCappedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > PresencePolicy.MaximumDocumentBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
