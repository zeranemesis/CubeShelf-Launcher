using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;
using CubeShelf.Core.Storage;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async void OpenGameStorage(object? sender,RoutedEventArgs args)
    {
        if(OwnedWindows.Any(w=>w.IsVisible))return;
        LaunchTransition.Stop();CloseBigPictureMenu();
        try
        {
            var stored=_catalogGames.Where(g=>Directory.Exists(Path.Combine(_paths.DataDirectory,"NativePorts",g.Id))
                ||Directory.Exists(Path.Combine(_paths.DataDirectory,"Runtimes",g.Id))||_phase6PreparedData?.HasData(g.Id)==true);
            var games=PersonalLibraryGames().Concat(stored).GroupBy(g=>g.Id,StringComparer.OrdinalIgnoreCase).Select(g=>StorageEntry(g.First())).ToArray();
            var service=new GameStorageService(_paths.ConfigurationDirectory,games,()=>_sessions.RunningGameIds.Count>0||_playIntents.Count>0||_portableQueue?.Items.Any(i=>!i.IsTerminal)==true);
            var window=CreatePhase7Dialog(P7("Stockage et sauvegardes","Storage and saves"),900,740);
            var view=new GameStorageView(service,_selectedGame?.Id,id=>ResolveCoverPath(_catalogGames.FirstOrDefault(g=>g.Id==id)?.Covers.FirstOrDefault()?.Front));window.Content=view;
            view.CloseRequested+=()=>window.Close();
            window.Closed+=(_,_)=>view.Release();
            view.Changed+=_=>{RefreshLibraryParity();RefreshStoragePhase6();if(_selectedGame is not null)RefreshGameState();};
            window.Closing+=(_,e)=>{if(view.IsBusy){e.Cancel=true;view.RequestClose();}};
            window.KeyDown+=(_,e)=>
            {
                if(e.Key==Key.Escape){view.BackFromController();e.Handled=true;}
            };
            await window.ShowDialog(this);
        }
        catch(Exception error){ShowToastParity(P7("Stockage","Storage"),error.Message);}
    }

    private GameStorageEntry StorageEntry(GameCatalogEntry game)
    {
        var native=_nativeShelfPorts.GetValueOrDefault(game.Id);
        var executable=native is not null?new NativePortLibrary(_paths).Executable(native):_installer.GetStatus(game.Id).ExecutablePath;
        if(string.IsNullOrWhiteSpace(executable))executable=game.Executable;
        string? directory=null;
        try
        {
            if(!string.IsNullOrWhiteSpace(executable)&&Path.IsPathRooted(executable))
            {var candidate=Path.GetDirectoryName(Path.GetFullPath(executable));if(candidate is not null&&candidate.TrimEnd(Path.DirectorySeparatorChar)!=Path.GetPathRoot(candidate)?.TrimEnd(Path.DirectorySeparatorChar))directory=candidate;}
        }
        catch(ArgumentException) { }
        var owned=Path.GetFullPath(_paths.DataDirectory);var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        var managed=!string.IsNullOrWhiteSpace(executable)&&Path.GetFullPath(executable).StartsWith(owned.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,comparison);
        var partyboard=NativePortCatalog.GameKey(game)==NativePortCatalog.GameKey("gamecube","Mario Party 4");
        var data=native is not null?directory:!string.IsNullOrWhiteSpace(game.GameRoot)?game.GameRoot:directory is not null&&Directory.Exists(Path.Combine(directory,game.Id))?Path.Combine(directory,game.Id):null;
        if(data is not null&&!Path.IsPathRooted(data))data=directory is null?null:Path.Combine(directory,data);
        if(data is null&&_phase6PreparedData?.HasData(game.Id)==true)data=_phase6PreparedData.GetPath(game.Id);
        var archives=new List<string>();
        var managedRoot=native is not null?Path.Combine(owned,"NativePorts",native.Id):Path.Combine(owned,"Runtimes",game.Id);
        var hasManaged=Directory.Exists(managedRoot);var vault=_phase6PreparedData?.GetPath(game.Id);
        if(native is not null&&managed&&directory is not null)
        {
            var bucket=directory;
            if(Path.GetFileName(directory).Equals("runtime",StringComparison.OrdinalIgnoreCase))bucket=Path.GetDirectoryName(directory)!;
            if(Directory.Exists(bucket))archives.AddRange(Directory.EnumerateFiles(bucket).Where(file=>native.Assets.Any(a=>a.Name==Path.GetFileName(file))&&(file.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".tar.gz",StringComparison.OrdinalIgnoreCase))));
        }
        return new(){Id=game.Id,GameKey=NativePortCatalog.GameKey(game),Title=game.Title,Engine=native?.Project??game.RuntimeName,OwnedRoot=owned,
            EngineDirectory=hasManaged?managedRoot:directory,DataDirectory=native is not null&&hasManaged?managedRoot:data,SaveSearchDirectory=data,
            AdditionalDataDirectories=vault is not null&&Directory.Exists(vault)&&vault!=data?[vault]:[],
            RemovableDirectories=(hasManaged?new[]{managedRoot}:[]).Concat(vault is not null&&Directory.Exists(vault)?[vault]:[]).ToArray(),
            SavesDirectory=partyboard?PhoneLinkServer.DefaultSavesDirectory():null,
            SaveFiles=partyboard?GameSaveFiles.MemoryCards:GameSaveFiles.All,DownloadFiles=archives.ToArray(),
            RemovableEngine=managed&&native is not null&&Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase)?executable:null,EngineIsDirectory=false};
    }
}
