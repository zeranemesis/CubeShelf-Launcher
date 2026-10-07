using System.Text;
using System.Text.RegularExpressions;

namespace CubeShelf.Core.Social;

/// <summary>Where a pasted link comes from, which decides how it is turned into the file itself.</summary>
public enum ShareLinkProvider
{
    /// <summary>Not a service we recognise: used as it is.</summary>
    Direct = 0,
    Nextcloud = 1,
    Dropbox = 2,
    OneDrive = 3,
    SharePoint = 4,
    GoogleDrive = 5
}

/// <param name="Candidates">
/// Addresses that may serve the file, best guess first. Several, because a share link does not
/// say whether it points at the file or at its folder, and each service spells the two
/// differently. The self-test tries them and keeps the one that actually returns our document.
/// </param>
public sealed record ShareLinkConversion(
    ShareLinkProvider Provider,
    string Pasted,
    IReadOnlyList<string> Candidates)
{
    public string Primary => Candidates[0];

    /// <summary>Whether the address that will be tried first differs from what was pasted.</summary>
    public bool Changed => !string.Equals(Primary, Pasted, StringComparison.Ordinal);
}

/// <summary>
/// Turns the link a sync service gives when you click "share" into an address that serves the
/// file itself.
///
/// Every service hands out a page meant for a person -- a preview, a viewer, a "download"
/// button -- and a program reading it gets HTML. Each has its own way of asking for the bytes
/// instead, and that is knowledge a user should not need: they paste what the service gave
/// them, and this does the rest. Nothing here is trusted, though. The result is only a set of
/// guesses, and the self-test decides which one, if any, really serves our document.
/// </summary>
public static class ShareLink
{
    private static readonly Regex UrlInText = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NextcloudShare = new(@"^(?<base>(?:/[^?#]*?)?(?:/index\.php)?/s/(?<token>[A-Za-z0-9]{8,64}))(?<download>/download)?/?$", RegexOptions.CultureInvariant);
    private static readonly Regex GoogleFileId = new(@"/file/d/(?<id>[A-Za-z0-9_-]{10,})", RegexOptions.CultureInvariant);
    private static readonly Regex GoogleIdParameter = new(@"(?:^|[?&])id=(?<id>[A-Za-z0-9_-]{10,})", RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds a link in <paramref name="pasted"/> -- a whole chat message is fine -- and works out
    /// the addresses that may serve <paramref name="fileName"/> through it.
    /// </summary>
    public static bool TryConvert(
        string? pasted,
        string fileName,
        out ShareLinkConversion? conversion,
        out string error)
    {
        conversion = null;
        error = "";

        var match = UrlInText.Match(pasted ?? "");
        if (!match.Success)
        {
            error = "Aucun lien trouvé. Colle le lien de partage que ton service t’a donné.";
            return false;
        }

        // Chat apps and mail clients glue punctuation to the end of a link.
        var text = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '!', '?', '»');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            error = "Ce lien est illisible.";
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Le lien doit commencer par https:// : sans chiffrement, n’importe qui sur le chemin pourrait le remplacer.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var file = Uri.EscapeDataString(string.IsNullOrWhiteSpace(fileName) ? SyncedFolderTarget.DefaultFileName : fileName);

        ShareLinkConversion? found;
        if (host is "drive.google.com" or "docs.google.com")
        {
            if (!Google(uri, text, out found, out error)) return false;
        }
        else if (host is "dropbox.com" or "www.dropbox.com")
        {
            if (!Dropbox(uri, text, out found, out error)) return false;
        }
        else if (host is "1drv.ms" or "onedrive.live.com")
            found = OneDrive(text, file);
        else if (host.EndsWith(".sharepoint.com", StringComparison.Ordinal))
            found = new(ShareLinkProvider.SharePoint, text, new[] { WithQuery(uri, "download", "1") });
        else if (NextcloudShare.Match(uri.AbsolutePath) is { Success: true } share)
            found = Nextcloud(uri, text, share, file);
        else
            found = new(ShareLinkProvider.Direct, text, new[] { text });

        // What was pasted is always worth a last try: a service we mistook for another, or a
        // link that already was the file, still works.
        conversion = found! with
        {
            Candidates = found!.Candidates.Append(text).Distinct(StringComparer.Ordinal).ToArray()
        };
        return true;
    }

