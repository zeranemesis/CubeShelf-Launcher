using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop.Controls;

public sealed class PlatformHomeControl : UserControl
{
    private readonly GameShelfControl _shelf = new() { UseCompositorScaling = true };
    private readonly TextBlock _name = new() { FontSize=30,FontWeight=FontWeight.Bold,Foreground=Brushes.White,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { FontSize=15,Foreground=new SolidColorBrush(Color.Parse("#BECADC")),TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap };
    private readonly TextBlock _hint = new() {Text="Modèles 3D stylisés · Glisse ou utilise les flèches pour parcourir",FontSize=12,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox _sort = new() { ItemsSource=new[]{"Nom","Marque","Année croissante","Année décroissante","Nombre de jeux"},SelectedIndex=0,MinHeight=44,MinWidth=140,Width=180 };
    private (ShelfProduct Product,int Count)[] _source=[],_items=[];
    private int _selected;
    private bool _desktop;
    private bool _english;
    private readonly Button _previous, _choose, _next;
    public bool IsGrid { get; private set; }
    private readonly TextBlock _heading = new() { Text="Consoles",FontSize=24,FontWeight=FontWeight.Bold,VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBlock _sortLabel = new() { Text="Trier par",FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),IsVisible=false };
    private readonly ShelfContourBackground _contours = new() { IsHitTestVisible=false,Opacity=.55 };
    private readonly WrapPanel _cards = new() { ItemWidth=240,ItemHeight=290,HorizontalAlignment=HorizontalAlignment.Center };
    private readonly ScrollViewer _gridScroll;
    private readonly StackPanel _summary;
    private readonly Dictionary<string,Bitmap> _images = new();
    private readonly ThemeVariantScope _theme = new();
    public bool SortHasFocus => _sort.IsKeyboardFocusWithin;
    public string? SelectedConsole => _items.ElementAtOrDefault(_selected).Product?.Id;
    public event Action<string>? ConsoleChosen;
    public PlatformHomeControl()
    {
        Background=new SolidColorBrush(Color.Parse("#0B101B"));
        var root=new Grid { RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(16) };
        var heading=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto") };
        heading.Children.Add(_heading);
        Grid.SetColumn(_sortLabel,1);heading.Children.Add(_sortLabel);
        Grid.SetColumn(_sort,2);heading.Children.Add(_sort);root.Children.Add(heading);
        Avalonia.Automation.AutomationProperties.SetName(_sort,"Trier les consoles");
        Grid.SetRow(_shelf,1);root.Children.Add(_shelf);
        _gridScroll=new ScrollViewer { Content=_cards,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,IsVisible=false,Margin=new Thickness(0,24,0,0) };
        _gridScroll.SizeChanged+=(_,_)=>FitCards();
        Grid.SetRow(_gridScroll,1);root.Children.Add(_gridScroll);
        var summary=new StackPanel { Spacing=10,MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Center };
        _summary=summary;
        summary.Children.Add(_name);summary.Children.Add(_detail);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0) };
        Button Action(string text,Action action) {var button=new Button {Content=text,MinHeight=48,FontSize=14,Padding=new Thickness(8,10),CornerRadius=new CornerRadius(12)};button.Click+=(_,_)=>action();actions.Children.Add(button);return button;}
        _previous=Action("Précédente",()=>Move(-1));_choose=Action("Voir les jeux",Choose);_next=Action("Suivante",()=>Move(1));
        summary.Children.Add(actions);
        summary.Children.Add(_hint);
        Grid.SetRow(summary,2);root.Children.Add(summary);
        var scene=new Grid();scene.Children.Add(_contours);scene.Children.Add(root);_theme.Child=scene;Content=_theme;
        _sort.SelectionChanged+=(_,_)=>Reorder();
        SizeChanged+=(_,_)=>_sort.Width=Math.Clamp(Bounds.Width*.43,140,280);
        _shelf.SelectionRequested+=index=>Select(index);
        Point? touch=null;
        _shelf.AddHandler(PointerPressedEvent,(_,e)=>{if(e.Pointer.Type==PointerType.Touch)touch=e.GetPosition(_shelf);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _shelf.AddHandler(PointerReleasedEvent,(_,e)=> {if(touch is {} start && Math.Abs(e.GetPosition(_shelf).X-start.X)>35)Move(e.GetPosition(_shelf).X<start.X?1:-1);touch=null;},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        IsVisibleChanged();
        ActualThemeVariantChanged+=(_,_)=>{ApplyBackdrop();if(IsVisible&&IsGrid)BuildCards();};
        PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty)IsVisibleChanged();};
        DetachedFromVisualTree+=(_,_)=>{_shelf.Clear();ClearCards();};
    }
    private void IsVisibleChanged() {if(!IsVisible){_shelf.Clear();ClearCards();}else if(_items.Length>0)Reorder();}
    public void Open(IEnumerable<(ShelfProduct Product,int Count)> platforms,string selected,bool desktop=false,bool grid=false,bool english=false)
    {
        _desktop=desktop;IsGrid=grid;
        if(_english!=english)
        {
            _english=english;
            var sort=_sort.SelectedIndex;
            _sort.ItemsSource=english?new[]{"Name","Brand","Year: oldest first","Year: newest first","Number of games"}:new[]{"Nom","Marque","Année croissante","Année décroissante","Nombre de jeux"};
            _sort.SelectedIndex=Math.Max(0,sort);
        }
        _previous.Content=english?"Previous":"Précédente";
        _choose.Content=english?"View games":"Voir les jeux";
        _next.Content=english?"Next":"Suivante";
        _sortLabel.Text=english?"Sort by":"Trier par";
        _hint.Text=english?"Stylised 3D models · Swipe or use the arrow keys to browse":"Modèles 3D stylisés · Glisse ou utilise les flèches pour parcourir";
        Avalonia.Automation.AutomationProperties.SetName(_sort,english?"Sort consoles":"Trier les consoles");
        _theme.RequestedThemeVariant=desktop&&!grid?ThemeVariant.Light:ThemeVariant.Default;
        ApplyBackdrop();
        _source=platforms.Where(p=>p.Count>0).ToArray();_selected=0;
        _shelf.IsVisible=!grid;_summary.IsVisible=!grid;_gridScroll.IsVisible=grid;_sortLabel.IsVisible=grid;
        IsVisible=true;Reorder(selected);_gridScroll.Offset=default;
    }
    private void ApplyBackdrop()
    {
        var light=_desktop&&(!IsGrid||ActualThemeVariant==ThemeVariant.Light);
        Background=new SolidColorBrush(Color.Parse(_desktop ? IsGrid ? light?"#F2F4F9":"#050811" : "#F5F0EE" : "#0B101B"));
        _contours.IsVisible=_desktop&&!IsGrid;
        _heading.Foreground=_name.Foreground=new SolidColorBrush(Color.Parse(light?"#303A44":"#F8FAFF"));
        _detail.Foreground=_hint.Foreground=_sortLabel.Foreground=new SolidColorBrush(Color.Parse(light?"#59616A":"#BECADC"));
    }
    private void ClearCards()
    {
        _cards.Children.Clear();
        foreach(var image in _images.Values)image.Dispose();
        _images.Clear();
    }
    private void BuildCards()
    {
        _cards.Children.Clear();
        var light=ActualThemeVariant==ThemeVariant.Light;
        foreach(var item in _items)
        {
            if(!_images.TryGetValue(item.Product.Id,out var image))
            {
                var directory=Path.Combine(AppContext.BaseDirectory,"Assets","Consoles");
                var photo=Directory.Exists(directory)?Directory.EnumerateFiles(directory,item.Product.Id+".*").FirstOrDefault(file=>Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp"):null;
                if(photo is not null)
                {
                    using var stream=File.OpenRead(photo);
                    image=Bitmap.DecodeToWidth(stream,480);_images.Add(item.Product.Id,image);
                }
            }
            var content=new StackPanel { Spacing=6 };
            if(image is not null)content.Children.Add(new Image {Source=image,Height=150,Stretch=Stretch.Uniform,IsHitTestVisible=false});
            else content.Children.Add(new TextBlock {Text=_english?"Photo unavailable":"Photo indisponible",Height=150,VerticalAlignment=VerticalAlignment.Center});
            content.Children.Add(new TextBlock {Text=item.Product.Console,FontSize=18,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse(light?"#151927":"#F8FAFF"))});
            var (brand,year)=Facts(item.Product.Id);
            content.Children.Add(new TextBlock {Text=$"{brand} · {YearLabel(year)}",FontSize=12,Foreground=new SolidColorBrush(Color.Parse(light?"#58637B":"#B7C3DA"))});
            content.Children.Add(new TextBlock {Text=GameCount(item.Count),FontSize=13,Foreground=new SolidColorBrush(Color.Parse(light?"#58637B":"#B7C3DA"))});
            var card=new Button {Content=content,Tag=item.Product.Id,Width=220,Height=270,Margin=new Thickness(10),Padding=new Thickness(14,10),CornerRadius=new CornerRadius(16),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};
            Avalonia.Automation.AutomationProperties.SetName(card,item.Product.Console+", "+GameCount(item.Count));
            var credits=Path.Combine(AppContext.BaseDirectory,"Assets","Consoles","sources.json");
            if(File.Exists(credits))
            {
                using var sources=System.Text.Json.JsonDocument.Parse(File.ReadAllText(credits));
                var credit=sources.RootElement.EnumerateArray().FirstOrDefault(source=>source.GetProperty("Console").GetString()==item.Product.Id);
                if(credit.ValueKind!=System.Text.Json.JsonValueKind.Undefined)
                    ToolTip.SetTip(card,$"Photo : {credit.GetProperty("Author").GetString()}\n{credit.GetProperty("License").GetString()}\n{credit.GetProperty("Source").GetString()}");
            }
            card.Click+=(_,_)=>ConsoleChosen?.Invoke(item.Product.Id);
            _cards.Children.Add(card);
        }
        FitCards();
    }
    private void FitCards()
    {
        if(!IsGrid||_gridScroll.Bounds.Width<=0)return;
        var columns=Math.Max(1,(int)(_gridScroll.Bounds.Width/220));
        var width=Math.Min(260,Math.Floor(_gridScroll.Bounds.Width/columns));
        _cards.ItemWidth=width;
        foreach(var card in _cards.Children.OfType<Button>())card.Width=width-20;
    }
    public void Move(int step) {if(_items.Length>0)Select((_selected+step+_items.Length)%_items.Length);}
    public void CycleSort() => _sort.SelectedIndex=(_sort.SelectedIndex+1)%5;
    public void Choose() {if(SelectedConsole is {} id)ConsoleChosen?.Invoke(id);}
    private string GameCount(int count)=>_english?$"{count} game{(count==1?"":"s")}":$"{count} jeu{(count==1?"":"x")}";
    private string YearLabel(int year)=>year>0?year.ToString():_english?"Several generations":"Plusieurs générations";
    private void Select(int index)
    {
        if(_items.Length==0)return;
        _selected=Math.Clamp(index,0,_items.Length-1);var item=_items[_selected];var (brand,year)=Facts(item.Product.Id);
        _name.Text=item.Product.Console;
        _detail.Text=$"{brand} · {YearLabel(year)} · {GameCount(item.Count)} · {_selected+1} / {_items.Length}";
        if(!IsGrid)_shelf.SetSelection(_selected,false);
    }
    private void Reorder() => Reorder(SelectedConsole);
    private void Reorder(string? selected)
    {
        _items=(_sort.SelectedIndex switch {
            1=>_source.OrderBy(p=>Facts(p.Product.Id).Brand).ThenBy(p=>p.Product.Console),
            2=>_source.OrderBy(p=>Facts(p.Product.Id).Year==0?int.MaxValue:Facts(p.Product.Id).Year).ThenBy(p=>p.Product.Console),
            3=>_source.OrderByDescending(p=>Facts(p.Product.Id).Year).ThenBy(p=>p.Product.Console),
            4=>_source.OrderByDescending(p=>p.Count).ThenBy(p=>p.Product.Console),
            _=>_source.OrderBy(p=>p.Product.Console) }).ToArray();
        if(IsGrid){_shelf.Clear();BuildCards();}
        else {ClearCards();_shelf.SetGames(_items.Select(p=>new GameShelfItem(p.Product.Console,"","","",p.Product.Id,Hardware:true)).ToArray());}
        Select(Math.Max(0,Array.FindIndex(_items,p=>p.Product.Id==selected)));
    }
    // Year of the original hardware's first launch; Arcade is a category, not one machine.
    public static (string Brand,int Year) Facts(string id) => id switch {
        "gamecube"=>("Nintendo",2001),"wii"=>("Nintendo",2006),"wiiu"=>("Nintendo",2012),"switch"=>("Nintendo",2017),
        "n64"=>("Nintendo",1996),"nes"=>("Nintendo",1983),"snes"=>("Nintendo",1990),"gb"=>("Nintendo",1989),
        "gbc"=>("Nintendo",1998),"gba"=>("Nintendo",2001),"nds"=>("Nintendo",2004),"3ds"=>("Nintendo",2011),"virtualboy"=>("Nintendo",1995),
        "mastersystem"=>("Sega",1985),"megadrive"=>("Sega",1988),"gamegear"=>("Sega",1990),"32x"=>("Sega",1994),
        "segacd"=>("Sega",1991),"saturn"=>("Sega",1994),"dreamcast"=>("Sega",1998),
        "ps1"=>("Sony",1994),"ps2"=>("Sony",2000),"ps3"=>("Sony",2006),"ps4"=>("Sony",2013),"psp"=>("Sony",2004),
        "xbox"=>("Microsoft",2001),"xbox360"=>("Microsoft",2005),"neogeo"=>("SNK",1990),"cdi"=>("Philips",1991),
        _=>("Arcade",0) };
}
