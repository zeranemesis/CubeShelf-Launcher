using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

var root = Path.Combine(Path.GetTempPath(), "native-shelves-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var catalogs = new[] { "gamecube", "wii", "switch" }.ToDictionary(id => id, id => NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Catalogs",id+".json")));
    Check(catalogs["gamecube"].Count >= 19 && catalogs["wii"].Count >= 13 && catalogs["switch"].Count >= 20, "all three catalogues load with verified source references");
    Check(catalogs.Values.SelectMany(p=>p).All(p => p.ReadmeUrl.StartsWith(p.Homepage+"/") && p.SourceCommit.Length==40 && p.VerifiedAt=="2026-10-26"), "primary README provenance is retained");
    Check(catalogs.Values.SelectMany(p=>p).All(p => !p.Repository.Contains("nxnative")), "mobile-only project is not labelled as a desktop port");
    Check(ShelfProduct.Resolve("wii").CreatePaths().DataDirectory != ShelfProduct.Resolve("switch").CreatePaths().DataDirectory, "WiiShelf and SwitchShelf profiles are isolated");
    var suiteGames = new[] {
        new GameCatalogEntry {Id="legacy",Title="Legacy"},
        new GameCatalogEntry {Id="native-wii-test",Title="Wii game",ShelfConsole="wii",NativePortId="wii-test",RuntimeOwnsSetup=true,DataPreparation=GameDataPreparation.Runtime},
        new GameCatalogEntry {Id="native-switch-test",Title="Switch game",ShelfConsole="switch",NativePortId="switch-test",RuntimeOwnsSetup=true,DataPreparation=GameDataPreparation.Runtime}
    };
    suiteGames[1].IsFavorite=true;suiteGames[2].PlayCount=7;
    Check(NativePortCatalog.ForConsole(suiteGames,"wii").Single().Id=="native-wii-test"
        && NativePortCatalog.ForConsole(suiteGames,"switch").Single().Id=="native-switch-test"
        && NativePortCatalog.ForConsole(suiteGames,"gamecube").Single().Id=="legacy", "console selector scopes the existing library without mixing games");
    Check(suiteGames[1].IsFavorite && suiteGames[2].PlayCount==7, "switching console keeps favourites and statistics");
    var sameGame = new[] {
        new GameCatalogEntry { Id="first", Title="Same Game!", ShelfConsole="wii" },
        new GameCatalogEntry { Id="second", Title="Same Game", ShelfConsole="wii" },
        new GameCatalogEntry { Id="switch", Title="Same Game", ShelfConsole="switch" }
    };
    var selectedVersion = new Dictionary<string,string> { [NativePortCatalog.GameKey(sameGame[0])]="second" };
    Check(NativePortCatalog.ForConsole(sameGame,"wii",selectedVersion).Single().Id=="second"
        && NativePortCatalog.ForConsole(sameGame,"switch",selectedVersion).Single().Id=="switch",
        "alternate ports share one game card while the same title on another console stays separate");
    selectedVersion[NativePortCatalog.GameKey(sameGame[0])]="removed-port";
    Check(NativePortCatalog.ForConsole(sameGame,"wii",selectedVersion).Single().Id=="first",
        "removed selected port falls back to an available version");
    Check(DiscImageService.Inspect("",suiteGames[1]).Supported && !DiscImageService.Inspect("",suiteGames[0]).Supported,
        "native Wii setup bypasses GameCube disc validation; legacy validation remains required");
    var shipped=Path.Combine(root,"suite.json");File.WriteAllText(shipped,JsonSerializer.Serialize(suiteGames));
    var store=new GameCatalogService(shipped,Path.Combine(root,"suite-profile"));
    store.Save(suiteGames);
    var loaded=store.Load();
    Check(loaded.Single(g=>g.Id=="native-switch-test") is {ShelfConsole:"switch",RuntimeOwnsSetup:true,NativePortId:"switch-test",PlayCount:7},
        "console and native launch contracts persist through catalogue migration");
    var payload = Zip("native.exe", "placeholder, never executed");
    var port = new NativePort { Id="fixture",Title="Fixture",Repository="owner/port" };
    var asset = new NativePortAsset { Name="port.zip",Url="https://github.com/owner/port/releases/download/v1/port.zip",Size=payload.Length,Sha256=Convert.ToHexString(SHA256.HashData(payload)),RuntimeIdentifiers=[NativePortAsset.CurrentRuntimeIdentifier] };
    var paths = new TestPaths(root); var library = new NativePortLibrary(paths);
    using var http = new HttpClient(new Fixture(()=>payload));
    var extracted = await library.InstallAsync(port,asset,null,CancellationToken.None,http);
    Check(File.ReadAllText(Path.Combine(extracted,"native.exe"))=="placeholder, never executed", "verified runtime is extracted without execution");
    library.Configure(port,Path.Combine(extracted,"native.exe"));
    Check(new NativePortLibrary(paths).IsConfigured(port), "associated executable survives restart");
    var before = asset.Url; asset.Url="https://example.org/game.zip";
    Throws(()=>NativePortCatalog.ValidateAsset(port,asset), "foreign release URL rejected"); asset.Url=before;
    asset.Sha256=new string('0',64);
    await ThrowsAsync(()=>library.InstallAsync(port,asset,null,CancellationToken.None,http), "modified package rejected before extraction");
    payload=Zip("../escape.exe","bad"); asset.Size=payload.Length;asset.Sha256=Convert.ToHexString(SHA256.HashData(payload));
    await ThrowsAsync(()=>library.InstallAsync(port,asset,null,CancellationToken.None,http), "path traversal in an otherwise verified archive rejected");
    Check(File.ReadAllText(Path.Combine(extracted,"native.exe"))=="placeholder, never executed", "failed installs preserve the previous runtime");
    var file=Path.Combine(root,"duplicates.json");File.WriteAllText(file,JsonSerializer.Serialize(new[]{port,port}));
    Throws(()=>NativePortCatalog.Load(file),"duplicate catalogue IDs rejected");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    await ThrowsAsync(()=>library.InstallAsync(port,asset,null,cancelled.Token,http),"cancellation cannot activate a package");
    Console.WriteLine("18 native shelf checks passed.");
}
finally { Directory.Delete(root,true); }
static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("OK  "+text);}
static void Throws(Action action,string text){try{action();}catch(InvalidDataException){Console.WriteLine("OK  "+text);return;}throw new Exception(text);}
static async Task ThrowsAsync(Func<Task> action,string text){try{await action();}catch(Exception error)when(error is InvalidDataException or CryptographicException or OperationCanceledException){Console.WriteLine("OK  "+text);return;}throw new Exception(text);}
static byte[] Zip(string name,string data){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){using var w=new StreamWriter(z.CreateEntry(name).Open());w.Write(data);}return m.ToArray();}
sealed class Fixture(Func<byte[]> content):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(content())});}}
sealed class TestPaths(string root):IPlatformPaths{public string DataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"Cache");public string ConfigurationDirectory=>root;public string DownloadHistoryFile=>Path.Combine(root,"history.json");}
