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
    await ManagedPortChecks.Run(root);
    await GitLabPortChecks.Run();
    var expanded = ShelfProduct.All.Where(product => File.Exists(Path.Combine(AppContext.BaseDirectory, "Catalogs", product.Id + ".json")))
        .ToDictionary(product => product.Id, product => NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Catalogs", product.Id + ".json")));
    if(args.Contains("--check-upstream"))
    {
        using var updates=new CubeShelf.Core.Updates.NativePortUpdateService();
        foreach(var known in expanded.Values.SelectMany(p=>p).Where(p=>p.Repository is "zzzarathustra-dev/power-moon-recomp" or "previouslevelgames/recharged"))
        {
            var latest=await updates.CheckAsync(known);
            Check(latest is {Assets.Count:>0}&&latest.Assets.Any(a=>a.MatchesCurrentPlatform),"live upstream package metadata: "+known.Project+" · "+latest?.ReleaseTag);
        }
    }
    using var additionsAudit=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs","native-additions-2026-11-05.json")));
    var nativeAdditions=additionsAudit.RootElement.GetProperty("Desktop").EnumerateArray().ToArray();
    using var refreshAudit=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs","native-refresh-2026-11-10.json")));
    var refreshAdded=refreshAudit.RootElement.GetProperty("DesktopAdded").EnumerateArray().ToArray();
    var refreshDate=DateOnly.ParseExact(refreshAudit.RootElement.GetProperty("CheckedAt").GetString()!,"yyyy-MM-dd");
    Check(expanded.Count == ShelfProduct.All.Length && expanded.Values.Sum(ports => ports.Count) == 664+nativeAdditions.Length+refreshAdded.Length+expanded["switch2"].Count, "every console catalogue validates with safe package origins and checksums");
    Check(expanded["switch2"].Count==5&&expanded["switch2"].Select(p=>p.Title).ToHashSet().SetEquals(new[]{"Pokémon Pokopia","Mario Kart World","Donkey Kong Bananza","The Legend of Zelda: Ocarina of Time"})
        &&expanded["switch2"].All(p=>p.Assets is [{RuntimeIdentifiers:var rids} asset]&&rids.SequenceEqual(new[]{"win-x64"})
            &&(asset.ExecutableRelativePath.Length>0||asset.Name.EndsWith(".exe",StringComparison.Ordinal))
            &&p.ReadmeUrl.Contains("/blob/"+p.SourceCommit+"/")),"Switch 2 has four games, including two Ocarina of Time ports with verified Windows packages");
    Check(ShelfProduct.Resolve("switch2").CreatePaths().DataDirectory!=ShelfProduct.Resolve("switch").CreatePaths().DataDirectory
        &&expanded["switch2"].Single(p=>p.Id=="switch2-bananza").Status.Contains("Prototype"),"Switch 2 is isolated from Switch and Bananza retains its prototype status");
    Check(refreshAdded.Length==6&&refreshAdded.All(item=>expanded[item.GetProperty("Console").GetString()!].Single(p=>p.Id==item.GetProperty("Id").GetString())
        is {VerifiedAt:var verified,Assets.Count:>0,SourceCommit:var source,ReadmeUrl:var readme}
        &&DateOnly.ParseExact(verified,"yyyy-MM-dd")>=refreshDate&&readme.Contains("/blob/"+source+"/")
        &&!item.GetProperty("GameplayTested").GetBoolean()),"November additions have verified packages and pinned primary sources without claiming tested gameplay");
    Check(expanded["gamecube"].Count(p=>p.Repository=="flyngmt/Nookery")==1
        &&expanded["gamecube"].Single(p=>p.Repository=="flyngmt/Nookery").Id=="flyngmt-acgc-pc-port-animal-crossing",
        "Animal Crossing follows the Nookery rename without duplicating the port or changing its saved identity");
    Check(nativeAdditions.Length==5&&nativeAdditions.All(item=>expanded[item.GetProperty("Console").GetString()!].Single(p=>p.Id==item.GetProperty("Id").GetString())
        is {Assets.Count:>0,SourceCommit:var source,ReadmeUrl:var readme}&&readme.Contains("/blob/"+source+"/")
        &&!item.GetProperty("GameplayTested").GetBoolean()),"new native ports retain pinned primary-source provenance and do not claim gameplay validation");
    Check(expanded.Keys.All(id => ShelfProduct.Resolve(id).Id == id) && ShelfProduct.All.Select(product => product.Id).Distinct().Count() == ShelfProduct.All.Length,
        "all platform identities resolve uniquely");
    using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs","windows-refresh-audit.json")));
    var checkedAt=DateOnly.ParseExact(audit.RootElement.GetProperty("CheckedAt").GetString()!,"yyyy-MM-dd");
    using var switchAudit=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs","switch-refresh-audit.json")));
    var switchCheckedAt=DateOnly.ParseExact(switchAudit.RootElement.GetProperty("CheckedAt").GetString()!,"yyyy-MM-dd");
    foreach (var entry in expanded)
        Check(entry.Value.Count > 0 && entry.Value.All(port => port.SourceCommit.Length == 40
            && DateOnly.TryParseExact(port.VerifiedAt,"yyyy-MM-dd",out _)
            && port.ReadmeUrl.StartsWith(port.Homepage + (port.RepositoryHost=="gitlab.com"?"/-/blob/":"/blob/"))), "dated primary-source provenance: " + entry.Key);
    var additions=audit.RootElement.GetProperty("Added").EnumerateArray().ToArray();
    Check(additions.Length==218&&additions.All(item=>expanded[item.GetProperty("Console").GetString()!].Single(p=>p.Id==item.GetProperty("Id").GetString())
        is {VerifiedAt:var date,Assets:var assets,ReadmeUrl:var url,SourceCommit:var commit}&&DateOnly.ParseExact(date,"yyyy-MM-dd")>=checkedAt
        &&url.Contains("/blob/"+commit+"/")&&assets.Any(a=>a.RuntimeIdentifiers.Any(r=>r.StartsWith("win-")))),
        "new references have pinned primary documentation and verified Windows package metadata");
    var switchAdditions=switchAudit.RootElement.GetProperty("Added").EnumerateArray().ToArray();
    Check(switchAdditions.Length==8 && switchAdditions.All(item=>expanded["switch"].Single(p=>p.Id==item.GetProperty("Id").GetString())
        is {ReadmeUrl:var readme,SourceCommit:var source,Platforms:var platforms}
        && readme.Contains("/blob/"+source+"/") && platforms.Contains("Windows")),"new Switch ports retain pinned Windows primary-source evidence");
    Check(expanded["switch"].Count==34 && expanded["switch"].Select(p=>p.Title).Distinct().Count()==26
        && expanded["switch"].Single(p=>p.Repository=="Puddz/AstralChain-PC").Assets.Count==1,
        "Switch has 26 games without duplicate variants, and Astral Chain now has a Windows package");
    var odyssey=expanded["switch"].First(p=>p.Title=="Super Mario Odyssey");
    Check(odyssey is {RepositoryHost:"gitlab.com",ReleaseTag:"v0.2.1",Assets.Count:1}
        &&odyssey.Assets[0].ExecutableRelativePath=="Power Moon Recomp/PowerMoonRecomp.exe"
        &&expanded["wii"].Count(p=>p.Repository=="previouslevelgames/recharged")==1,"verified GitLab Odyssey is the default packaged variant and Recharged is not duplicated");
    Check(expanded.Values.SelectMany(ports=>ports).All(p=>!p.Repository.Equals("Mabec-games/F-ZERO-GLINUX",StringComparison.OrdinalIgnoreCase)
        &&!p.Repository.Equals("walz0/Melee-3D",StringComparison.OrdinalIgnoreCase)),"previously declined emulator and mod projects remain excluded");
    Check(expanded["gamecube"].Where(p=>p.Repository=="sonsegajp/Kirby-Air-Ride-Recompiled").All(p=>p.Assets.All(a=>!a.Name.Contains("MapEditor",StringComparison.OrdinalIgnoreCase))),
        "map editors are not offered as game runtime packages");
    var catalogs = new[] { "gamecube", "wii", "switch", "n64" }.ToDictionary(id => id, id => NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Catalogs",id+".json")));
    Check(catalogs["gamecube"].Count >= 19 && catalogs["wii"].Count >= 13 && catalogs["switch"].Count >= 20, "all four catalogues load with verified source references");
    Check(catalogs.Values.SelectMany(p=>p).All(p => p.ReadmeUrl.StartsWith(p.Homepage+"/") && p.SourceCommit.Length==40 && DateOnly.TryParseExact(p.VerifiedAt,"yyyy-MM-dd",out _)), "primary README provenance is retained");
    Check(catalogs.Values.SelectMany(p=>p).All(p => !p.Repository.Contains("nxnative")), "mobile-only project is not labelled as a desktop port");
    Check(ShelfProduct.Resolve("wii").CreatePaths().DataDirectory != ShelfProduct.Resolve("switch").CreatePaths().DataDirectory, "WiiShelf and SwitchShelf profiles are isolated");
    Check(catalogs["n64"].Select(p=>p.Title).Distinct().Count()>29
        &&new[]{"Super Mario 64","Banjo-Kazooie","Virtual Pro Wrestling 2"}.All(title=>catalogs["n64"].Any(p=>p.Title==title)), "Nintendo 64 expands while retaining existing games");
    Check(ShelfProduct.Resolve("n64").Console=="Nintendo 64", "Nintendo 64 identity resolves");
    var suiteGames = new[] {
        new GameCatalogEntry {Id="legacy",Title="Legacy"},
        new GameCatalogEntry {Id="native-wii-test",Title="Wii game",ShelfConsole="wii",NativePortId="wii-test",RuntimeOwnsSetup=true,DataPreparation=GameDataPreparation.Runtime},
        new GameCatalogEntry {Id="native-switch-test",Title="Switch game",ShelfConsole="switch",NativePortId="switch-test",RuntimeOwnsSetup=true,DataPreparation=GameDataPreparation.Runtime}
    };
    suiteGames[1].IsFavorite=true;suiteGames[2].PlayCount=7;
    suiteGames[2].Covers=[new GameCover { Front="front",Back="back",Spine="left",Right="right",Top="top",Bottom="bottom" }];
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
    Check(loaded.Single(g=>g.Id=="native-switch-test").Covers.Single() is {Right:"right",Top:"top",Bottom:"bottom"},
        "all six box faces survive catalogue cloning and persistence");
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
    Console.WriteLine("21 native shelf checks passed.");
}
finally { Directory.Delete(root,true); }
static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("OK  "+text);}
static void Throws(Action action,string text){try{action();}catch(InvalidDataException){Console.WriteLine("OK  "+text);return;}throw new Exception(text);}
static async Task ThrowsAsync(Func<Task> action,string text){try{await action();}catch(Exception error)when(error is InvalidDataException or CryptographicException or OperationCanceledException){Console.WriteLine("OK  "+text);return;}throw new Exception(text);}
static byte[] Zip(string name,string data){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){using var w=new StreamWriter(z.CreateEntry(name).Open());w.Write(data);}return m.ToArray();}
sealed class Fixture(Func<byte[]> content):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(content())});}}
sealed class TestPaths(string root):IPlatformPaths{public string DataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"Cache");public string ConfigurationDirectory=>root;public string DownloadHistoryFile=>Path.Combine(root,"history.json");}
