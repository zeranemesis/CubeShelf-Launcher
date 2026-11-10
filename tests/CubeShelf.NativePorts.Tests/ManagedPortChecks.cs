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
        }
        foreach(var json in new[]{
            "{\"tag_name\":\"v1.1\",\"prerelease\":true,\"assets\":[]}",
            "{\"tag_name\":\"v1.1\",\"assets\":[{\"name\":\"game-v1.1-windows-x64.zip\",\"digest\":null}]}",
            JsonSerializer.Serialize(new{tag_name="v1.1",assets=new[]{new{name="game-v1.1-windows-x64.zip",size=1,digest="sha256:"+new string('a',64),browser_download_url="https://example.invalid/game.zip"}}})})
        {using var invalid=JsonDocument.Parse(json);Check(NativePortUpdateService.Parse(port,invalid.RootElement) is null,"preview, unverified or foreign packages are never offered as updates");}
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
