using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly Border _gameOptionsOverlay=new(){IsVisible=false,ZIndex=110,Background=new SolidColorBrush(Color.Parse("#B3000000"))};
    private readonly List<Button> _gameOptionButtons=[];
    private int _gameOptionIndex;
    private Control? _gameOptionsReturnFocus;
    private Action? _highlightGameOption;
    public void OptionsFromController()
    {
        SetControllerInput(true);
        if(_gameOptionsOverlay.IsVisible){CloseGameOptions();return;}
        if(_launching||_nativePreparation is not null||_downloadsOverlay.IsVisible||_storageOverlay.IsVisible||_inspectionCase is not null
            ||_platformHome.IsVisible||DualInputActive&&(HomeInput.IsPlatforms||HomeInput.IsPopupOpen)
            ||_catalog.ElementAtOrDefault(_selected) is not {} game)return;
        var light=UsesHome&&_presentation.LightHome;
        IBrush Ink(string day,string night)=>new SolidColorBrush(Color.Parse(light?day:night));
        var textInk=Ink("#202E38","#F4F8FC");
        var muted=Ink("#4D606D","#B7C3DA");
        var panelInk=Ink("#F8FAFC","#151D2D");
        var outline=Ink("#B9C8D1","#52687A");
        var rowInk=Ink("#EDF2F6","#202B40");
        var focusInk=Ink("#D3E8EF","#27475E");
        var focusBorder=Ink("#24637B","#B4E4EC");
        var panel=new StackPanel{Spacing=8};_gameOptionButtons.Clear();_gameOptionIndex=0;
        var header=new Grid{ColumnDefinitions=new("Auto,*"),ColumnSpacing=12,Margin=new Thickness(0,0,0,6)};
        header.Children.Add(new HomeArtworkControl{CoverPath=Cover(game.Covers.FirstOrDefault()?.Front),Title=game.Title,
            Platform=game.ShelfConsole,LightPalette=light,IsPreview=true,Width=54,Height=64});
        var heading=new StackPanel{Spacing=3,VerticalAlignment=VerticalAlignment.Center};
        heading.Children.Add(new TextBlock{Text=game.Title,FontSize=22,FontWeight=FontWeight.Bold,Foreground=textInk,TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=_availability.Text,FontSize=13,Foreground=muted,TextWrapping=TextWrapping.Wrap});
        Grid.SetColumn(heading,1);header.Children.Add(heading);panel.Children.Add(header);
        Button Action(string text,Action action)
        {
            var button=MakeButton(text);
            button.Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap};
            button.MinHeight=48;button.FontSize=16;button.Padding=new Thickness(14,9);
            button.CornerRadius=new CornerRadius(12);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            button.Foreground=textInk;button.Background=rowInk;button.BorderBrush=outline;button.Transitions=null;
            AutomationProperties.SetName(button,text);
            var index=_gameOptionButtons.Count;
            button.GotFocus+=(_,_)=>{_gameOptionIndex=index;_highlightGameOption?.Invoke();};
            button.Click+=(_,_)=>{CloseGameOptions();action();};_gameOptionButtons.Add(button);
            return button;
        }
        var portId=SelectedNativePort?.Id??game.Id;
        if(_games.NativePorts is {} native)
        {
            if(SelectedNativePort is {} port&&native.IsUpdateAvailable(portId)&&native.CompatibilityError(portId) is null)
                panel.Children.Add(Action("Mettre à jour dans DecompShelf · "+port.Version,()=>_ = PrepareSelectedPortAsync(update:true)));
            if(native.UpstreamUpdate(portId) is {} release)
            {
                if(release.CanInstall&&native.CompatibilityError(portId) is null)
                    panel.Children.Add(Action("Mettre à jour dans DecompShelf · "+release.Version,()=>_ = PrepareSelectedPortAsync(update:true)));
                else if(!release.CanInstall)
                {
                    panel.Children.Add(new TextBlock{Text=SelectedNativePort?.ExternalInstall==true
                        ? "APK vérifiable en attente · "+release.Version
                        : "Intégration dans DecompShelf en attente · "+release.Version,
                        FontSize=13,Foreground=muted,TextWrapping=TextWrapping.Wrap});
                    if(_games.AppUpdates is not null)panel.Children.Add(Action("Vérifier DecompShelf",CheckLauncherForPortUpdate));
                }
            }
        }
        panel.Children.Add(Action("Paramètres et préparation",ShowSelectedGameDetails));
        if(SelectedNativePort is {RegionOf.Length:>0} regional&&RegionVariants(regional).Length>1)
        {
            panel.Children.Add(Action("Région du disque · "+regional.Region,()=>SwitchRegion(regional)));
            panel.Children.Add(new TextBlock{Text="Choisis la région correspondant à ton disque.",FontSize=13,Foreground=muted,TextWrapping=TextWrapping.Wrap});
        }
        if(_games.Storage is not null)panel.Children.Add(Action("Stockage et sauvegardes",OpenStorage));
        panel.Children.Add(Action("Retourner le boîtier",()=>{ShowSelectedGameDetails();FlipFromController();}));
        var close=Action("Fermer",()=>{});
        var footer=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=10,Margin=new Thickness(0,10,0,0)};
        footer.Children.Add(new TextBlock{Text="A Valider · X / B Fermer",FontSize=13,Foreground=muted,
            VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});
        Grid.SetColumn(close,1);footer.Children.Add(close);
        // The controller may still belong to the other Android display.
        _highlightGameOption=()=>
        {
            for(var i=0;i<_gameOptionButtons.Count;i++)
            {
                var button=_gameOptionButtons[i];
                button.Background=i==_gameOptionIndex?focusInk:rowInk;
                button.BorderBrush=i==_gameOptionIndex?focusBorder:outline;
                button.BorderThickness=new Thickness(i==_gameOptionIndex?2:1);
            }
        };
        _highlightGameOption();
        ShowGameOptionsPanel(panel,footer,panelInk,outline);
    }
    private void ShowGameOptionsPanel(StackPanel panel,Control? footer=null,IBrush? background=null,IBrush? outline=null)
    {
        var body=new Grid{RowDefinitions=new("*,Auto")};
        body.Children.Add(new ScrollViewer{Content=panel,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        if(footer is not null){Grid.SetRow(footer,1);body.Children.Add(footer);}
        _gameOptionsOverlay.Child=new Border{Child=body,Padding=new Thickness(16),Margin=new Thickness(12),MaxWidth=500,
            MaxHeight=Math.Max(180,Bounds.Height-28),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            CornerRadius=new CornerRadius(22),Background=background??new SolidColorBrush(Color.Parse("#151D2D")),
            BorderBrush=outline??new SolidColorBrush(Color.Parse("#344159")),BorderThickness=new Thickness(1)};
        _gameOptionsReturnFocus=DualInputActive?TopLevel.GetTopLevel(HomeInput)?.FocusManager?.GetFocusedElement() as Control:null;
        _dualLibrary.StopMusic();_secondaryHome?.StopMusic();_gameOptionsOverlay.IsVisible=true;
        _gameOptionButtons[0].Focus(NavigationMethod.Directional);
    }
    private bool _achievementLibrary;
    private string? _achievementReturnGame,_achievementReturnPlatform;

    private void OpenRetroAchievements()
    {
        if(!_realized||_achievementLibrary||_launching||_nativePreparation is not null||_storageOverlay.IsVisible)return;
        _achievementReturnGame=_catalog.ElementAtOrDefault(_selected)?.Id;
        _achievementReturnPlatform=_dualLibrary.IsVisible&&_dualPlatforms?_dualLibrary.SelectedId:null;
        _achievementLibrary=true;
        _dualPlatforms=false;_dualDetail=false;
        _dualLibrary.IsVisible=true;
        ReloadVisibleCatalog(_achievementReturnGame);
        RefreshDualLibrary(false);
        HomeInput.Move(0,0);
    }

    private void CloseRetroAchievements()
    {
        if(!_achievementLibrary)return;
        _achievementLibrary=false;
        var game=_achievementReturnGame;
        var platform=_achievementReturnPlatform;
        _achievementReturnGame=_achievementReturnPlatform=null;
        _dualLibrary.IsVisible=_presentation.IsDual;
        ReloadVisibleCatalog(game);
        RefreshDualLibrary(platform is not null);
        if(platform is not null)
        {
            _dualLibrary.SelectItem(platform);
            _secondaryHome?.SelectItem(platform);
        }
        if(DualInputActive)HomeInput.Move(0,0);
        else FocusPrimaryAction();
    }

    private void ShowSelectedGameDetails()
    {
        if(UsesHome)
        {
            _dualDetail=true;
            _dualLibrary.IsVisible=false;
            var game=_catalog[_selected];
            _title.SetGame(game.Id,game.Title,game.ShelfConsole);
            RebuildMobileShelf();
            _shelf.SetSelection(_selected,_ribbon,animate:false);
        }
        FocusPrimaryAction();
    }
    private void CloseGameOptions(bool restoreFocus=true)
    {
        if(!_gameOptionsOverlay.IsVisible)return;
        var previous=_gameOptionsReturnFocus;_gameOptionsReturnFocus=null;
        _gameOptionsOverlay.IsVisible=false;_gameOptionsOverlay.Child=null;_gameOptionButtons.Clear();_highlightGameOption=null;
        if(!restoreFocus)return;
        if(!DualInputActive){FocusPrimaryAction();return;}
        if(previous is null||!HomeInput.IsVisualAncestorOf(previous)||!previous.Focus(NavigationMethod.Directional))
            HomeInput.Move(0,0);
        _dualLibrary.SetPlaybackActive(_presentationActive);
    }
    private void MoveGameOption(int step)
    {
        if(_gameOptionButtons.Count==0)return;
        _gameOptionIndex=Math.Clamp(_gameOptionIndex+step,0,_gameOptionButtons.Count-1);
        _highlightGameOption?.Invoke();
        var button=_gameOptionButtons[_gameOptionIndex];button.Focus(NavigationMethod.Directional);button.BringIntoView();
    }
    private void ActivateGameOption()=>_gameOptionButtons.ElementAtOrDefault(_gameOptionIndex)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
