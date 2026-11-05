using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform;
using CubeShelf.Core.Library;
namespace CubeShelf.Desktop.Controls;
public sealed record DualScreenItem(string Id,string Title,string Console,string Cover,string Status,string Action="Ouvrir",bool CanLaunch=true,
    string Back="",string Spine="",string Right="",string Top="",string Bottom="");
/// <summary>One screen with two regions; independent controls can later target a second Android display.</summary>
public sealed partial class DualScreenLibraryView : UserControl
{
    private int PageSize=>Math.Min(24,_columns*_rows);
    private readonly TextBlock _heading=Text(20),_status=Text(13),_pageLabel=Text(13),_message=Text(13);
    private readonly RetroAchievementsButton _achievements=new(){IsVisible=false};
    private readonly GameTitleControl _title=new(){FontSize=24,Foreground=Brushes.Black,MaxHeight=85};
    private readonly Grid _art=new();
    private GameCaseControl? _casePreview;
    public Func<string,string>? PreviewFacePath { get; set; }
    private DualScreenItem? _previewItem;
    private bool _previewForPlatform;
    private readonly ListBox _grid=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0),Focusable=true};
    private readonly TextBox _search=new(){Watermark="Rechercher…",MinHeight=48};
    private readonly Button _launch=Button("Ouvrir"),_music=Button("Musique"),_mute=Button("Musique : oui"),_options=Button("Options du jeu");
    private readonly Button _previous=Button("Précédente"),_next=Button("Suivante"),_soundToggle=Button("Sons : oui"),_import=Button("Associer une piste MP3 / WAV");
    private readonly DispatcherTimer _musicDelay=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly Border _hero,_lower,_dock;
    private readonly ThemeVariantScope _scope;
    private readonly HomeMenuBackdrop _backdrop=new(),_banner=new(){Banner=true};
    private readonly Button _appearance=Button("Affichage : clair"),_find=Button("Rechercher");
    private readonly List<Button> _navigation=[];
    private readonly Grid _heroContent=new(){RowDefinitions=new("*,Auto"),RowSpacing=12};
    private readonly Grid _lowerContent=new(){RowDefinitions=new("Auto,Auto,Auto,Auto,Auto"),RowSpacing=12};
    private double _tileSize=76;
    private bool LightPalette=>_settings?.LightHome??true;
    public bool IsPopupOpen=>_music.Flyout?.IsOpen==true;
    private readonly TextBlock _clock=Text(14),_selectedName=Text(17);
    private readonly StackPanel _time=new(){Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
    public bool ShowClock
    {
        get=>_time.IsVisible;
        set{_time.IsVisible=value;if(!value)_clockTimer.Stop();else if(IsEffectivelyVisible){_clock.Text=DateTime.Now.ToString("HH:mm");_clockTimer.Start();}}
    }
    private readonly Grid _root=new(){RowDefinitions=new("Auto,*,Auto,Auto,Auto"),RowSpacing=14,MaxWidth=1040,Margin=new Thickness(24,12)};
    private readonly TranslateTransform _bannerOffset=new();
    private readonly DemandAnimation _selectionAnimation;
    private readonly WrapPanel _tiles=new();
    private readonly DispatcherTimer _clockTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private Action<string?>? _playSound;
    private string? _audioDirectory;
    private double _selectionTime;
    private long _lastSound;
    private string? _displayedId;
    private bool _reduceMotion;
    public bool ReduceMotion
    {
        get=>_reduceMotion;
        set{_reduceMotion=value;if(_casePreview is {} preview)preview.IsInteractive=!value&&_playbackActive;}
    }
    public bool SoundsAllowed { get; set; }=true;
    public bool ShowAchievements { get; set; }=true;
    private DualScreenItem[] _items=[],_filtered=[];
    private DualScreenItem? _selected;
    private LibraryPresentationStore? _settings;
    private Action<string?>? _playMusic;
    private int _page,_columns=4,_rows=3;
    private bool _platforms,_building,_playbackActive=true;
    private string? _playing;
    private bool _importingMusic;
    public bool IsPlatforms=>_platforms;
    public string? SelectedId=>_selected?.Id;
    public event Action<string>? SelectionChanged;
    public event Action<string>? LaunchRequested;
    public event Action<string>? OptionsRequested;
    public event Action? ConsolesRequested,LibraryRequested,SettingsRequested;
    public event Action<bool>? AppearanceChanged;
    public event Action? PreferencesChanged;
    public event Action? AchievementsRequested;
    public DualScreenLibraryView()
    {
        Name="DualScreenLibraryView";
        FontFamily=new FontFamily($"avares://{typeof(DualScreenLibraryView).Assembly.GetName().Name}/Assets/Home/Fonts#Nunito");
        var header=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new Thickness(6,0)};
        _heading.FontSize=15;_heading.FontWeight=FontWeight.SemiBold;var headerWords=new Grid();headerWords.Children.Add(_heading);headerWords.Children.Add(_selectedName);_selectedName.FontSize=15;_selectedName.FontWeight=FontWeight.SemiBold;_selectedName.MaxLines=1;_selectedName.TextTrimming=TextTrimming.CharacterEllipsis;header.Children.Add(headerWords);
        var time=_time;
        var clockGlyph=new HomeGlyph{Kind="clock",Width=17,Height=17};clockGlyph.Bind(HomeGlyph.InkProperty,new DynamicResourceExtension("HomeMuted"));time.Children.Add(clockGlyph);time.Children.Add(_clock);
        _clock.Text=DateTime.Now.ToString("HH:mm");Grid.SetColumn(time,1);header.Children.Add(time);_root.Children.Add(header);
        _clockTimer.Tick+=(_,_)=>_clock.Text=DateTime.Now.ToString("HH:mm");
        _heroContent.Margin=new Thickness(28);_heroContent.RenderTransform=_bannerOffset;
        _art.HorizontalAlignment=HorizontalAlignment.Center;_art.VerticalAlignment=VerticalAlignment.Center;
        _heroContent.Children.Add(_art);
        _title.HorizontalAlignment=HorizontalAlignment.Center;_title.VerticalAlignment=VerticalAlignment.Center;_title.MaxWidth=500;_title.MaxHeight=170;
        Grid.SetRow(_title,1);_heroContent.Children.Add(_title);
        var heroLayers=new Panel();heroLayers.Children.Add(_banner);heroLayers.Children.Add(_heroContent);
        _hero=new Border{Name="HomePreview",Child=heroLayers,MaxWidth=760,CornerRadius=new CornerRadius(30),ClipToBounds=true,BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Stretch};
        Grid.SetRow(_hero,1);_root.Children.Add(_hero);
        _selectionAnimation=new DemandAnimation(this,dt=>{_selectionTime+=dt;var t=Math.Min(1,_selectionTime/.24);_bannerOffset.Y=10*Math.Pow(1-t,3);return t<1;});
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Center};
        void Tool(string label,string icon,Action action)
        {
            var button=Button(label);button.Tag=icon;button.Classes.Add("home-dock");button.Click+=(_,_)=>{Cue("open");action();};
            navigation.Children.Add(button);_navigation.Add(button);
        }
        Tool("Consoles","console",()=>ConsolesRequested?.Invoke());
        Tool("Mes jeux","library",()=>LibraryRequested?.Invoke());
        Tool("Paramètres","settings",()=>SettingsRequested?.Invoke());
        _find.Tag="search";_find.Classes.Add("home-dock");_find.Click+=(_,_)=>{_search.IsVisible=!_search.IsVisible;ResizeHome();if(_search.IsVisible)_search.Focus();else _grid.Focus();};navigation.Children.Add(_find);
        _music.Content="Personnaliser";_music.Tag="customize";_music.Classes.Add("home-dock");ToolTip.SetTip(_music,"Personnaliser · audio et affichage");AutomationProperties.SetName(_music,"Personnaliser · audio et affichage");navigation.Children.Add(_music);
        _dock=new Border{Child=navigation,Padding=new Thickness(8,5),CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(_dock,2);_root.Children.Add(_dock);
        _lowerContent.Margin=new Thickness(16,10,16,14);
        var pageControls=new Grid{ColumnDefinitions=new("Auto,*,Auto"),ColumnSpacing=8};
        _previous.Tag="left";_previous.Classes.Add("home-icon");_next.Tag="right";_next.Classes.Add("home-icon");
        pageControls.Children.Add(_previous);_pageLabel.HorizontalAlignment=HorizontalAlignment.Center;_pageLabel.FontSize=14;_pageLabel.FontWeight=FontWeight.SemiBold;Grid.SetColumn(_pageLabel,1);pageControls.Children.Add(_pageLabel);Grid.SetColumn(_next,2);pageControls.Children.Add(_next);_lowerContent.Children.Add(pageControls);
        _previous.Click+=(_,_)=>ChangePage(-1);_next.Click+=(_,_)=>ChangePage(1);
        _grid.ItemsPanel=new FuncTemplate<Panel?>(()=>_tiles);
        _grid.ItemTemplate=new FuncDataTemplate<DualScreenItem>((item,_)=>item is null?null:Thumbnail(item));
        _grid.HorizontalAlignment=HorizontalAlignment.Center;_grid.VerticalAlignment=VerticalAlignment.Top;
        _grid.SelectionChanged+=(_,_)=>{if(!_building&&_grid.SelectedItem is DualScreenItem item)Select(item,true);};
        Grid.SetRow(_grid,1);_lowerContent.Children.Add(_grid);
        _search.Watermark="Rechercher un jeu…";_search.IsVisible=false;_search.TextChanged+=(_,_)=>{_page=0;Filter();};
        Grid.SetRow(_search,2);_lowerContent.Children.Add(_search);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center};
        _launch.Classes.Add("home-primary");_launch.MinWidth=154;_launch.MaxWidth=280;_launch.Click+=(_,_)=>Activate();actions.Children.Add(_launch);
        _options.Tag="more";_options.Classes.Add("home-icon");ToolTip.SetTip(_options,"Options du jeu · X");_options.Click+=(_,_)=>{if(_selected is {} item&&!_platforms){Cue("open");OptionsRequested?.Invoke(item.Id);}};actions.Children.Add(_options);
        _achievements.Click+=(_,_)=>AchievementsRequested?.Invoke();actions.Children.Add(_achievements);
        Grid.SetRow(actions,3);_lowerContent.Children.Add(actions);
        _lower=new Border{Name="HomeGrid",Child=_lowerContent,MaxWidth=960,CornerRadius=new CornerRadius(26),BorderThickness=new Thickness(1)};Grid.SetRow(_lower,3);_root.Children.Add(_lower);
        _message.FontSize=12;_message.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(_message,4);_root.Children.Add(_message);
        _status.MaxLines=1;_status.TextTrimming=TextTrimming.CharacterEllipsis;_status.FontSize=12;_status.FontWeight=FontWeight.SemiBold;_status.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(_status,4);_lowerContent.Children.Add(_status);
        _selectedName.IsVisible=false;
        _import.Click+=async(_,_)=>{_music.Flyout?.Hide();await ImportMusic();};
        _music.Flyout=new Flyout{Content=new StackPanel{Spacing=8,Children={_import,_mute,_soundToggle,_appearance}}};
        _mute.Click+=(_,_)=>{try{_settings?.ToggleMusic();UpdateMusic();PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        _soundToggle.Click+=(_,_)=>{try{_settings?.ToggleSounds();UpdateMusic();if(_settings?.SoundsEnabled!=true)_playSound?.Invoke(null);PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        _appearance.Click+=(_,_)=>{try{_settings?.ToggleHomeAppearance();ApplyPalette();PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        var scene=new Panel();scene.Children.Add(_backdrop);scene.Children.Add(_root);_scope=new ThemeVariantScope{Child=scene};Content=_scope;
        ConfigureStyles();
        AutomationProperties.SetName(_grid,"Grille des consoles et jeux");AutomationProperties.SetName(_search,"Rechercher dans la grille");
        _musicDelay.Tick+=(_,_)=>{_musicDelay.Stop();PlaySelectedTrack();};
        PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty){if(IsVisible){if(ShowClock)_clockTimer.Start();RefreshPreview();UpdateMusic();}else{_clockTimer.Stop();_selectionAnimation.Stop();ClearPreview();StopMusic();}}};
        AttachedToVisualTree+=(_,_)=>{if(IsVisible&&ShowClock)_clockTimer.Start();ResizeHome();RefreshPreview();UpdateMusic();};
        DetachedFromVisualTree+=(_,_)=>{_clockTimer.Stop();_selectionAnimation.Stop();ClearPreview();StopMusic();};
        SizeChanged+=(_,_)=>ResizeHome();ApplyPalette();ResizeHome();
        AddHandler(KeyDownEvent,(_,e)=>{if(IsPopupOpen)return;if(HandleKey(e.Key)){e.Handled=true;return;}if(e.Key==Key.Enter&&!_search.IsKeyboardFocusWithin){ActivateFocused();e.Handled=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private static TextBlock Text(double size)=>new(){FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    private static Button Button(string text)
    {
        var result=new Button{Content=text,FontSize=14,FontWeight=FontWeight.SemiBold,MinHeight=48,HorizontalContentAlignment=HorizontalAlignment.Center};
        AutomationProperties.SetName(result,text);ToolTip.SetTip(result,text);
        result.Template=new FuncControlTemplate<Button>((button,_)=>
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            if(button.Tag is string kind){var icon=new HomeGlyph{Kind=kind,Width=button.Classes.Contains("home-dock")?24:19,Height=button.Classes.Contains("home-dock")?24:19};icon.Bind(HomeGlyph.InkProperty,new Binding("Foreground"){Source=button});content.Children.Add(icon);}
            if(!button.Classes.Contains("home-dock")&&!button.Classes.Contains("home-icon"))
            {
                var label=new TextBlock{TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxLines=2,VerticalAlignment=VerticalAlignment.Center};
                label.Bind(TextBlock.TextProperty,new Binding("Content"){Source=button});label.Bind(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=button});label.Bind(TextBlock.FontSizeProperty,new Binding("FontSize"){Source=button});label.Bind(TextBlock.FontWeightProperty,new Binding("FontWeight"){Source=button});content.Children.Add(label);
            }
            var border=new Border{Child=content};border.Bind(Border.BackgroundProperty,new Binding("Background"){Source=button});border.Bind(Border.BorderBrushProperty,new Binding("BorderBrush"){Source=button});border.Bind(Border.BorderThicknessProperty,new Binding("BorderThickness"){Source=button});border.Bind(Border.CornerRadiusProperty,new Binding("CornerRadius"){Source=button});border.Bind(Border.PaddingProperty,new Binding("Padding"){Source=button});return border;
        });
        return result;
    }
    private void ConfigureStyles()
    {
        static Setter Brush(AvaloniaProperty property,string key)=>new(property,new DynamicResourceExtension(key));
        void Style(Func<Selector?,Selector> selector,params Setter[] setters){var style=new Style(selector);foreach(var setter in setters)style.Setters.Add(setter);Styles.Add(style);}
        Style(s=>s.OfType<Button>(),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeControl"),Brush(global::Avalonia.Controls.Button.ForegroundProperty,"HomeText"),Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeLine"),new Setter(global::Avalonia.Controls.Button.BorderThicknessProperty,new Thickness(1)),new Setter(global::Avalonia.Controls.Button.PaddingProperty,new Thickness(18,10)),new Setter(global::Avalonia.Controls.Button.CornerRadiusProperty,new CornerRadius(14)));
        Style(s=>s.OfType<Button>().Class("home-dock"),new Setter(global::Avalonia.Controls.Button.WidthProperty,48d),new Setter(global::Avalonia.Controls.Button.HeightProperty,48d),new Setter(global::Avalonia.Controls.Button.PaddingProperty,new Thickness(10)),new Setter(global::Avalonia.Controls.Button.CornerRadiusProperty,new CornerRadius(16)),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeTransparent"),Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeTransparent"));
        Style(s=>s.OfType<Button>().Class("home-icon"),new Setter(global::Avalonia.Controls.Button.WidthProperty,48d),new Setter(global::Avalonia.Controls.Button.HeightProperty,48d),new Setter(global::Avalonia.Controls.Button.PaddingProperty,new Thickness(12)),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeTransparent"),Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeTransparent"));
        Style(s=>s.OfType<Button>().Class("home-primary"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeAccent"),Brush(global::Avalonia.Controls.Button.ForegroundProperty,"HomeOnAccent"),Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeAccent"));
        Style(s=>s.OfType<Button>().Class(":pointerover"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeHover"));
        Style(s=>s.OfType<Button>().Class(":pressed"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomePressed"));
        Style(s=>s.OfType<Button>().Class("home-primary").Class(":pointerover"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeAccentHover"),Brush(global::Avalonia.Controls.Button.ForegroundProperty,"HomeOnAccent"));
        Style(s=>s.OfType<Button>().Class("home-primary").Class(":pressed"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeAccentPressed"),Brush(global::Avalonia.Controls.Button.ForegroundProperty,"HomeOnAccent"));
        Style(s=>s.OfType<Button>().Class(":focus-visible"),Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeAccent"),new Setter(global::Avalonia.Controls.Button.BorderThicknessProperty,new Thickness(2)));
        Style(s=>s.OfType<Button>().Class(":disabled"),new Setter(global::Avalonia.Controls.Button.OpacityProperty,.38));
        Style(s=>s.OfType<ListBoxItem>(),new Setter(ListBoxItem.PaddingProperty,new Thickness(4)),new Setter(ListBoxItem.MarginProperty,new Thickness(2)),new Setter(ListBoxItem.CornerRadiusProperty,new CornerRadius(18)),new Setter(ListBoxItem.BorderThicknessProperty,new Thickness(0)),new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Center),new Setter(ListBoxItem.VerticalContentAlignmentProperty,VerticalAlignment.Center),Brush(ListBoxItem.BackgroundProperty,"HomeTransparent"));
        Style(s=>s.OfType<ListBoxItem>().Class(":selected"),Brush(ListBoxItem.BackgroundProperty,"HomeTransparent"));
        Style(s=>s.OfType<ListBoxItem>().Class(":pointerover"),Brush(ListBoxItem.BackgroundProperty,"HomeHover"));
        Style(s=>s.OfType<ListBoxItem>(),new Setter(ListBoxItem.TemplateProperty,new FuncControlTemplate<ListBoxItem>((item,_)=>
        {
            var content=new ContentPresenter{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};content.Bind(ContentPresenter.ContentProperty,new Binding("Content"){Source=item});content.Bind(ContentPresenter.ContentTemplateProperty,new Binding("ContentTemplate"){Source=item});
            var frame=new Border{Child=content,CornerRadius=new CornerRadius(18)};frame.Bind(Border.BackgroundProperty,new Binding("Background"){Source=item});frame.Bind(Border.PaddingProperty,new Binding("Padding"){Source=item});return frame;
        })));

    }
    private void ApplyPalette()
    {
        var light=LightPalette;_scope.RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark;
        string[] values=light?["#243044","#4F5D73","#FFFFFF","#E9EDF4","#DCE4EE","#D6DCE7","#417D95","#FFFFFF"]:["#F3F5FC","#B5C1D4","#232C3D","#303C51","#39475D","#465167","#A8D7E2","#142835"];
        var keys=new[]{"HomeText","HomeMuted","HomeControl","HomeHover","HomePressed","HomeLine","HomeAccent","HomeOnAccent"};for(var i=0;i<keys.Length;i++)Resources[keys[i]]=new SolidColorBrush(Color.Parse(values[i]));Resources["HomeTransparent"]=Brushes.Transparent;Resources["HomeAccentHover"]=new SolidColorBrush(Color.Parse(light?"#356B82":"#BCE3EA"));Resources["HomeAccentPressed"]=new SolidColorBrush(Color.Parse(light?"#2D5C72":"#90C7D4"));
        Foreground=(IBrush)Resources["HomeText"]!;_title.Foreground=Foreground;_heading.Foreground=(IBrush)Resources["HomeMuted"]!;_status.Foreground=(IBrush)Resources["HomeMuted"]!;_pageLabel.Foreground=(IBrush)Resources["HomeMuted"]!;_clock.Foreground=Foreground;
        _backdrop.LightPalette=_banner.LightPalette=light;
        _hero.Background=new SolidColorBrush(Color.Parse(light?"#DFFFFFFF":"#CE192334"));_hero.BorderBrush=new SolidColorBrush(Color.Parse(light?"#FFFFFF":"#56627A"));_hero.BoxShadow=BoxShadows.Parse(light?"0 14 40 0 #142B3E5B":"0 18 48 0 #40000000");
        foreach(var panel in new[]{_lower,_dock}){panel.Background=new SolidColorBrush(Color.Parse(light?"#EBFFFFFF":"#E01C2535"));panel.BorderBrush=new SolidColorBrush(Color.Parse(light?"#E0E5EE":"#475267"));panel.BoxShadow=BoxShadows.Parse(light?"0 6 22 0 #122B3E5B":"0 8 26 0 #35000000");}
        _search.Background=(IBrush)Resources["HomeControl"]!;_search.Foreground=Foreground;_search.BorderBrush=(IBrush)Resources["HomeLine"]!;
        _appearance.Content=light?"Affichage : clair":"Affichage : sombre";
        AppearanceChanged?.Invoke(light);
        foreach(var tile in this.GetVisualDescendants().OfType<HomeArtworkControl>())tile.LightPalette=light;
        if(_selected is {} item)_title.SetGame(_platforms||ScreenRole==HomeScreenRole.Navigation?null:item.Id,item.Title,_platforms?null:item.Console);
    }
    private void ResizeHome()
    {
        var width=Math.Max(280,Bounds.Width);var height=Math.Max(320,Bounds.Height);var compact=width<600;var preview=ScreenRole==HomeScreenRole.Preview;var navigation=ScreenRole==HomeScreenRole.Navigation;var shortScreen=!preview&&(navigation||height<560&&width>600);
        var game=_selected is not null&&!_platforms;
        var rows=shortScreen?(height<370?1:2):game&&height<800?2:compact?3:height<1100?2:3;
        if(game&&!compact&&height<700)rows=1;
        _root.MaxHeight=shortScreen?double.PositiveInfinity:compact?760:940;_root.VerticalAlignment=VerticalAlignment.Center;
        _root.Margin=new Thickness(compact?12:28,shortScreen?6:compact?12:game?14:18);_root.RowSpacing=shortScreen?6:compact?(game?10:12):game?12:16;
        _hero.IsVisible=!shortScreen;_heading.IsVisible=!shortScreen;_selectedName.IsVisible=shortScreen;_root.RowDefinitions[1].Height=shortScreen?new GridLength(0):new GridLength(1,GridUnitType.Star);
        var gamePreviewHeight=compact?(height<800?190:180):Math.Clamp((height-800)*.4+210,110,270);
        _hero.Height=game&&!shortScreen?gamePreviewHeight:double.NaN;_hero.MaxHeight=compact?300:360;_hero.MinHeight=shortScreen?0:game?gamePreviewHeight:110;
        ArrangePreview(compact);
        var available=Math.Min(960,width-(compact?24:56))-32;
        var columns=Math.Clamp((int)(available/(compact?78:108)),3,8);
        var cell=Math.Floor(available/columns);_tileSize=Math.Max(54,Math.Min(shortScreen?64:112,cell-12));
        _achievements.SetCompact(compact);_achievements.IsVisible=game&&ShowAchievements;_status.IsVisible=!shortScreen;
        _lower.IsVisible=_dock.IsVisible=_message.IsVisible=!preview;
        if(preview){_root.MaxHeight=double.PositiveInfinity;_root.VerticalAlignment=VerticalAlignment.Stretch;_hero.Height=double.NaN;_hero.MaxHeight=double.PositiveInfinity;_hero.MinHeight=110;}
        if(navigation)rows=Math.Clamp((int)((height-260-(_search.IsVisible?56:0))/(_tileSize+12)),1,4);
        _grid.Width=cell*columns;_tiles.ItemWidth=cell;_tiles.ItemHeight=_tileSize+12;
        _lowerContent.RowSpacing=shortScreen?4:game?8:10;_lowerContent.Margin=new Thickness(compact?12:16,shortScreen?4:8,compact?12:16,shortScreen?6:14);
        foreach(var tile in _grid.GetVisualDescendants().OfType<HomeArtworkControl>()){tile.Width=_tileSize;tile.Height=_tileSize;}
        if(_columns!=columns||_rows!=rows){_columns=columns;_rows=rows;var index=Array.FindIndex(_filtered,i=>i.Id==_selected?.Id);_page=Math.Max(0,index)/PageSize;ShowPage();}
        RefreshPreview();
    }
    public void Configure(LibraryPresentationStore settings,Action<string?>? playMusic,Action<string?>? playSound=null)
    {
        _settings=settings;_playMusic=playMusic;_playSound=playSound;ApplyPalette();
        try
        {
            _audioDirectory=settings.BundledAudioDirectory;Directory.CreateDirectory(_audioDirectory);
            foreach(var name in new[]{"ambience","move","open","launch"})
            {
                var target=Path.Combine(_audioDirectory,name+".wav");if(File.Exists(target))continue;
                using var input=AssetLoader.Open(new Uri($"avares://{typeof(DualScreenLibraryView).Assembly.GetName().Name}/Assets/Sounds/home-{name}.wav"));
                using var output=File.Create(target);input.CopyTo(output);
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){_audioDirectory=null;}
        UpdateMusic();
    }
    private void Cue(string name)
    {
        if(_settings?.SoundsEnabled!=true||!SoundsAllowed||!_playbackActive||!IsEffectivelyVisible||_audioDirectory is null)return;
        var now=Environment.TickCount64;if(name=="move"&&now-_lastSound<85)return;_lastSound=now;
        try{_playSound?.Invoke(Path.Combine(_audioDirectory,name+".wav"));}catch(Exception e) when(e is IOException or InvalidOperationException){ }
    }
    public void SetItems(IEnumerable<DualScreenItem> items,string heading,bool platforms,string? selectedId=null)
    {
        _items=items.ToArray();_platforms=platforms;_heading.Text=heading;_search.Text="";_page=0;
        _selected=_items.FirstOrDefault(i=>i.Id==selectedId)??_items.FirstOrDefault();Filter();
    }
    private void Filter()
    {
        var previousId=_selected?.Id;
        var search=_search.Text?.Trim()??"";_filtered=_items.Where(i=>i.Title.Contains(search,StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if(_selected is null||!_filtered.Any(i=>i.Id==_selected.Id))_selected=_filtered.FirstOrDefault();
        var index=Array.FindIndex(_filtered,i=>i.Id==_selected?.Id);if(index>=0)_page=index/PageSize;
        ShowPage();if(_selected is {} item)Select(item,previousId!=item.Id);else ClearSelection();
    }
    private void ShowPage()
    {
        _building=true;
        try{_grid.ItemsSource=ScreenRole==HomeScreenRole.Preview?null:_filtered.Skip(_page*PageSize).Take(PageSize).ToArray();_grid.SelectedItem=_selected;}
        finally{_building=false;}
        _previous.IsEnabled=_page>0;_next.IsEnabled=(_page+1)*PageSize<_filtered.Length;
        _pageLabel.Text=_filtered.Length==0?"Aucun résultat":$"{_filtered.Length} · {_page+1}/{Math.Max(1,(_filtered.Length+PageSize-1)/PageSize)}";
    }
    private void ChangePage(int step)
    {
        var target=_page+step;if(target<0||target*PageSize>=_filtered.Length)return;_page=target;_selected=_filtered[target*PageSize];ShowPage();Select(_selected,true);
    }
    private Control Thumbnail(DualScreenItem item)
    {
        var tile=new HomeArtworkControl{CoverPath=item.Cover,Platform=item.Console,Title=item.Title,LightPalette=LightPalette,IsSelected=item.Id==_selected?.Id,Width=_tileSize,Height=_tileSize,Tag=item.Id};
        ToolTip.SetTip(tile,item.Title);AutomationProperties.SetName(tile,item.Title);return tile;
    }
    private void Select(DualScreenItem item,bool notify)
    {
        var changed=_displayedId!=item.Id;_displayedId=item.Id;_selected=item;_title.SetGame(_platforms||ScreenRole==HomeScreenRole.Navigation?null:item.Id,item.Title,_platforms?null:item.Console);_selectedName.Text=item.Title;_status.Text=item.Status;_message.Text="";
        ResizeHome();_import.IsEnabled=!_platforms;
        _launch.Content=item.Action;AutomationProperties.SetName(_launch,item.Action);_launch.IsEnabled=item.CanLaunch;
        foreach(var tile in _grid.GetVisualDescendants().OfType<HomeArtworkControl>())tile.IsSelected=Equals(tile.Tag,item.Id);
        ToolTip.SetTip(_launch,item.Status);ToolTip.SetTip(_hero,item.Title);
        _options.IsVisible=!_platforms;UpdateMusic();
        _options.IsEnabled=true;_music.IsEnabled=!_importingMusic;
        if(notify)SelectionChanged?.Invoke(item.Id);
        if(changed){if(notify)Cue("move");_selectionAnimation.Stop();_selectionTime=0;_bannerOffset.Y=0;if(_hero.IsVisible&&!ReduceMotion)_selectionAnimation.Start();}
    }
    private void ArrangePreview(bool compact)
    {
        var game=_selected is not null&&!_platforms;
        _heroContent.RowDefinitions=new(game?"*":_selected is null?"*":"*,Auto");
        _heroContent.ColumnDefinitions=new(game?"1*,1.25*":"*");_heroContent.ColumnSpacing=game?(compact?10:22):0;
        _heroContent.Margin=new Thickness(game?(compact?12:18):(compact?20:30));
        _art.IsVisible=_selected is not null;_art.Width=game?double.NaN:compact?180:280;_art.Height=double.NaN;
        _art.MaxHeight=game?double.PositiveInfinity:compact?110:170;
        _art.HorizontalAlignment=game?HorizontalAlignment.Stretch:HorizontalAlignment.Center;
        _art.VerticalAlignment=game?VerticalAlignment.Stretch:VerticalAlignment.Center;
        _title.MaxHeight=game?(compact?110:140):compact?120:160;_title.FontSize=game?(compact?18:24):compact?23:32;
        Grid.SetRow(_art,0);Grid.SetColumn(_art,0);Grid.SetRow(_title,game||_selected is null?0:1);Grid.SetColumn(_title,game?1:0);
    }
    private void ClearPreview()
    {
        var preview=_casePreview;_casePreview=null;_previewItem=null;_art.Children.Clear();preview?.Dispose();
    }
    private void RefreshPreview()
    {
        if(_selected is not {} item||!_hero.IsVisible||!IsEffectivelyVisible||TopLevel.GetTopLevel(this) is null){ClearPreview();return;}
        if(_previewItem==item&&_previewForPlatform==_platforms&&_art.Children.Count>0)return;
        ClearPreview();_previewItem=item;_previewForPlatform=_platforms;
        if(!_platforms&&File.Exists(item.Cover))
        {
            GameCaseControl? preview=null;
            try
            {
                var validated=CoverBitmapCache.Acquire(item.Cover);
                try
                {
                    preview=new GameCaseControl{MinWidth=0,MinHeight=0,RealisticShell=true,UseImportedModel=true,UseCompactTextureMesh=true,
                        CaseFormat=GameCaseControl.FormatForConsole(item.Console),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
                    string Face(string path)=>string.IsNullOrWhiteSpace(path)?path:PreviewFacePath?.Invoke(path)??path;
                    preview.SetCover(item.Cover,Face(item.Back),Face(item.Spine),Face(item.Right),Face(item.Top),Face(item.Bottom));
                    preview.SetShelfPose(24,true,animate:false);preview.IsInteractive=!ReduceMotion&&_playbackActive;
                    AutomationProperties.SetName(preview,item.Title);_casePreview=preview;_art.Children.Add(preview);return;
                }
                finally{CoverBitmapCache.Release(validated);}
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){preview?.Dispose();_casePreview=null;}
        }
        _art.Children.Add(new HomeArtworkControl{CoverPath=item.Cover,Platform=item.Console,Title=item.Title,IsPreview=true,LightPalette=LightPalette});
    }
    private void ClearSelection(){_selected=null;_achievements.IsVisible=false;_displayedId=null;_selectedName.Text="Aucun résultat";ClearPreview();ArrangePreview(Bounds.Width<600);_title.SetGame(null,"Aucun jeu trouvé");_status.Text="Essaie une autre recherche ou ouvre le catalogue.";_launch.IsEnabled=_options.IsEnabled=_import.IsEnabled=false;_music.IsEnabled=true;StopMusic();}
    public void UpdateSelected(string status,string action,bool enabled)
    {_status.Text=status;ToolTip.SetTip(_launch,status);_launch.Content=action;AutomationProperties.SetName(_launch,action);_launch.IsEnabled=enabled;}
    public void ShowMessage(string? message)=>_message.Text=message;
    public bool DismissPopup()
    {
        if(!IsPopupOpen)return false;_music.Flyout?.Hide();_music.Focus();return true;
    }
    public void Move(int horizontal,int vertical)
    {
        if(IsPopupOpen)
        {
            var buttons=new[]{_import,_mute,_soundToggle,_appearance}.Where(button=>button.IsEffectivelyVisible&&button.IsEnabled).ToArray();
            if(buttons.Length==0)return;
            var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();var choiceIndex=Array.FindIndex(buttons,button=>ReferenceEquals(button,focused));
            choiceIndex=choiceIndex<0?0:Math.Clamp(choiceIndex+Math.Sign(horizontal+vertical),0,buttons.Length-1);buttons[choiceIndex].Focus();return;
        }
        if(_filtered.Length==0)return;var index=Math.Max(0,Array.FindIndex(_filtered,i=>i.Id==_selected?.Id));
        index=Math.Clamp(index+horizontal+vertical*_columns,0,_filtered.Length-1);var item=_filtered[index];var page=index/PageSize;
        if(page!=_page){_page=page;_selected=item;ShowPage();}
        else{_building=true;try{_grid.SelectedItem=item;}finally{_building=false;}}
        Select(item,true);_grid.Focus();_grid.ScrollIntoView(item);
    }
    public void Activate(){if(_selected is {} item&&_launch.IsEnabled){Cue(_platforms?"open":"launch");LaunchRequested?.Invoke(item.Id);}}
    public void ActivateFocused()
    {
        var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if(focused is TextBox)return;
        if(focused is Button button)
        {if(button.IsEffectivelyVisible&&button.IsEnabled)button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));return;}
        if(IsPopupOpen||focused is Control other&&!other.GetVisualAncestors().Contains(this)&&!ReferenceEquals(other,this))return;
        Activate();
    }
    public bool HandleKey(Key key)
    {
        if(IsPopupOpen||_search.IsKeyboardFocusWithin)return false;
        switch(key){case Key.Left:Move(-1,0);break;case Key.Right:Move(1,0);break;case Key.Up:Move(0,-1);break;case Key.Down:Move(0,1);break;default:return false;}return true;
    }
    public void SetPlaybackActive(bool active){_playbackActive=active;if(_casePreview is {} preview)preview.IsInteractive=active&&!ReduceMotion;if(active)UpdateMusic();else{StopMusic();_playSound?.Invoke(null);}}
    private void UpdateMusic(){_mute.Content=_settings?.MusicEnabled==true?"Musique : oui":"Musique : non";_soundToggle.Content=_settings?.SoundsEnabled==true?"Sons : oui":"Sons : non";_musicDelay.Stop();if(_playMusic is not null&&_settings?.MusicEnabled==true&&_playbackActive&&IsEffectivelyVisible)_musicDelay.Start();else StopMusic();}
    private void PlaySelectedTrack()
    {
        if(!_playbackActive||!IsEffectivelyVisible)return;
        var path=_settings?.MusicEnabled==true?(!_platforms&&_selected is {} item?_settings.Track(item.Id):null)??(_audioDirectory is not null?Path.Combine(_audioDirectory,"ambience.wav"):null):null;
        if(_playing==path)return;
        _playing=path;try{_playMusic?.Invoke(path);}catch(Exception){_playing=null;_message.Text="Cette piste ne peut pas être lue. Choisis un autre MP3 ou WAV.";}
    }
    public void StopMusic(){_musicDelay.Stop();if(_playing is not null){_playing=null;_playMusic?.Invoke(null);}}
    private async Task ImportMusic()
    {
        if(_importingMusic||_settings is null||_selected is not {} item||_platforms||TopLevel.GetTopLevel(this)?.StorageProvider is not {} provider)return;
        _importingMusic=true;_music.IsEnabled=false;
        try
        {
            var files=await provider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Musique de "+item.Title,AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Musique MP3 ou WAV"){Patterns=["*.mp3","*.wav"],MimeTypes=["audio/mpeg","audio/wav","audio/x-wav"]}]});
            if(files.Count==0)return;using var file=files[0];await using var input=await file.OpenReadAsync();
            await _settings.ImportMusicAsync(item.Id,Path.GetExtension(file.Name),input);StopMusic();_message.Text="Musique locale associée au jeu.";UpdateMusic();PreferencesChanged?.Invoke();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException){_message.Text=e.Message;}
        finally{_importingMusic=false;_music.IsEnabled=true;}
    }
}
internal sealed class HomeGlyph : Control
{
    public static readonly StyledProperty<IBrush> InkProperty=AvaloniaProperty.Register<HomeGlyph,IBrush>(nameof(Ink),Brushes.White);
    public IBrush Ink { get=>GetValue(InkProperty);set=>SetValue(InkProperty,value); }
    public string Kind { get; init; }="more";
    static HomeGlyph()=>AffectsRender<HomeGlyph>(InkProperty);
    public HomeGlyph(){IsHitTestVisible=false;}
    public override void Render(DrawingContext context)
    {
        var size=Math.Min(Bounds.Width,Bounds.Height);if(size<=0)return;
        using(context.PushTransform(Matrix.CreateScale(size/24,size/24)*Matrix.CreateTranslation((Bounds.Width-size)/2,(Bounds.Height-size)/2)))
        {
            var pen=new Pen(Ink,1.8, lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);
            void Line(double x1,double y1,double x2,double y2)=>context.DrawLine(pen,new Point(x1,y1),new Point(x2,y2));
            void Dot(double x,double y,double radius=1.3)=>context.DrawEllipse(Ink,null,new Point(x,y),radius,radius);
            switch(Kind)
            {
                case "console":
                    context.DrawRectangle(null,pen,new Rect(3,7,18,12),4,4);Line(7,10,7,16);Line(4.5,13,9.5,13);Dot(15,11);Dot(18,15);break;
                case "library":
                    context.DrawRectangle(null,pen,new Rect(3,5,5,15),1.5,1.5);context.DrawRectangle(null,pen,new Rect(10,5,5,15),1.5,1.5);Line(18,5,21,19);Line(4.5,9,6.5,9);Line(11.5,9,13.5,9);break;
                case "settings":
                    context.DrawEllipse(null,pen,new Point(12,12),6,6);context.DrawEllipse(null,pen,new Point(12,12),2.2,2.2);
                    for(var i=0;i<8;i++){var a=i*Math.PI/4;Line(12+Math.Cos(a)*7.2,12+Math.Sin(a)*7.2,12+Math.Cos(a)*9.4,12+Math.Sin(a)*9.4);}break;
                case "search":context.DrawEllipse(null,pen,new Point(10,10),6,6);Line(14.5,14.5,21,21);break;
                case "customize":
                    for(var i=0;i<3;i++){var x=5+i*7;Line(x,3,x,21);var y=i==1?15:8;context.DrawRectangle(Ink,null,new Rect(x-2.7,y-2,5.4,4),1.5,1.5);}break;
                case "play":context.DrawGeometry(Ink,null,Geometry.Parse("M8,4 L20,12 L8,20 Z"));break;
                case "left":Line(14.5,5,7.5,12);Line(7.5,12,14.5,19);break;
                case "right":Line(9.5,5,16.5,12);Line(16.5,12,9.5,19);break;
                case "clock":context.DrawEllipse(null,pen,new Point(12,12),9,9);Line(12,6,12,12);Line(12,12,16,14);break;
                default:Dot(5,12);Dot(12,12);Dot(19,12);break;
            }
        }
    }
}