    /// <summary>
    /// Nextcloud and ownCloud share links all end in /s/&lt;token&gt;, on whatever host the
    /// instance lives. A link to the file wants /download; a link to its folder wants the file
    /// named, and newer servers also offer it over public WebDAV.
    /// </summary>
    private static ShareLinkConversion Nextcloud(Uri uri, string pasted, Match share, string file)
    {
        var root = uri.GetLeftPart(UriPartial.Authority) + share.Groups["base"].Value;
        var token = share.Groups["token"].Value;
        var prefix = share.Groups["base"].Value[..^("/s/".Length + token.Length)];
        var origin = uri.GetLeftPart(UriPartial.Authority) + (prefix.EndsWith("/index.php", StringComparison.Ordinal)
            ? prefix[..^"/index.php".Length]
            : prefix);

        return new(ShareLinkProvider.Nextcloud, pasted, new[]
        {
            root + "/download",
            root + "/download?path=%2F&files=" + file,
            origin + "/public.php/dav/files/" + token + "/" + file
        });
    }

    /// <summary>
    /// Dropbox serves a preview page unless dl=1 is asked for, and moves the file itself to
    /// dl.dropboxusercontent.com. A folder link cannot name one file without its API.
    /// </summary>
    private static bool Dropbox(Uri uri, string pasted, out ShareLinkConversion? conversion, out string error)
    {
        conversion = null;
        error = "";
        var path = uri.AbsolutePath;

        if (path.StartsWith("/scl/fo/", StringComparison.Ordinal) || path.StartsWith("/sh/", StringComparison.Ordinal))
        {
            error = "C’est le lien d’un dossier Dropbox. Partage le fichier cubeshelf-presence.json lui-même : clic droit sur le fichier, « Copier le lien Dropbox ».";
            return false;
        }

        var direct = WithQuery(uri, "dl", "1");
        var content = new UriBuilder(WithoutQuery(uri, "dl")) { Host = "dl.dropboxusercontent.com" }.Uri.AbsoluteUri;
        conversion = new(ShareLinkProvider.Dropbox, pasted, new[] { direct, content });
        return true;
    }

    /// <summary>
    /// OneDrive personal links are read through its sharing API, which takes the link itself,
    /// encoded, and answers with the file -- or, for a shared folder, with the file named inside.
    /// </summary>
    private static ShareLinkConversion OneDrive(string pasted, string file)
    {
        var encoded = "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pasted))
            .TrimEnd('=').Replace('/', '_').Replace('+', '-');
        var shares = "https://api.onedrive.com/v1.0/shares/" + encoded;
        return new(ShareLinkProvider.OneDrive, pasted, new[]
        {
            shares + "/root/content",
            shares + "/root:/" + file + ":/content"
        });
    }

    /// <summary>Google Drive serves a viewer page; the file itself is behind its download endpoint.</summary>
    private static bool Google(Uri uri, string pasted, out ShareLinkConversion? conversion, out string error)
    {
        conversion = null;
        error = "";

        if (uri.AbsolutePath.Contains("/folders/", StringComparison.Ordinal))
        {
            error = "C’est le lien d’un dossier Google Drive. Partage le fichier cubeshelf-presence.json lui-même.";
            return false;
        }

        var id = GoogleFileId.Match(uri.AbsolutePath) is { Success: true } path
            ? path.Groups["id"].Value
            : GoogleIdParameter.Match(uri.Query) is { Success: true } query
                ? query.Groups["id"].Value
                : "";

        if (id.Length == 0)
        {
            error = "Ce lien Google Drive ne désigne pas de fichier. Utilise « Copier le lien » sur le fichier lui-même.";
            return false;
        }

        conversion = new(ShareLinkProvider.GoogleDrive, pasted, new[]
        {
            "https://drive.usercontent.google.com/download?id=" + id + "&export=download&confirm=t",
            "https://drive.google.com/uc?export=download&id=" + id
        });
        return true;
    }

    private static string WithQuery(Uri uri, string name, string value)
    {
        var pairs = ParseQuery(uri.Query)
            .Where(pair => !pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Append(new KeyValuePair<string, string>(name, value));
        return new UriBuilder(uri) { Query = string.Join('&', pairs.Select(Format)) }.Uri.AbsoluteUri;
    }

    private static Uri WithoutQuery(Uri uri, string name)
    {
        var pairs = ParseQuery(uri.Query)
            .Where(pair => !pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new UriBuilder(uri) { Query = string.Join('&', pairs.Select(Format)) }.Uri;
    }

    private static string Format(KeyValuePair<string, string> pair) =>
        pair.Value.Length == 0 ? pair.Key : pair.Key + "=" + pair.Value;

    /// <summary>Keeps every parameter exactly as written: rlkey and friends are opaque tokens.</summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            yield return equals < 0
                ? new(part, "")
                : new(part[..equals], part[(equals + 1)..]);
        }
    }
}
