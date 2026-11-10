using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Mods;

namespace CubeShelf.Mobile;

public sealed class EmbeddedEngineSpec
{
    public string Id {get;set;}="";
    public string PackageName {get;set;}="";
    public string ApkSha256 {get;set;}="";
    public long ApkSize {get;set;}
    public string MainActivity {get;set;}="";
    public string Process {get;set;}="";
    public string BridgeSha256 {get;set;}="";
    public string Revision {get;set;}="";
    public List<EmbeddedEngineFile> Code {get;set;}=[];
    public List<EmbeddedEngineFile> Native {get;set;}=[];
    public List<EmbeddedEngineComponent> Components {get;set;}=[];
}

public sealed class EmbeddedEngineFile
{
    public string Name {get;set;}="";
    public string SourceSha256 {get;set;}="";
    public string Sha256 {get;set;}="";
    public long Size {get;set;}
    public string Patch {get;set;}="";
    public string PatchSha256 {get;set;}="";
}

public sealed class EmbeddedEngineComponent
{
    public string Kind {get;set;}="";
    public string Name {get;set;}="";
    public string Bridge {get;set;}="";
    public string Authority {get;set;}="";
    public string OriginalAuthority {get;set;}="";
}

/// <summary>Stages only pinned engine code; game data and saves live in another directory.</summary>
public sealed class EmbeddedNativeModule
{
    private readonly EmbeddedEngineSpec _spec;
    private readonly Func<string,Stream> _asset;
    private readonly string _root;
    public string DirectoryPath {get;}
    public bool HasPreviousVersion
    {
        get
        {
            if(!Directory.Exists(_root))return false;
            foreach(var directory in Directory.EnumerateDirectories(_root))
            {
                var name=Path.GetFileName(directory);
                if(directory==DirectoryPath||name.Length!=20||!name.All(Uri.IsHexDigit))continue;
                try
                {
                    if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
                    var marker=Path.Combine(directory,"ready");
                    if(!File.Exists(marker)||new FileInfo(marker).Length>128)continue;
                    var revision=File.ReadAllText(marker);
                    if(revision.Length==64&&revision.All(Uri.IsHexDigit)&&revision.StartsWith(name,StringComparison.Ordinal)
                        &&File.Exists(Path.Combine(directory,"source.apk"))&&File.Exists(Path.Combine(directory,"bridges.dex")))return true;
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException){}
            }
            return false;
        }
    }
    public EmbeddedNativeModule(string files,EmbeddedEngineSpec spec,Func<string,Stream> asset)
    {
        static bool Hash(string text)=>text.Length==64&&text.All(Uri.IsHexDigit);
        static bool Name(string text)=>text.Length>0&&text.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.' or '+')&&text is not "." and not "..";
        if(!Name(spec.Id)||!Hash(spec.ApkSha256)||!Hash(spec.BridgeSha256)||!Hash(spec.Revision)||spec.ApkSize is <=0 or >268435456
            ||spec.Code.Count==0||spec.Native.Count==0
            ||spec.Code.Concat(spec.Native).Any(f=>!Name(f.Name)||!Hash(f.Sha256)||f.Size is <=0 or >268435456)
            ||spec.Code.Any(f=>!Hash(f.SourceSha256)||!Hash(f.PatchSha256)||f.Patch!=$"nativehost/patches/{spec.Id}/{f.Name}.vcdiff")
            ||spec.Code.Select(f=>f.Name).Distinct().Count()!=spec.Code.Count||spec.Native.Select(f=>f.Name).Distinct().Count()!=spec.Native.Count)
            throw new InvalidDataException("Module natif intégré invalide.");
        _spec=spec;_asset=asset;
        _root=Path.GetFullPath(Path.Combine(files,"native-modules",spec.Id));
        DirectoryPath=Path.Combine(_root,spec.Revision[..20]);
    }

