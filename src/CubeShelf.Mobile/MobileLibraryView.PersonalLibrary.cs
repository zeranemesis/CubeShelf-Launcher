using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private bool _personalLibrary;
    private readonly Button _libraryButton=new()
    {
        Content="Mes jeux",MinHeight=44,FontSize=14,FontWeight=FontWeight.SemiBold,
        Padding=new Thickness(12,6),CornerRadius=new CornerRadius(12)
    };
    private readonly TextBlock _emptyLibrary=new()
    {
        Text="Ta bibliothèque est vide.\nPrépare ton jeu depuis le catalogue.",
        FontSize=19,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,
        HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
        MaxWidth=400,Margin=new Thickness(24),IsVisible=false
    };

    private MobileGame[] VisibleCatalog() => _allCatalog.Where(game=>
    {
        if(_achievementLibrary)return _games.SupportsRetroAchievements(game.Id);
        if(!_personalLibrary)return game.ShelfConsole==_activeConsole;
        if(game.Id==PartyBoardId)return _games.IsInstalled();
        var native=_games.NativePorts;
        return native is not null&&native.IsAdded(game.Id);
    }).ToArray();

    public void OpenPersonalLibrary() => ShowPersonalLibrary(true);

    private void ShowPersonalLibrary(bool personal)
    {
        if(!_realized||_launching||_nativePreparation is not null)return;
        CloseGameOptions(false);
        _dualPlatforms=false;_dualDetail=false;
        var leavingAchievements=_achievementLibrary;
        _achievementLibrary=false;_achievementReturnGame=_achievementReturnPlatform=null;
        _personalLibrary=personal;
        _consolePicker.IsDropDownOpen=false;
        _platformHome.IsVisible=false;
        CloseDownloads(false);CloseInspection(false);ResetTouch();
        if(leavingAchievements)_dualLibrary.IsVisible=_presentation.IsDual;
        ReloadVisibleCatalog();
        if(leavingAchievements)RefreshDualLibrary(false);
        FocusPrimaryAction();
    }

    private void ReloadVisibleCatalog(string? selected=null)
    {
        selected??=_catalog.ElementAtOrDefault(_selected)?.Id;
        _catalog=VisibleCatalog();
        _selected=Math.Max(0,Array.FindIndex(_catalog,game=>game.Id==selected));
        RebuildMobileShelf();UpdatePersonalLibraryUi();
        if(_catalog.Length>0)Select(_selected);
    }

    private void UpdatePersonalLibraryUi()
    {
        _libraryButton.Content=_personalLibrary?"Catalogue":"Mes jeux";
        Avalonia.Automation.AutomationProperties.SetName(_libraryButton,_personalLibrary?"Revenir au catalogue des ports Android":"Mes jeux installés ou ajoutés, toutes consoles");
        _consolePicker.IsVisible=!_personalLibrary&&_mobilePlatforms.Length>1;
        _emptyLibrary.IsVisible=_personalLibrary&&_catalog.Length==0;
        _shelf.IsVisible=_gamePanel.IsVisible=_catalog.Length>0;
        if(_catalog.Length==0)_position.Text="0 / 0";
        UpdatePlatformCaption();
    }
}
