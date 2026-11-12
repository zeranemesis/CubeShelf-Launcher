using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CubeShelf.Core.Library;
using CubeShelf.Core.Achievements;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly DualScreenLibraryView _dualLibrary=new(){IsVisible=false,ZIndex=50,ShowClock=false};
    private DualScreenLibraryView? _secondaryHome;
    private bool _secondaryAttached;
    private ComboBox? _stylePicker;
    private DualScreenLibraryView HomeInput=>_secondaryAttached&&_secondaryHome is {} home?home:_dualLibrary;
    public bool ReduceMotion
    {
        get=>_dualLibrary.ReduceMotion;
        set{_dualLibrary.ReduceMotion=value;if(_secondaryHome is {} home)home.ReduceMotion=value;}
    }
    public event Action? SecondaryScreenStateChanged;
    // Overlays keep the bottom screen up (inert): dismissing the Presentation would expose the device's own launcher there.
    public bool WantsSecondaryScreen=>UsesHome&&_presentation.SecondaryDisplay&&_presentationActive&&_dualLibrary.IsEffectivelyVisible&&!_launchTransition.IsVisible;
    private LibraryPresentationStore _presentation=null!;
    private bool _dualPlatforms=true,_dualDetail,_presentationActive=true;
    private bool UsesHome=>_presentation.IsDual||_achievementLibrary;
    private bool DualInputActive=>_dualLibrary.IsEffectivelyVisible&&!_downloadsOverlay.IsVisible&&!_storageOverlay.IsVisible&&!_gameOptionsOverlay.IsVisible&&!_launchTransition.IsVisible;
    private void InitializePresentation()
    {
        _dualLibrary.PreviewFacePath=Cover;
        _presentation=new(_games.DataDirectory);_dualLibrary.Configure(_presentation,_games.PlayPreviewMusic,_games.PlayMenuSound);
        _dualLibrary.AppearanceChanged+=_=>RefreshPresentationStatusBar();
        _dualLibrary.PreferencesChanged+=()=>_secondaryHome?.RefreshPreferences();
        _dualLibrary.PropertyChanged+=(_,e)=>
        {
            if(e.Property!=IsVisibleProperty)return;
            _heading.IsVisible=_hint.IsVisible=!_dualLibrary.IsVisible;
            _body.IsVisible=!_dualLibrary.IsVisible&&!_downloadsOverlay.IsVisible;
            if(_dualLibrary.IsVisible)_shelf.Clear();
            RefreshPresentationStatusBar();
        };
        _updatePromptTimer.Tick+=(_,_)=>OfferUpdate();
        WireHome(_dualLibrary);
        foreach(var overlay in new Control[]{_dualLibrary,_downloadsOverlay,_storageOverlay,_gameOptionsOverlay,_launchTransition})
            overlay.PropertyChanged+=(_,e)=>{if(e.Property!=IsVisibleProperty)return;if(_secondaryHome is {} home)home.IsEnabled=DualInputActive;SecondaryScreenStateChanged?.Invoke();if(!overlay.IsVisible)ScheduleUpdatePrompt();};
    }
    private readonly AchievementProgressClient _achievementProgress=new();
    private void WireHome(DualScreenLibraryView surface)
    {
        surface.FetchAchievements=(id,token)=>_achievementProgress.LoadAsync(
            id==PartyBoardId?AchievementProgressClient.MarioParty4:0,_games.AchievementSession,null,token);
        surface.ConsolesRequested+=()=>RefreshDualLibrary(true);
        surface.LibraryRequested+=()=>{_dualPlatforms=false;ShowPersonalLibrary(true);RefreshDualLibrary(false);};
        surface.SettingsRequested+=ShowDownloads;
        surface.AchievementsRequested+=OpenRetroAchievements;
        surface.BackRequested+=CloseRetroAchievements;
        surface.SelectionChanged+=id=>
        {
            if(surface.IsPlatforms)
            {
                if(!ReferenceEquals(surface,_dualLibrary))_dualLibrary.SelectItem(id);
                if(!ReferenceEquals(surface,_secondaryHome))_secondaryHome?.SelectItem(id);
            }
            else
            {
                var index=Array.FindIndex(_catalog,g=>g.Id==id);
                if(index>=0)Select(index);
            }
        };
        surface.LaunchRequested+=id=>
        {
            if(_launching||_nativePreparation is not null)return;
            if(id.StartsWith(DualScreenLibraryView.ResumePrefix,StringComparison.Ordinal))
            {
                var gameId=id[DualScreenLibraryView.ResumePrefix.Length..];
                if(_allCatalog.FirstOrDefault(g=>g.Id==gameId) is not {} last)return;
                _dualPlatforms=false;_personalLibrary=false;_consolePicker.SelectedIndex=Array.FindIndex(_mobilePlatforms,p=>p.Id==last.ShelfConsole);
                ReloadVisibleCatalog();RefreshDualLibrary(false);
                var resumed=Array.FindIndex(_catalog,g=>g.Id==gameId);if(resumed<0)return;
                Select(resumed);_dualLibrary.StopMusic();_secondaryHome?.StopMusic();ActivatePrimaryAction();
            }
            else if(surface.IsPlatforms)
            {
                _dualPlatforms=false;_personalLibrary=false;_consolePicker.SelectedIndex=Array.FindIndex(_mobilePlatforms,p=>p.Id==id);
                ReloadVisibleCatalog();RefreshDualLibrary(false);
            }
            else{var index=Array.FindIndex(_catalog,g=>g.Id==id);if(index<0)return;Select(index);_dualLibrary.StopMusic();_secondaryHome?.StopMusic();ActivatePrimaryAction();}
        };
        surface.OptionsRequested+=_=>OptionsFromController();
    }
    public Control CreateSecondaryHome()
    {
        if(_secondaryHome is null)
        {
            _secondaryHome=new(){ScreenRole=HomeScreenRole.Navigation,ShowClock=false,PreviewFacePath=Cover,ReduceMotion=ReduceMotion};
            _secondaryHome.Configure(_presentation,null,_games.PlayMenuSound);WireHome(_secondaryHome);
            _secondaryHome.PreferencesChanged+=()=>_dualLibrary.RefreshPreferences();
            _secondaryHome.SetPlaybackActive(false);
        }
        _secondaryHome.IsEnabled=DualInputActive;
        RefreshDualLibrary();return _secondaryHome;
    }
    public void SetSecondaryScreenAttached(bool attached)
    {
        _secondaryAttached=attached;
        _dualLibrary.ScreenRole=attached?HomeScreenRole.Preview:HomeScreenRole.Combined;
        if(_secondaryHome is {} home){home.IsVisible=attached;home.SetPlaybackActive(attached&&_presentationActive);}
    }
    public void ReleaseSecondaryHome()
    {
        SetSecondaryScreenAttached(false);
        // A queued layout pass belongs to the old native root; never reparent its controls into a new one.
        _secondaryHome=null;
    }
    private Control BuildPresentationSetting()
    {
        var content=new StackPanel{Spacing=8};content.Children.Add(new TextBlock{Text="Thème de la bibliothèque",FontSize=18});
        var picker=_stylePicker=new ComboBox{Name="LibraryStylePicker",ItemsSource=new[]{"Classique · boîtes 3D","Double écran · HOME"},SelectedIndex=_presentation.IsDual?1:0,MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};
        content.Children.Add(picker);
        var secondary=new CheckBox{Content="Second écran automatique · AYN Thor / écran compatible",IsChecked=_presentation.SecondaryDisplay,MinHeight=48};
        content.Children.Add(secondary);
        var swap=new CheckBox{Content="Disposition Nintendo · A à droite, B en bas",IsChecked=_presentation.SwapFaceButtons,MinHeight=48};
        content.Children.Add(swap);
        swap.IsCheckedChanged+=(_,_)=>{try{_presentation.SetSwapFaceButtons(swap.IsChecked==true);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){SetStatus(error.Message);}};
        secondary.IsCheckedChanged+=(_,_)=>{try{_presentation.SetSecondaryDisplay(secondary.IsChecked==true);SecondaryScreenStateChanged?.Invoke();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){SetStatus(error.Message);}};
        picker.SelectionChanged+=(_,_)=>
        {
            if(picker.SelectedIndex==(_presentation.IsDual?1:0))return;
            try{_presentation.SetStyle(picker.SelectedIndex==1);CloseRetroAchievements();_dualDetail=false;_dualPlatforms=true;CloseDownloads(false);_platformHome.IsVisible=false;RefreshDualLibrary();if(!_presentation.IsDual){RebuildMobileShelf();OpenPlatformHome();}}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){SetStatus(error.Message);}
        };
        return DownloadCard(content);
    }
    private void RefreshDualLibrary(bool? platforms=null)
    {
        if(platforms is {} mode){_dualPlatforms=mode;_dualDetail=false;}
        _dualLibrary.IsVisible=UsesHome&&!_dualDetail;
        if(!UsesHome||_dualDetail){SecondaryScreenStateChanged?.Invoke();return;}
        _platformHome.IsVisible=false;
        var platformSelection=_dualLibrary.IsPlatforms?_dualLibrary.SelectedId??_activeConsole:_activeConsole;
        foreach(var surface in new[]{_dualLibrary,_secondaryHome}.OfType<DualScreenLibraryView>())
        if(_dualPlatforms)
            surface.SetItems(ResumeTile().Concat(_mobilePlatforms.Select(p=>new DualScreenItem(p.Id,p.Console,p.Id,"",$"{_allCatalog.Count(g=>g.ShelfConsole==p.Id)} jeux","Voir les jeux"))),"Accueil",true,platformSelection);
        else
            surface.SetItems(_catalog.OrderBy(g=>FoundGames.ContainsKey(g.Id)?0:1).Select(g=>
            {
                var cover=g.Covers.FirstOrDefault();
                return new DualScreenItem(g.Id,g.Title,g.ShelfConsole,Cover(cover?.Front),(FoundGames.ContainsKey(g.Id)?FoundPrefix:"")+"Port Android · "+g.Genre,
                    Back:cover?.Back??"",Spine:cover?.Spine??"",Right:cover?.Right??"",Top:cover?.Top??"",Bottom:cover?.Bottom??"");
            }),_achievementLibrary?"RetroAchievements":_personalLibrary?"Mes jeux":_mobilePlatforms.FirstOrDefault(p=>p.Id==_activeConsole)?.Console??"Jeux",
                false,_catalog.ElementAtOrDefault(_selected)?.Id,_achievementLibrary);
        if(_dualPlatforms&&ScanNeverRan)_dualLibrary.ShowMessage("Astuce : Réglages → Chercher mes jeux pour retrouver tes fichiers.");
        RefreshDualSelection();
        SecondaryScreenStateChanged?.Invoke();
    }
    /// <summary>The last launched game leads the HOME grid: one A press resumes it.</summary>
    private IEnumerable<DualScreenItem> ResumeTile()
    {
        if(_allCatalog.FirstOrDefault(g=>g.Id==_presentation.LastPlayed) is not {} last)yield break;
        yield return new DualScreenItem(DualScreenLibraryView.ResumePrefix+last.Id,last.Title,last.ShelfConsole,Cover(last.Covers.FirstOrDefault()?.Front),"Continuer · dernier jeu lancé","▶ Continuer");
    }
    /// <summary>Dual-screen devices (AYN Thor…) open on HOME unless the player already picked a theme.</summary>
    public void PreferHomeForDualScreen()
    {
        if(_presentation.HasStyleChoice||_launching||_downloadsOverlay.IsVisible)return;
        try{_presentation.SetStyle(true);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){return;}
        _dualDetail=false;_platformHome.IsVisible=false;RefreshDualLibrary(true);
    }
    public bool SwapFaceButtons=>_presentation.SwapFaceButtons;
    public bool CanRepeatHomeDirections=>DualInputActive&&!HomeInput.IsPopupOpen;
    public void StepHomeColumn(int step){SetControllerInput(true);if(DualInputActive)HomeInput.StepColumn(step);}
    private void RefreshDualSelection()
    {
        if(!UsesHome||_dualPlatforms)return;
        if(_catalog.ElementAtOrDefault(_selected) is not {} game)return;
        var portId=SelectedNativePort?.Id??game.Id;
        var hasUpdate=_games.NativePorts is {} native&&
            (SelectedNativePort is not null&&native.IsUpdateAvailable(portId)||native.UpstreamUpdate(portId) is not null);
        foreach(var surface in new[]{_dualLibrary,_secondaryHome}.OfType<DualScreenLibraryView>())
        {
            surface.SelectItem(game.Id);
            var action=_play.IsVisible?(SelectedNativePort?.ExternalInstall==true?"Ouvrir le port":"Jouer"):
                _download.IsVisible?(SelectedNativePort?.ExternalInstall==true?"Préparer le port":"Préparer le jeu"):"Indisponible";
            surface.UpdateSelected((FoundGames.ContainsKey(_catalog.ElementAtOrDefault(_selected)?.Id??"")?FoundPrefix:"")+_availability.Text,
                action,_nativePreparation is null&&(_play.IsVisible&&_play.IsEnabled||_download.IsVisible&&_download.IsEnabled),hasUpdate);
        }
    }
}
