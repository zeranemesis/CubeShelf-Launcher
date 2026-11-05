using System.IO.Compression;
using System.Security.Cryptography;
using CubeShelf.Mobile;

internal static class EmbeddedModuleTests
{
    internal static async Task Run(string root)
    {
        var home=Path.Combine(root,"engine-host");Directory.CreateDirectory(home);
        var original="original dex fixture"u8.ToArray();var adapted="adapted dex fixture"u8.ToArray();
        var bridge="bridge dex fixture"u8.ToArray();
        var elf=new byte[24];new byte[]{127,69,76,70,2,1}.CopyTo(elf,0);elf[18]=183;
        // One ADD-only VCDIFF window exercises the same decoder used for real adapters.
        var patch=new byte[]{0xD6,0xC3,0xC4,0,0,0,(byte)(adapted.Length+7),(byte)adapted.Length,0,(byte)adapted.Length,2,0}
            .Concat(adapted).Concat(new byte[]{1,(byte)adapted.Length}).ToArray();
        var apk=Path.Combine(home,"engine.apk");
        using(var zip=ZipFile.Open(apk,ZipArchiveMode.Create))
        {
            using(var file=zip.CreateEntry("classes.dex").Open())file.Write(original);
            using(var file=zip.CreateEntry("lib/arm64-v8a/libmain.so").Open())file.Write(elf);
            using(var file=zip.CreateEntry("../../escape").Open())file.Write(original);
        }
        var spec=new EmbeddedEngineSpec
        {
            Id="fixture",ApkSha256=Hash(await File.ReadAllBytesAsync(apk)),ApkSize=new FileInfo(apk).Length,BridgeSha256=Hash(bridge),Revision=Hash("revision 1"u8.ToArray()),
            Code=[new(){Name="classes.dex",SourceSha256=Hash(original),Sha256=Hash(adapted),Size=adapted.Length,Patch="nativehost/patches/fixture/classes.dex.vcdiff",PatchSha256=Hash(patch)}],
            Native=[new(){Name="libmain.so",Sha256=Hash(elf),Size=elf.Length}]
        };
        var assets=new Dictionary<string,byte[]>{{"nativehost/bridges.dex",bridge},{spec.Code[0].Patch,patch}};
        var module=new EmbeddedNativeModule(home,spec,name=>new MemoryStream(assets[name]));
        var saves=Path.Combine(home,"native-games/fixture/saves");Directory.CreateDirectory(saves);
        var save=Path.Combine(saves,"progress.dat");await File.WriteAllTextAsync(save,"player save");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();await Refuse<OperationCanceledException>(()=>module.PrepareAsync(apk,cancelled.Token));
            Check(!module.IsReady,"cancelled engine is never published");
        }
        assets[spec.Code[0].Patch]=[1,2,3];
        await Refuse<InvalidDataException>(()=>module.PrepareAsync(apk));
        Check(!module.IsReady&&!Directory.EnumerateDirectories(Path.GetDirectoryName(module.DirectoryPath)!,"preparing-*").Any(),"invalid adapter is rejected and staging is cleaned");
        assets[spec.Code[0].Patch]=patch;await module.PrepareAsync(apk);
        Check(module.IsReady&&File.ReadAllBytes(Path.Combine(module.DirectoryPath,"code/classes.dex")).SequenceEqual(adapted),"checked adapter produces the exact pinned code");
        Check(!File.Exists(Path.Combine(home,"escape"))&&await File.ReadAllTextAsync(save)=="player save","unrelated ZIP entries are ignored and saves are preserved");
        Check(OperatingSystem.IsWindows()?(File.GetAttributes(Path.Combine(module.DirectoryPath,"bridges.dex"))&FileAttributes.ReadOnly)!=0
            :(File.GetUnixFileMode(Path.Combine(module.DirectoryPath,"bridges.dex"))&UnixFileMode.UserWrite)==0,"executable module is published read-only");
        spec=System.Text.Json.JsonSerializer.Deserialize<EmbeddedEngineSpec>(System.Text.Json.JsonSerializer.Serialize(spec))!;
        spec.Revision=Hash("revision 2"u8.ToArray());var next=new EmbeddedNativeModule(home,spec,name=>new MemoryStream(assets[name]));
        Check(!next.IsReady&&next.DirectoryPath!=module.DirectoryPath&&module.IsReady,"adapter updates use a new revision without altering the previous module");
        Check(next.HasPreviousVersion&&!module.HasPreviousVersion,"a new application detects a previously prepared engine without treating its current revision as an update");
        var corrupt=Path.Combine(home,"corrupt.apk");await File.WriteAllTextAsync(corrupt,"wrong source");
        await Refuse<InvalidDataException>(()=>next.PrepareAsync(corrupt));
        Check(next.HasPreviousVersion&&module.IsReady&&await File.ReadAllTextAsync(save)=="player save","a failed engine upgrade preserves the previous revision and game saves");
        await next.PrepareAsync(apk);
        Check(next.IsReady&&module.IsReady&&await File.ReadAllTextAsync(save)=="player save","a valid engine upgrade publishes its pinned revision without reinstalling game data");
        spec.Native[0].Name="../escape.so";
        try {new EmbeddedNativeModule(home,spec,_=>Stream.Null);throw new Exception("unsafe library accepted");}catch(InvalidDataException){}
        Console.WriteLine("Embedded module: integrity, cancellation, atomic publish, owned storage and revision checks passed.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static async Task Refuse<T>(Func<Task> action) where T:Exception
    {
        try {await action();throw new Exception("Expected "+typeof(T).Name);}catch(T){}
    }
}
