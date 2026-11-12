using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private LibraryPresentationStore? _presentation;
    private readonly PreviewMusicPlayer _previewMusic=new();
    private GameCatalogEntry[] _dualGames=[];
    private bool _loadingPresentation;
    private void InitializeLibraryPresentation()
    {
        PropertyChanged+=CatchUpLibrary;LibraryView.PropertyChanged+=CatchUpLibrary;PlatformHome.PropertyChanged+=CatchUpLibrary;
        _presentation=new(_paths.DataDirectory);DualScreenView.Configure(_presentation,_previewMusic.Play,BigPictureSounds.PlayFile);
        DualScreenView.FetchAchievements=ReadAchievementProgressAsync;
        DualScreenView.AchievementsRequested+=OpenRetroAchievementLibrary;
        DualScreenView.BackRequested+=()=>CloseRetroAchievementLibrary();
        DualScreenView.ReduceMotion=_preferences.ReduceShelfMotion;
        _loadingPresentation=true;LibraryStylePicker.SelectedIndex=_presentation.IsDual?1:0;_loadingPresentation=false;
        DualScreenView.ConsolesRequested+=ShowDualPlatforms;
        DualScreenView.LibraryRequested+=()=>OpenPersonalLibrary(this,new RoutedEventArgs());
        DualScreenView.SettingsRequested+=()=>{if(_bigPictureActive)ShowBigPictureMenu("settings");else ParityShowSettings(this,new RoutedEventArgs());};
        DualScreenView.AppearanceChanged+=_=>UpdateBigPictureChrome();
        DualScreenView.SelectionChanged+=id=>{if(!DualScreenView.IsPlatforms)SelectDualGame(id);};
        DualScreenView.OptionsRequested+=id=>{if(_dualGames.FirstOrDefault(g=>g.Id==id) is {} game){if(_bigPictureActive){SelectDualGame(id);ShowBigPictureMenu("game");}else SelectCatalogGame(game);}};
        DualScreenView.LaunchRequested+=async id=>
        {
            if(id.StartsWith(DualScreenLibraryView.ResumePrefix,StringComparison.Ordinal))
            {
                if(_catalogGames.FirstOrDefault(g=>g.Id==id[DualScreenLibraryView.ResumePrefix.Length..]) is not {} last)return;
                _selectedGame=GameVariants.Resolve(_catalogGames,last,_paths);HydrateGamePathsPhase3(_selectedGame);
                DualScreenView.StopMusic();await RequestPlayAsync(_selectedGame);
            }
            else if(DualScreenView.IsPlatforms)
            {
                _personalLibrary=false;_favoritesOnly=false;ConsoleShelfPicker.SelectedIndex=ConsoleIndex(id);
                ShowParityView(_bigPictureActive?BigPictureView:LibraryView);
            }
            else if(_dualGames.FirstOrDefault(g=>g.Id==id) is {} game)
            {DualScreenView.StopMusic();await RequestPlayAsync(GameVariants.Resolve(_catalogGames,game,_paths));RefreshDualStatus();}
        };
        Deactivated+=(_,_)=>DualScreenView.SetPlaybackActive(false);
        Activated+=(_,_)=>DualScreenView.SetPlaybackActive(true);
        _sessions.Started+=_=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>DualScreenView.SetPlaybackActive(false));
        _sessions.Ended+=_=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{DualScreenView.SetPlaybackActive(IsActive);RefreshDualStatus();DualScreenView.RefreshAchievementProgress();});
        Closed+=(_,_)=>{DualScreenView.StopMusic();_previewMusic.Dispose();};
    }
    private void LibraryStyleChanged(object? sender,SelectionChangedEventArgs args)
    {
        if(_loadingPresentation||_presentation is null)return;
        try{_presentation.SetStyle(LibraryStylePicker.SelectedIndex==1);ShowPlatformHome();}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){SettingsStatus.Text=e.Message;}
    }
    private void ShowDualPlatforms()
    {
        if(_presentation?.IsDual!=true)return;
        LeaveRetroAchievementLibrary();
        DualScreenView.ReduceMotion=_preferences.ReduceShelfMotion;
        DualScreenView.ShowAchievements=_bigPictureActive;
        CloseBigPictureMenu();
        var consoles=ShelfProducts.Select(p=>new DualScreenItem(p.Id,p.Console,p.Id,"",P7($"{NativePortCatalog.ForConsole(_catalogGames,p.Id).Count()} jeux",$"{NativePortCatalog.ForConsole(_catalogGames,p.Id).Count()} games"),P7("Voir les jeux","Browse games"))).Where(i=>!i.Status.StartsWith("0 ",StringComparison.Ordinal));
        // The most recently played game leads the grid, so one press resumes it (Steam / iiSU home).
        var last=_catalogGames.Where(g=>g.LastPlayedAt is not null).MaxBy(g=>g.LastPlayedAt);
        if(last is not null)
        {
            var cover=last.Covers.FirstOrDefault();
            consoles=consoles.Prepend(new DualScreenItem(DualScreenLibraryView.ResumePrefix+last.Id,last.Title,last.ShelfConsole,ResolveCoverPath(cover?.Front),
                P7("Continuer · joué le ","Continue · played ")+last.LastPlayedAt!.Value.ToLocalTime().ToString("dd/MM"),P7("▶ Continuer","▶ Continue")));
        }
        DualScreenView.SetItems(consoles,P7("Accueil","Home"),true,last is not null&&_activeShelfConsole==last.ShelfConsole?DualScreenLibraryView.ResumePrefix+last.Id:_activeShelfConsole);
        ShowParityView(DualScreenView);
    }
    private void BuildDualGames()
    {
        DualScreenView.ShowAchievements=_bigPictureActive;
        IEnumerable<GameCatalogEntry> source=_achievementLibrary?CompatibleRetroAchievementGames():
            _personalLibrary?PersonalLibraryGames():ShelfGames;
        if(_favoritesOnly&&!_achievementLibrary)source=source.Where(g=>g.IsFavorite);
        // Games the player already has come first, then the ones whose files a scan found; the rest of the catalogue follows.
        var owned=_personalLibrary?null:PersonalLibraryGames().Select(PlayKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found=new FoundGamesStore(_paths.DataDirectory).Load();
        int Rank(GameCatalogEntry g)=>owned is null||owned.Contains(PlayKey(g))?0:found.ContainsKey(PlayKey(g))||found.ContainsKey(g.Id)?1:2;
        _dualGames=source.OrderBy(Rank).ThenBy(g=>g.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
        DualScreenView.SetItems(_dualGames.Select(g=>
        {
            var cover=g.Covers.FirstOrDefault();
            return new DualScreenItem(g.Id,g.Title,g.ShelfConsole,ResolveCoverPath(cover?.Front),Rank(g) switch{0=>P7("Dans ta bibliothèque","In your library"),1=>P7("Fichier trouvé","File found"),_=>P7("À installer","To install")},
                Back:ResolveCoverPath(cover?.Back),Spine:ResolveCoverPath(cover?.Spine),Right:ResolveCoverPath(cover?.Right),Top:ResolveCoverPath(cover?.Top),Bottom:ResolveCoverPath(cover?.Bottom));
        }),_achievementLibrary?"RetroAchievements":_personalLibrary?P7("Mes jeux","My games"):
            _favoritesOnly?P7("Favoris","Favourites"):ShelfProduct.Resolve(_activeShelfConsole).Console,
            false,_selectedGame?.Id,_achievementLibrary);
        if(DualScreenView.SelectedId is {} id)SelectDualGame(id);
        else if(_achievementLibrary)_selectedGame=null;
    }
    private void SelectDualGame(string id)
    {
        if(_dualGames.FirstOrDefault(g=>g.Id==id) is not {} game)return;
        _selectedGame=GameVariants.Resolve(_catalogGames,game,_paths);HydrateGamePathsPhase3(_selectedGame);RefreshDualStatus();
        if(!_achievementLibrary)
        {
            var index=Array.FindIndex(_bigPictureGames,g=>g.Id==id);
            if(index>=0)_bigPictureIndex=index;
        }
    }
    private void RefreshDualStatus()
    {
        if(DualScreenView.IsVisible&&!DualScreenView.IsPlatforms&&_selectedGame is {} game)
        {
            DualScreenView.UpdateSelected(QuickPlayHint(game),QuickPlayLabel(game),PlayIntentFor(game) is null,_nativeReleaseUpdates.ContainsKey(game.Id));
        }
    }
    private bool HandleDualKey(Key key)
    {
        if(!DualScreenView.IsVisible||BigPictureMenuOverlay.IsVisible||LaunchTransition.IsVisible||OwnedWindows.Any(w=>w.IsVisible))return false;
        if(DualScreenView.IsPopupOpen)return false;
        if(key==Key.F11)return false;
        if(key==Key.Escape){if(DualScreenView.DismissPopup()||CloseRetroAchievementLibrary())return true;if(DualScreenView.IsPlatforms&&_bigPictureActive)ExitBigPicture(this,new RoutedEventArgs());else ShowDualPlatforms();return true;}
        if(key==Key.F10){ParityShowSettings(this,new RoutedEventArgs());return true;}
        if(key==Key.Enter){DualScreenView.ActivateFocused();return true;}
        return DualScreenView.HandleKey(key);
    }
}
