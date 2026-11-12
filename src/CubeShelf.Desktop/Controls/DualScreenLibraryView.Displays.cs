namespace CubeShelf.Desktop.Controls;

public enum HomeScreenRole { Combined, Preview, Navigation }

public sealed partial class DualScreenLibraryView
{
    private HomeScreenRole _screenRole;
    public HomeScreenRole ScreenRole
    {
        get=>_screenRole;
        set
        {
            if(_screenRole==value)return;
            StopSelectionMotion();CancelQuickPreview();_screenRole=value;
            if(value!=HomeScreenRole.Navigation&&_selected is {} item)SetTitle(item);
            ResizeHome();ShowPage();UpdateAchievementSelection();
        }
    }
    public bool SelectItem(string id)
    {
        if(_selected?.Id==id)return true;
        var index=Array.FindIndex(_filtered,item=>item.Id==id);if(index<0)return false;
        SelectOnPage(_filtered[index],index/PageSize,false);
        return true;
    }
    private void SelectOnPage(DualScreenItem item,int page,bool notify)
    {
        if(page!=_page){_page=page;_selected=item;ShowPage();}
        else if(ScreenRole!=HomeScreenRole.Preview)
        {
            _building=true;
            try{_grid.SelectedItem=item;}
            finally{_building=false;}
        }
        Select(item,notify);
    }
    public void RefreshPreferences(){ApplyPalette();UpdateMusic();}
}
