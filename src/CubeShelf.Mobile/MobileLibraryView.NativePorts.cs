using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly ComboBox _consolePicker=new() {ItemsSource=new[]{"GameCube","Nintendo 64"},SelectedIndex=0,MinHeight=40,FontSize=14,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ConsoleBrandMark _consoleLogo=new() {Width=26,Height=26};
    private readonly TextBlock _nativeNotes=new() {FontSize=13,TextWrapping=TextWrapping.Wrap,IsVisible=false};
    private readonly Button _cancelNativePreparation=MakeButton("Annuler la préparation");
    private readonly ProgressBar _nativePreparationProgress=new() {IsVisible=false,IsIndeterminate=true,Height=4};
    private CancellationTokenSource? _nativePreparation;
    private MobileNativePort? SelectedNativePort => _catalog.Length==0?null:_games.NativePorts?.Catalog.FirstOrDefault(p=>p.Id==_catalog[_selected].Id);
    private Control BuildPlatformHeading()
    {
        var heading=new StackPanel {Spacing=6,Margin=new Thickness(0,0,8,0)};
        var name=new StackPanel {Orientation=Orientation.Horizontal,Spacing=7};
        name.Children.Add(_consoleLogo);name.Children.Add(new TextBlock {Text="DecompShelf",FontSize=18,FontWeight=FontWeight.Bold,VerticalAlignment=VerticalAlignment.Center});
        heading.Children.Add(name);heading.Children.Add(_consolePicker);
        _consolePicker.IsVisible=_games.NativePorts?.Catalog.Count>0;
        _consolePicker.SelectionChanged+=(_,_)=>
        {
            if(!_realized||_launching||_nativePreparation is not null)return;
            var console=_consolePicker.SelectedIndex==1?"n64":"gamecube";
            if(console==_activeConsole)return;
            CloseInspection(false);ResetTouch();_activeConsole=console;_consoleLogo.Platform=console;
            _catalog=_allCatalog.Where(game=>game.ShelfConsole==console).ToArray();_selected=0;
            RebuildMobileShelf();Select(0,false);
        };
        return heading;
    }
    private IEnumerable<MobileGame> NativeCatalogGames() => (_games.NativePorts?.Catalog??[]).Select(p=>new MobileGame
    {
        Id=p.Id,Title=p.Title,ShelfConsole="n64",Genre=p.Project,Players=p.Status,
        Year=p.Id switch {"n64-sm64" or "n64-pilotwings"=>1996,"n64-oot"=>1998,"n64-mm"=>2000,_=>1999},
        Covers=[new MobileCover {Front=p.Front,Back=p.Back,Spine=p.Spine,Right=p.Right,Top=p.Top,Bottom=p.Bottom,Media=p.Media}]
    });
    private void RebuildMobileShelf()
    {
        _consoleLogo.Platform=_activeConsole;
        _shelf.SetGames(_catalog.Select(game=>
        {
            var cover=game.Covers.FirstOrDefault();
            return new GameShelfItem(game.Title,Cover(cover?.Front),Cover(cover?.Back),Cover(cover?.Spine),game.ShelfConsole,
                Cover(cover?.Right),Cover(cover?.Top),Cover(cover?.Bottom));
        }).ToArray());
    }
    private void RefreshNativeAvailability(MobileNativePort port)
    {
        var native=_games.NativePorts!;
        var installed=native.IsInstalled(port.Id);var issue=native.CompatibilityError(port.Id);
        var embedded=native.IsEmbedded(port.Id);
        var entry=DownloadManager?.Entries.FirstOrDefault(e=>e.GameId==port.Id);
        var state=entry?.Transfer.State??RuntimeDownloadState.NotInstalled;
        var ready=native.IsPackageReady(port.Id);
        _info.Text=port.Project+" · "+port.Status;
        _nativeNotes.Text=embedded?"Moteur intégré à DecompShelf. Choisis ta propre ROM Super Mario 64 US ou japonaise au format .z64 ; aucun APK de jeu à installer.":port.Notes;_nativeNotes.IsVisible=true;
        _availability.Text=installed?"Prêt à jouer · "+port.Version:issue??(embedded?"Moteur intégré · Ta ROM est nécessaire":ready?"APK vérifié · Installation à confirmer":TransferLabel(state));
        _availability.Foreground=installed?Brushes.LightGreen:Brushes.LightGray;
        if(embedded&&_nativePreparation is not null)_availability.Text="Préparation dans DecompShelf…";
        _play.IsVisible=installed;
        _download.IsVisible=_nativePreparation is null&&issue is null&&(!installed||native.IsUpdateAvailable(port.Id));
        _download.Content=ActionLabel(embedded?"＋":ready?"＋":"↓",embedded?"Choisir ma ROM Super Mario 64":ready?"Installer l’APK":state is RuntimeDownloadState.Downloading or RuntimeDownloadState.Queued or RuntimeDownloadState.Paused or RuntimeDownloadState.Verifying
            ?"Voir le téléchargement":installed?"Mettre à jour le port":"Télécharger le port");
        _unavailableAction.IsVisible=!installed&&issue is not null;_unavailableAction.Text=issue??"Port indisponible";
        _onlinePanel.IsVisible=false;_join.IsEnabled=false;
        ToolTip.SetTip(_download,_nativeNotes.Text);UpdateInputHint();
        Avalonia.Automation.AutomationProperties.SetName(_download,embedded?"Préparer Super Mario 64 dans DecompShelf":"Installer "+port.Title);
    }
    private async Task PrepareSelectedPortAsync()
    {
        if(SelectedNativePort is not { } port) {PreparePartyBoard();return;}
        if(_nativePreparation is not null)return;
        var native=_games.NativePorts!;
        using var preparation=new CancellationTokenSource();
        var embedded=native.IsEmbedded(port.Id);
        if(embedded)
        {
            _nativePreparation=preparation;
            _download.IsEnabled=false;_consolePicker.IsEnabled=false;
            _downloadsButton.IsEnabled=false;_appUpdatesButton.IsEnabled=false;
            _cancelNativePreparation.IsVisible=true;
            _nativePreparationProgress.IsVisible=true;
            RefreshAvailability();
            SetStatus("Préparation de ta ROM… Cela peut prendre quelques minutes.");
        }
        try
        {
            var ready=native.IsPackageReady(port.Id);
            var message=await native.PrepareAsync(port.Id,preparation.Token);
            SetStatus(message);
            if(!embedded&&!ready)ShowDownloads();
        }
        catch(OperationCanceledException) {SetStatus("Préparation annulée. Tes sauvegardes sont conservées.");}
        catch(Exception error) {SetStatus(error.Message);}
        finally
        {
            if(embedded)
            {
                _nativePreparation=null;_download.IsEnabled=true;_consolePicker.IsEnabled=true;
                _downloadsButton.IsEnabled=true;_appUpdatesButton.IsEnabled=true;
                _cancelNativePreparation.IsVisible=false;
                _nativePreparationProgress.IsVisible=false;
            }
        }
        RefreshAvailability();
    }
}
