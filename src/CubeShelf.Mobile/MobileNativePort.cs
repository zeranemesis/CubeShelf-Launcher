using System.Text.Json;

namespace CubeShelf.Mobile;

public sealed class MobileNativePort
{
    public string Id { get; set; } = "";
    public string ShelfConsole { get; set; } = "n64";
    public int Year { get; set; }
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Version { get; set; } = "";
    public string PackageName { get; set; } = "";
    public long VersionCode { get; set; }
    public int MinimumSdk { get; set; }
    public string[] Abis { get; set; } = [];
    public bool RequiresVulkan12 { get; set; }
    public bool RequiresVulkan13 { get; set; }
    public bool RequiresGles3 { get; set; }
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string ApkSha256 { get; set; } = "";
    public long ApkSize { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public string Spine { get; set; } = "";
    public string Right { get; set; } = "";
    public string Top { get; set; } = "";
    public string Bottom { get; set; } = "";
    public string Media { get; set; } = "";

    public void Validate()
    {
        static bool Hash(string hash) => hash.Length==64 && hash.All(Uri.IsHexDigit);
        if(!CubeShelf.Core.Library.ShelfProduct.All.Any(product => product.Id == ShelfConsole)
            || Id.Length is <1 or >80 || Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')
            || Repository.Split('/') is not [var owner,var repo] || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
            || Name.Length==0 || Name!=Path.GetFileName(Name) || Name.Contains('\\')
            || !(Name.EndsWith(".apk",StringComparison.OrdinalIgnoreCase)||Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
            || !Url.StartsWith("https://github.com/"+Repository+"/releases/download/",StringComparison.Ordinal)
            || !Uri.TryCreate(Url,UriKind.Absolute,out var uri) || uri.Host!="github.com" || uri.UserInfo.Length>0
            || uri.AbsolutePath.Split('/').Last()!=Uri.EscapeDataString(Name)
            || Size is <=0 or >268435456 || ApkSize is <=0 or >268435456 || !Hash(Sha256) || !Hash(ApkSha256)
            || PackageName=="io.github.zeranemesis.cubeshelf" || PackageName.Split('.').Length<2
            || PackageName.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='.'&&c!='_') || VersionCode<=0 || MinimumSdk<24 || Abis.Length==0)
            throw new InvalidDataException("Entrée de port Android invalide.");
    }

    public static MobileNativePort[] Load(Stream input)
    {
        var ports=JsonSerializer.Deserialize<MobileNativePort[]>(input)??[];
        var ids=new HashSet<string>();var packages=new HashSet<string>();
        foreach(var port in ports)
        {
            port.Validate();
            if(!ids.Add(port.Id)||!packages.Add(port.PackageName))throw new InvalidDataException("Port Android dupliqué.");
        }
        return ports;
    }
}

public interface IMobileNativePorts
{
    IReadOnlyList<MobileNativePort> Catalog { get; }
    bool IsInstalled(string id);
    bool IsUpdateAvailable(string id);
    bool IsPackageReady(string id);
    bool IsEmbedded(string id) => false;
    string? CompatibilityError(string id);
    Task<string> PrepareAsync(string id, CancellationToken cancellation = default);
    void Launch(string id);
}