    public bool IsReady
    {
        get
        {
            try
            {
                if(File.ReadAllText(Path.Combine(DirectoryPath,"ready"))!=_spec.Revision)return false;
                return new FileInfo(Path.Combine(DirectoryPath,"source.apk")).Length==_spec.ApkSize
                    &&File.Exists(Path.Combine(DirectoryPath,"bridges.dex"))
                    &&_spec.Code.All(f=>new FileInfo(Path.Combine(DirectoryPath,"code",f.Name)).Length==f.Size)
                    &&_spec.Native.All(f=>new FileInfo(Path.Combine(DirectoryPath,"lib",f.Name)).Length==f.Size);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
        }
    }

    public async Task PrepareAsync(string verifiedApk,CancellationToken cancellation=default)
    {
        if(IsReady)return;
        await Verify(verifiedApk,_spec.ApkSha256,_spec.ApkSize,cancellation);
        CubeShelf.Core.Storage.DiskSpaceGuard.Require(_root,_spec.ApkSize+_spec.Native.Sum(f=>f.Size)+_spec.Code.Sum(f=>f.Size)+128L*1024*1024);
        Directory.CreateDirectory(_root);
        var stage=Path.Combine(_root,"preparing-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            Directory.CreateDirectory(Path.Combine(stage,"lib"));Directory.CreateDirectory(Path.Combine(stage,"code"));
            await Copy(new FileStream(verifiedApk,FileMode.Open,FileAccess.Read,FileShare.Read),Path.Combine(stage,"source.apk"),_spec.ApkSize,cancellation);
            using var zip=ZipFile.OpenRead(verifiedApk);
            foreach(var file in _spec.Native)
            {
                var entry=zip.GetEntry("lib/arm64-v8a/"+file.Name)??throw new InvalidDataException("Bibliothèque ARM64 absente.");
                var target=Path.Combine(stage,"lib",file.Name);
                await Copy(entry.Open(),target,file.Size,cancellation);await Verify(target,file.Sha256,file.Size,cancellation);
                using var elf=File.OpenRead(target);var header=new byte[20];await elf.ReadExactlyAsync(header,cancellation);
                if(!header.AsSpan(0,6).SequenceEqual(new byte[]{127,69,76,70,2,1})||BitConverter.ToUInt16(header,18)!=183)
                    throw new InvalidDataException("Bibliothèque du moteur non ARM64.");
            }
            foreach(var file in _spec.Code)
            {
                var entry=zip.GetEntry(file.Name)??throw new InvalidDataException("Code du moteur absent.");
                if(entry.Length>67108864)throw new InvalidDataException("Code du moteur trop volumineux.");
                var original=Path.Combine(stage,"original.dex");
                await Copy(entry.Open(),original,entry.Length,cancellation);
                await Verify(original,file.SourceSha256,entry.Length,cancellation);
                var patch=Path.Combine(stage,"bridge.vcdiff");
                await Copy(_asset(file.Patch),patch,67108864,cancellation,exact:false);
                await Verify(patch,file.PatchSha256,new FileInfo(patch).Length,cancellation);
                var target=Path.Combine(stage,"code",file.Name);
                await VcdiffDecoder.ApplyAsync(original,patch,target,cancellationToken:cancellation);
                await Verify(target,file.Sha256,file.Size,cancellation);
                File.Delete(original);File.Delete(patch);
            }
            var bridge=Path.Combine(stage,"bridges.dex");
            await Copy(_asset("nativehost/bridges.dex"),bridge,8388608,cancellation,exact:false);
            await Verify(bridge,_spec.BridgeSha256,new FileInfo(bridge).Length,cancellation);
            await Verify(Path.Combine(stage,"source.apk"),_spec.ApkSha256,_spec.ApkSize,cancellation);
            await File.WriteAllTextAsync(Path.Combine(stage,"ready"),_spec.Revision,cancellation);
            cancellation.ThrowIfCancellationRequested();
            foreach(var file in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories))
                if(OperatingSystem.IsWindows())File.SetAttributes(file,FileAttributes.ReadOnly);
                else File.SetUnixFileMode(file,UnixFileMode.UserRead);
            if(Directory.Exists(DirectoryPath))Directory.Move(DirectoryPath,Path.Combine(_root,"invalid-"+Guid.NewGuid().ToString("N")));
            Directory.Move(stage,DirectoryPath);
        }
        finally
        {
            if(Directory.Exists(stage)&&Path.GetFullPath(stage).StartsWith(_root+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            {
                foreach(var file in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
                Directory.Delete(stage,true);
            }
        }
    }

    private static async Task Copy(Stream input,string file,long limit,CancellationToken cancellation,bool exact=true)
    {
        using(input)
        {
            await using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var buffer=new byte[131072];long total=0;int read;
            while((read=await input.ReadAsync(buffer,cancellation))>0)
            {
                total+=read;if(total>limit)throw new InvalidDataException("Module trop volumineux.");
                await output.WriteAsync(buffer.AsMemory(0,read),cancellation);
            }
            if(exact&&total!=limit)throw new InvalidDataException("Module incomplet.");
        }
    }

    private static async Task Verify(string file,string expected,long size,CancellationToken cancellation)
    {
        if(new FileInfo(file).Length!=size)throw new InvalidDataException("Taille du moteur incorrecte.");
        await using var stream=File.OpenRead(file);
        if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellation)).Equals(expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’intégrité du moteur ne peut pas être confirmée.");
    }
}
