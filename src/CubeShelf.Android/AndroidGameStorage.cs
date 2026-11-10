using Android.App;
using Android.Content;
using CubeShelf.Core.Library;
using CubeShelf.Core.Storage;

namespace CubeShelf.Android;

internal static class AndroidGameStorage
{
    private static GameStorageService? _instance;
    internal static GameStorageService Instance=>_instance??=Create();
    private static Context Context=>global::Android.App.Application.Context;
    private static GameStorageService Create()
    {
        var files=Context.FilesDir!.CanonicalPath!;var owned=new Java.IO.File(Context.ApplicationInfo!.DataDir!).CanonicalPath!;
        var games=new List<GameStorageEntry>
        {
            new(){Id="GMPE01_00",GameKey=NativePortCatalog.GameKey("gamecube","Mario Party 4"),Title="Mario Party 4",Engine="PartyBoard",OwnedRoot=owned,
                DataDirectory=Path.Combine(files,"partyboard"),SavesDirectory=Path.Combine(files,"partyboard"),SaveFiles=GameSaveFiles.MemoryCards,
                RemovableDirectories=[Path.Combine(files,"partyboard"),Path.Combine(files,"partyboard-downloads")],
                DownloadDirectories=[Path.Combine(files,"partyboard-downloads")]}
        };
        foreach(var port in AndroidNativePorts.Instance.Catalog)
        {
            var sm64=port.Id==AndroidSm64.Id;
            games.Add(new(){Id=port.Id,GameKey=NativePortCatalog.GameKey(port.ShelfConsole,port.Title),Title=port.Title,Engine=port.Project,OwnedRoot=owned,
                EngineDirectory=sm64?null:Path.Combine(files,"native-modules",port.Id),DataDirectory=Path.Combine(files,"native-games",sm64?"sm64":port.Id),
                SavesDirectory=sm64?Path.Combine(files,"native-games/sm64/saves"):null,
                RemovableDirectories=sm64?[Path.Combine(files,"native-games/sm64")]:[Path.Combine(files,"native-games",port.Id),Path.Combine(files,"native-modules",port.Id),Path.Combine(files,"native-port-packages",port.Id)],
                DownloadDirectories=sm64?[]:[Path.Combine(files,"native-port-packages",port.Id)],
                RemovableEngine=sm64?null:Path.Combine(files,"native-modules",port.Id),EngineIsDirectory=!sm64});
        }
        return new(Path.Combine(files,"storage"),games,Busy);
    }
    private static bool Busy()
    {
        if(NativeGameService.Downloads.Entries.Any(e=>e.Transfer.IsBusy))return true;
        var manager=Context.GetSystemService(Context.ActivityService) as ActivityManager;
        var prefix=Context.PackageName+":";var uid=Context.ApplicationInfo!.Uid;
        return manager?.RunningAppProcesses?.Any(p=>p.Uid==uid&&p.ProcessName is { } name&&name.StartsWith(prefix,StringComparison.Ordinal)
            &&(name.StartsWith(prefix+"engine_",StringComparison.Ordinal)||name.StartsWith(prefix+"partyboard",StringComparison.Ordinal)||name.StartsWith(prefix+"sm64",StringComparison.Ordinal)))==true;
    }
}
