namespace CubeShelf.Desktop.Controls;

public enum HomeScreenRole { Combined, Preview, Navigation }

public sealed partial class DualScreenLibraryView
{
    private HomeScreenRole _screenRole;
    public HomeScreenRole ScreenRole
    {
        get=>_screenRole;
        set{if(_screenRole==value)return;_screenRole=value;ResizeHome();ShowPage();}
    }
    public bool SelectItem(string id)
    {
        if(_selected?.Id==id)return true;
        var index=Array.FindIndex(_filtered,item=>item.Id==id);if(index<0)return false;
        _selected=_filtered[index];_page=index/PageSize;ShowPage();Select(_selected,false);return true;
    }
    public void RefreshPreferences(){ApplyPalette();UpdateMusic();}
}
