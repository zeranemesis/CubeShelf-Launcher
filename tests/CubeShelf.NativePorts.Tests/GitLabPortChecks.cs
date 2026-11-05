using System.Net;
using System.Text;
using System.Text.Json;
using CubeShelf.Core.Library;
using CubeShelf.Core.Updates;

internal static class GitLabPortChecks
{
    internal static async Task Run()
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("OK "+label);}
        var hash=new string('a',64);var upload=new string('b',32);
        var port=new NativePort{Id="gitlab-fixture",RepositoryHost="gitlab.com",Repository="owner/project",ReleaseTag="v1.0",
            Assets=[new(){Name="Power_Moon-1.0-windows.zip",RuntimeIdentifiers=[NativePortAsset.CurrentRuntimeIdentifier],ExecutableRelativePath="Power Moon/game.exe"}]};
        var description=$"[Download](/uploads/{upload}/Power_Moon-1.1-windows.zip)\n- `{hash}` Power Moon-1.1-windows.zip";
        string Release(string text,string tag="v1.1",bool upcoming=false)=>JsonSerializer.Serialize(new{tag_name=tag,description=text,upcoming_release=upcoming});
        var json=Release(description);var heads=0;
        using var http=new HttpClient(new Handler(request=>
        {
            var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request};
            if(request.Method==HttpMethod.Head)
            {
                heads++;Check(request.RequestUri!.AbsoluteUri.StartsWith("https://gitlab.com/api/v4/projects/owner%2Fproject/uploads/",StringComparison.Ordinal),"GitLab file metadata is queried only within its declared project");
                response.Content=new ByteArrayContent([]);response.Content.Headers.ContentLength=321;
            }
            else response.Content=new StringContent(json,Encoding.UTF8,"application/json");
            return response;
        }));
        using var service=new NativePortUpdateService(http);
        var candidate=await service.CheckAsync(port);
        Check(candidate is {RepositoryHost:"gitlab.com",ReleaseTag:"v1.1",Assets.Count:1}
            &&candidate.Assets[0] is {Size:321,ExecutableRelativePath:"Power Moon/game.exe"} asset&&asset.Sha256==hash
            &&candidate.ReleaseUrl=="https://gitlab.com/owner/project/-/releases/v1.1","GitLab updates preserve the forge, known package, checksum and executable contract");
        foreach(var invalid in new[]{Release(description.Replace(hash,"missing")),Release(description,"v1.1-rc1"),Release(description,upcoming:true),
            Release(description.Replace("/uploads/","https://example.invalid/uploads/")),Release(description+$"\n- `{new string('c',64)}` Power Moon-1.1-windows.zip")})
        {
            json=invalid;var previous=heads;
            Check(await service.CheckAsync(port) is null&&heads==previous,"unverified, preview, ambiguous and foreign GitLab updates are ignored without a file request");
        }
        var valid=candidate!.Assets[0];NativePortCatalog.ValidateAsset(candidate,valid);
        json=Release($"[Download](/uploads/{upload}/Power_Moon-1.1-windows.zip)\n```\n{hash}  Power Moon-1.1-windows.zip\n```");
        Check(await service.CheckAsync(port) is {Assets.Count:1},"GitLab also accepts an unambiguous SHA256SUMS code block");
        foreach(var foreign in new[]{valid.Url.Replace("owner%2Fproject","other%2Fproject"),valid.Url+"?url=https://example.invalid",valid.Url.Replace("/uploads/","/repository/")})
        {
            var before=valid.Url;valid.Url=foreign;
            try{NativePortCatalog.ValidateAsset(candidate,valid);throw new Exception("Foreign GitLab attachment accepted");}catch(InvalidDataException){}finally{valid.Url=before;}
        }
        valid.ExecutableRelativePath="../outside.exe";
        try{NativePortCatalog.ValidateAsset(candidate,valid);throw new Exception("Unsafe executable hint accepted");}catch(InvalidDataException){}
        var github=new NativePort{Repository="owner/recharged",ReleaseTag="v1.1.1",Assets=[new(){Name="recharged-1.1.1+g123456789abc-windows-x86_64.zip",RuntimeIdentifiers=[NativePortAsset.CurrentRuntimeIdentifier]}]};
        using var release=JsonDocument.Parse(JsonSerializer.Serialize(new{tag_name="v1.1.2",assets=new[]{new{name="recharged-1.1.2+gabcdef123456-windows-x86_64.zip",size=5,digest="sha256:"+hash,browser_download_url=github.Homepage+"/releases/download/v1.1.2/game.zip"}}}));
        Check(NativePortUpdateService.Parse(github,release.RootElement) is {Assets.Count:1},"Recharged updates match a new version and source hash without changing the platform family");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(response(request));}
}
