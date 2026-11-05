using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubeShelf.Core.Library;
using CubeShelf.Core.Releases;

namespace CubeShelf.Core.Updates;

/// <summary>Offers known package families only when the upstream release publishes a SHA-256.</summary>
public sealed class NativePortUpdateService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _owns;
    public NativePortUpdateService(HttpClient? http=null)
    {_owns=http is null;_http=http??new HttpClient{Timeout=TimeSpan.FromSeconds(30)};}

    public async Task<NativePort?> CheckAsync(NativePort port,CancellationToken cancellation=default)
    {
        if(!NativePortCatalog.IsSafeRepository(port))
            throw new InvalidDataException("Dépôt amont invalide.");
        var gitlab=port.RepositoryHost=="gitlab.com";
        using var request=new HttpRequestMessage(HttpMethod.Get,gitlab
            ?$"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString(port.Repository)}/releases/permalink/latest"
            :$"https://api.github.com/repos/{port.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("DecompShelf-native-updates/1.0");request.Headers.Accept.ParseAdd("application/json");
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        await using var input=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer=new MemoryStream();var block=new byte[16384];int count;
        while((count=await input.ReadAsync(block,cancellation).ConfigureAwait(false))>0)
        {if(buffer.Length+count>4*1024*1024)throw new InvalidDataException("Réponse du dépôt trop volumineuse.");buffer.Write(block,0,count);}
        using var document=JsonDocument.Parse(buffer.ToArray());
        return gitlab?await ParseGitLabAsync(port,document.RootElement,cancellation).ConfigureAwait(false):Parse(port,document.RootElement);
    }

    private async Task<NativePort?> ParseGitLabAsync(NativePort port,JsonElement release,CancellationToken cancellation)
    {
        var version=release.GetProperty("tag_name").GetString()??"";
        if(version.Length is 0 or >160||release.TryGetProperty("upcoming_release",out var upcoming)&&upcoming.GetBoolean()
            ||Regex.IsMatch(version,@"(?:^|[-_.])(alpha|beta|rc|preview|nightly)(?:[-_.0-9]|$)",RegexOptions.IgnoreCase))return null;
        var description=release.GetProperty("description").GetString()??"";
        // Only project-scoped Markdown uploads with an unambiguous published checksum are supported.
        var checksums=Regex.Matches(description,@"(?m)^(?:-[ \t]*`([a-fA-F0-9]{64})`[ \t]+|([a-fA-F0-9]{64})[ \t]{2,}\*?)([^\r\n]+)")
            .GroupBy(m=>m.Groups[3].Value.Trim().Replace(' ','_')).Where(g=>g.Count()==1)
            .ToDictionary(g=>g.Key,g=>g.Single().Groups[1].Success?g.Single().Groups[1].Value:g.Single().Groups[2].Value);
        var assets=new List<object>();
        foreach(Match link in Regex.Matches(description,@"\]\(/uploads/([a-fA-F0-9]{32})/([^/()\r\n]+)\)"))
        {
            var name=Uri.UnescapeDataString(link.Groups[2].Value);
            if(!checksums.TryGetValue(name,out var hash)||!port.Assets.Any(a=>a.MatchesCurrentPlatform&&ReleaseAssetPattern.Matches(name,Pattern(port,a))))continue;
            var asset=new NativePortAsset{Name=name,Url=$"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString(port.Repository)}/uploads/{link.Groups[1].Value}/{Uri.EscapeDataString(name)}",Sha256=hash,Size=1};
            try{NativePortCatalog.ValidateAsset(port,asset);}catch(InvalidDataException){continue;}
            using var request=new HttpRequestMessage(HttpMethod.Head,asset.Url);
            request.Headers.UserAgent.ParseAdd("DecompShelf-native-updates/1.0");
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode||response.RequestMessage?.RequestUri?.Scheme!="https")continue;
            asset.Size=response.Content.Headers.ContentLength??0;
            try{NativePortCatalog.ValidateAsset(port,asset);}catch(InvalidDataException){continue;}
            assets.Add(new{name,size=asset.Size,digest="sha256:"+hash,browser_download_url=asset.Url});
        }
        using var normalized=JsonDocument.Parse(JsonSerializer.Serialize(new{tag_name=version,assets}));
        return Parse(port,normalized.RootElement);
    }

    private static string Pattern(NativePort port,NativePortAsset asset)
    {
        var pattern=asset.Name;var token=port.ReleaseTag.TrimStart('v','V');
        var at=token.Length>0?pattern.IndexOf(token,StringComparison.Ordinal):-1;
        if(at<0||pattern.LastIndexOf(token,StringComparison.Ordinal)!=at)return pattern;
        var suffix=Regex.Replace(pattern[(at+token.Length)..],@"^\+g[0-9a-fA-F]{7,40}(?=[-_.])","");
        return pattern[..at]+"*"+suffix;
    }

    public static NativePort? Parse(NativePort port,JsonElement release)
    {
        if(release.TryGetProperty("draft",out var draft)&&draft.GetBoolean()
            ||release.TryGetProperty("prerelease",out var preview)&&preview.GetBoolean())return null;
        var version=release.GetProperty("tag_name").GetString()??"";
        if(version.Length is 0 or >160||!release.TryGetProperty("assets",out var assets))return null;
        var candidates=new List<NativePortAsset>();
        foreach(var known in port.Assets.Where(a=>a.MatchesCurrentPlatform))
        {
            var pattern=Pattern(port,known);
            var matches=assets.EnumerateArray().Where(a=>ReleaseAssetPattern.Matches(a.GetProperty("name").GetString()??"",pattern)).Take(2).ToArray();
            if(matches.Length!=1)continue;
            var item=matches[0];var digest=item.TryGetProperty("digest",out var hash)?hash.GetString()??"":"";
            if(!digest.StartsWith("sha256:",StringComparison.Ordinal)||digest.Length!=71)continue;
            var candidate=new NativePortAsset{Name=item.GetProperty("name").GetString()??"",Url=item.GetProperty("browser_download_url").GetString()??"",
                Size=item.GetProperty("size").GetInt64(),Sha256=digest[7..],RuntimeIdentifiers=known.RuntimeIdentifiers.ToList(),ExecutableRelativePath=known.ExecutableRelativePath};
            try{NativePortCatalog.ValidateAsset(port,candidate);}catch(InvalidDataException){continue;}
            if(!candidates.Any(a=>a.Url==candidate.Url))candidates.Add(candidate);
        }
        if(candidates.Count==0)return null;
        return new NativePort{Id=port.Id,Title=port.Title,Project=port.Project,Repository=port.Repository,RepositoryHost=port.RepositoryHost,Method=port.Method,
            Notes=port.Notes,Status=port.Status,VerifiedAt=port.VerifiedAt,SourceCommit=port.SourceCommit,ReadmeUrl=port.ReadmeUrl,
            ReleaseTag=version,ReleaseUrl=port.Homepage+(port.RepositoryHost=="gitlab.com"?"/-/releases/":"/releases/tag/")+Uri.EscapeDataString(version),Platforms=port.Platforms.ToList(),Assets=candidates};
    }
    public void Dispose(){if(_owns)_http.Dispose();}
}
