using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CubeShelf.Desktop.Controls;
using CubeShelf.Core.Library;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private void NativePortsChanged()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{RefreshAvailability();RefreshDualSelection();});
    private readonly ComboBox _consolePicker=new() {MinHeight=44,FontSize=14,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ConsoleBrandMark _consoleLogo=new() {Width=30,Height=30};
    private readonly TextBlock _platformCaption = new() { FontSize = 12, Foreground = Brushes.LightGray };
    private ShelfProduct[] _mobilePlatforms = [];
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
        var home=new Button {Content=name,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
        Avalonia.Automation.AutomationProperties.SetName(home,"Retour aux consoles");
        home.Click+=(_,_)=>OpenPlatformHome();
        heading.Children.Add(home);heading.Children.Add(_consolePicker); heading.Children.Add(_platformCaption);
        _consolePicker.SelectionChanged+=(_,_)=>
        {
            if(!_realized||_launching||_nativePreparation is not null)return;
            if (_consolePicker.SelectedIndex < 0 || _consolePicker.SelectedIndex >= _mobilePlatforms.Length) return;
            var console=_mobilePlatforms[_consolePicker.SelectedIndex].Id;
            if(console==_activeConsole&&!_personalLibrary)return;
            _personalLibrary=false;
            CloseInspection(false);ResetTouch();_activeConsole=console;_consoleLogo.Platform=console;
            _catalog=_allCatalog.Where(game=>game.ShelfConsole==console).ToArray();_selected=0;
            RebuildMobileShelf();UpdatePersonalLibraryUi();Select(0);
        };
        return heading;
    }
    private void OpenPlatformHome()
    {
        if(_launching || _nativePreparation is not null)return;
        CloseRetroAchievements();
        if(_presentation.IsDual){RefreshDualLibrary(true);return;}
        _platformHome.Open(_mobilePlatforms.Select(p=>(p,_allCatalog.Count(g=>g.ShelfConsole==p.Id))),_activeConsole);
    }
    private Dictionary<string,string>? _portRegions;
    private string RegionsFile => Path.Combine(_games.DataDirectory,"port-regions.json");
    // One package per region family: the one picked in the game's options, else an installed one, else the first.
    private string ChosenRegion(string family,IReadOnlyList<MobileNativePort> variants)
    {
        if(_portRegions is null)
        {
            try{_portRegions=File.Exists(RegionsFile)&&new FileInfo(RegionsFile).Length<=16384
                ?System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(RegionsFile)):null;}
            catch(Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){}
            _portRegions??=[];
        }
        if(_portRegions.TryGetValue(family,out var id)&&variants.Any(p=>p.Id==id))return id;
        return (variants.FirstOrDefault(p=>_games.NativePorts?.IsInstalled(p.Id)==true)??variants[0]).Id;
    }
    private MobileNativePort[] RegionVariants(MobileNativePort port)=>port.RegionOf.Length==0?[port]
        :(_games.NativePorts?.Catalog??[]).Where(p=>p.RegionOf==port.RegionOf).ToArray();
    private void SwitchRegion(MobileNativePort port)
    {
        var variants=RegionVariants(port);if(variants.Length<2)return;
        var next=variants[(Array.FindIndex(variants,p=>p.Id==port.Id)+1)%variants.Length];
        _portRegions![port.RegionOf]=next.Id;
        try{File.WriteAllText(RegionsFile,System.Text.Json.JsonSerializer.Serialize(_portRegions));}
        catch(Exception error) when (error is IOException or UnauthorizedAccessException){}
        _allCatalog=_allCatalog.Select(g=>g.Id==port.Id?NativeGame(next):g).ToArray();
        _catalog=VisibleCatalog();_selected=Math.Max(0,Array.FindIndex(_catalog,g=>g.Id==next.Id));
        RebuildMobileShelf();Select(_selected);
    }
    private IEnumerable<MobileGame> NativeCatalogGames()
    {
        var ports=_games.NativePorts?.Catalog??[];
        return ports.Where(p=>p.RegionOf.Length==0||p.Id==ChosenRegion(p.RegionOf,RegionVariants(p))).Select(NativeGame);
    }
    private static MobileGame NativeGame(MobileNativePort p) => new MobileGame
    {
        Id=p.Id,Title=p.Title,ShelfConsole=p.ShelfConsole,Genre=p.Project,Players=p.Status,
        Year=p.Year > 0 ? p.Year : p.Id switch {"n64-sm64" or "n64-pilotwings"=>1996,"n64-oot"=>1998,"n64-mm"=>2000,_=>1999},
        Covers=[new MobileCover {Front=p.Front,Back=p.Back,Spine=p.Spine,Right=p.Right,Top=p.Top,Bottom=p.Bottom,Media=p.Media}]
    };
    private void UpdatePlatformCaption() => _platformCaption.Text = _personalLibrary
        ?$"Mes jeux · {_catalog.Length} jeu(x) · Toutes consoles":$"{_catalog.Length} jeu(x) · Android";
    private void RebuildMobileShelf()
    {
        RefreshDualLibrary();
        if(UsesHome&&!_dualDetail){_shelf.Clear();return;}
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
        var updating=native.IsUpdateAvailable(port.Id);
        var embedded=native.IsEmbedded(port.Id);
        var rom=embedded&&port.Id=="n64-sm64";
        var entry=DownloadManager?.Entries.FirstOrDefault(e=>e.GameId==port.Id);
        var state=entry?.Transfer.State??RuntimeDownloadState.NotInstalled;
        var ready=native.IsPackageReady(port.Id);
        _info.Text=port.Project+" · "+(embedded&&!rom?"Intégration expérimentale · partie non testée":port.Status);
        _nativeNotes.Text=rom?"Moteur intégré à DecompShelf. Choisis ta propre ROM Super Mario 64 US ou japonaise au format .z64 ; aucun APK de jeu à installer.":embedded?"Moteur hébergé dans DecompShelf. Au premier démarrage, importe les fichiers de ta propre copie du jeu.\n"+port.Notes:port.Notes;_nativeNotes.IsVisible=true;
        _availability.Text=issue??(installed?"Moteur prêt · "+port.Version:rom?"Moteur intégré · Ta ROM est nécessaire":ready?"Moteur vérifié · Préparation nécessaire":TransferLabel(state));
        _availability.Foreground=installed?Brushes.LightGreen:Brushes.LightGray;
        if(_nativePreparation is not null)_availability.Text="Préparation dans DecompShelf…";
        if(updating&&_nativePreparation is null)_availability.Text="Mise à jour du moteur disponible · "+port.Version;
        var standaloneUpdate=native.UpstreamUpdate(port.Id) is {CanInstall:true};
        _play.IsVisible=installed&&issue is null;
        _download.IsVisible=_nativePreparation is null&&issue is null&&(!installed||updating||standaloneUpdate);
        _download.Content=ActionLabel(rom?"＋":ready?"＋":"↓",rom?"Choisir ma ROM Super Mario 64":ready?embedded?"Préparer dans DecompShelf":"Installer l’APK":state is RuntimeDownloadState.Downloading or RuntimeDownloadState.Queued or RuntimeDownloadState.Paused or RuntimeDownloadState.Verifying
            ?"Voir le téléchargement":installed?"Mettre à jour le port":"Télécharger le port");
        if(updating&&state is not (RuntimeDownloadState.Downloading or RuntimeDownloadState.Queued or RuntimeDownloadState.Paused or RuntimeDownloadState.Verifying))
            _download.Content=ActionLabel(ready?"＋":"↓",ready?"Mettre à jour dans DecompShelf":"Télécharger la mise à jour du moteur");
        if(standaloneUpdate)_download.Content=ActionLabel("↓","Mettre à jour dans DecompShelf");
        _unavailableAction.IsVisible=issue is not null;_unavailableAction.Text=issue??"Port indisponible";
        _onlinePanel.IsVisible=false;_join.IsEnabled=false;
        ToolTip.SetTip(_download,_nativeNotes.Text);UpdateInputHint();
        Avalonia.Automation.AutomationProperties.SetName(_download,updating||standaloneUpdate?"Mettre à jour "+port.Title
            :embedded?"Préparer "+port.Title+" dans DecompShelf":"Installer "+port.Title);
    }
    private bool SelectedPortHasManagedUpdate()=>SelectedNativePort is {} port&&_games.NativePorts is {} native
        &&(native.IsUpdateAvailable(port.Id)||native.UpstreamUpdate(port.Id) is {CanInstall:true});

    private async Task PrepareSelectedPortAsync(bool update=false)
    {
        if(SelectedNativePort is not { } port) {PreparePartyBoard();return;}
        if(_nativePreparation is not null)return;
        var native=_games.NativePorts!;
        using var preparation=new CancellationTokenSource();
        var embedded=native.IsEmbedded(port.Id);
        if(embedded||update)
        {
            _nativePreparation=preparation;
            _download.IsEnabled=false;_consolePicker.IsEnabled=false;
            _libraryButton.IsEnabled=false;
            _downloadsButton.IsEnabled=false;_appUpdatesButton.IsEnabled=false;
            _cancelNativePreparation.IsVisible=true;
            _nativePreparationProgress.IsVisible=true;
            RefreshAvailability();
            SetStatus(update?"Mise à jour de "+port.Title+" en cours…":"");
        }
        try
        {
            var ready=update||native.IsPackageReady(port.Id);
            var message=update?await native.UpdateAsync(port.Id,preparation.Token)
                :await native.PrepareAsync(port.Id,preparation.Token);
            SetStatus(message);
            if(!ready&&(!embedded||port.Id!="n64-sm64"))
            {
                // The download remains in the queue after this short preparation request.
                _nativePreparation=null;ShowDownloads();
            }
        }
        catch(OperationCanceledException) {SetStatus("Préparation annulée. Tes sauvegardes sont conservées.");}
        catch(Exception error) {SetStatus(error.Message);}
        finally
        {
            if(embedded||update)
            {
                _nativePreparation=null;_download.IsEnabled=true;_consolePicker.IsEnabled=true;
                _libraryButton.IsEnabled=true;
                _downloadsButton.IsEnabled=true;_appUpdatesButton.IsEnabled=true;
                _cancelNativePreparation.IsVisible=false;
                _nativePreparationProgress.IsVisible=false;
            }
        }
        RefreshAvailability();
    }
}
