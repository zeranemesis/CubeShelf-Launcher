using Avalonia.Interactivity;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;
public sealed partial class MainWindow
{
    private bool _platformHomeWired;
    private void OpenPlatformHome(object? sender, RoutedEventArgs args) => ShowPlatformHome();
    private void ShowPlatformHome()
    {
        ConsoleShelfPicker.IsDropDownOpen=false;
        CloseBigPictureMenu();
        ShelfTransition.Stop();
        if(!_platformHomeWired)
        {
            _platformHomeWired=true;
            PlatformHome.ConsoleChosen+=id=>
            {
                ConsoleShelfPicker.SelectedIndex=ConsoleIndex(id);
                ShowParityView(_bigPictureActive?BigPictureView:LibraryView);
                if(_bigPictureActive)BigPicturePlayButton.Focus();
            };
        }
        Avalonia.Controls.Grid.SetColumn(PlatformHome,_bigPictureActive?0:1);
        Avalonia.Controls.Grid.SetColumnSpan(PlatformHome,_bigPictureActive?2:1);
        PlatformHome.Open(ShelfProducts.Select(p=>(p,NativePortCatalog.ForConsole(_catalogGames,p.Id).Count())),_activeShelfConsole,desktop:true,grid:!_bigPictureActive,english:UiLocalization.IsEnglish(_preferences.Language));
        UpdateNavigationStatePhase5();
    }
}
