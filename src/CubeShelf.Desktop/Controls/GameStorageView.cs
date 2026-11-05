using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CubeShelf.Core.Storage;

namespace CubeShelf.Desktop.Controls;

/// <summary>The same storage and save operations are presented on desktop and mobile.</summary>
public sealed partial class GameStorageView : UserControl
{
    private readonly GameStorageService _service;
    private readonly Func<string,string?>? _coverPath;
    private readonly ComboBox _games=new(){HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
    private readonly ComboBox _backups=new(){HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
    private readonly StackPanel _sizes=new(){Spacing=0};
    private readonly TextBlock _space=Text(14),_savePath=Text(14),_status=Text(14),_backupStatus=Text(14);
    private readonly StackPanel _content=new(){Spacing=18,MaxWidth=1040,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly StackPanel _confirmation=new(){Spacing=10,IsVisible=false};
    private readonly ProgressBar _progress=new(){IsIndeterminate=true,Height=4,IsVisible=false};
    private readonly Button _close=MakeButton("Retour"),_cancel=MakeButton("Annuler l’opération"),_restore=MakeButton("Restaurer cette copie");
    private readonly Button _deleteCopy=MakeButton("Supprimer cette copie");
    private readonly List<Button> _actions=[];
    private readonly TextBlock _heading=Heading("Stockage des jeux");
    private ScrollViewer _details=null!;
    private GameSaveBackup[] _snapshots=[];
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _refresh;
    private bool _pickingFile;
    private Action? _discardConfirmation;
    public event Action? CloseRequested;
    public event Action<GameStorageEntry>? Changed;
    public event Action<bool>? BusyChanged;
    public bool IsBusy=>_operation is not null||_pickingFile;
    private GameStorageEntry? Game=>_games.SelectedItem as GameStorageEntry;

    public GameStorageView(GameStorageService service,string? selectedGame=null,Func<string,string?>? coverPath=null)
    {
        _service=service;_coverPath=coverPath;
        ApplyAppearance();
        var header=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=12,Margin=new Thickness(20,20,20,14),MaxWidth=1040,HorizontalAlignment=HorizontalAlignment.Stretch};
        _heading.FontSize=24;header.Children.Add(_heading);
        Grid.SetColumn(_close,1);header.Children.Add(_close);_close.Click+=(_,_)=>RequestClose();
        _games.ItemsSource=service.Games.OrderBy(g=>g.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
        _games.ItemTemplate=new FuncDataTemplate<GameStorageEntry>((game,_)=>new TextBlock{Text=game.Title,FontSize=15,TextTrimming=TextTrimming.CharacterEllipsis});
        _backups.ItemTemplate=new FuncDataTemplate<string>((copy,_)=>Text(14,copy));
        _games.SelectedItem=service.Games.FirstOrDefault(g=>g.Id==selectedGame)??service.Games.FirstOrDefault();
        var chooser=new StackPanel{Spacing=8};chooser.Children.Add(Muted("Jeu"));chooser.Children.Add(_games);_content.Children.Add(chooser);
        _content.Children.Add(_progress);_content.Children.Add(_cancel);_content.Children.Add(_status);_content.Children.Add(_confirmation);
        _status.IsVisible=false;_status.PropertyChanged+=(_,e)=>{if(e.Property==TextBlock.TextProperty)_status.IsVisible=!string.IsNullOrEmpty(_status.Text);};
        var usage=new StackPanel{Spacing=12};usage.Children.Add(Heading("Espace occupé"));usage.Children.Add(_space);usage.Children.Add(_sizes);
        _space.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted"));
        var savesContent=new StackPanel{Spacing=12};savesContent.Children.Add(Heading("Sauvegardes"));savesContent.Children.Add(_savePath);
        _savePath.MaxLines=2;_savePath.TextTrimming=TextTrimming.CharacterEllipsis;_savePath.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted"));
        var configure=ActionButton("Choisir le dossier",ConfigureSaves);configure.HorizontalAlignment=HorizontalAlignment.Left;savesContent.Children.Add(configure);
        var saves=new WrapPanel{Orientation=Orientation.Horizontal};
        var create=ActionButton("Créer une copie locale",()=>Run(async token=>
        {
            if(Game is not { } game)return;var backup=await _service.BackupAsync(game,token);_status.Text=$"Copie créée · {backup.FileCount} fichier(s).";
        }));create.Classes.Add("storage-primary");saves.Children.Add(create);
        saves.Children.Add(ActionButton("Exporter…",Export));saves.Children.Add(ActionButton("Importer…",Import));savesContent.Children.Add(saves);
        savesContent.Children.Add(_backupStatus);savesContent.Children.Add(_backups);
        _backupStatus.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted"));
        _restore.Click+=(_,_)=>{if(_backups.SelectedIndex>=0&&_backups.SelectedIndex<_snapshots.Length)Confirm("Restaurer cette copie ?","Les fichiers actuels seront copiés avant remplacement. Ferme le jeu ; le format de sauvegarde doit correspondre à ce moteur.",()=>Restore(_snapshots[_backups.SelectedIndex].File));};
        _actions.Add(_restore);savesContent.Children.Add(_restore);
        _deleteCopy.Click+=(_,_)=>
        {
            if(Game is not { } game||_backups.SelectedIndex<0||_backups.SelectedIndex>=_snapshots.Length)return;
            var snapshot=_snapshots[_backups.SelectedIndex];Confirm("Supprimer cette copie locale ?","Les sauvegardes utilisées par le jeu ne seront pas modifiées.",()=>Run(_=>
            {_service.DeleteBackup(game,snapshot.File);_status.Text="Copie locale supprimée. La progression du jeu est conservée.";return Task.CompletedTask;}));
        };_actions.Add(_deleteCopy);_deleteCopy.Classes.Add("storage-danger");savesContent.Children.Add(_deleteCopy);
        var cleanup=new StackPanel{Spacing=12};cleanup.Children.Add(Heading("Libérer de l’espace"));
        cleanup.Children.Add(Muted("Le nettoyage conserve le jeu. La suppression de l’installation demande une confirmation séparée."));
        var cleanupActions=new WrapPanel();cleanup.Children.Add(cleanupActions);
        cleanupActions.Children.Add(ActionButton("Nettoyer les téléchargements",()=>Confirm("Nettoyer les téléchargements ?","Les paquets téléchargés seront retirés. Les données du jeu, le moteur déjà préparé et les sauvegardes restent en place.",()=>Run(async _=>
        {
            if(Game is not { } game)return;var bytes=await Task.Run(()=>_service.CleanDownloads(game));_status.Text=$"Téléchargements nettoyés · {Size(bytes)} libérés.";Changed?.Invoke(game);
        }))));
        var remove=ActionButton("Retirer le moteur",()=>
        {
            if(Game is not { } game)return;
            if(game.RemovableEngine is null){_status.Text="Ce moteur est inclus dans l’application ou associé depuis un dossier externe. Il ne sera pas supprimé ici.";return;}
            Confirm("Retirer le moteur ?",game.EngineIsDirectory?"Le code du moteur sera retiré. Les fichiers du jeu et les sauvegardes seront conservés.":"Le programme géré par DecompShelf sera retiré. Ses dépendances, les fichiers du jeu et les sauvegardes seront conservés.",()=>Run(async _=>
            {
                var bytes=await Task.Run(()=>_service.RemoveEngine(game));_status.Text=$"Programme retiré · {Size(bytes)} libérés. Données et sauvegardes conservées.";Changed?.Invoke(game);
            }));
        });remove.Classes.Add("storage-danger");cleanupActions.Children.Add(remove);
        var uninstall=ActionButton("Supprimer l’installation",()=>
        {
            if(Game is not { } game)return;
            if(game.RemovableDirectories.Length==0){_status.Text="Ce jeu est associé depuis un dossier externe. Ses fichiers ne seront pas supprimés ici.";return;}
            var location=_service.Saves(game);var unknown=location is null;
            var choices=new StackPanel{Spacing=6};choices.Children.Add(Heading("Conserver la sauvegarde ?"));
            RadioButton SaveChoice(string label,bool selected)
            {
                var text=Text(14,label);var radio=new RadioButton{Content=text,GroupName="storage-save-choice",IsChecked=selected,MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                radio.SizeChanged+=(_,_)=>text.MaxWidth=Math.Max(80,radio.Bounds.Width-40);return radio;
            }
            var keep=SaveChoice("Oui, conserver ma sauvegarde",true);var delete=SaveChoice("Non, supprimer la sauvegarde du jeu",false);
            keep.Name="StorageKeepSaves";delete.Name="StorageDeleteSaves";
            choices.Children.Add(keep);choices.Children.Add(delete);var consequence=Muted(unknown?"Dossier de sauvegarde non détecté. Indique-le d’abord si tu as déjà joué.":"La progression reste à son emplacement actuel.");choices.Children.Add(consequence);
            delete.IsCheckedChanged+=(_,_)=>consequence.Text=delete.IsChecked==true
                ?location is null?"Aucun dossier de sauvegarde connu. Les copies locales restent disponibles.":"Sauvegardes utilisées par le jeu retirées du dossier : "+location.Directory+". La copie de sécurité est conservée."
                :unknown?"Dossier de sauvegarde non détecté. Indique-le d’abord si tu as déjà joué.":"La progression reste à son emplacement actuel.";
            Confirm("Supprimer l’installation de "+game.Title+" ?","L’installation gérée par DecompShelf sera retirée. Une copie des sauvegardes connues sera créée avant suppression. Les copies locales restent disponibles.",()=>Run(async token=>
            {
                var retain=keep.IsChecked!=false;
                var bytes=await _service.RemoveInstallationAsync(game,unknown,token,keepSaves:retain);_status.Text=$"Installation retirée · {Size(bytes)} supprimés. "+(retain?"Sauvegardes reconnues conservées.":"Sauvegardes du dossier choisi retirées ; copies locales conservées.");Changed?.Invoke(game);
            }),requireNoSavesAcknowledgement:unknown,choices:choices);
        });uninstall.Classes.Add("storage-danger");cleanupActions.Children.Add(uninstall);
        var sections=new Grid{ColumnSpacing=18,RowSpacing=18};
        var usageCard=Card(usage);usageCard.Name="StorageUsageCard";var savesCard=Card(savesContent);savesCard.Name="StorageSavesCard";var cleanupCard=Card(cleanup);
        sections.Children.Add(usageCard);sections.Children.Add(savesCard);sections.Children.Add(cleanupCard);_content.Children.Add(sections);
        void Reflow()
        {
            var wide=Bounds.Width>=760;sections.ColumnDefinitions=new(wide?"2*,3*":"*");sections.RowDefinitions=new(wide?"Auto,Auto":"Auto,Auto,Auto");
            Grid.SetColumn(savesCard,wide?1:0);Grid.SetRow(savesCard,wide?0:1);Grid.SetRow(cleanupCard,wide?1:2);Grid.SetColumnSpan(cleanupCard,wide?2:1);
        }
        var wideLayout=false;
        SizeChanged+=(_,_)=>{var wide=Bounds.Width>=760;if(wide==wideLayout)return;wideLayout=wide;Reflow();};Reflow();
        _cancel.IsVisible=false;_cancel.Click+=(_,_)=>_operation?.Cancel();
        var help=Muted("Les copies restent sur cet appareil. Export/import ne convertit pas les formats entre moteurs.");help.FontSize=13;_content.Children.Add(help);
        var layout=new Grid{RowDefinitions=new("Auto,*")};layout.Children.Add(header);
        _details=new ScrollViewer{Content=_content,IsVisible=false,Margin=new Thickness(20,0,20,20),HorizontalScrollBarVisibility=global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};Grid.SetRow(_details,1);layout.Children.Add(_details);InitializeInventory(layout);Content=layout;
        _games.SelectionChanged+=(_,_)=>{DiscardConfirmation();_confirmation.IsVisible=false;_status.Text="";_=Refresh();};
        AttachedToVisualTree+=(_,_)=>{if(_inventory.IsVisible)_=RefreshInventory();else _=Refresh();};
        DetachedFromVisualTree+=(_,_)=>{_inventoryScan?.Cancel();_refresh?.Cancel();if(!_pickingFile)_operation?.Cancel();};
        Avalonia.Automation.AutomationProperties.SetName(_games,"Jeu dont gérer le stockage");
        Avalonia.Automation.AutomationProperties.SetName(_backups,"Copies locales disponibles");
    }
    public void RequestClose(){if(IsBusy){_operation?.Cancel();return;}if(!_inventory.IsVisible){ShowInventory();return;}_inventoryScan?.Cancel();CloseRequested?.Invoke();}
    public void Release(){_inventoryScan?.Cancel();_refresh?.Cancel();_operation?.Cancel();DiscardConfirmation();}
    private void DiscardConfirmation()
    {
        var discard=_discardConfirmation;_discardConfirmation=null;
        try{discard?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
    public void ActivateFromController()
    {
        if(_inventory.IsVisible){if(_inventoryList.SelectedItem is GameStorageItem item)OpenDetails(item.Game);else if(_inventoryList.ItemCount>0)_inventoryList.SelectedIndex=0;return;}
        var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if(focused is RadioButton radio&&radio.GetVisualAncestors().Contains(this)){radio.IsChecked=true;return;}
        if(focused is CheckBox check&&check.GetVisualAncestors().Contains(this)){check.IsChecked=check.IsChecked!=true;return;}
        if(focused is ComboBox combo&&combo.GetVisualAncestors().Contains(this)){combo.IsDropDownOpen=!combo.IsDropDownOpen;return;}
        if(focused is Button button&&button.GetVisualAncestors().Contains(this)&&button.IsEnabled)button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        else _games.Focus();
    }
    public void MoveFromController(int direction)
    {
        if(_inventory.IsVisible){_inventoryList.SelectedIndex=Math.Clamp(_inventoryList.SelectedIndex+direction,0,Math.Max(0,_inventoryList.ItemCount-1));_inventoryList.Focus();if(_inventoryList.SelectedItem is { } item)_inventoryList.ScrollIntoView(item);return;}
        var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if(focused is ComboBox combo&&combo.IsDropDownOpen){combo.SelectedIndex=Math.Clamp(combo.SelectedIndex+direction,0,Math.Max(0,combo.ItemCount-1));return;}
        var controls=this.GetVisualDescendants().OfType<Control>().Where(c=>c is Button or ComboBox or CheckBox or RadioButton&&c.IsEffectivelyVisible&&c.IsEnabled&&c.Focusable).ToArray();
        if(controls.Length==0)return;var index=Array.FindIndex(controls,c=>ReferenceEquals(c,focused));var next=controls[(index+direction+controls.Length)%controls.Length];next.Focus();next.BringIntoView();
    }
    public void BackFromController()
    {
        if(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is ComboBox {IsDropDownOpen:true} combo){combo.IsDropDownOpen=false;return;}
        RequestClose();
    }
    private async Task Refresh()
    {
        _refresh?.Cancel();_refresh?.Dispose();_refresh=new();var token=_refresh.Token;
        if(Game is not { } game){_sizes.Children.Clear();_sizes.Children.Add(Text(15,"Aucun jeu ajouté. Prépare ou ajoute d’abord un jeu à ta bibliothèque."));foreach(var button in _actions)button.IsEnabled=false;return;}
        try
        {
            var usage=await Task.Run(()=>_service.Measure(game,token),token);if(Game!=game||token.IsCancellationRequested)return;
            _space.Text=usage.AvailableBytes is { } free?$"{Size(free)} disponibles sur le stockage de DecompShelf":"Espace disponible non communiqué par ce stockage.";
            _sizes.Children.Clear();AddSize(usage.Combined?"Moteur et fichiers du jeu":"Moteur",usage.EngineBytes);
            if(!usage.Combined||usage.GameBytes>0)AddSize(usage.Combined?"Autres fichiers du jeu":"Fichiers du jeu",usage.GameBytes);
            AddSize("Sauvegardes",usage.SaveBytes);AddSize("Téléchargements",usage.DownloadBytes);AddSize("Copies locales",usage.BackupBytes);
            AddSize("Total pour ce jeu",usage.TotalBytes);
            _savePath.Text=_service.Saves(game) is { } location?(OperatingSystem.IsAndroid()||Bounds.Width<600?"Dossier : "+Path.GetFileName(location.Directory):"Dossier : "+location.Directory):"Aucune sauvegarde détectée. Lance le jeu une fois ou choisis son dossier de sauvegardes.";
            ToolTip.SetTip(_savePath,_service.Saves(game)?.Directory);
            _snapshots=(await Task.Run(()=>_service.Backups(game))).ToArray();if(Game!=game)return;
            _backups.ItemsSource=_snapshots.Select(b=>$"{b.CreatedAt.ToLocalTime():g} · {b.FileCount} fichier(s) · {Size(b.Bytes)}").ToArray();_backups.SelectedIndex=_snapshots.Length>0?0:-1;
            _backups.IsVisible=_restore.IsVisible=_deleteCopy.IsVisible=_snapshots.Length>0;
            _backupStatus.Text=_snapshots.Length==0?"Aucune copie locale pour ce jeu.":$"{_snapshots.Length} copie(s) locale(s).";
        }
        catch(OperationCanceledException) { }
        catch(Exception error){_status.Text=error.Message;}
    }
    private void ConfigureSaves()
    {
        if(Game is not { } game||IsBusy)return;
        if(OperatingSystem.IsAndroid()&&game.DataDirectory is { } root)
        {
            DiscardConfirmation();
            var folders=Directory.Exists(root)?Directory.EnumerateDirectories(root).Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0).OrderBy(Path.GetFileName).ToArray():[];
            if(folders.Length==0){_status.Text="Aucun dossier disponible. Lance le moteur et crée une sauvegarde avant de revenir ici.";return;}
            _confirmation.Children.Clear();_confirmation.Children.Add(Text(14,"Choisis uniquement le dossier contenant la progression du jeu."));
            var choose=new ComboBox{ItemsSource=folders.Select(Path.GetFileName).ToArray(),SelectedIndex=0,MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};_confirmation.Children.Add(choose);
            var use=MakeButton("Utiliser ce dossier");use.Click+=(_,_)=>{try{_service.SetSavesDirectory(game,folders[choose.SelectedIndex]);_confirmation.IsVisible=false;_=Refresh();}catch(Exception e){_status.Text=e.Message;}};_confirmation.Children.Add(use);_confirmation.IsVisible=true;return;
        }
        Run(async _=>
        {
            var provider=TopLevel.GetTopLevel(this)?.StorageProvider;if(provider?.CanPickFolder!=true)throw new InvalidOperationException("Sélection de dossier indisponible.");
            var folders=await Pick(()=>provider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Dossier des sauvegardes de "+game.Title,AllowMultiple=false}));
            if(folders.Count==0)return;using var folder=folders[0];var path=folder.TryGetLocalPath()??throw new InvalidOperationException("Choisis un dossier local accessible à DecompShelf.");
            _service.SetSavesDirectory(game,path);_status.Text="Dossier des sauvegardes mémorisé.";
        });
    }
    private void Export()=>Run(async token=>
    {
        if(Game is not { } game)return;var snapshot=await _service.BackupAsync(game,token);
        var provider=TopLevel.GetTopLevel(this)?.StorageProvider;if(provider?.CanSave!=true)throw new InvalidOperationException("Export indisponible. La copie locale a été conservée.");
        using var picked=await Pick(()=>provider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Exporter les sauvegardes de "+game.Title,SuggestedFileName="DecompShelf-"+game.Id+"-"+DateTime.Now.ToString("yyyyMMdd")+".zip",DefaultExtension="zip",FileTypeChoices=[new("Sauvegardes DecompShelf"){Patterns=["*.zip"]}]}));
        if(picked is null){_status.Text="Export annulé. La copie locale est conservée.";return;}
        await using var output=await picked.OpenWriteAsync();if(output.CanSeek)output.SetLength(0);await using var input=File.OpenRead(snapshot.File);await input.CopyToAsync(output,token);_status.Text="Sauvegardes exportées. La copie locale est aussi conservée.";
    });
    private async void Import()
    {
        if(IsBusy||Game is not { } game)return;
        string? temporary=null;
        try
        {
            var provider=TopLevel.GetTopLevel(this)?.StorageProvider;if(provider?.CanOpen!=true)throw new InvalidOperationException("Sélection de fichier indisponible.");
            var picked=await Pick(()=>provider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Importer les sauvegardes de "+game.Title,AllowMultiple=false,FileTypeFilter=[new("Sauvegardes DecompShelf"){Patterns=["*.zip"]}]}));
            if(picked.Count==0)return;
            using var chosen=picked[0];GameSaveDetails? details=null;
            await RunAsync(async token=>
            {
                var directory=Path.Combine(_service.BackupsDirectory,"imports");Directory.CreateDirectory(directory);temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".zip");
                await using(var input=await chosen.OpenReadAsync())await using(var output=File.Create(temporary))
                {
                    var buffer=new byte[131072];long total=0;int count;
                    while((count=await input.ReadAsync(buffer,token))>0){total+=count;if(total>2L*1024*1024*1024+16L*1024*1024)throw new InvalidDataException("Archive trop volumineuse.");await output.WriteAsync(buffer.AsMemory(0,count),token);}
                }
                details=await Task.Run(()=>GameSaveArchive.Inspect(temporary),token);
                if(details.GameKey!=game.GameKey){details=null;throw new InvalidDataException("Cette archive appartient à un autre jeu.");}
            });
            if(details is null){if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);return;}
            var importFile=temporary!;temporary=null;
            Confirm("Importer ces sauvegardes ?",$"{details.Title} · {details.Engine} · {details.Files} fichier(s).\nMoteur choisi : {game.Engine}. Le format doit être compatible. Les sauvegardes actuelles seront copiées avant remplacement.",()=>Run(async token=>
            {
                try{var result=await _service.RestoreAsync(game,importFile,token);_status.Text=$"{result.Files} fichier(s) de sauvegarde importé(s).";Changed?.Invoke(game);}
                finally{if(File.Exists(importFile))File.Delete(importFile);}
            }),()=>{if(File.Exists(importFile))File.Delete(importFile);});
        }
        catch(Exception error){_status.Text=error.Message;if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);}
    }
    private void Restore(string file)=>Run(async token=>{if(Game is { } game){var result=await _service.RestoreAsync(game,file,token);_status.Text=$"{result.Files} fichier(s) restauré(s). La copie précédente reste disponible.";Changed?.Invoke(game);}});
    private void Confirm(string title,string explanation,Action action,Action? discarded=null,bool requireNoSavesAcknowledgement=false,Control? choices=null)
    {
        if(IsBusy){discarded?.Invoke();return;}DiscardConfirmation();_discardConfirmation=discarded;_confirmation.Children.Clear();_confirmation.Children.Add(Heading(title));_confirmation.Children.Add(Text(14,explanation));
        if(choices is not null)_confirmation.Children.Add(choices);
        var row=new WrapPanel();var apply=MakeButton("Confirmer");apply.Classes.Add("storage-primary");var cancel=MakeButton("Annuler");apply.Click+=(_,_)=>{_confirmation.IsVisible=false;_discardConfirmation=null;action();};cancel.Click+=(_,_)=>{_confirmation.IsVisible=false;DiscardConfirmation();};row.Children.Add(apply);row.Children.Add(cancel);_confirmation.Children.Add(row);_confirmation.IsVisible=true;
        Control initialFocus=apply;
        if(requireNoSavesAcknowledgement)
        {
            var label=Text(14,"J’ai vérifié : aucune progression non détectée n’est à conserver dans ces fichiers.");
            var check=new CheckBox{Content=label,MinHeight=48,Margin=new Thickness(0,0,0,8),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            check.SizeChanged+=(_,_)=>label.MaxWidth=Math.Max(80,check.Bounds.Width-40);initialFocus=check;
            apply.IsEnabled=false;check.IsCheckedChanged+=(_,_)=>apply.IsEnabled=check.IsChecked==true;_confirmation.Children.Insert(_confirmation.Children.Count-1,check);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>{initialFocus.Focus();_confirmation.BringIntoView();},Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private async Task<T> Pick<T>(Func<Task<T>> select)
    {
        _pickingFile=true;BusyChanged?.Invoke(true);
        try{return await select();}finally{_pickingFile=false;if(_operation is null)BusyChanged?.Invoke(false);}
    }
    private async void Run(Func<CancellationToken,Task> action)=>await RunAsync(action);
    private async Task RunAsync(Func<CancellationToken,Task> action)
    {
        if(IsBusy)return;using var operation=new CancellationTokenSource();_operation=operation;_status.Text="";BusyChanged?.Invoke(true);_games.IsEnabled=false;_progress.IsVisible=_cancel.IsVisible=true;foreach(var button in _actions)button.IsEnabled=false;
        try{_service.RequireIdle();await action(operation.Token);}
        catch(OperationCanceledException){_status.Text="Opération interrompue. Les copies locales existantes sont conservées.";}
        catch(Exception error){_status.Text=error.Message;}
        finally{_operation=null;_games.IsEnabled=true;_progress.IsVisible=_cancel.IsVisible=false;foreach(var button in _actions)button.IsEnabled=true;BusyChanged?.Invoke(false);await Refresh();}
    }
    private Button ActionButton(string label,Action action){var button=MakeButton(label);button.Click+=(_,_)=>action();_actions.Add(button);return button;}
    private void ApplyAppearance()
    {
        // Desktop supplies these tokens; the shared mobile view uses its existing blue-black palette.
        if(Application.Current?.TryGetResource("Panel",ActualThemeVariant,out _)!=true)
        {
            ResourceDictionary Palette(string[] colors)
            {
                var keys=new[]{"Bg","Panel","Panel2","Border","Text","Muted","HoverBg","Accent","Accent2","Danger"};var palette=new ResourceDictionary();
                for(var i=0;i<keys.Length;i++)palette[keys[i]]=new SolidColorBrush(Color.Parse(colors[i]));return palette;
            }
            Resources.ThemeDictionaries[ThemeVariant.Dark]=Palette(["#0B101B","#151D2D","#1A2538","#34435E","#F8FAFF","#B7C3DA","#2A3852","#8A76FF","#A895FF","#FF91A8"]);
            Resources.ThemeDictionaries[ThemeVariant.Light]=Palette(["#F2F4F9","#FFFFFF","#F8F9FC","#D8DEEA","#151927","#58657F","#E9EDFA","#6347EE","#7359F0","#B8294A"]);
        }
        foreach(var (theme,color) in new[]{(ThemeVariant.Dark,"#100B22"),(ThemeVariant.Light,"#FFFFFF")})
        {
            var palette=Resources.ThemeDictionaries.TryGetValue(theme,out var existing)?(ResourceDictionary)existing:new ResourceDictionary();
            palette["StoragePrimaryText"]=new SolidColorBrush(Color.Parse(color));Resources.ThemeDictionaries[theme]=palette;
        }
        void Add(Func<Selector?,Selector> selector,params Setter[] setters){var style=new Style(selector);foreach(var setter in setters)style.Setters.Add(setter);Styles.Add(style);}
        static Setter Brush(AvaloniaProperty property,string key)=>new(property,new DynamicResourceExtension(key));
        Add(s=>s.OfType<Button>(),Brush(Button.BackgroundProperty,"Panel2"),Brush(Button.ForegroundProperty,"Text"),Brush(Button.BorderBrushProperty,"Border"),
            new(Button.BorderThicknessProperty,new Thickness(1)),new(Button.CornerRadiusProperty,new CornerRadius(12)),new(Button.FontWeightProperty,FontWeight.SemiBold));
        Add(s=>s.OfType<Button>().Class("storage-primary"),Brush(Button.BackgroundProperty,"Accent"),Brush(Button.BorderBrushProperty,"Accent"),Brush(Button.ForegroundProperty,"StoragePrimaryText"));
        Add(s=>s.OfType<Button>().Class("storage-danger"),Brush(Button.ForegroundProperty,"Danger"));
        Add(s=>s.OfType<Button>().Class(":pointerover"),Brush(Button.BackgroundProperty,"HoverBg"),Brush(Button.BorderBrushProperty,"Accent"));
        Add(s=>s.OfType<Button>().Class(":pressed"),Brush(Button.BackgroundProperty,"HoverBg"));
        Add(s=>s.OfType<Button>().Class(":focus-visible"),Brush(Button.BorderBrushProperty,"Accent"),new(Button.BorderThicknessProperty,new Thickness(2)));
        Add(s=>s.OfType<Button>().Class(":disabled"),new Setter(Button.OpacityProperty,.55));
        foreach(var state in new[]{":pointerover",":pressed"})
        {
            Add(s=>s.OfType<Button>().Class(state).Template().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"),
                Brush(global::Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty,"HoverBg"),Brush(global::Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty,"Text"),Brush(global::Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty,"Accent"));
            Add(s=>s.OfType<Button>().Class("storage-primary").Class(state).Template().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"),
                Brush(global::Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty,"Accent2"),Brush(global::Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty,"StoragePrimaryText"));
            Add(s=>s.OfType<Button>().Class("storage-danger").Class(state).Template().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"),
                Brush(global::Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty,"Danger"));
        }
        Add(s=>s.OfType<ComboBox>(),Brush(ComboBox.BackgroundProperty,"Panel2"),Brush(ComboBox.ForegroundProperty,"Text"),Brush(ComboBox.BorderBrushProperty,"Border"),
            new(ComboBox.CornerRadiusProperty,new CornerRadius(12)),new(ComboBox.PaddingProperty,new Thickness(12,10)),new(ComboBox.FontSizeProperty,15d));
        Add(s=>s.OfType<ComboBox>().Class(":focus-visible"),Brush(ComboBox.BorderBrushProperty,"Accent"));
        this.Bind(BackgroundProperty,new DynamicResourceExtension("Bg"));this.Bind(ForegroundProperty,new DynamicResourceExtension("Text"));
        _progress.Bind(ProgressBar.ForegroundProperty,new DynamicResourceExtension("Accent"));
    }
    private void AddSize(string label,long bytes)
    {
        var row=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=12,Margin=new Thickness(0,8)};row.Children.Add(Muted(label));
        var value=Text(15,Size(bytes));value.FontWeight=FontWeight.SemiBold;Grid.SetColumn(value,1);row.Children.Add(value);_sizes.Children.Add(row);
    }
    private static Border Card(Control content)
    {
        var card=new Border{Child=content,Padding=new Thickness(18),CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Top};
        card.Bind(Border.BackgroundProperty,new DynamicResourceExtension("Panel"));card.Bind(Border.BorderBrushProperty,new DynamicResourceExtension("Border"));return card;
    }
    private static TextBlock Muted(string value){var text=Text(14,value);text.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted"));return text;}
    private static Button MakeButton(string text)=>new(){Content=text,MinHeight=48,FontSize=14,Padding=new Thickness(14,10),Margin=new Thickness(0,0,8,8),VerticalContentAlignment=VerticalAlignment.Center};
    private static TextBlock Text(double size,string? value=null)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap};
    private static TextBlock Heading(string text)=>new(){Text=text,FontSize=19,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
    private static string Size(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/(1024d*1024*1024):0.##} Go":bytes>=1024*1024?$"{bytes/(1024d*1024):0.#} Mo":bytes>=1024?$"{bytes/1024d:0.#} Ko":$"{bytes} o";
}
