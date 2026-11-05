using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

/// <summary>"Chercher mes jeux": recognises the player's own ROM/disc files in a picked folder.</summary>
public sealed partial class MobileLibraryView
{
    private const string FoundPrefix="Fichier trouvé · ";
    private readonly TextBlock _scanStatus=DownloadText(13);
    private CancellationTokenSource? _scanCancel;
    private IReadOnlyDictionary<string,string>? _foundGames;
    private IReadOnlyDictionary<string,string> FoundGames=>_foundGames??=new FoundGamesStore(_games.DataDirectory).Load();
    private bool ScanNeverRan=>!File.Exists(Path.Combine(_games.DataDirectory,"found-games.json"));

    private Control BuildScanSetting()
    {
        var content=new StackPanel{Spacing=8};content.Children.Add(new TextBlock{Text="Mes fichiers de jeux",FontSize=18});
        var note=DownloadText(13);note.Text="Retrouve tes ROM et images disque dans un dossier : les jeux reconnus passent en tête de la grille.";content.Children.Add(note);
        var scan=new Button{Name="ScanGamesButton",Content="Chercher mes jeux…",MinHeight=48,FontSize=14,HorizontalAlignment=HorizontalAlignment.Stretch};
        scan.Click+=(_,_)=>PickAndScan();
        content.Children.Add(scan);content.Children.Add(_scanStatus);
        return DownloadCard(content);
    }
    private async void PickAndScan()
    {
        if(_scanCancel is not null)return;
        try
        {
            if(TopLevel.GetTopLevel(this)?.StorageProvider is not {CanPickFolder:true} provider){_scanStatus.Text="Le choix d'un dossier n'est pas disponible sur cet appareil.";return;}
            var folders=await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Dossier de tes jeux"});
            using var folder=folders.FirstOrDefault();if(folder is null)return;
            await ScanGamesAsync(GameFolderWalker.Files(folder));
        }
        catch(Exception error){_scanStatus.Text="Recherche impossible : "+error.Message;}
    }
    /// <summary>Scans files against the whole mobile catalogue and remembers the finds; never throws.</summary>
    public async Task ScanGamesAsync(IAsyncEnumerable<GameFileMatcher.ScannedFile> files)
    {
        _scanCancel?.Cancel();using var cancel=_scanCancel=new CancellationTokenSource();
        _scanStatus.Text="Recherche en cours…";var shown=0L;
        try
        {
            var catalog=_allCatalog.Select(g=>new CatalogTitle(g.Id,g.Title,g.ShelfConsole)).ToArray();
            var found=await GameFileMatcher.ScanAsync(files,catalog,count=>
            {
                var now=Environment.TickCount64;if(now-shown<250)return;shown=now;_scanStatus.Text=$"{count} fichiers examinés…";
            },cancel.Token);
            _foundGames=new FoundGamesStore(_games.DataDirectory).Merge(found);
            var titles=found.Select(f=>_allCatalog.FirstOrDefault(g=>g.Id==f.GameId)?.Title).OfType<string>().Distinct().ToArray();
            _scanStatus.Text=titles.Length==0?"Aucun jeu reconnu dans ce dossier."
                :$"{titles.Length} {(titles.Length>1?"jeux reconnus":"jeu reconnu")} : {string.Join(", ",titles.Take(5))}{(titles.Length>5?"…":"")}";
            RefreshDualLibrary();
        }
        catch(OperationCanceledException){_scanStatus.Text="Recherche annulée.";}
        catch(Exception error){_scanStatus.Text="Recherche impossible : "+error.Message;}
        finally{if(_scanCancel==cancel)_scanCancel=null;}
    }
}
