using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Updates;

internal static class ManagedPortChecks
{
    internal static async Task Run(string root)
    {
        void Check(bool good,string text){if(!good)throw new Exception(text);Console.WriteLine("OK "+text);}
        var port=new NativePort{Id="managed-fixture",Title="Fixture",Repository="owner/project",ReleaseTag="v1.0",Assets=[]};
        var executable=new byte[256];
        BitConverter.GetBytes((ushort)0x5a4d).CopyTo(executable,0);BitConverter.GetBytes(64).CopyTo(executable,0x3c);
        BitConverter.GetBytes(0x00004550).CopyTo(executable,64);
        BitConverter.GetBytes((ushort)(NativePortAsset.CurrentRuntimeIdentifier.EndsWith("arm64")?0xaa64:0x8664)).CopyTo(executable,68);
        BitConverter.GetBytes((ushort)2).CopyTo(executable,86);BitConverter.GetBytes((ushort)0x20b).CopyTo(executable,88);
        BitConverter.GetBytes((ushort)2).CopyTo(executable,156);
        byte[] Package(string config,bool ambiguous=false)
        {
            using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
            {
                using(var entry=zip.CreateEntry("game.exe").Open())entry.Write(executable);
                using(var entry=zip.CreateEntry("config.json").Open())entry.Write(System.Text.Encoding.UTF8.GetBytes(config));
                using(var entry=zip.CreateEntry("updater.exe").Open())entry.Write(executable);
                if(ambiguous){using var entry=zip.CreateEntry("another-game.exe").Open();entry.Write(executable);}
            }
            return output.ToArray();
        }
        var bytes=Package("shipped default");
        NativePortAsset Asset()=>new(){Name="game-v"+port.ReleaseTag.TrimStart('v')+"-windows-x64.zip",Url=port.Homepage+"/releases/download/"+port.ReleaseTag+"/game.zip",
            Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),Size=bytes.Length,RuntimeIdentifiers=[NativePortAsset.CurrentRuntimeIdentifier]};
        var asset=Asset();port.Assets=[asset];
        using(var latest=JsonDocument.Parse(JsonSerializer.Serialize(new{tag_name="v1.1",draft=false,prerelease=false,assets=new[]{new{name="game-v1.1-windows-x64.zip",size=32,digest="sha256:"+new string('a',64),browser_download_url=port.Homepage+"/releases/download/v1.1/game-v1.1-windows-x64.zip"}}})))
        {
            var candidate=NativePortUpdateService.Parse(port,latest.RootElement);
            Check(candidate is {ReleaseTag:"v1.1",Assets.Count:1}&&candidate.Assets[0].Sha256==new string('a',64),"upstream updates preserve a known platform package family and require a digest");
            asset.ExecutableRelativePath="game-1.0/plugins-11.0/GameLauncher.exe";
            var moved=NativePortUpdateService.Parse(port,latest.RootElement);
            Check(moved?.Assets[0].ExecutableRelativePath=="game-1.1/plugins-11.0/GameLauncher.exe",
                "updates follow a versioned archive directory without selecting a different launcher or touching unrelated versions");
        }
        foreach(var json in new[]{
            "{\"tag_name\":\"v1.1\",\"prerelease\":true,\"assets\":[]}",
            "{\"tag_name\":\"v1.1\",\"assets\":[{\"name\":\"game-v1.1-windows-x64.zip\",\"digest\":null}]}",
            JsonSerializer.Serialize(new{tag_name="v1.1",assets=new[]{new{name="game-v1.1-windows-x64.zip",size=1,digest="sha256:"+new string('a',64),browser_download_url="https://example.invalid/game.zip"}}})})
        {using var invalid=JsonDocument.Parse(json);Check(NativePortUpdateService.Parse(port,invalid.RootElement) is null,"preview, unverified or foreign packages are never offered as updates");}
        static object Release(string tag,string name,bool preview=false,bool draft=false)=>
            new {tag_name=tag,prerelease=preview,draft,assets=new[]{new{name,size=32,digest="sha256:"+new string('a',64),
                browser_download_url=(tag=="v1.4.0"?"https://example.invalid/":"https://github.com/owner/project/")+"releases/download/"+tag+"/"+name}}};
        var releases=new[]{
            Release("v2.0.0","port-2.0.0-windows-x64.zip"),
            Release("v1.9.0","port-1.9.0-eu-android-arm64.apk"),
            Release("v1.8.0-beta","port-1.8.0-beta-us-android-arm64.apk",preview:true),
            Release("v1.7.0","port-1.7.0-us-android-arm64.apk",draft:true),
            Release("v1.6.0","port-1.3.0-us-android-arm64.apk"),
            Release("v1.5.0","port-1.5.0-us-android-arm64.apk"),
            Release("v1.4.0","port-1.4.0-us-android-arm64.apk")};
        using var androidHttp=new HttpClient(new Bytes(()=>JsonSerializer.SerializeToUtf8Bytes(releases)));
        using var androidUpdates=new NativePortUpdateService(androidHttp);
        var android=await androidUpdates.CheckAndroidAsync(port.Repository,"v1.3.0","port-1.3.0-us-android-arm64.apk");
        Check(android is {Version:"v1.5.0",Apk:{Size:32} apk}&&apk.Sha256==new string('a',64)
            &&apk.Url==port.Homepage+"/releases/download/v1.5.0/port-1.5.0-us-android-arm64.apk",
            "Android selects the newest verified APK matching the known region and stable channel");
        using var foreignHttp=new HttpClient(new Bytes(()=>JsonSerializer.SerializeToUtf8Bytes(new[]{releases[^1]})));
        using var foreignUpdates=new NativePortUpdateService(foreignHttp);
        Check(await foreignUpdates.CheckAndroidAsync(port.Repository,"v1.3.0","port-1.3.0-us-android-arm64.apk") is {Version:"v1.4.0",Apk:null},
            "an APK from a foreign host is detectable but cannot enter the Android installer");
        Check(await androidUpdates.CheckAndroidAsync(port.Repository,"v1.3.0","port-1.3.0-us-android-arm64.apk","v1.5.0") is null,
            "a newer standalone Android installation is not offered an older package");
        Check(NativePortUpdateService.IsNewerRelease("v1.0.3-android-r10","v1.0.3-android-r9")
            &&NativePortUpdateService.IsNewerRelease("v9.0.2P3","v9.0.2P2")
            &&NativePortUpdateService.IsNewerRelease("B0.4","B0.3")
            &&NativePortUpdateService.IsNewerRelease("v0.3","B0.3")
            &&NativePortUpdateService.IsNewerRelease("v0.2.0","v0.2.0-beta")
            &&!NativePortUpdateService.IsNewerRelease("v1.2","v1.2.0"),
            "release ordering handles Android revisions, prereleases and equivalent numeric tags");
        if(!OperatingSystem.IsWindows())return;
        var directory=Path.Combine(root,"managed-ports");var paths=new Paths(directory);var library=new NativePortLibrary(paths);
        using var http=new HttpClient(new Bytes(()=>bytes));
        asset.ExecutableRelativePath="game.exe";
        var first=await library.InstallManagedAsync(port,asset,null,default,http);
        Check(first.Executable is not null&&library.IsConfigured(port)&&!library.CanRollback(port),"a unique Windows game program is associated without running it or its updater");
        var old=first.Executable!;var oldRoot=Path.GetDirectoryName(old)!;
        File.WriteAllText(Path.Combine(oldRoot,"config.json"),"player preferences");Directory.CreateDirectory(Path.Combine(oldRoot,"saves"));
        File.WriteAllText(Path.Combine(oldRoot,"saves/progress.bin"),"player progress");
        port.ReleaseTag="v1.1";bytes=Package("new default");asset=Asset();port.Assets=[asset];
        var second=await library.InstallManagedAsync(port,asset,null,default,http);
        var newRoot=Path.GetDirectoryName(second.Executable!)!;
        Check(File.ReadAllText(Path.Combine(newRoot,"config.json"))=="player preferences"&&File.ReadAllText(Path.Combine(newRoot,"saves/progress.bin"))=="player progress",
            "updates migrate modified settings and recognized save folders instead of replacing them with defaults");
        Check(File.Exists(old)&&library.CanRollback(port)&&library.Installation(port)?.Version=="v1.1","updates keep the previous engine and record the installed package version");
        var configured=library.Executable(port);
        bytes=Package("ambiguous",true);asset=Asset();
        var ambiguous=await library.InstallManagedAsync(port,asset,null,default,http);
        Check(ambiguous.Executable is null&&library.Executable(port)==configured,"ambiguous packages preserve the active engine and require an explicit program choice");
        Check(NativePortLibrary.FindWindowsGameExecutable(ambiguous.Directory,"game.exe") is {} selected&&Path.GetFileName(selected)=="game.exe",
            "a pinned executable contract selects the game when an archive also contains compiler or helper programs");
        try{NativePortLibrary.FindWindowsGameExecutable(ambiguous.Directory,"missing.exe");throw new Exception("Missing declared program accepted");}catch(InvalidDataException){}
        bytes=Package("bad digest");asset=Asset();asset.Sha256=new string('0',64);
        try{await library.InstallManagedAsync(port,asset,null,default,http);throw new Exception("Bad checksum accepted");}catch(CryptographicException){}
        Check(library.Executable(port)==configured,"a failed update cannot change the active engine");
        library.Rollback(port);
        Check(library.Executable(port)==old&&File.Exists(Path.Combine(newRoot,"saves/progress.bin")),"rollback changes the selected engine without deleting either version's saves");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();try{await library.InstallManagedAsync(port,Asset(),null,cancellation.Token,http);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        }
        Check(library.Executable(port)==old,"cancelled upgrades preserve the selected version");
        var stale=new NativePortLibrary(paths);var other=new NativePort{Id="other"};stale.Configure(other,old);
        library.Configure(port,old);
        Check(new NativePortLibrary(paths).Executable(other)==old,"concurrent library instances merge associations instead of dropping another game");
    }
    private sealed class Bytes(Func<byte[]> bytes):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes())});}
    private sealed class Paths(string root):IPlatformPaths
    {public string DataDirectory=>root;public string ConfigurationDirectory=>root;public string CacheDirectory=>Path.Combine(root,"cache");public string DownloadHistoryFile=>Path.Combine(root,"downloads.json");}
}
