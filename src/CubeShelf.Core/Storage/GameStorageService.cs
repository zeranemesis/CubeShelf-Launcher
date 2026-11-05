using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Core.Storage;

public enum GameSaveFiles { All, MemoryCards, Recognized }

public sealed record GameStorageEntry
{
    public required string Id { get; init; }
    public required string GameKey { get; init; }
    public required string Title { get; init; }
    public string Engine { get; init; }="";
    public required string OwnedRoot { get; init; }
    public string? EngineDirectory { get; init; }
    public string? DataDirectory { get; init; }
    public string? SavesDirectory { get; init; }
    public string? SaveSearchDirectory { get; init; }
    public string[] AdditionalDataDirectories { get; init; }=[];
    public string[] RemovableDirectories { get; init; }=[];
    public GameSaveFiles SaveFiles { get; init; }
    public string[] DownloadDirectories { get; init; }=[];
    public string[] DownloadFiles { get; init; }=[];
    public string? RemovableEngine { get; init; }
    public bool EngineIsDirectory { get; init; }
    public override string ToString()=>Title;
}

public sealed record GameSaveLocation(string Directory,GameSaveFiles Files);
public sealed record GameStorageUsage(long EngineBytes,long GameBytes,long SaveBytes,long DownloadBytes,long BackupBytes,long? AvailableBytes,bool Combined)
{
    public long TotalBytes=>EngineBytes+GameBytes+SaveBytes+DownloadBytes+BackupBytes;
}
public sealed record GameStorageItem(GameStorageEntry Game,GameStorageUsage? Usage,string? Error);

/// <summary>Only declared owned engine/download paths can be removed; save locations are independently configurable.</summary>
public sealed class GameStorageService
{
    public IReadOnlyList<GameStorageEntry> Games { get; }
    public string BackupsDirectory { get; }
    private readonly string _settings;
    private readonly Func<bool> _busy;
    private readonly Dictionary<string,string> _saveDirectories;

