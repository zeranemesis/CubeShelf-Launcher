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
    private readonly TextBlock _heading=Text(20),_status=Text(13),_emptyGrid=Text(13),_message=Text(13);
    private readonly RetroAchievementsButton _achievements=new(){IsVisible=false};
    private readonly GameTitleControl _title=new(){FontSize=24,Foreground=Brushes.Black,MaxHeight=85};
    private readonly Grid _art=new(),_previewBody=new();
    private readonly HomeArtworkControl _quickPreview=new(){IsPreview=true,PreviewDecodeHeight=256,DecodeOffThread=true};
    private readonly DispatcherTimer _previewDelay=new(){Interval=TimeSpan.FromMilliseconds(55)};
    private CancellationTokenSource? _upperPreviewPreparation;
    private GameCaseControl? _casePreview;
    public Func<string,string>? PreviewFacePath { get; set; }
    private DualScreenItem? _previewItem;
    private bool _previewForPlatform;
    private readonly ListBox _grid=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0),Focusable=true,AutoScrollToSelectedItem=false};
    private Vector _cursorTravel;
    private readonly TextBox _search=new(){Watermark="Rechercher…",MinHeight=48};
    private readonly Button _launch=Button("Ouvrir"),_music=Button("Musique"),_mute=Button("Musique : oui"),_options=Button("Options du jeu");
    private readonly Button _soundToggle=Button("Sons : oui"),_import=Button("Associer une piste MP3 / WAV");
    private readonly DispatcherTimer _musicDelay=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly Border _hero,_lower,_dock;
    private readonly ThemeVariantScope _scope;
    private readonly HomeMenuBackdrop _backdrop=new(),_banner=new(){Banner=true};
    private readonly Button _appearance=Button("Affichage : clair"),_find=Button("Rechercher");
    private readonly List<Button> _navigation=[];
    private readonly Button _back=Button("Revenir à la bibliothèque");
    private readonly Button _consoles;
    private bool _achievementPage;
    private readonly TextBlock _focusedAction=Text(12);
    private readonly Control[][] _focusRows;
    private const int GridFocusRow=1;
    private readonly Grid _heroContent=new(){RowDefinitions=new("*,Auto"),RowSpacing=12};
    private readonly Grid _lowerContent=new(){RowDefinitions=new("Auto,Auto,Auto,Auto,Auto"),RowSpacing=12};
    private double _tileSize=76;
    private bool LightPalette=>_settings?.LightHome??true;
    public bool IsPopupOpen=>_music.Flyout?.IsOpen==true;
    private readonly TextBlock _clock=Text(14),_selectedName=Text(17),_updateNotice=Text(13);
    private readonly StackPanel _time=new(){Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
    public bool ShowClock
    {
        get=>_time.IsVisible;
        set{_time.IsVisible=value;if(!value)_clockTimer.Stop();else if(IsEffectivelyVisible){_clock.Text=DateTime.Now.ToString("HH:mm");_clockTimer.Start();}}
    }
    private readonly Grid _root=new(){RowDefinitions=new("Auto,*,Auto,Auto,Auto"),RowSpacing=14,MaxWidth=1040,Margin=new Thickness(24,12)};
    private bool _layoutForGame;
    private readonly WrapPanel _tiles=new(){Orientation=Orientation.Vertical};
    private readonly DispatcherTimer _clockTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private Action<string?>? _playSound;
    private string? _audioDirectory;
    private long _lastSound;
    private string? _displayedId;
    private bool _reduceMotion;
    private readonly DemandAnimation _selectionAnimation;
    private readonly MatrixTransform _previewPose=new(),_tilePose=new();
    private HomeArtworkControl? _movingTile;
    private ListBoxItem? _movingContainer;
    private const double PreviewDuration=0.19;
    private double _selectionElapsed=PreviewDuration;
    private int _selectionDirection;
    public bool ReduceMotion
    {
        get=>_reduceMotion;
        set
        {
            if(_reduceMotion==value)return;_reduceMotion=value;
            if(value)StopSelectionMotion();else ResumeSelectionMotion();
            if(_casePreview is {} preview){if(value)preview.SetShelfPose(24,true,animate:false);preview.IsInteractive=!value&&_playbackActive;}
        }
    }
    public bool SoundsAllowed { get; set; }=true;
    public bool ShowAchievements { get; set; }=true;
    private DualScreenItem[] _items=[],_filtered=[];
    private DualScreenItem? _selected;
    private LibraryPresentationStore? _settings;
    private Action<string?>? _playMusic;
    private int _rows=3;
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
    public event Action? AchievementsRequested,BackRequested;
    public DualScreenLibraryView()
    {
        Name="DualScreenLibraryView";
        _selectionAnimation=new DemandAnimation(this,AdvanceSelection);
        FontFamily=new FontFamily($"avares://{typeof(DualScreenLibraryView).Assembly.GetName().Name}/Assets/Home/Fonts#Nunito");
        var header=new Grid{ColumnDefinitions=new("*,Auto,Auto"),Margin=new Thickness(6,0)};
        _heading.FontSize=15;_heading.FontWeight=FontWeight.SemiBold;_heading.MaxLines=1;_heading.TextTrimming=TextTrimming.CharacterEllipsis;var headerWords=new Grid();headerWords.Children.Add(_heading);headerWords.Children.Add(_selectedName);_selectedName.FontSize=18;_selectedName.FontWeight=FontWeight.SemiBold;_selectedName.MaxLines=1;_selectedName.TextTrimming=TextTrimming.CharacterEllipsis;header.Children.Add(headerWords);
        var time=_time;
        _updateNotice.Text="Mise à jour · X";_updateNotice.IsVisible=false;_updateNotice.FontWeight=FontWeight.SemiBold;
        _updateNotice.VerticalAlignment=VerticalAlignment.Center;
        _updateNotice.Margin=new Thickness(8,0);
        _updateNotice.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("HomeText"));
        Grid.SetColumn(_updateNotice,1);header.Children.Add(_updateNotice);
        var clockGlyph=new HomeGlyph{Kind="clock",Width=17,Height=17};clockGlyph.Bind(HomeGlyph.InkProperty,new DynamicResourceExtension("HomeMuted"));time.Children.Add(clockGlyph);time.Children.Add(_clock);
        _clock.Text=DateTime.Now.ToString("HH:mm");Grid.SetColumn(time,2);header.Children.Add(time);_root.Children.Add(header);
        _clockTimer.Tick+=(_,_)=>_clock.Text=DateTime.Now.ToString("HH:mm");
        _heroContent.Margin=new Thickness(28);
        _art.HorizontalAlignment=HorizontalAlignment.Center;_art.VerticalAlignment=VerticalAlignment.Center;
        _art.Children.Add(_previewBody);
        _heroContent.Children.Add(_art);
        _title.HorizontalAlignment=HorizontalAlignment.Center;_title.VerticalAlignment=VerticalAlignment.Center;_title.MaxWidth=500;_title.MaxHeight=170;
        Grid.SetRow(_title,1);_heroContent.Children.Add(_title);
        var heroLayers=new Panel();heroLayers.Children.Add(_banner);heroLayers.Children.Add(_heroContent);
        _hero=new Border{Name="HomePreview",Child=heroLayers,MaxWidth=760,CornerRadius=new CornerRadius(30),ClipToBounds=true,BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Stretch};
        Grid.SetRow(_hero,1);_root.Children.Add(_hero);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Center};
        Button Tool(string label,string icon,Action action)
        {
            var button=Button(label);button.Tag=icon;button.Classes.Add("home-dock");button.Click+=(_,_)=>{Cue("open");action();};
            navigation.Children.Add(button);_navigation.Add(button);return button;
        }
        _back.Tag="left";_back.Classes.Add("home-dock");_back.IsVisible=false;
        _back.Click+=(_,_)=>BackRequested?.Invoke();navigation.Children.Add(_back);_navigation.Add(_back);
        _consoles=Tool("Consoles","console",()=>ConsolesRequested?.Invoke());
        Tool("Mes jeux","library",()=>LibraryRequested?.Invoke());
        Tool("Paramètres","settings",()=>SettingsRequested?.Invoke());
        _find.Tag="search";_find.Classes.Add("home-dock");_find.Click+=(_,_)=>{_search.IsVisible=!_search.IsVisible;ResizeHome();if(_search.IsVisible)_search.Focus();else _grid.Focus();};navigation.Children.Add(_find);_navigation.Add(_find);
        _music.Content="Personnaliser";_music.Tag="customize";_music.Classes.Add("home-dock");ToolTip.SetTip(_music,"Personnaliser · audio et affichage");AutomationProperties.SetName(_music,"Personnaliser · audio et affichage");navigation.Children.Add(_music);_navigation.Add(_music);
        _dock=new Border{Child=navigation,Padding=new Thickness(8,5),CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(_dock,2);_root.Children.Add(_dock);
        _lowerContent.Margin=new Thickness(16,10,16,14);
        _emptyGrid.HorizontalAlignment=HorizontalAlignment.Center;
        _lowerContent.Children.Add(_emptyGrid);
        _grid.ItemsPanel=new FuncTemplate<Panel?>(()=>_tiles);
        _grid.ItemTemplate=new FuncDataTemplate<DualScreenItem>((item,_)=>item is null?null:Thumbnail(item));
        _grid.HorizontalAlignment=HorizontalAlignment.Center;_grid.VerticalAlignment=VerticalAlignment.Top;
        _grid.SelectionChanged+=(_,_)=>
        {
            if(_building||_grid.SelectedItem is not DualScreenItem item)return;
            if(!_grid.IsKeyboardFocusWithin)_grid.Focus();
            Select(item,true);
        };
        _grid.AddHandler(TappedEvent,(_,args)=>
        {
            if(!_achievementPage||args.Source is not Control source||
                (source as ListBoxItem??source.FindAncestorOfType<ListBoxItem>())?.Content is not DualScreenItem item)return;
            _grid.SelectedItem=item;OpenAchievementDetails();args.Handled=true;
        },Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        BuildRail();
        Grid.SetRow(_rail,1);_lowerContent.Children.Add(_rail);
        _search.Watermark="Rechercher un jeu…";_search.IsVisible=false;_search.TextChanged+=(_,_)=>Filter();
        Grid.SetRow(_search,2);_lowerContent.Children.Add(_search);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Center};
        _launch.Classes.Add("home-primary");_launch.MinWidth=154;_launch.MaxWidth=280;_launch.Click+=(_,_)=>Activate();actions.Children.Add(_launch);
        _options.Tag="more";_options.Classes.Add("home-icon");ToolTip.SetTip(_options,"Options du jeu · X");_options.Click+=(_,_)=>{if(_selected is {} item&&!_platforms){Cue("open");OptionsRequested?.Invoke(item.Id);}};actions.Children.Add(_options);
        _achievements.Click+=(_,_)=>AchievementsRequested?.Invoke();actions.Children.Add(_achievements);
        var gameActions=new StackPanel{Spacing=6,Children={_achievementSummary,actions}};
        Grid.SetRow(gameActions,3);_lowerContent.Children.Add(gameActions);
        _lower=new Border{Name="HomeGrid",Child=_lowerContent,MaxWidth=960,CornerRadius=new CornerRadius(26),BorderThickness=new Thickness(1)};Grid.SetRow(_lower,3);_root.Children.Add(_lower);
        _message.FontSize=12;_message.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(_message,4);_root.Children.Add(_message);
        _focusedAction.HorizontalAlignment=HorizontalAlignment.Center;_focusedAction.IsVisible=false;_focusedAction.FontWeight=FontWeight.SemiBold;
        _focusedAction.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("HomeText"));Grid.SetRow(_focusedAction,4);_root.Children.Add(_focusedAction);
        _focusRows=[[.._navigation],[_grid],[_search],[_launch,_options,_achievements]];
        foreach(var button in _focusRows.SelectMany(row=>row).OfType<Button>())
        {
            button.FocusAdorner=null;
            button.GotFocus+=(_,e)=>
            {
                if(e.NavigationMethod is NavigationMethod.Directional or NavigationMethod.Tab)
                {
                    _focusedAction.Text=AutomationProperties.GetName(button);
                    _focusedAction.IsVisible=true;_message.Opacity=0;
                }
            };
            button.LostFocus+=(_,_)=>{_focusedAction.IsVisible=false;_message.Opacity=1;};
        }
        _status.MaxLines=1;_status.TextTrimming=TextTrimming.CharacterEllipsis;_status.FontSize=12;_status.FontWeight=FontWeight.SemiBold;_status.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(_status,4);_lowerContent.Children.Add(_status);
        _selectedName.IsVisible=false;
        _import.Click+=async(_,_)=>{_music.Flyout?.Hide();await ImportMusic();};
        _music.Flyout=new Flyout{Content=new StackPanel{Spacing=8,Children={_import,_mute,_soundToggle,_appearance}}};
        _mute.Click+=(_,_)=>{try{_settings?.ToggleMusic();UpdateMusic();PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        _soundToggle.Click+=(_,_)=>{try{_settings?.ToggleSounds();UpdateMusic();if(_settings?.SoundsEnabled!=true)_playSound?.Invoke(null);PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        _appearance.Click+=(_,_)=>{try{_settings?.ToggleHomeAppearance();ApplyPalette();PreferencesChanged?.Invoke();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}};
        var scene=new Panel();scene.Children.Add(_backdrop);scene.Children.Add(_root);BuildAchievementViews(scene);_scope=new ThemeVariantScope{Child=scene};Content=_scope;
        ConfigureStyles();
        AutomationProperties.SetName(_grid,"Grille des consoles et jeux");AutomationProperties.SetName(_search,"Rechercher dans la grille");
        _musicDelay.Tick+=(_,_)=>{_musicDelay.Stop();PlaySelectedTrack();};
        _previewDelay.Tick+=(_,_)=>FinishGamePreview();
        PropertyChanged+=(_,e)=>
        {
            if(e.Property==IsVisibleProperty)
            {
                if(IsVisible){if(ShowClock)_clockTimer.Start();RefreshPreview();ResumeSelectionMotion();UpdateMusic();}
                else{_clockTimer.Stop();StopSelectionMotion();ClearPreview();StopMusic();}
            }
            else if(e.Property==IsEnabledProperty)
            {
                if(IsEnabled)ResumeSelectionMotion();else StopSelectionMotion();
            }
        };
        AttachedToVisualTree+=(_,_)=>{if(IsVisible&&ShowClock)_clockTimer.Start();ResizeHome();RefreshPreview();ResumeSelectionMotion();UpdateMusic();UpdateAchievementSelection();};
        DetachedFromVisualTree+=(_,_)=>{_clockTimer.Stop();StopSelectionMotion();ClearPreview();StopMusic();ResetAchievementProgress();};
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
        Style(s=>s.OfType<Button>().Class(":focus-visible"),Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeFocus"),
            Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeAccent"));
        Style(s=>s.OfType<Button>().Class("home-primary").Class(":focus-visible"),
            Brush(global::Avalonia.Controls.Button.BackgroundProperty,"HomeAccentHover"),
            Brush(global::Avalonia.Controls.Button.BorderBrushProperty,"HomeOnAccent"));
        Style(s=>s.OfType<Button>().Class(":disabled"),new Setter(global::Avalonia.Controls.Button.OpacityProperty,.38));
        Style(s=>s.OfType<ListBoxItem>(),new Setter(ListBoxItem.PaddingProperty,new Thickness(0)),new Setter(ListBoxItem.MarginProperty,new Thickness(0)),new Setter(ListBoxItem.HorizontalAlignmentProperty,HorizontalAlignment.Center),new Setter(ListBoxItem.VerticalAlignmentProperty,VerticalAlignment.Center),new Setter(ListBoxItem.CornerRadiusProperty,new CornerRadius(18)),new Setter(ListBoxItem.BorderThicknessProperty,new Thickness(0)),new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Center),new Setter(ListBoxItem.VerticalContentAlignmentProperty,VerticalAlignment.Center),Brush(ListBoxItem.BackgroundProperty,"HomeTransparent"));
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
        Resources["HomeFocus"]=new SolidColorBrush(Color.Parse(light?"#DAEAF0":"#344C5D"));
        Foreground=(IBrush)Resources["HomeText"]!;_title.Foreground=Foreground;_heading.Foreground=_achievementPage?Foreground:(IBrush)Resources["HomeMuted"]!;_status.Foreground=(IBrush)Resources["HomeMuted"]!;_emptyGrid.Foreground=(IBrush)Resources["HomeMuted"]!;_clock.Foreground=Foreground;
        _backdrop.LightPalette=_banner.LightPalette=light;
        _hero.Background=new SolidColorBrush(Color.Parse(light?"#DFFFFFFF":"#CE192334"));_hero.BorderBrush=new SolidColorBrush(Color.Parse(light?"#FFFFFF":"#56627A"));_hero.BoxShadow=BoxShadows.Parse(light?"0 14 40 0 #142B3E5B":"0 18 48 0 #40000000");
        foreach(var panel in new[]{_lower,_dock}){panel.Background=new SolidColorBrush(Color.Parse(light?"#EBFFFFFF":"#E01C2535"));panel.BorderBrush=new SolidColorBrush(Color.Parse(light?"#E0E5EE":"#475267"));panel.BoxShadow=BoxShadows.Parse(light?"0 6 22 0 #122B3E5B":"0 8 26 0 #35000000");}
        _search.Background=(IBrush)Resources["HomeControl"]!;_search.Foreground=Foreground;_search.BorderBrush=(IBrush)Resources["HomeLine"]!;
        _appearance.Content=light?"Affichage : clair":"Affichage : sombre";
        AppearanceChanged?.Invoke(light);
        foreach(var tile in this.GetVisualDescendants().OfType<HomeArtworkControl>())tile.LightPalette=light;
        if(_selected is {} item)SetTitle(item);
    }
    private void ResizeHome()
    {
        var width=Math.Max(280,Bounds.Width);var height=Math.Max(320,Bounds.Height);var compact=width<600;var preview=ScreenRole==HomeScreenRole.Preview;var navigation=ScreenRole==HomeScreenRole.Navigation;var shortScreen=!preview&&(navigation||height<560&&width>600);
        var game=!_platforms;
        _root.MaxHeight=shortScreen?double.PositiveInfinity:940;_root.VerticalAlignment=VerticalAlignment.Center;
        _root.Margin=new Thickness(compact?12:28,shortScreen?6:compact?12:game?14:18);_root.RowSpacing=shortScreen?6:compact?(game?10:12):game?12:16;
        _hero.IsVisible=!shortScreen;_heading.IsVisible=!shortScreen||_achievementPage;_selectedName.IsVisible=shortScreen&&!_achievementPage;_root.RowDefinitions[1].Height=shortScreen?new GridLength(0):new GridLength(1,GridUnitType.Star);
        var gamePreviewHeight=compact?170:Math.Clamp((height-800)*.4+210,110,270);
        _hero.Height=game&&!shortScreen?gamePreviewHeight:double.NaN;_hero.MaxHeight=compact?300:360;_hero.MinHeight=shortScreen?0:game?gamePreviewHeight:110;
        ArrangePreview(compact);
        _railWidth=Math.Min(960,width-(compact?24:56))-(compact?26:34);
        var chrome=shortScreen?182:game?gamePreviewHeight+194:318;
        var space=height-chrome-(_search.IsVisible?56:0)-(_achievementPage?36:0);
        _rows=space>=162?3:2;
        _tileSize=Math.Clamp(Math.Floor(Math.Min(space/_rows-8,compact?88:116)),46,116);
        _achievements.SetCompact(compact);_achievements.IsVisible=ShowAchievements&&!_achievementPage;_status.IsVisible=!shortScreen;
        _lower.IsVisible=_dock.IsVisible=_message.IsVisible=!preview;
        if(preview){_root.MaxHeight=double.PositiveInfinity;_root.VerticalAlignment=VerticalAlignment.Stretch;_hero.Height=double.NaN;_hero.MaxHeight=double.PositiveInfinity;_hero.MinHeight=110;}
        _tiles.ItemWidth=_tiles.ItemHeight=_tileSize+8;
        _lowerContent.RowSpacing=shortScreen?4:game?8:10;_lowerContent.Margin=new Thickness(compact?12:16,shortScreen?4:8,compact?12:16,shortScreen?6:14);
        foreach(var tile in _thumbnails.Values){tile.Width=_tileSize;tile.Height=_tileSize;}
        SizeRail();
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
    public void SetItems(IEnumerable<DualScreenItem> items,string heading,bool platforms,string? selectedId=null,bool achievementPage=false)
    {
        var pageChanged=_achievementPage!=achievementPage;
        if(pageChanged)
        {
            CloseAchievementDetails();ResetAchievementProgress();
            _achievementPage=achievementPage;
            _back.IsVisible=achievementPage;_consoles.IsVisible=!achievementPage;
            _heading.FontSize=achievementPage?18:15;
            _heading.Foreground=achievementPage?Foreground:(IBrush)Resources["HomeMuted"]!;
            _search.IsVisible=false;
        }
        _items=items.ToArray();_platforms=platforms;_heading.Text=heading;_search.Text="";
        _selected=_items.FirstOrDefault(i=>i.Id==selectedId)??_items.FirstOrDefault();Filter();
        if(pageChanged)ResizeHome();
        UpdateAchievementSelection();
    }
    private void Filter()
    {
        var previousId=_selected?.Id;
        var search=_search.Text?.Trim()??"";_filtered=_items.Where(i=>i.Title.Contains(search,StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if(_selected is null||!_filtered.Any(i=>i.Id==_selected.Id))_selected=_filtered.FirstOrDefault();
        ShowGrid();if(_selected is {} item)Select(item,previousId!=item.Id);else ClearSelection();
    }
    private void ShowGrid()
    {
        ResetTilePose();
        _thumbnails.Clear();_coverFirst=_coverEnd=-1;
        _building=true;
        try{_grid.ItemsSource=ScreenRole==HomeScreenRole.Preview?null:_filtered;_grid.SelectedItem=_selected;}
        finally{_building=false;}
        _emptyGrid.IsVisible=_filtered.Length==0;
        _emptyGrid.Text=_achievementPage&&_items.Length==0?"Aucun jeu compatible":"Aucun résultat";
        SizeRail(reset:true);
        ResumeSelectionMotion();
    }
    /// <summary>Id prefix of the "Continue" tile placed before the consoles.</summary>
    public const string ResumePrefix="resume:";
    private void SetTitle(DualScreenItem item)
    {
        var resume=item.Id.StartsWith(ResumePrefix,StringComparison.Ordinal);
        _title.SetGame(resume?item.Id[ResumePrefix.Length..]:_platforms||ScreenRole==HomeScreenRole.Navigation?null:item.Id,item.Title,_platforms&&!resume?null:item.Console);
    }
    private Control Thumbnail(DualScreenItem item)
    {
        var index=Array.IndexOf(_filtered,item);
        var tile=new HomeArtworkControl{DecodeOffThread=true,Platform=item.Console,Title=item.Title,LightPalette=LightPalette,
            IsSelected=item.Id==_selected?.Id,Width=_tileSize,Height=_tileSize,Tag=item.Id};
        _thumbnails[index]=tile;
        if(index>=_coverFirst&&index<_coverEnd)tile.CoverPath=item.Cover;
        ToolTip.SetTip(tile,item.Title);AutomationProperties.SetName(tile,item.Title);
        return tile;
    }
    private void Select(DualScreenItem item,bool notify)
    {
        var previousId=_displayedId;
        var changed=previousId!=item.Id;
        _displayedId=item.Id;_selected=item;
        var upperPreviewPending=ScreenRole==HomeScreenRole.Preview&&_layoutForGame&&!_platforms
            &&_previewItem is not null&&!_previewForPlatform&&_previewItem!=item&&IsEffectivelyVisible;
        if(ScreenRole!=HomeScreenRole.Navigation&&!upperPreviewPending)SetTitle(item);
        _selectedName.Text=item.Title;_status.Text=item.Status;_message.Text="";_updateNotice.IsVisible=false;
        var layoutChanged=_layoutForGame!=!_platforms;
        if(layoutChanged){CancelQuickPreview();ResizeHome();}
        _import.IsEnabled=!_platforms;
        var action=_achievementPage?"Voir les succès":item.Action;
        _launch.Content=action;AutomationProperties.SetName(_launch,action);_launch.IsEnabled=_achievementPage||item.CanLaunch;
        _achievementPlay.Content=item.Action;_achievementPlay.IsEnabled=item.CanLaunch;
        var oldIndex=Array.FindIndex(_filtered,i=>i.Id==previousId);
        var newIndex=Array.IndexOf(_filtered,item);
        if(_thumbnails.TryGetValue(oldIndex,out var oldTile)&&oldIndex!=newIndex)oldTile.IsSelected=false;
        if(_thumbnails.TryGetValue(newIndex,out var newTile))newTile.IsSelected=true;
        if(changed)
        {
            if(upperPreviewPending)StopSelectionMotion();
            else AnimateSelection(previousId,item.Id);
        }
        else ResumeSelectionMotion();
        RevealSelection(animate:changed);
        ToolTip.SetTip(_launch,item.Status);ToolTip.SetTip(_hero,item.Title);
        _options.IsVisible=!_platforms;UpdateMusic();
        _options.IsEnabled=true;_music.IsEnabled=!_importingMusic;
        if(notify)SelectionChanged?.Invoke(item.Id);
        if(!layoutChanged&&_selected?.Id==item.Id)
        {
            if(changed&&previousId is not null&&!_platforms&&_hero.IsVisible&&IsEffectivelyVisible
                &&_previewItem is not null&&_previewItem!=item)
                QueueGamePreview(item);
            else if(_previewItem==item&&(_previewDelay.IsEnabled||_upperPreviewPreparation is not null))
                FinishGamePreview();
            else
                RefreshPreview();
        }
        if(changed&&notify)Cue("move");
        UpdateAchievementSelection();
    }
    private bool CanAnimateSelection=>_selected is not null&&!ReduceMotion&&_playbackActive
        &&IsEffectivelyVisible&&IsEnabled&&TopLevel.GetTopLevel(this) is not null;

    private void ResumeSelectionMotion()
    {
        if(ScreenRole!=HomeScreenRole.Preview&&CanAnimateSelection)_selectionAnimation.Start();
    }

    private void AnimateSelection(string? previousId,string selectedId)
    {
        var interruptedOffset=_cursorTravel*CursorRemaining;
        ResetTilePose();
        _cursorTravel=default;
        if(!CanAnimateSelection){StopSelectionMotion();return;}
        if(previousId is null){ResumeSelectionMotion();return;}
        var previous=Array.FindIndex(_filtered,item=>item.Id==previousId);
        var current=Array.FindIndex(_filtered,item=>item.Id==selectedId);
        _selectionDirection=current<previous?-1:1;
        if(previous>=0&&current>=0&&ScreenRole!=HomeScreenRole.Preview)
        {
            _cursorTravel=new Vector((previous/_rows-current/_rows)*_tiles.ItemWidth,
                (previous%_rows-current%_rows)*_tiles.ItemHeight)+interruptedOffset;
            if(Math.Abs(_cursorTravel.X)>_rail.Width)_cursorTravel=default;
            _movingContainer=_grid.ContainerFromIndex(current) as ListBoxItem;
            if(_movingContainer is not null)_movingContainer.ZIndex=1;
        }
        _selectionElapsed=0;
        AdvanceSelection(0);
        _selectionAnimation.Start();
    }

    private bool AdvanceSelection(double elapsed)
    {
        const double tileDuration=0.145;
        const double orbitDuration=4.8;
        const int orbitSteps=288;
        var hasGrid=ScreenRole!=HomeScreenRole.Preview;
        _selectionElapsed+=elapsed;
        if(!CanAnimateSelection||(!hasGrid&&_selectionElapsed>=PreviewDuration))
        {
            ResetSelectionPose();
            return false;
        }
        if(_selectionElapsed>=orbitDuration+PreviewDuration)
            _selectionElapsed=PreviewDuration+(_selectionElapsed-PreviewDuration)%orbitDuration;

        if(_hero.IsVisible&&_selectionElapsed<PreviewDuration)
        {
            var remaining=Math.Pow(1-_selectionElapsed/PreviewDuration,3);
            var scale=1-0.018*remaining;
            _previewPose.Matrix=new Matrix(scale,0,0,scale,15*_selectionDirection*remaining,0);
            _heroContent.RenderTransform=_previewPose;
            _heroContent.Opacity=1-0.13*remaining;
        }
        else if(_heroContent.RenderTransform is not null)
        {
            _heroContent.RenderTransform=null;
            _heroContent.Opacity=1;
        }

        if(hasGrid)
        {
            var distance=_railTarget-_railOffset;
            if(Math.Abs(distance)>.3)SetRailOffset(_railOffset+distance*DemandAnimation.Response(.30,elapsed));
            else if(distance!=0)SetRailOffset(_railTarget);
            if(_movingTile is null)_thumbnails.TryGetValue(Array.IndexOf(_filtered,_selected),out _movingTile);
            if(_movingTile is not null)
            {
                var phase=_selectionElapsed%orbitDuration/orbitDuration;
                _movingTile.SetSelectionSweep(Math.Floor(phase*orbitSteps)/orbitSteps);
                if(_selectionElapsed<tileDuration)
                {
                    var progress=_selectionElapsed/tileDuration;
                    var lift=16*Math.Pow(progress*(1-progress),2);
                    var scale=1+0.038*lift;
                    _tilePose.Matrix=new Matrix(scale,0,0,scale,0,-1.8*lift);
                    _movingTile.RenderTransform=_tilePose;
                }
                else if(_movingTile.RenderTransform is not null)_movingTile.RenderTransform=null;
                _movingTile.SetSelectionOffset(_cursorTravel*CursorRemaining);
            }
        }
        return true;
    }

    private void StopSelectionMotion()
    {
        _selectionAnimation.Stop();
        _selectionElapsed=PreviewDuration;
        _cursorTravel=default;
        SetRailOffset(_railTarget);
        ResetSelectionPose();
    }

    private void ResetSelectionPose()
    {
        ResetTilePose();
        _heroContent.RenderTransform=null;
        _heroContent.Opacity=1;
    }

    private void ResetTilePose()
    {
        if(_movingContainer is not null){_movingContainer.ZIndex=0;_movingContainer=null;}
        if(_movingTile is null)return;
        _movingTile.RenderTransform=null;
        _movingTile.SetSelectionSweep(null);
        _movingTile.SetSelectionOffset(default);
        _movingTile=null;
    }

    private double CursorRemaining=>Math.Pow(1-Math.Min(1,_selectionElapsed/.15),3);

    private void ArrangePreview(bool compact)
    {
        var game=_selected is not null&&!_platforms;
        _layoutForGame=game;
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
    private void QueueGamePreview(DualScreenItem item)
    {
        _previewDelay.Stop();
        CancelUpperPreview();
        if(ScreenRole==HomeScreenRole.Preview)
        {
            _previewDelay.Interval=TimeSpan.FromMilliseconds(32);
            _previewDelay.Start();
            return;
        }
        _previewDelay.Interval=TimeSpan.FromMilliseconds(100);
        _previewBody.IsVisible=false;
        _quickPreview.Title=item.Title;
        _quickPreview.Platform=item.Console;
        _quickPreview.CoverPath=item.Cover;
        _quickPreview.LightPalette=LightPalette;
        if(!_art.Children.Contains(_quickPreview))_art.Children.Add(_quickPreview);
        // The front uses the same decode size as the grid. Build the full case only after the last input.
        _previewDelay.Start();
    }

    private void FinishGamePreview()
    {
        _previewDelay.Stop();
        CancelUpperPreview();
        if(!_platforms&&_selected is {} item&&_previewItem!=item&&File.Exists(item.Cover))
        {
            _upperPreviewPreparation=new CancellationTokenSource();
            _=PrepareUpperPreviewAsync(item,_upperPreviewPreparation);
        }
        else{CancelQuickPreview();RefreshPreview();}
    }

    private async Task PrepareUpperPreviewAsync(DualScreenItem item,CancellationTokenSource preparation)
    {
        var cancellation=preparation.Token;
        try
        {
            var faces=ResolvePreviewFaces(item);
            var useTextures=GameCaseControl.PreferGpuTextures;
            await Task.Run(()=>CoverBitmapCache.WarmPreview(faces,useTextures,cancellation),cancellation);
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(cancellation.IsCancellationRequested||_selected!=item
                    ||!IsEffectivelyVisible||!ReferenceEquals(_upperPreviewPreparation,preparation))return;
                _upperPreviewPreparation=null;
                var previousId=_previewItem?.Id;
                CancelQuickPreview();
                SetTitle(item);
                RefreshPreview();
                if(ScreenRole==HomeScreenRole.Preview)AnimateSelection(previousId,item.Id);
            });
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(ReferenceEquals(_upperPreviewPreparation,preparation))_upperPreviewPreparation=null;
                preparation.Dispose();
            });
        }
    }

    private string[] ResolvePreviewFaces(DualScreenItem item)
    {
        string Face(string path)=>string.IsNullOrWhiteSpace(path)?path:PreviewFacePath?.Invoke(path)??path;
        return [item.Cover,Face(item.Back),Face(item.Spine),Face(item.Right),Face(item.Top),Face(item.Bottom)];
    }

    private void CancelUpperPreview()
    {
        _upperPreviewPreparation?.Cancel();
        _upperPreviewPreparation=null;
    }

    private void CancelQuickPreview()
    {
        _previewDelay.Stop();
        CancelUpperPreview();
        _art.Children.Remove(_quickPreview);
        _previewBody.IsVisible=true;
    }

    private void ClearPreview()
    {
        CancelQuickPreview();
        var preview=_casePreview;_casePreview=null;_previewItem=null;_previewBody.Children.Clear();preview?.Dispose();
    }
    private void RefreshPreview()
    {
        if(_selected is not {} item||!_hero.IsVisible||!IsEffectivelyVisible||TopLevel.GetTopLevel(this) is null){ClearPreview();return;}
        if(_previewDelay.IsEnabled||_upperPreviewPreparation is not null
            ||(_previewItem==item&&_previewForPlatform==_platforms&&_previewBody.Children.Count>0))return;
        if(!_platforms&&File.Exists(item.Cover))
        {
            var preview=_casePreview??new GameCaseControl{MinWidth=0,MinHeight=0,RealisticShell=true,UseImportedModel=true,UseCompactTextureMesh=true,
                HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
            try
            {
                preview.CaseFormat=GameCaseControl.FormatForConsole(item.Console);
                var faces=ResolvePreviewFaces(item);
                preview.SetCover(faces[0],faces[1],faces[2],faces[3],faces[4],faces[5]);
                if(preview.HasFrontArtwork)
                {
                    preview.SetShelfPose(24,true,animate:false);
                    preview.IsInteractive=!ReduceMotion&&_playbackActive;
                    AutomationProperties.SetName(preview,item.Title);
                    if(_casePreview is null){_previewBody.Children.Clear();_casePreview=preview;_previewBody.Children.Add(preview);}
                    _previewItem=item;_previewForPlatform=false;
                    return;
                }
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){}
            if(_casePreview is null)preview.Dispose();
        }
        ClearPreview();_previewItem=item;_previewForPlatform=_platforms;
        _previewBody.Children.Add(new HomeArtworkControl{CoverPath=item.Cover,Platform=item.Console,Title=item.Title,IsPreview=true,LightPalette=LightPalette});
    }
    private void ClearSelection(){CloseAchievementDetails();_updateNotice.IsVisible=false;StopSelectionMotion();_selected=null;_achievements.IsVisible=ShowAchievements&&!_achievementPage;_displayedId=null;_selectedName.Text="Aucun résultat";ClearPreview();ArrangePreview(Bounds.Width<600);_title.SetGame(null,"Aucun jeu trouvé");_status.Text="Essaie une autre recherche ou ouvre le catalogue.";_launch.IsEnabled=_options.IsEnabled=_import.IsEnabled=false;_music.IsEnabled=true;StopMusic();UpdateAchievementSelection();}
    public void UpdateSelected(string status,string action,bool enabled,bool hasUpdate=false)
    {
        _status.Text=status;ToolTip.SetTip(_launch,status);
        _achievementPlay.Content=action;_achievementPlay.IsEnabled=enabled;
        if(_achievementPage)action="Voir les succès";
        _launch.Content=action;AutomationProperties.SetName(_launch,action);_launch.IsEnabled=_achievementPage||enabled;
        _updateNotice.IsVisible=hasUpdate;
        var options=hasUpdate?"Mise à jour disponible · X":"Options du jeu · X";
        ToolTip.SetTip(_options,options);AutomationProperties.SetName(_options,options);
    }
    public void ShowMessage(string? message)=>_message.Text=message;
    public bool DismissPopup()
    {
        if(CloseAchievementDetails())return true;
        if(IsPopupOpen){_music.Flyout?.Hide();FocusHomeControl(_music);return true;}
        if(!_search.IsKeyboardFocusWithin)return false;
        _search.IsVisible=false;ResizeHome();FocusHomeControl(_find);return true;
    }
    public void Move(int horizontal,int vertical)
    {
        if(_achievementDetails.IsVisible){MoveAchievementDetails(horizontal,vertical);return;}
        if(IsPopupOpen)
        {
            var buttons=new[]{_import,_mute,_soundToggle,_appearance}.Where(button=>button.IsEffectivelyVisible&&button.IsEnabled).ToArray();
            if(buttons.Length==0)return;
            var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();var choiceIndex=Array.FindIndex(buttons,button=>ReferenceEquals(button,focused));
            choiceIndex=choiceIndex<0?0:Math.Clamp(choiceIndex+Math.Sign(horizontal+vertical),0,buttons.Length-1);buttons[choiceIndex].Focus();return;
        }
        if(horizontal==0&&vertical==0){_grid.Focus();return;}
        var focusedControl=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var index=Math.Max(0,Array.FindIndex(_filtered,i=>i.Id==_selected?.Id));
        var row=Array.FindIndex(_focusRows,controls=>controls.Contains(focusedControl));
        if(row<0)row=GridFocusRow;
        if(row!=GridFocusRow&&horizontal!=0)
        {
            var visible=_focusRows[row].Where(CanFocus).ToArray();
            if(visible.Length==0)return;
            var column=Array.IndexOf(visible,focusedControl);
            var next=Math.Clamp(column+Math.Sign(horizontal),0,visible.Length-1);
            if(visible[next]!=focusedControl)FocusHomeControl(visible[next]);
            return;
        }
        if(row==GridFocusRow&&_filtered.Length>0)
        {
            var target=vertical==0?index+horizontal*_rows:index+Math.Sign(vertical);
            if(Math.Abs(horizontal)>1)target=Math.Clamp(target,0,_filtered.Length-1);
            var staysInGrid=vertical==0||(vertical<0?index%_rows>0:index%_rows<_rows-1&&target<_filtered.Length);
            if(staysInGrid)
            {
                if(target>=0&&target<_filtered.Length&&target!=index)SelectInGrid(_filtered[target],true);
                if(!_grid.IsKeyboardFocusWithin)_grid.Focus();
                return;
            }
        }
        if(vertical==0)return;
        for(var next=row+Math.Sign(vertical);next>=0&&next<_focusRows.Length;next+=Math.Sign(vertical))
        {
            if(next==GridFocusRow&&_filtered.Length==0)continue;
            var options=_focusRows[next].Where(CanFocus).ToArray();
            if(options.Length==0)continue;
            var anchor=row==GridFocusRow&&_selected is {} item?_grid.ContainerFromItem(item):focusedControl;
            var x=anchor?.TranslatePoint(new Point(anchor.Bounds.Width/2,0),this)?.X??Bounds.Width/2;
            var nearest=options.MinBy(control=>Math.Abs((control.TranslatePoint(new Point(control.Bounds.Width/2,0),this)?.X??x)-x))!;
            FocusHomeControl(nearest);
            return;
        }
    }


    private static bool CanFocus(Control control)=>control.IsEffectivelyVisible&&control.IsEffectivelyEnabled;
    private static void FocusHomeControl(Control control)
    {
        control.Focus(NavigationMethod.Directional);
        control.BringIntoView();
    }

    public void Activate()
    {
        if(_selected is not {} item||!_launch.IsEnabled)return;
        if(_achievementPage){Cue("open");OpenAchievementDetails();return;}
        Cue(_platforms?"open":"launch");LaunchRequested?.Invoke(item.Id);
    }
    public void ActivateFocused()
    {
        var focused=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if(focused is TextBox)return;
        if(focused is Button button)
        {
            if(!button.IsEffectivelyVisible||!button.IsEffectivelyEnabled)return;
            if(button==_music&&_music.Flyout is {} flyout)
            {
                flyout.ShowAt(_music);
                Move(0,1);
            }
            else button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            return;
        }
        if(_achievementDetails.IsVisible||IsPopupOpen||focused is Control other&&!other.GetVisualAncestors().Contains(this)&&!ReferenceEquals(other,this))return;
        Activate();
    }
    public bool HandleKey(Key key)
    {
        if(IsPopupOpen||_search.IsKeyboardFocusWithin)return false;
        switch(key){case Key.PageUp:StepColumn(-1);break;case Key.PageDown:StepColumn(1);break;case Key.Left:Move(-1,0);break;case Key.Right:Move(1,0);break;case Key.Up:Move(0,-1);break;case Key.Down:Move(0,1);break;default:return false;}return true;
    }
    public void SetPlaybackActive(bool active){_playbackActive=active;if(_casePreview is {} preview)preview.IsInteractive=active&&!ReduceMotion;if(active){ResumeSelectionMotion();UpdateMusic();}else{StopSelectionMotion();StopMusic();_playSound?.Invoke(null);}}
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
