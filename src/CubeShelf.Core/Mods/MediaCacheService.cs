using System.Security.Cryptography;
using System.Text;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Mods;

public sealed class MediaCacheService
{
    private const long MaximumCacheBytes = 250L * 1024 * 1024;
    private readonly string _root;
    private readonly HttpClient _http;

    public MediaCacheService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _root = Path.Combine(paths.CacheDirectory, "Media", "GameBanana");
        _http = httpClient ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-preview");
    }

    public async Task<string> GetAsync(string url, bool thumbnail, CancellationToken cancellationToken = default)
    {
        var uri = ValidateUrl(url);
        var maximum = thumbnail ? 5L * 1024 * 1024 : 20L * 1024 * 1024;
        Directory.CreateDirectory(_root);
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp" and not ".gif") extension = ".img";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant() + extension;
        var destination = Path.Combine(_root, name);
        if (File.Exists(destination))
        {
            File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
            return destination;
        }

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        _ = ValidateUrl(response.RequestMessage?.RequestUri?.AbsoluteUri ?? "");
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidDataException("Le média GameBanana n’est pas une image.");
        if (response.Content.Headers.ContentLength > maximum)
            throw new InvalidDataException("L’image GameBanana dépasse la taille autorisée.");

        var temporary = destination + ".tmp";
        try
        {
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > maximum) throw new InvalidDataException("L’image GameBanana dépasse la taille autorisée.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }
            ValidateImageSignature(temporary);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Trim();
        return destination;
    }

    public long Clear()
    {
        if (!Directory.Exists(_root)) return 0;
        var size = Directory.EnumerateFiles(_root).Sum(path => new FileInfo(path).Length);
        Directory.Delete(_root, true);
        return size;
    }

    private void Trim()
    {
        var files = Directory.EnumerateFiles(_root).Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastAccessTimeUtc).ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files.AsEnumerable().Reverse())
        {
            if (total <= MaximumCacheBytes) break;
            total -= file.Length;
            file.Delete();
        }
    }

    private static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Adresse média GameBanana non approuvée.");
        return uri;
    }

    private static void ValidateImageSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        var png = read >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        var gif = read >= 6 && Encoding.ASCII.GetString(header[..6]) is "GIF87a" or "GIF89a";
        var webp = read >= 12 && Encoding.ASCII.GetString(header[..4]) == "RIFF" && Encoding.ASCII.GetString(header[8..12]) == "WEBP";
        if (!png && !jpeg && !gif && !webp) throw new InvalidDataException("Le média téléchargé n’a pas une signature d’image reconnue.");
    }
}
