using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CubeShelf.Core.Storage;

public sealed record GameSaveBackup(string File,string Title,DateTimeOffset CreatedAt,int FileCount,long Bytes);
public sealed record GameSaveRestore(int Files,GameSaveBackup? Previous);
public sealed record GameSaveDetails(string GameKey,string Title,string Engine,int Files,DateTimeOffset CreatedAt);

/// <summary>Game-scoped, checked ZIP snapshots; restores preserve replacements before changing any save.</summary>
public static class GameSaveArchive
{
    private const long MaximumBytes=2L*1024*1024*1024;
    private const int MaximumFiles=10000;
    private const string ManifestName="decompshelf-saves.json";
    private sealed record SaveFile(string Path,long Bytes,string Sha256);
    private sealed record Manifest(int Version,string GameKey,string Title,string Engine,DateTimeOffset CreatedAt,SaveFile[] Files);
    public static GameSaveDetails Inspect(string file)
    {
        StorageFiles.CheckRoot(file);using var zip=ZipFile.OpenRead(file);var manifest=ReadManifest(zip);
        return new(manifest.GameKey,manifest.Title,manifest.Engine,manifest.Files.Length,manifest.CreatedAt);
    }

    public static IReadOnlyList<GameSaveBackup> List(string root)
    {
        if(!Directory.Exists(root))return [];
        var result=new List<GameSaveBackup>();
        foreach(var file in Directory.EnumerateFiles(root,"*.zip"))
        try
        {
            StorageFiles.CheckRoot(file);using var zip=ZipFile.OpenRead(file);var manifest=ReadManifest(zip);
            result.Add(new(file,manifest.Title,manifest.CreatedAt,manifest.Files.Length,new FileInfo(file).Length));
        }
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        return result.OrderByDescending(b=>b.CreatedAt).ToArray();
    }

