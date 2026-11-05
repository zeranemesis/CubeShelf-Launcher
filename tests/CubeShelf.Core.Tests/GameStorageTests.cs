using System.IO.Compression;
using CubeShelf.Core.Storage;

internal static class GameStorageTests
{
    internal static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"decompshelf-game-storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Test(root).GetAwaiter().GetResult();}finally{Directory.Delete(root,true);}
    }
    private static async Task Test(string root)
    {
        var owned=Path.Combine(root,"owned");var engine=Path.Combine(owned,"engines/test");var data=Path.Combine(owned,"games/test");var saves=Path.Combine(data,"saves");var downloads=Path.Combine(owned,"downloads/test");
        Directory.CreateDirectory(engine);Directory.CreateDirectory(saves);Directory.CreateDirectory(downloads);
        await File.WriteAllTextAsync(Path.Combine(engine,"engine.bin"),"engine");await File.WriteAllTextAsync(Path.Combine(downloads,"package.zip"),"download");
        var progress=Path.Combine(saves,"progress.sav");await File.WriteAllTextAsync(progress,"first progress");
        var game=new GameStorageEntry{Id="fixture",GameKey="n64:TEST",Title="Test",OwnedRoot=owned,EngineDirectory=engine,DataDirectory=data,DownloadDirectories=[downloads],RemovableEngine=engine,EngineIsDirectory=true,RemovableDirectories=[engine,data,downloads]};
        var busy=false;var storage=new GameStorageService(Path.Combine(owned,"settings"),[game],()=>busy);
        Check(storage.Saves(game)?.Directory==saves,"save directory is detected without including the game files");
        var backup=await storage.BackupAsync(game);Check(backup.FileCount==1&&storage.Backups(game).Count==1,"a checked local save snapshot is listed");
        await File.WriteAllTextAsync(progress,"new progress");
        var restored=await storage.RestoreAsync(game,backup.File);Check(await File.ReadAllTextAsync(progress)=="first progress"&&restored.Previous is not null,"restore first preserves the current save");
        await storage.RestoreAsync(game,restored.Previous!.File);Check(await File.ReadAllTextAsync(progress)=="new progress","the automatic safety snapshot restores the replaced progress");
        var wrong=game with{GameKey="n64:OTHER"};await Refuse<InvalidDataException>(()=>storage.RestoreAsync(wrong,backup.File));Check(await File.ReadAllTextAsync(progress)=="new progress","another game's archive cannot overwrite saves");
        var altered=Path.Combine(root,"altered.zip");File.Copy(backup.File,altered);
        using(var zip=ZipFile.Open(altered,ZipArchiveMode.Update))
        {var entry=zip.GetEntry("saves/progress.sav")!;entry.Delete();using var stream=zip.CreateEntry("saves/progress.sav").Open();stream.Write("broken payload"u8);}
        await Refuse<InvalidDataException>(()=>storage.RestoreAsync(game,altered));Check(await File.ReadAllTextAsync(progress)=="new progress","archive tampering is rejected before replacement");
        var traversal=Path.Combine(root,"traversal.zip");File.Copy(backup.File,traversal);
        using(var zip=ZipFile.Open(traversal,ZipArchiveMode.Update)){using var stream=zip.CreateEntry("saves/../../escape.sav").Open();stream.WriteByte(1);}
        await Refuse<InvalidDataException>(()=>storage.RestoreAsync(game,traversal));Check(!File.Exists(Path.Combine(root,"escape.sav")),"unexpected archive paths cannot escape staging");
        using(var cancellation=new CancellationTokenSource())
        {cancellation.Cancel();await Refuse<OperationCanceledException>(()=>storage.RestoreAsync(game,backup.File,cancellation.Token));}
        busy=true;await Refuse<InvalidOperationException>(()=>storage.BackupAsync(game));await Refuse<InvalidOperationException>(()=>storage.RestoreAsync(game,backup.File));busy=false;
        var first=Path.Combine(saves,"a.sav");var last=Path.Combine(saves,"z.sav");await File.WriteAllTextAsync(first,"old a");await File.WriteAllTextAsync(last,"old z");
        var two=await storage.BackupAsync(game);await File.WriteAllTextAsync(first,"current a");await File.WriteAllTextAsync(last,"current z");
        var interrupted=new GameStorageService(Path.Combine(owned,"settings"),[game],()=>File.ReadAllText(first)=="old a");
        await Refuse<InvalidOperationException>(()=>interrupted.RestoreAsync(game,two.File));
        Check(await File.ReadAllTextAsync(first)=="current a"&&await File.ReadAllTextAsync(last)=="current z","a game becoming active during replacement rolls back the files already changed");
        File.Delete(first);File.Delete(last);
        var usage=storage.Measure(game);Check(usage.SaveBytes==new FileInfo(progress).Length&&usage.EngineBytes==6&&usage.DownloadBytes==8,"storage separates engine, saves and downloads");
        var package=Path.Combine(data,"package.zip");File.WriteAllBytes(package,new byte[4096]);
        var combined=game with{Id="combined",GameKey="n64:COMBINED",EngineDirectory=data,DownloadDirectories=[],DownloadFiles=[package]};
        var combinedService=new GameStorageService(Path.Combine(owned,"settings"),[combined],()=>false);
        var counted=combinedService.Measure(combined);
        Check(counted.TotalBytes==4096+new FileInfo(progress).Length&&counted.DownloadBytes==4096,"a package and save inside the installation are counted once");
        var empty=game with{Id="empty",GameKey="n64:EMPTY",EngineDirectory=null,DataDirectory=null,DownloadDirectories=[],RemovableDirectories=[]};
        var inventory=new GameStorageService(Path.Combine(owned,"settings"),[empty,combined,game],()=>false).Scan();
        Check(inventory.Count==2&&inventory[0].Usage!.TotalBytes>=inventory[1].Usage!.TotalBytes,"inventory omits empty catalogue entries and sorts real storage largest first");
        Check(storage.CleanDownloads(game)==8&&File.Exists(progress)&&File.Exists(Path.Combine(engine,"engine.bin")),"download cleanup preserves the engine and progress");
        Check(storage.RemoveEngine(game)==6&&File.Exists(progress)&&!Directory.Exists(engine),"removing an owned engine preserves game data and saves");
        try{storage.RemoveEngine(game with{RemovableEngine=root});throw new Exception("outside root accepted");}catch(InvalidDataException){}
        try{storage.RemoveEngine(game with{RemovableEngine=data});throw new Exception("data directory accepted");}catch(InvalidOperationException){}
        try{storage.RemoveEngine(game with{RemovableEngine=progress,EngineIsDirectory=false});throw new Exception("save file accepted as engine");}catch(InvalidOperationException){}
        try{storage.CleanDownloads(game with{DownloadDirectories=[saves]});throw new Exception("save folder accepted as cache");}catch(InvalidOperationException){}
        var neighbor=game with{Id="neighbor",GameKey="n64:NEIGHBOR",DataDirectory=Path.Combine(data,"another-game")};Directory.CreateDirectory(neighbor.DataDirectory);
        var shared=new GameStorageService(Path.Combine(owned,"settings"),[game,neighbor],()=>false);
        await Refuse<InvalidOperationException>(()=>shared.RemoveInstallationAsync(game));Check(File.Exists(package),"shared game folders are refused before deleting anything");
        await Refuse<InvalidDataException>(()=>storage.RemoveInstallationAsync(game with{RemovableDirectories=[root]}));
        busy=true;await Refuse<InvalidOperationException>(()=>storage.RemoveInstallationAsync(game));busy=false;
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();await Refuse<OperationCanceledException>(()=>storage.RemoveInstallationAsync(game,cancellation:cancelled.Token));}
        var legacy=Path.Combine(data,"legacy","progress.sav");Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);File.WriteAllText(legacy,"legacy save");
        var extra=Path.Combine(data,"SaveData","slot.bin");Directory.CreateDirectory(Path.GetDirectoryName(extra)!);File.WriteAllText(extra,"other save");
        var before=storage.Backups(game).Count;var freed=await storage.RemoveInstallationAsync(game);
        Check(freed==4096&&File.Exists(progress)&&!File.Exists(package)&&storage.Backups(game).Count==before+1,"installation removal frees game assets while preserving progress and a safety copy");
        Check(File.Exists(legacy)&&File.Exists(extra),"recognized save files and additional conventional save folders are retained");
        var unplayed=Path.Combine(owned,"unplayed");Directory.CreateDirectory(unplayed);var unplayedFile=Path.Combine(unplayed,"game.bin");File.WriteAllText(unplayedFile,"asset");
        var unknown=game with{Id="unplayed",GameKey="n64:UNPLAYED",DataDirectory=unplayed,SavesDirectory=null,RemovableDirectories=[unplayed]};
        var noProgress=new GameStorageService(Path.Combine(owned,"settings"),[unknown],()=>false);
        await Refuse<InvalidOperationException>(()=>noProgress.RemoveInstallationAsync(unknown));Check(File.Exists(unplayedFile),"unknown save locations require explicit acknowledgement");
        Check(await noProgress.RemoveInstallationAsync(unknown,true)==5&&!File.Exists(unplayedFile),"a confirmed unplayed installation can be cleared");
        var external=Path.Combine(root,"custom-saves");Directory.CreateDirectory(external);storage.SetSavesDirectory(game,external);
        var externalSave=Path.Combine(external,"slot.sav");File.WriteAllText(externalSave,"external progress");File.WriteAllText(package,"game data");
        var sharesSaves=empty with{Id="shared-progress",SavesDirectory=external};
        var sharing=new GameStorageService(Path.Combine(owned,"settings"),[game,sharesSaves],()=>false);
        await Refuse<InvalidOperationException>(()=>sharing.RemoveInstallationAsync(game,keepSaves:false));
        Check(File.Exists(package)&&File.Exists(externalSave),"deleting shared progress is refused before removing installation files");
        var original=Path.Combine(external,"original.z64");File.WriteAllText(original,"original game");
        await Refuse<InvalidOperationException>(()=>storage.RemoveInstallationAsync(game,keepSaves:false));
        Check(File.Exists(original)&&File.Exists(package)&&File.Exists(externalSave),"a game image in a mistaken external save folder blocks deletion before any files are removed");
        var managedImage=Path.Combine(data,"copy.z64");File.Move(original,managedImage);
        var copies=storage.Backups(game).Count;
        Check(await storage.RemoveInstallationAsync(game,keepSaves:false)==39&&!File.Exists(externalSave)&&!File.Exists(package)&&!File.Exists(managedImage),"explicit deletion removes the chosen progress and managed installation files including their game image copy");
        Check(storage.Backups(game).Count==copies+1&&File.Exists(legacy)&&File.Exists(extra),"progress deletion retains a recovery copy and other recognized save locations");
        await storage.RestoreAsync(game,storage.Backups(game)[0].File);Check(File.ReadAllText(externalSave)=="external progress","the safety copy restores progress explicitly removed during uninstall");
        var secondSave=Path.Combine(external,"second.sav");File.WriteAllText(secondSave,"second progress");
        var halted=new GameStorageService(Path.Combine(owned,"settings"),[game],()=>Directory.EnumerateFiles(external).Count()<2);
        await Refuse<InvalidOperationException>(()=>halted.RemoveInstallationAsync(game,keepSaves:false));
        await storage.RestoreAsync(game,storage.Backups(game)[0].File);
        Check(File.ReadAllText(externalSave)=="external progress"&&File.ReadAllText(secondSave)=="second progress","a save deletion interrupted by game activity has a complete restorable safety copy");
        Check(new GameStorageService(Path.Combine(owned,"settings"),[game],()=>false).Saves(game)?.Directory==external,"a manually selected save folder survives restart");
        storage.DeleteBackup(game,two.File);Check(!File.Exists(two.File)&&File.Exists(progress),"deleting a local snapshot leaves the player's save intact");
        Console.WriteLine("Game storage: save snapshots, rollback copies, identity/integrity checks, owned cleanup and custom locations passed.");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static async Task Refuse<T>(Func<Task> action) where T:Exception{try{await action();throw new Exception("Expected "+typeof(T).Name);}catch(T){}}
}
