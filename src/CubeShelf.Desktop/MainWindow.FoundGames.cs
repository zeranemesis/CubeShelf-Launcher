using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async void FindMyGames(object? sender,RoutedEventArgs args)
    {
        var picked=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title=P7("Dossier de tes jeux","Your games folder"),AllowMultiple=false});
        if(picked.FirstOrDefault() is not {} folder)return;
        FindGamesButton.IsEnabled=false;
        try
        {
            var summary=await ScanGameFolderAsync(folder,text=>FindGamesStatusText.Text=text);
            FindGamesStatusText.Text=summary;
            if(!SettingsView.IsVisible)ShowToastParity(P7("Tes jeux","Your games"),summary);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){FindGamesStatusText.Text=e.Message;}
        finally{FindGamesButton.IsEnabled=true;}
    }

    /// <summary>Finds the player's own game files in a folder, links them to the catalogue and returns a one-line summary.</summary>
    internal Task<string> ScanGameFolderAsync(IStorageFolder folder,Action<string> status)=>ScanGameFilesAsync(GameFolderWalker.Files(folder),status);

    internal async Task<string> ScanGameFilesAsync(IAsyncEnumerable<GameFileMatcher.ScannedFile> files,Action<string> status)
    {
        var catalog=_catalogGames.Where(g=>!GameVariants.IsVariant(g)).Select(g=>new CatalogTitle(g.Id,g.Title,g.ShelfConsole)).ToArray();
        var clock=Stopwatch.StartNew();long next=0;var done=false;
        // At most five updates a second; the last one must not overwrite the summary.
        void Progress(int seen)
        {
            if(clock.ElapsedMilliseconds<next)return;next=clock.ElapsedMilliseconds+200;
            Dispatcher.UIThread.Post(()=>{if(!done)status(P7($"{seen} fichiers examinés…",$"{seen} files checked…"));});
        }
        status(P7("Recherche de tes jeux…","Looking for your games…"));
        var found=await Task.Run(()=>GameFileMatcher.ScanAsync(files,catalog,Progress));
        done=true;
        new FoundGamesStore(_paths.DataDirectory).Merge(found);
        var linked=false;
        foreach(var hit in found)
        {
            if(_catalogGames.FirstOrDefault(g=>g.Id==hit.GameId) is not {RuntimeOwnsSetup:false} game||!string.IsNullOrWhiteSpace(game.DiscImage))continue;
            // Same accepted formats as DiscImageService.Inspect.
            var formats=game.DataPreparation==GameDataPreparation.Runtime?game.DiscFormats:[".iso",".gcm",".rvz"];
            if(!formats.Contains(Path.GetExtension(hit.Path),StringComparer.OrdinalIgnoreCase))continue;
            game.DiscImage=hit.Path;_profileStore.Save(new GameProfileEntry(game.Id,game.Executable,game.DiscImage));linked=true;
        }
        if(linked)PersistCatalog();
        RefreshLibraryParity();
        var titles=found.Select(f=>_catalogGames.First(g=>g.Id==f.GameId).Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(titles.Length==0)return P7("Aucun jeu reconnu dans ce dossier.","No game recognised in this folder.");
        var list=string.Join(", ",titles.Take(5))+(titles.Length>5?"…":"");
        return titles.Length==1?P7($"1 jeu reconnu : {list}",$"1 game recognised: {list}"):P7($"{titles.Length} jeux reconnus : {list}",$"{titles.Length} games recognised: {list}");
    }
}
