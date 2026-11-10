using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubeShelf.Core.Storage;

namespace CubeShelf.Core.Library;

public sealed record NativePortInstallation(string Executable,string Version,string PackageSha256,string PreviousExecutable,
    DateTimeOffset InstalledAt,Dictionary<string,string> OriginalUserFiles);
public sealed record NativePortInstallResult(string Directory,string? Executable);

public sealed partial class NativePortLibrary
{
    private static readonly SemaphoreSlim ManagedInstallGate=new(1);
    private string PortRoot(NativePort port)
    {
        if(!NativePortCatalog.IsSafeId(port.Id))throw new InvalidDataException("Identifiant de port invalide.");
        return Path.Combine(_paths.DataDirectory,"NativePorts",port.Id);
    }

    public NativePortInstallation? Installation(NativePort port)
    {
        var executable=Executable(port);var root=PortRoot(port);
        if(string.IsNullOrEmpty(executable)||!StorageFiles.Within(root,executable))return null;
        StorageFiles.CheckRoot(executable);
        for(var directory=Path.GetDirectoryName(executable);directory is not null&&StorageFiles.Within(root,directory)&&!StorageFiles.Same(root,directory);directory=Path.GetDirectoryName(directory))
        {
            var file=Path.Combine(directory,"installation.json");
            if(!File.Exists(file))continue;
            StorageFiles.CheckRoot(file);
            if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("Historique du moteur trop volumineux.");
            var result=JsonSerializer.Deserialize<NativePortInstallation>(File.ReadAllText(file));
            if(result is null||string.IsNullOrEmpty(result.Executable)||!StorageFiles.Same(result.Executable,executable)||result.OriginalUserFiles is null)return null;
            if(result.PreviousExecutable is null||result.OriginalUserFiles.Count>10000)throw new InvalidDataException("Historique du moteur invalide.");
            if(result.PreviousExecutable.Length>0)StorageFiles.RequireOwned(root,result.PreviousExecutable);
            foreach(var (relative,hash) in result.OriginalUserFiles)
            {StorageFiles.ValidateRelative(relative);if(hash is null||hash.Length!=64||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Historique du moteur invalide.");}
            return result;
        }
        return null;
    }

    public bool CanRollback(NativePort port)=>Installation(port) is {PreviousExecutable.Length:>0} installation&&File.Exists(installation.PreviousExecutable);
    public void Rollback(NativePort port)
    {
        var installation=Installation(port)??throw new InvalidOperationException("Aucune version précédente enregistrée.");
        if(installation.PreviousExecutable.Length==0||!File.Exists(installation.PreviousExecutable))throw new InvalidOperationException("La version précédente n’est plus disponible.");
        Configure(port,installation.PreviousExecutable);
    }

    public async Task<NativePortInstallResult> InstallManagedAsync(NativePort port,NativePortAsset asset,IProgress<double>? progress,
        CancellationToken cancellation=default,HttpClient? http=null)
    {
        await ManagedInstallGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
        lock(ConfigurationGate)if(File.Exists(_file))_executables=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(_file))??[];
        var previous=Executable(port);var previousInstallation=Installation(port);
        var directory=await InstallAsync(port,asset,progress,cancellation,http).ConfigureAwait(false);
        if(!OperatingSystem.IsWindows())return new(directory,null);
        if(!asset.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)&&!asset.Name.EndsWith(".tar.gz",StringComparison.OrdinalIgnoreCase))return new(directory,null);
        var executable=FindWindowsGameExecutable(directory,asset.ExecutableRelativePath);
        if(executable is null)return new(directory,null);
        // External installations remain under the player's control.
        if(previous is {Length:>0}&&File.Exists(previous)&&!StorageFiles.Within(PortRoot(port),previous))return new(directory,null);
        var currentDirectory=Path.GetDirectoryName(executable)!;
        var originals=await UserFileHashes(currentDirectory,cancellation).ConfigureAwait(false);
        if(previous is {Length:>0}&&File.Exists(previous))
        {
            var oldDirectory=Path.GetDirectoryName(previous)!;
            var files=UserFiles(oldDirectory,cancellation);
            if(files.Length>10000||files.Sum(p=>new FileInfo(StorageFiles.PathIn(oldDirectory,p)).Length)>2L*1024*1024*1024)
                throw new InvalidDataException("Les données personnelles de ce moteur dépassent la limite de migration automatique.");
            DiskSpaceGuard.Require(currentDirectory,files.Sum(p=>new FileInfo(StorageFiles.PathIn(oldDirectory,p)).Length));
            foreach(var relative in files)
            {
                cancellation.ThrowIfCancellationRequested();
                var source=StorageFiles.PathIn(oldDirectory,relative);
                if(previousInstallation?.OriginalUserFiles.TryGetValue(relative,out var original)==true
                    &&await Hash(source,cancellation).ConfigureAwait(false)==original)continue;
                var target=StorageFiles.PathIn(currentDirectory,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source,target,true);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        var installation=new NativePortInstallation(executable,port.ReleaseTag,asset.Sha256,
            previous is {Length:>0}&&File.Exists(previous)?previous:"",DateTimeOffset.UtcNow,originals);
        var record=Path.Combine(directory,"installation.json");var temporary=record+".new";
        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(installation),cancellation).ConfigureAwait(false);
        File.Move(temporary,record,true);
        cancellation.ThrowIfCancellationRequested();
        lock(ConfigurationGate)
        {
            var latest=File.Exists(_file)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(_file)):null;
            if(latest?.GetValueOrDefault(port.Id)!=previous)throw new InvalidOperationException("Le programme associé a changé pendant l’installation. L’association actuelle est conservée.");
            Configure(port,executable);
        }
        return new(directory,executable);
        }
        finally{ManagedInstallGate.Release();}
    }

    public static string? FindWindowsGameExecutable(string directory,string relative="")
    {
        StorageFiles.CheckRoot(directory);
        if(relative.Length>0)
        {
            var declared=StorageFiles.PathIn(directory,relative);
            if(!File.Exists(declared)||!IsWindowsProgram(declared))throw new InvalidDataException("Le programme natif déclaré est absent ou incompatible.");
            return declared;
        }
        var candidates=StorageFiles.Files(new(directory,GameSaveFiles.All),default).Select(relative=>StorageFiles.PathIn(directory,relative))
            .Where(path=>Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))
            .Where(path=>!Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"^(?:unins|setup|install|updat|crashreport|crashhandler|vc_redist)|(?:^|[-_. ])(?:launcher|extractor|server|tool|helper)(?:$|[-_. ])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
            .Where(IsWindowsProgram).Take(2).ToArray();
        return candidates.Length==1?candidates[0]:null;
    }

    private static bool IsWindowsProgram(string file)
    {
        StorageFiles.CheckRoot(file);
        using var stream=File.OpenRead(file);if(stream.Length<160)return false;
        using var reader=new BinaryReader(stream);
        if(reader.ReadUInt16()!=0x5a4d)return false;
        stream.Position=0x3c;var offset=reader.ReadInt32();if(offset<64||offset>stream.Length-96)return false;
        stream.Position=offset;if(reader.ReadUInt32()!=0x00004550)return false;
        var machine=reader.ReadUInt16();
        var expected=NativePortAsset.CurrentRuntimeIdentifier.EndsWith("arm64",StringComparison.Ordinal)?0xaa64:0x8664;
        if(machine!=expected)return false;
        stream.Position=offset+22;var flags=reader.ReadUInt16();
        if((flags&2)==0||(flags&0x2000)!=0||reader.ReadUInt16()!=0x20b)return false;
        stream.Position=offset+24+68;return reader.ReadUInt16() is 2 or 3;
    }

    private static string[] UserFiles(string root,CancellationToken cancellation)=>StorageFiles.Files(new(root,GameSaveFiles.All),cancellation)
        .Where(relative=>Path.GetFileName(relative)!="installation.json")
        .Where(relative=>!new[]{".exe",".dll",".so",".dylib",".apk",".dex",".bat",".cmd",".ps1",".js",".vbs"}.Contains(Path.GetExtension(relative).ToLowerInvariant()))
        .Where(relative=>StorageFiles.Recognized(relative)||relative.Split('/').SkipLast(1).Any(StorageFiles.SaveFolder)
            ||new[]{".ini",".cfg",".json"}.Contains(Path.GetExtension(relative).ToLowerInvariant())).ToArray();
    private static async Task<Dictionary<string,string>> UserFileHashes(string root,CancellationToken cancellation)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        var files=UserFiles(root,cancellation);
        if(files.Length>10000||files.Sum(p=>new FileInfo(StorageFiles.PathIn(root,p)).Length)>2L*1024*1024*1024)
            throw new InvalidDataException("Trop de données personnelles dans ce moteur : utilise la gestion du stockage avant la mise à jour.");
        foreach(var relative in files)result[relative]=await Hash(StorageFiles.PathIn(root,relative),cancellation).ConfigureAwait(false);
        return result;
    }
    private static async Task<string> Hash(string file,CancellationToken cancellation)
    {await using var stream=File.OpenRead(file);return Convert.ToHexString(await SHA256.HashDataAsync(stream,cancellation).ConfigureAwait(false)).ToLowerInvariant();}
}
