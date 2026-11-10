using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly Button _storageButton=MakeButton("Stockage et sauvegardes");
    private readonly Border _storageOverlay=new(){IsVisible=false,ZIndex=120,Background=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B101B"))};
    private GameStorageView? _storageView;
    private void OpenStorage()
    {
        if(_launching||_nativePreparation is not null||_storageView?.IsBusy==true)return;
        try
        {
            if(_games.Storage is not { } service)return;
            var selected=_catalog.ElementAtOrDefault(_selected)?.Id;
            _storageView=new GameStorageView(service,selected,id=>Cover(_allCatalog.FirstOrDefault(g=>g.Id==id)?.Covers.FirstOrDefault()?.Front));
            _storageView.CloseRequested+=CloseStorage;
            _storageView.BusyChanged+=busy=>{_libraryButton.IsEnabled=!busy;_closeDownloads.IsEnabled=!busy;};
            _storageView.Changed+=game=>{_games.StorageChanged(game.Id);if(_personalLibrary)ReloadVisibleCatalog();else RefreshAvailability();};
            _storageOverlay.Child=_storageView;_storageOverlay.IsVisible=true;
        }
        catch(Exception error){SetStatus(error.Message);}
    }
    private void CloseStorage()
    {
        if(_storageView?.IsBusy==true){_storageView.RequestClose();return;}
        _storageView?.Release();_storageOverlay.IsVisible=false;_storageOverlay.Child=null;_storageView=null;
        _libraryButton.IsEnabled=_closeDownloads.IsEnabled=true;
        if(_personalLibrary)ReloadVisibleCatalog();else RefreshAvailability();
    }
}
