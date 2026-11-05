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
        _presentation=new(_paths.DataDirectory);DualScreenView.Configure(_presentation,_previewMusic.Play,BigPictureSounds.PlayFile);
        DualScreenView.AchievementsRequested+=()=>ShowBigPictureMenu("achievements");
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
            if(DualScreenView.IsPlatforms)
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
        _sessions.Ended+=_=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{DualScreenView.SetPlaybackActive(IsActive);RefreshDualStatus();});
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
        DualScreenView.ReduceMotion=_preferences.ReduceShelfMotion;
        CloseBigPictureMenu();
        DualScreenView.SetItems(ShelfProducts.Select(p=>new DualScreenItem(p.Id,p.Console,p.Id,"",$"{NativePortCatalog.ForConsole(_catalogGames,p.Id).Count()} port(s) Windows","Voir les jeux")).Where(i=>!i.Status.StartsWith("0 ",StringComparison.Ordinal)),"Consoles",true,_activeShelfConsole);
        ShowParityView(DualScreenView);
    }
    private void BuildDualGames()
    {
        DualScreenView.ShowAchievements=_bigPictureActive;
        IEnumerable<GameCatalogEntry> source=_personalLibrary?PersonalLibraryGames():ShelfGames;
        if(_favoritesOnly)source=source.Where(g=>g.IsFavorite);
        _dualGames=source.OrderBy(g=>g.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
        DualScreenView.SetItems(_dualGames.Select(g=>
        {
            var cover=g.Covers.FirstOrDefault();
            return new DualScreenItem(g.Id,g.Title,g.ShelfConsole,ResolveCoverPath(cover?.Front),"Port Windows",
                Back:ResolveCoverPath(cover?.Back),Spine:ResolveCoverPath(cover?.Spine),Right:ResolveCoverPath(cover?.Right),Top:ResolveCoverPath(cover?.Top),Bottom:ResolveCoverPath(cover?.Bottom));
        }),_personalLibrary?"Mes jeux":_favoritesOnly?"Favoris":"Catalogue Windows",false,_selectedGame?.Id);
        if(DualScreenView.SelectedId is {} id)SelectDualGame(id);
    }
    private void SelectDualGame(string id)
    {
        if(_dualGames.FirstOrDefault(g=>g.Id==id) is not {} game)return;
        _selectedGame=GameVariants.Resolve(_catalogGames,game,_paths);HydrateGamePathsPhase3(_selectedGame);RefreshDualStatus();
        var index=Array.FindIndex(_bigPictureGames,g=>g.Id==id);if(index>=0)_bigPictureIndex=index;
    }
    private void RefreshDualStatus()
    {
        if(DualScreenView.IsVisible&&!DualScreenView.IsPlatforms&&_selectedGame is {} game)
        {
            DualScreenView.UpdateSelected(QuickPlayHint(game),QuickPlayLabel(game),PlayIntentFor(game) is null);
        }
    }
    private bool HandleDualKey(Key key)
    {
        if(!DualScreenView.IsVisible||BigPictureMenuOverlay.IsVisible||LaunchTransition.IsVisible||OwnedWindows.Any(w=>w.IsVisible))return false;
        if(DualScreenView.IsPopupOpen)return false;
        if(key==Key.F11)return false;
        if(key==Key.Escape){if(DualScreenView.IsPlatforms&&_bigPictureActive)ExitBigPicture(this,new RoutedEventArgs());else ShowDualPlatforms();return true;}
        if(key==Key.F10){ParityShowSettings(this,new RoutedEventArgs());return true;}
        if(key==Key.Enter){DualScreenView.ActivateFocused();return true;}
        return DualScreenView.HandleKey(key);
    }
}