    internal static GameSaveBackup Create(GameStorageEntry game,GameSaveLocation saves,string root,Action idle,CancellationToken cancellation)
    {
        idle();var files=StorageFiles.Files(saves,cancellation);
        if(files.Length==0)throw new InvalidOperationException("Aucune sauvegarde trouvée dans ce dossier.");
        if(files.Length>MaximumFiles)throw new InvalidDataException("Trop de fichiers : choisis le dossier des sauvegardes uniquement.");
        if(files.Any(ProgramFile))throw new InvalidDataException("Ce dossier contient des programmes. Choisis uniquement les sauvegardes du jeu.");
        var total=files.Sum(p=>new FileInfo(StorageFiles.PathIn(saves.Directory,p)).Length);
        if(total>MaximumBytes)throw new InvalidDataException("Les sauvegardes dépassent 2 Go. Vérifie que le dossier ne contient pas les fichiers du jeu.");
        StorageFiles.CheckRoot(root);Directory.CreateDirectory(root);DiskSpaceGuard.Require(root,total);
        var created=DateTimeOffset.UtcNow;var target=Path.Combine(root,created.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".zip");var temporary=target+".partial";
        try
        {
            var records=new List<SaveFile>();long written=0;
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create))
            foreach(var relative in files)
            {
                idle();cancellation.ThrowIfCancellationRequested();
                var source=StorageFiles.PathIn(saves.Directory,relative);var entry=zip.CreateEntry("saves/"+relative,CompressionLevel.Fastest);
                using var input=File.OpenRead(source);using var destination=entry.Open();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer=new byte[131072];int count;long bytes=0;
                while((count=input.Read(buffer))>0){cancellation.ThrowIfCancellationRequested();bytes+=count;written+=count;if(written>MaximumBytes)throw new InvalidDataException("Sauvegardes trop volumineuses.");hash.AppendData(buffer,0,count);destination.Write(buffer,0,count);}
                records.Add(new(relative,bytes,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
                if(records.Count==files.Length)
                {
                    destination.Dispose();using var metadata=zip.CreateEntry(ManifestName).Open();
                    JsonSerializer.Serialize(metadata,new Manifest(1,game.GameKey,game.Title,game.Engine,created,records.ToArray()));
                }
            }
            idle();cancellation.ThrowIfCancellationRequested();File.Move(temporary,target);
            return new(target,game.Title,created,files.Length,new FileInfo(target).Length);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }

    internal static GameSaveRestore Restore(GameStorageEntry game,GameSaveLocation saves,string file,string backups,Action idle,CancellationToken cancellation)
    {
        idle();StorageFiles.CheckRoot(file);StorageFiles.CheckRoot(saves.Directory);
        if(new FileInfo(file).Length>MaximumBytes+16L*1024*1024)throw new InvalidDataException("Archive de sauvegardes trop volumineuse.");
        Directory.CreateDirectory(backups);var stage=Path.Combine(backups,"restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var changed=new List<string>();var originals=new HashSet<string>(StringComparer.Ordinal);GameSaveBackup? previous=null;
        try
        {
            using var zip=ZipFile.OpenRead(file);var manifest=ReadManifest(zip);
            if(manifest.GameKey!=game.GameKey)throw new InvalidDataException("Cette archive appartient à un autre jeu. Aucun fichier n’a été remplacé.");
            if(manifest.Files.Any(f=>saves.Files==GameSaveFiles.MemoryCards&&!CubeShelf.Core.Social.PhoneLinkServer.IsSavePath(f.Path)
                ||saves.Files==GameSaveFiles.Recognized&&(f.Path.Contains('/')||f.Path.Contains('\\')||!StorageFiles.Recognized(f.Path))))
                throw new InvalidDataException("Cette archive contient des fichiers qui ne sont pas des sauvegardes reconnues de ce jeu.");
            var expected=manifest.Files.Select(f=>"saves/"+f.Path).Append(ManifestName).ToHashSet(StringComparer.Ordinal);
            if(zip.Entries.Count!=expected.Count||zip.Entries.Any(e=>!expected.Contains(e.FullName))||zip.Entries.Select(e=>e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=zip.Entries.Count)
                throw new InvalidDataException("Le contenu de l’archive ne correspond pas à son manifeste.");
            DiskSpaceGuard.Require(backups,manifest.Files.Sum(f=>f.Bytes)*2);
            DiskSpaceGuard.Require(saves.Directory,manifest.Files.Sum(f=>f.Bytes));
            foreach(var record in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();var entry=zip.GetEntry("saves/"+record.Path)!;
                if(entry.Length!=record.Bytes)throw new InvalidDataException("Taille de sauvegarde incorrecte.");
                var target=StorageFiles.PathIn(Path.Combine(stage,"incoming"),record.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using(var input=entry.Open())using(var output=File.Create(target))CopyBounded(input,output,record.Bytes,cancellation);
                using var checkedFile=File.OpenRead(target);
                if(!Convert.ToHexString(SHA256.HashData(checkedFile)).Equals(record.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Une sauvegarde a été altérée.");
            }
            idle();cancellation.ThrowIfCancellationRequested();
            if(StorageFiles.Files(saves,cancellation).Length>0)previous=Create(game,saves,backups,idle,cancellation);
            foreach(var record in manifest.Files)
            {
                var target=StorageFiles.PathIn(saves.Directory,record.Path);
                if(File.Exists(target))
                {
                    var copy=StorageFiles.PathIn(Path.Combine(stage,"previous"),record.Path);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(target,copy);originals.Add(record.Path);
                }
            }
            try
            {
                foreach(var record in manifest.Files)
                {
                    idle();cancellation.ThrowIfCancellationRequested();var target=StorageFiles.PathIn(saves.Directory,record.Path);
                    changed.Add(record.Path);CopyAtomic(StorageFiles.PathIn(Path.Combine(stage,"incoming"),record.Path),target);
                }
            }
            catch(Exception failure)
            {
                var errors=new List<Exception>{failure};
                foreach(var relative in changed.AsEnumerable().Reverse())
                try
                {
                    var target=StorageFiles.PathIn(saves.Directory,relative);
                    if(originals.Contains(relative))CopyAtomic(StorageFiles.PathIn(Path.Combine(stage,"previous"),relative),target);
                    else if(File.Exists(target))File.Delete(target);
                }
                catch(Exception error){errors.Add(error);}
                if(errors.Count>1)throw new AggregateException("Restauration interrompue. La copie de sécurité reste disponible dans "+backups,errors);
                throw;
            }
            return new(changed.Count,previous);
        }
        finally
        {
            if(Directory.Exists(stage)&&StorageFiles.Within(backups,stage)&&!StorageFiles.Same(backups,stage))Directory.Delete(stage,true);
        }
    }

    private static Manifest ReadManifest(ZipArchive zip)
    {
        var entry=zip.GetEntry(ManifestName)??throw new InvalidDataException("Ce fichier n’est pas une sauvegarde exportée par DecompShelf.");
        if(entry.Length>4*1024*1024)throw new InvalidDataException("Manifeste de sauvegarde trop volumineux.");
        using var input=entry.Open();var manifest=JsonSerializer.Deserialize<Manifest>(input)??throw new InvalidDataException("Manifeste invalide.");
        if(manifest.Version!=1||string.IsNullOrWhiteSpace(manifest.GameKey)||manifest.Files is not {Length:>0 and <=MaximumFiles}
            ||manifest.Files.Any(f=>f is null||f.Path is null||f.Sha256 is null||f.Bytes<0||f.Bytes>MaximumBytes||f.Sha256.Length!=64||!f.Sha256.All(Uri.IsHexDigit))
            ||manifest.Files.Sum(f=>f.Bytes)>MaximumBytes||manifest.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Files.Length)
            throw new InvalidDataException("Manifeste de sauvegarde invalide.");
        foreach(var record in manifest.Files)
        {
            StorageFiles.ValidateRelative(record.Path);
            if(ProgramFile(record.Path))throw new InvalidDataException("Une sauvegarde ne doit pas contenir de programme.");
        }
        return manifest;
    }
    private static bool ProgramFile(string path)=>new[]{".exe",".dll",".so",".dylib",".apk",".dex",".bat",".cmd",".ps1"}.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static void CopyAtomic(string source,string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temporary=target+".restore-"+Guid.NewGuid().ToString("N");
        try{File.Copy(source,temporary);File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void CopyBounded(Stream input,Stream output,long expected,CancellationToken cancellation)
    {
        var buffer=new byte[131072];long total=0;int count;
        while((count=input.Read(buffer))>0){cancellation.ThrowIfCancellationRequested();total+=count;if(total>expected)throw new InvalidDataException("Sauvegarde trop volumineuse.");output.Write(buffer,0,count);}
        if(total!=expected)throw new InvalidDataException("Sauvegarde tronquée.");
    }
}