    public GameStorageService(string metadataDirectory,IReadOnlyList<GameStorageEntry> games,Func<bool> busy)
    {
        Games=games;_busy=busy;
        if(games.Any(g=>!NativePortCatalog.IsSafeId(g.Id)||string.IsNullOrWhiteSpace(g.GameKey))||games.Select(g=>g.Id).Distinct().Count()!=games.Count)
            throw new InvalidDataException("Liste des jeux pour le stockage invalide.");
        BackupsDirectory=Path.Combine(Path.GetFullPath(metadataDirectory),"game-save-backups");
        _settings=Path.Combine(Path.GetFullPath(metadataDirectory),"game-storage.json");
        try
        {
            _saveDirectories=File.Exists(_settings)&&new FileInfo(_settings).Length<262144
                ?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(_settings))??[]:[];
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){_saveDirectories=[];}
    }

    public bool IsBusy=>_busy();
    public void RequireIdle(){if(IsBusy)throw new InvalidOperationException("Ferme les jeux et termine les préparations en cours avant cette opération.");}
    internal string BackupFolder(GameStorageEntry game)=>Path.Combine(BackupsDirectory,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(game.GameKey))).ToLowerInvariant()[..24]);

    public GameSaveLocation? Saves(GameStorageEntry game)
    {
        lock(_saveDirectories)if(_saveDirectories.TryGetValue(game.Id,out var configured))return new(configured,GameSaveFiles.All);
        if(game.SavesDirectory is { Length:>0 } known)return new(known,game.SaveFiles);
        if((game.SaveSearchDirectory??game.DataDirectory) is not { } root||!Directory.Exists(root))return null;
        foreach(var relative in new[]{"saves","save","Save","Saves","savegames","SaveGames","SaveData","savedata","User/GC","user/GC",".config/saves"})
        {
            var folder=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
            if(Directory.Exists(folder)&&!StorageFiles.IsLink(folder))return new(folder,GameSaveFiles.All);
        }
        return Directory.EnumerateFiles(root).Any(path=>StorageFiles.Recognized(Path.GetFileName(path)))?new(root,GameSaveFiles.Recognized):null;
    }

    public void SetSavesDirectory(GameStorageEntry game,string directory)
    {
        RequireIdle();directory=Path.GetFullPath(directory);StorageFiles.CheckRoot(directory);
        if(StorageFiles.Within(directory,BackupsDirectory)||StorageFiles.Within(BackupsDirectory,directory))throw new InvalidDataException("Choisis le dossier des sauvegardes du jeu, distinct des copies de sécurité de DecompShelf.");
        lock(_saveDirectories)
        {
            _saveDirectories[game.Id]=directory;
            Directory.CreateDirectory(Path.GetDirectoryName(_settings)!);
            var temporary=_settings+".new";File.WriteAllText(temporary,JsonSerializer.Serialize(_saveDirectories));File.Move(temporary,_settings,true);
        }
    }

    public GameStorageUsage Measure(GameStorageEntry game,CancellationToken cancellation=default)
    {
        var combined=game.EngineDirectory is not null&&game.DataDirectory is not null&&StorageFiles.Same(game.EngineDirectory,game.DataDirectory);
        var files=new Dictionary<string,(long Bytes,int Category)>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        void AddFile(string file,int category){cancellation.ThrowIfCancellationRequested();StorageFiles.CheckRoot(file);if(File.Exists(file))files[Path.GetFullPath(file)]=(new FileInfo(file).Length,category);}
        void AddFolder(string? root,int category)
        {
            if(root is null)return;
            foreach(var relative in StorageFiles.Files(new(root,GameSaveFiles.All),cancellation))AddFile(StorageFiles.PathIn(root,relative),category);
        }
        AddFolder(game.EngineDirectory,0);if(!combined)AddFolder(game.DataDirectory,1);
        foreach(var folder in game.AdditionalDataDirectories)AddFolder(folder,1);
        foreach(var folder in game.DownloadDirectories)AddFolder(folder,3);foreach(var file in game.DownloadFiles)AddFile(file,3);
        if(Saves(game) is { } saves)foreach(var file in StorageFiles.Files(saves,cancellation))AddFile(StorageFiles.PathIn(saves.Directory,file),2);
        AddFolder(BackupFolder(game),4);
        long Bytes(int category)=>files.Values.Where(f=>f.Category==category).Sum(f=>f.Bytes);
        return new(Bytes(0),Bytes(1),Bytes(2),Bytes(3),Bytes(4),DiskSpaceGuard.AvailableBytes(game.OwnedRoot),combined);
    }

    public IReadOnlyList<GameStorageItem> Scan(CancellationToken cancellation=default)
    {
        var result=new List<GameStorageItem>();
        foreach(var game in Games)
        {
            cancellation.ThrowIfCancellationRequested();
            try{var usage=Measure(game,cancellation);if(usage.TotalBytes>0)result.Add(new(game,usage,null));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException){result.Add(new(game,null,e.Message));}
        }
        return result.OrderByDescending(item=>item.Usage?.TotalBytes??-1).ThenBy(item=>item.Game.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task<long> RemoveInstallationAsync(GameStorageEntry game,bool noSavesConfirmed=false,CancellationToken cancellation=default,bool keepSaves=true)=>Task.Run(()=>
    {
        RequireIdle();if(game.RemovableDirectories.Length==0)throw new InvalidOperationException("Cette installation est externe ou fournie dans l’application.");
        var saves=Saves(game);
        if(saves is null&&!noSavesConfirmed)throw new InvalidOperationException("Indique le dossier des sauvegardes, ou confirme qu’aucune progression n’est à conserver.");
        var kept=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        if(saves is not null)foreach(var file in StorageFiles.Files(saves,cancellation))kept.Add(StorageFiles.PathIn(saves.Directory,file));
        var targets=game.RemovableDirectories.Select(Path.GetFullPath).Distinct(kept.Comparer).ToArray();
        var otherRoots=Games.Where(g=>g.Id!=game.Id).SelectMany(g=>new[]{g.EngineDirectory,g.DataDirectory,Saves(g)?.Directory}.Concat(g.AdditionalDataDirectories).Concat(g.RemovableDirectories)).Where(p=>p is not null).Cast<string>().ToArray();
        foreach(var target in targets)
        {
            StorageFiles.RequireOwned(game.OwnedRoot,target);
            if(StorageFiles.Within(target,BackupsDirectory)||StorageFiles.Within(BackupsDirectory,target)
                ||otherRoots.Any(p=>StorageFiles.Within(target,p)||StorageFiles.Within(p,target)))
                throw new InvalidOperationException("Ce dossier est partagé avec un autre jeu ou contient les copies de sécurité. Aucun fichier supprimé.");
        }
        if(!keepSaves)foreach(var file in kept)
        {
            StorageFiles.CheckRoot(file);
            if(StorageFiles.Within(BackupsDirectory,file)||StorageFiles.Same(_settings,file)||otherRoots.Any(root=>StorageFiles.Within(root,file)))
                throw new InvalidOperationException("Ces sauvegardes sont partagées avec un autre jeu ou appartiennent au profil. Conserve-les lors de la désinstallation.");
            if(!targets.Any(target=>StorageFiles.Within(target,file))&&Path.GetExtension(file).ToLowerInvariant() is ".iso" or ".gcm" or ".rvz" or ".wbfs" or ".z64" or ".n64" or ".v64" or ".nds" or ".3ds" or ".xci" or ".nsp" or ".nes" or ".sfc" or ".smc" or ".gb" or ".gbc" or ".gba" or ".chd" or ".cso" or ".wud" or ".wux")
                throw new InvalidOperationException("Le dossier de sauvegardes contient une image de jeu externe. Choisis uniquement le dossier de progression ou conserve les sauvegardes.");
        }
        var files=new HashSet<string>(kept.Comparer);
        foreach(var target in targets)foreach(var relative in StorageFiles.Files(new(target,GameSaveFiles.All),cancellation))
        {
            var file=StorageFiles.PathIn(target,relative);
            if(kept.Contains(file)){if(!keepSaves)files.Add(file);continue;}
            if(saves is {Files:GameSaveFiles.All}&&StorageFiles.Within(saves.Directory,file)
                ||StorageFiles.Recognized(Path.GetFileName(file))||relative.Split('/').SkipLast(1).Any(StorageFiles.SaveFolder))continue;
            files.Add(file);
        }
        if(!keepSaves)files.UnionWith(kept);
        if(saves is not null&&kept.Count>0)GameSaveArchive.Create(game,saves,BackupFolder(game),RequireIdle,cancellation);
        long removed=0;
        foreach(var file in files){RequireIdle();cancellation.ThrowIfCancellationRequested();StorageFiles.CheckRoot(file);removed+=StorageFiles.Remove(file,false);}
        return removed;
    },cancellation);

    public IReadOnlyList<GameSaveBackup> Backups(GameStorageEntry game)=>GameSaveArchive.List(BackupFolder(game));
    public void DeleteBackup(GameStorageEntry game,string file)
    {
        RequireIdle();StorageFiles.RequireOwned(BackupFolder(game),file);
        if(!StorageFiles.Same(Path.GetDirectoryName(file)!,BackupFolder(game))||!file.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Copie de sécurité invalide.");
        File.Delete(file);
    }
    public Task<GameSaveBackup> BackupAsync(GameStorageEntry game,CancellationToken cancellation=default)=>Task.Run(()=>
    {
        RequireIdle();var saves=Saves(game)??throw new InvalidOperationException("Aucune sauvegarde détectée. Lance le jeu une première fois ou indique son dossier de sauvegardes.");
        return GameSaveArchive.Create(game,saves,BackupFolder(game),RequireIdle,cancellation);
    },cancellation);
    public Task<GameSaveRestore> RestoreAsync(GameStorageEntry game,string archive,CancellationToken cancellation=default)=>Task.Run(()=>
    {
        RequireIdle();var saves=Saves(game)??throw new InvalidOperationException("Indique d’abord le dossier de destination des sauvegardes.");
        return GameSaveArchive.Restore(game,saves,archive,BackupFolder(game),RequireIdle,cancellation);
    },cancellation);

    public long CleanDownloads(GameStorageEntry game)
    {
        RequireIdle();long bytes=0;var saves=Saves(game);
        foreach(var directory in game.DownloadDirectories){StorageFiles.RequireOwned(game.OwnedRoot,directory);ProtectSaves(saves,directory,true);}
        foreach(var file in game.DownloadFiles){StorageFiles.RequireOwned(game.OwnedRoot,file);ProtectSaves(saves,file,false);}
        foreach(var directory in game.DownloadDirectories){RequireIdle();bytes+=StorageFiles.Remove(directory,true);}
        foreach(var file in game.DownloadFiles){RequireIdle();bytes+=StorageFiles.Remove(file,false);}
        return bytes;
    }
    public long RemoveEngine(GameStorageEntry game)
    {
        RequireIdle();var path=game.RemovableEngine??throw new InvalidOperationException("Le moteur est fourni par l’application ou associé depuis un dossier externe.");
        StorageFiles.RequireOwned(game.OwnedRoot,path);
        var saves=Saves(game);
        ProtectSaves(saves,path,game.EngineIsDirectory);
        if(game.EngineIsDirectory&&(game.DataDirectory is not null&&StorageFiles.Within(path,game.DataDirectory)||saves is not null&&StorageFiles.Within(path,saves.Directory)))
            throw new InvalidOperationException("Ce dossier contient aussi les données du joueur. Aucun fichier n’a été supprimé.");
        return StorageFiles.Remove(path,game.EngineIsDirectory);
    }
    private static void ProtectSaves(GameSaveLocation? saves,string target,bool directory)
    {
        if(saves is null)return;
        var overlaps=directory&&StorageFiles.Within(target,saves.Directory)
            ||saves.Files==GameSaveFiles.All&&StorageFiles.Within(saves.Directory,target)
            ||!directory&&StorageFiles.Files(saves,default).Any(p=>StorageFiles.Same(StorageFiles.PathIn(saves.Directory,p),target));
        if(overlaps)throw new InvalidOperationException("Cet emplacement contient des sauvegardes. Aucun fichier n’a été supprimé.");
    }
}

internal static class StorageFiles
{
    private static StringComparison Comparison=>OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
    internal static bool Same(string a,string b)=>Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),Comparison);
    internal static bool Within(string root,string target)=>Same(root,target)||Path.GetFullPath(target).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,Comparison);
    internal static bool IsLink(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    internal static void CheckRoot(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((Directory.Exists(current)||File.Exists(current))&&IsLink(current))throw new InvalidDataException("Les liens symboliques ne sont pas acceptés pour cette opération.");
    }
    internal static string PathIn(string root,string relative)
    {
        ValidateRelative(relative);var parts=relative.Split('/');
        var result=Path.Combine(root,Path.Combine(parts));CheckRoot(result);return result;
    }
    internal static void ValidateRelative(string relative)
    {
        var parts=relative.Replace('\\','/').Split('/');
        static bool Device(string part)
        {
            var stem=part.Split('.')[0].ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$"||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9';
        }
        if(relative.Length>1024||relative.Contains('\\')||Path.IsPathRooted(relative)||parts.Any(p=>string.IsNullOrWhiteSpace(p)||p is "." or ".."||p.EndsWith(' ')||p.EndsWith('.')||Device(p)||p.IndexOfAny([':', '\0','<','>','"','|','?','*'])>=0))throw new InvalidDataException("Chemin de sauvegarde invalide.");
    }
    internal static void RequireOwned(string root,string target)
    {
        if(Same(root,target)||!Within(root,target))throw new InvalidDataException("Ce fichier n’appartient pas au stockage géré par DecompShelf.");
        CheckRoot(target);
    }
    internal static bool Recognized(string name)=>new[]{".sav",".sra",".eep",".fla",".gci",".srm",".mcr",".mcd",".save"}.Contains(Path.GetExtension(name).ToLowerInvariant())
        ||name.StartsWith("save",StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(name).ToLowerInvariant() is ".bin" or ".dat" or ".json";
    internal static bool SaveFolder(string name)=>name.ToLowerInvariant() is "save" or "saves" or "savegames" or "savedata" or "save-backups" or "card a" or "card b";
    internal static string[] Files(GameSaveLocation location,CancellationToken cancellation)
    {
        if(!Directory.Exists(location.Directory))return [];
        CheckRoot(location.Directory);var result=new List<string>();
        if(location.Files==GameSaveFiles.MemoryCards)
        {
            foreach(var file in Directory.EnumerateFiles(location.Directory))if(PhoneLinkServer.IsSavePath(Path.GetFileName(file))){if(IsLink(file))throw new InvalidDataException("Une sauvegarde contient un lien symbolique.");result.Add(Path.GetFileName(file));}
            foreach(var region in new[]{"USA","EUR","JAP"})foreach(var card in new[]{"Card A","Card B"})
            {
                var relative=region+"/"+card;var folder=PathIn(location.Directory,relative);if(!Directory.Exists(folder))continue;
                foreach(var file in Directory.EnumerateFiles(folder)){cancellation.ThrowIfCancellationRequested();var name=relative+"/"+Path.GetFileName(file);if(PhoneLinkServer.IsSavePath(name)){if(IsLink(file))throw new InvalidDataException("Une sauvegarde contient un lien symbolique.");result.Add(name);}}
            }
            return result.Order(StringComparer.Ordinal).ToArray();
        }
        void Visit(string directory,int depth)
        {
            cancellation.ThrowIfCancellationRequested();if(depth>40)throw new InvalidDataException("Dossier de sauvegardes trop profond.");
            foreach(var file in Directory.EnumerateFiles(directory))
            {
                if(IsLink(file))throw new InvalidDataException("Une sauvegarde contient un lien symbolique.");
                var relative=Path.GetRelativePath(location.Directory,file).Replace('\\','/');
                if(location.Files==GameSaveFiles.All||location.Files==GameSaveFiles.MemoryCards&&PhoneLinkServer.IsSavePath(relative)||location.Files==GameSaveFiles.Recognized&&depth==0&&Recognized(Path.GetFileName(file)))result.Add(relative);
            }
            if(location.Files==GameSaveFiles.Recognized)return;
            foreach(var child in Directory.EnumerateDirectories(directory))
            {
                if(location.Files==GameSaveFiles.MemoryCards&&Path.GetFileName(child)=="save-backups")continue;
                if(IsLink(child))throw new InvalidDataException("Une sauvegarde contient un lien symbolique.");
                Visit(child,depth+1);
            }
        }
        Visit(location.Directory,0);return result.Order(StringComparer.Ordinal).ToArray();
    }
    internal static long Size(string? root,CancellationToken cancellation)
    {
        if(root is null||!Directory.Exists(root))return 0;
        CheckRoot(root);long size=0;var pending=new Stack<string>();pending.Push(root);
        while(pending.TryPop(out var directory))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach(var file in Directory.EnumerateFiles(directory))if(!IsLink(file))size+=new FileInfo(file).Length;
            foreach(var child in Directory.EnumerateDirectories(directory))if(!IsLink(child))pending.Push(child);
        }
        return size;
    }
    internal static long Remove(string path,bool directory)
    {
        if(directory&&!Directory.Exists(path)||!directory&&!File.Exists(path))return 0;
        if(!directory){var size=new FileInfo(path).Length;File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);return size;}
        var files=Files(new(path,GameSaveFiles.All),default);var bytes=files.Sum(p=>new FileInfo(PathIn(path,p)).Length);
        foreach(var file in files)File.SetAttributes(PathIn(path,file),FileAttributes.Normal);
        Directory.Delete(path,true);return bytes;
    }
}
