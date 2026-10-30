using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

var profile = Path.Combine(Path.GetTempPath(), "cubeshelf-selector-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CUBESHELF_DATA_DIR",profile);
new CubeShelf.Core.Platform.UserPreferencesStore(new CubeShelf.Core.Platform.PlatformPaths()).Save(
    new CubeShelf.Core.Platform.UserPreferences(FirstRunCompleted:true,MeshDiscoveryNoticeSeen:true,
        MeshEnabled:false,MeshPublicDiscovery:false,CheckGamesOnStartup:false));
var snapshotDirectory = Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR");
var builder = AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();
builder.SetupWithoutStarting();
if(args.Contains("--render-benchmark")) { RenderBenchmark.Run();return; }
var window = new MainWindow();
try
{
    var selector=window.FindControl<ComboBox>("ConsoleShelfPicker")!;
    var englishHome=new PlatformHomeControl { IsVisible=false };
    englishHome.Open(new[]{(CubeShelf.Core.Library.ShelfProduct.Resolve("gamecube"),2)},"gamecube",desktop:true,grid:true,english:true);
    if(!englishHome.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="2 games") || !englishHome.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="Sort by"))
        throw new Exception("English console home must not retain French action labels.");
    englishHome.IsVisible=false;
    var fullscreen=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
    var cards=window.FindControl<ItemsControl>("LibraryCards")!;
    var cubeCount=cards.Items.Count;
    AssertBrand("gamecube");
    if(selector.ItemCount < 25) throw new Exception("Expanded platforms are missing from the unified selector.");
    foreach(var picker in new[]{selector,fullscreen})
    foreach(var item in picker.Items.Cast<ComboBoxItem>())
    {
        var id=(string)item.Tag!;
        if(item.Content is not StackPanel row || row.Children[0] is not ConsoleBrandMark mark || mark.Platform!=id
            || row.Children[1] is not TextBlock { Text: var consoleName } || consoleName!=CubeShelf.Core.Library.ShelfProduct.Resolve(id).Console)
            throw new Exception("Each console selector entry must show its own logo before its name.");
        if(id is not "arcade" and not "n64" && !Avalonia.Platform.AssetLoader.Exists(new Uri($"avares://CubeShelf/Assets/Brand/Consoles/{id}.png"))
            && !Avalonia.Platform.AssetLoader.Exists(new Uri($"avares://CubeShelf/Assets/Brand/Consoles/{id}.gif")))
            throw new Exception("Missing console logo artwork: "+id);
    }
    if(cubeCount!=21)throw new Exception($"GameCube catalogue should contain 21 unique games, got {cubeCount}.");
    AssertCovers();
    selector.SelectedIndex=1;
    AssertBrand("wii");
    if(cards.Items.Count!=13 || fullscreen.SelectedIndex!=1 || !window.Title!.Contains("Wii"))throw new Exception("Wii selector did not update the existing library.");
    AssertCovers();
    selector.SelectedIndex=2;
    AssertBrand("switch");
    if(cards.Items.Count!=16 || fullscreen.SelectedIndex!=2 || !window.Title!.Contains("Switch"))throw new Exception("Switch selector did not update the existing library.");
    if(!window.FindControl<TextBlock>("LibrarySectionTitle")!.Text!.Contains("Switch"))throw new Exception("Library heading does not match selected console.");
    var multi = cards.Items.Cast<MainWindow.DesktopGameCard>().First(card => card.HasVersionChoice);
    var alternative = multi.Versions.First(choice => choice.GameId != multi.Game.Id);
    var versionPicker = new ComboBox { Tag = multi, ItemsSource = multi.Versions };
    versionPicker.SelectionChanged += (sender, args) => typeof(MainWindow).GetMethod("CardPortVersionChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [sender, args]);
    versionPicker.SelectedItem = alternative;
    if(cards.Items.Count!=16 || !cards.Items.Cast<MainWindow.DesktopGameCard>().Any(card => card.Game.Id == alternative.GameId))
        throw new Exception("Choosing another port duplicated the game or did not replace its version.");
    if(!File.ReadAllText(Path.Combine(profile,"selected-port-versions.json")).Contains(alternative.GameId))
        throw new Exception("Selected port version was not persisted.");
    AssertCovers();
    selector.SelectedIndex=3;
    AssertBrand("n64");
    if(cards.Items.Count!=29 || fullscreen.SelectedIndex!=3)throw new Exception("Nintendo 64 catalogue or selectors incorrect.");
    AssertCovers();
    var japanese=cards.Items.Cast<MainWindow.DesktopGameCard>().Single(card=>card.Title=="Virtual Pro Wrestling 2").Game.Covers.First();
    var landscapeCard=cards.Items.Cast<MainWindow.DesktopGameCard>().Single(card=>card.Title=="Banjo-Kazooie");
    var portraitCard=cards.Items.Cast<MainWindow.DesktopGameCard>().Single(card=>card.Title=="Virtual Pro Wrestling 2");
    var thumbnail=new GameCoverControl { Width=234,Format=GameCaseFormat.Nintendo64,Cover=landscapeCard.Cover };
    thumbnail.Measure(new Size(234,double.PositiveInfinity));
    if(thumbnail.DesiredSize.Height>=234)throw new Exception("Landscape Nintendo 64 cover retained a portrait frame.");
    var aspect=(thumbnail.DesiredSize.Height-8)/(thumbnail.DesiredSize.Width-6);
    if(Math.Abs(aspect-(double)landscapeCard.Cover!.PixelSize.Height/landscapeCard.Cover.PixelSize.Width)>.01)
        throw new Exception("Nintendo 64 thumbnail added letterboxing or changed the printed artwork's proportions.");
    thumbnail.Cover=portraitCard.Cover;
    thumbnail.Measure(new Size(234,double.PositiveInfinity));
    if(thumbnail.DesiredSize.Height<=234)throw new Exception("Japanese portrait cover did not resize its card.");
    var complete=cards.Items.Cast<MainWindow.DesktopGameCard>().Single(card=>card.Title=="Paper Mario").Game.Covers.First();
    using(var box=new GameCaseControl { CaseFormat=GameCaseFormat.Nintendo64 })
    {
        string Art(string path)=>Path.Combine(AppContext.BaseDirectory,path);
        box.SetCover(Art(complete.Front),Art(complete.Back),Art(complete.Spine),Art(complete.Right),Art(complete.Top),Art(complete.Bottom));
        if(box.Nintendo64OpensAtTop)throw new Exception("Landscape Nintendo 64 cartons must open at the short side.");
        foreach(var face in new[]{"_front","_back","_spine","_right","_top","_bottom"})
            if(typeof(GameCaseControl).GetField(face,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(box) is null)
                throw new Exception("Nintendo 64 face not decoded: "+face);
        if(box.SetCover(Art(complete.Front),Art(complete.Back),Art(complete.Spine),Art(complete.Right),Art(complete.Top),Art(complete.Bottom)))
            throw new Exception("Unchanged six-face artwork was decoded again.");
    }
    using(var box=new GameCaseControl { CaseFormat=GameCaseFormat.Nintendo64 })
    {
        box.SetCover(Path.Combine(AppContext.BaseDirectory,japanese.Front),"","");
        if(box.CaseAspectRatio>=1)throw new Exception("Japanese Nintendo 64 packaging was stretched into a landscape box.");
        if(!box.Nintendo64OpensAtTop)throw new Exception("Portrait Nintendo 64 cartons must open at the top.");
    }
    if(File.ReadAllText(Path.Combine(profile,"selected-console.txt"))!="n64")throw new Exception("Nintendo 64 selection was not persisted.");
    fullscreen.SelectedIndex=0;
    if(cards.Items.Count!=cubeCount || selector.SelectedIndex!=0)throw new Exception("Big Picture selector did not restore the GameCube library.");
    if(File.ReadAllText(Path.Combine(profile,"selected-console.txt"))!="gamecube")throw new Exception("Last console was not persisted.");
    window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    fullscreen.SelectedIndex=1;
    AssertBrand("wii");
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=13 || selector.SelectedIndex!=1)
        throw new Exception("Changing console left Big Picture or used the wrong catalogue.");
    fullscreen.SelectedIndex=2;
    AssertBrand("switch");
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=16)
        throw new Exception("Switch library did not remain in the existing Big Picture interface.");
    Console.WriteLine($"OK  one window: GameCube ({cubeCount}) → Wii (13) → Switch (16) → Nintendo 64 (29) → GameCube ({cubeCount})");
    Console.WriteLine("OK  alternate port replaces the same card and persists; all Switch games have covers.");
    Console.WriteLine("OK  desktop and Big Picture selectors stay synchronised; last console persists.");
    Console.WriteLine("OK  switching Wii/Switch keeps Big Picture active in the same window.");
    foreach (var index in Enumerable.Range(4, selector.ItemCount - 4))
    {
        selector.SelectedIndex = index;
        var id = ((ComboBoxItem)selector.SelectedItem!).Tag!.ToString()!;
        AssertBrand(id);
        if(window.FindControl<TextBlock>("GameTitle")!.Text!=window.FindControl<TextBlock>("SelectedTitleText")!.Text)
            throw new Exception("Console switching retained the previous game's detail header: "+id);
        if(id=="3ds" && cards.Items.Cast<MainWindow.DesktopGameCard>().Any(card=>!card.PlayLabel.Contains("config",StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Ports without a verified compatible package must offer configuration rather than automatic installation.");
        if (cards.Items.Count == 0 || fullscreen.SelectedIndex != index ||
            cards.Items.Cast<MainWindow.DesktopGameCard>().Select(card => card.Title).Distinct().Count() != cards.Items.Count)
            throw new Exception("Platform selection is empty, duplicated or out of sync: " + id);
        if (cards.Items.Cast<MainWindow.DesktopGameCard>().Any(card => card.CaseFormat != GameCaseFormat.OriginalBox))
            throw new Exception("New console inherited a GameCube case: " + id);
    }
    fullscreen.SelectedIndex = 2;
    Console.WriteLine("OK  all expanded catalogues switch in one window, with matching branding and no duplicate cards.");
    typeof(MainWindow).GetMethod("ExitBigPicture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [window,new RoutedEventArgs()]);
    window.Width=1440;window.Height=1000;window.Show();
    GameHelpChecks.Run(window);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var consoleHome=window.FindControl<PlatformHomeControl>("PlatformHome")!;
    if (!consoleHome.IsVisible || !consoleHome.IsGrid) throw new Exception("Desktop must start with console image tiles.");
    var sidebar=window.FindControl<Border>("DesktopSidebar")!;
    if(!sidebar.IsEffectivelyVisible || Grid.GetColumn(consoleHome)!=1 || Grid.GetColumnSpan(consoleHome)!=1 ||
        consoleHome.TranslatePoint(default,window)!.Value.X < sidebar.TranslatePoint(default,window)!.Value.X+sidebar.Bounds.Width)
        throw new Exception("Desktop console home must leave the sidebar visible and unobstructed.");
    var libraryNav=window.FindControl<Button>("NavLibraryButton")!;
    var libraryNavPoint=libraryNav.TranslatePoint(new Point(libraryNav.Bounds.Width/2,libraryNav.Bounds.Height/2),window)!.Value;
    if(window.InputHitTest(libraryNavPoint) is not Visual navHit || (navHit!=libraryNav&&!libraryNav.IsVisualAncestorOf(navHit)))
        throw new Exception("The console home blocks pointer access to sidebar navigation.");
    var consoleTiles=consoleHome.GetLogicalDescendants().OfType<Button>().Where(b=>b.Tag is string).ToArray();
    if(consoleTiles.Length!=selector.ItemCount || consoleHome.GetLogicalDescendants().OfType<Image>().Count(image=>image.Source is not null)!=consoleTiles.Length)
        throw new Exception("Each desktop console must have one illustrated tile.");
    if(snapshotDirectory is not null)
    {
        Directory.CreateDirectory(snapshotDirectory);AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        using var desktopConsoles=window.CaptureRenderedFrame();desktopConsoles!.Save(Path.Combine(snapshotDirectory,"desktop-consoles.png"));
    }
    var previousTheme=window.RequestedThemeVariant;
    foreach(var theme in new[]{Avalonia.Styling.ThemeVariant.Light,Avalonia.Styling.ThemeVariant.Dark})
    {
        window.RequestedThemeVariant=theme;window.Width=960;window.Height=700;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        var sidebarScroll=window.FindControl<ScrollViewer>("SidebarScroll")!;
        if(sidebarScroll.Extent.Height>sidebarScroll.Viewport.Height)
        {
            sidebarScroll.Offset=new Vector(0,sidebarScroll.Extent.Height);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if(sidebarScroll.Offset.Y<=0)throw new Exception("Small windows must allow reaching the bottom of the sidebar.");
            sidebarScroll.Offset=default;
        }
        var selectorLabel=(TextBlock)((StackPanel)((ComboBoxItem)selector.SelectedItem!).Content!).Children[1];
        var expected=theme==Avalonia.Styling.ThemeVariant.Light?"#151927":"#F8FAFF";
        if(selectorLabel.Foreground is not Avalonia.Media.ISolidColorBrush brush || brush.Color!=Avalonia.Media.Color.Parse(expected))throw new Exception("Console selector text must follow the theme for readable contrast.");
        foreach(var card in consoleHome.GetLogicalDescendants().OfType<Button>().Where(b=>b.Tag is string))
        {
            var lastLine=((StackPanel)card.Content!).Children.Last();
            if(lastLine.TranslatePoint(default,card)!.Value.Y+lastLine.Bounds.Height>card.Bounds.Height)throw new Exception("Console card information is cut off for a long name.");
        }
        if(snapshotDirectory is not null)
        {
            Thread.Sleep(180);Avalonia.Threading.Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var compactConsoles=window.CaptureRenderedFrame();compactConsoles!.Save(Path.Combine(snapshotDirectory,$"desktop-consoles-960-{theme}.png"));
        }
    }
    window.RequestedThemeVariant=previousTheme;window.Width=1440;window.Height=1000;
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    foreach(var (button,page) in new[]{("NavLibraryButton","LibraryView"),("NavDownloadsButton","DownloadsView"),("NavSettingsButton","SettingsView")})
    {
        window.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(consoleHome.IsVisible||!window.FindControl<Control>(page)!.IsVisible)throw new Exception("Sidebar navigation did not leave console home: "+button);
        window.FindControl<Button>("NavConsolesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(!consoleHome.IsVisible||!window.FindControl<Button>("NavConsolesButton")!.Classes.Contains("selected"))throw new Exception("Sidebar must reopen and highlight console home.");
    }
    var targetTile=consoleHome.GetLogicalDescendants().OfType<Button>().First(b=>b.Tag is "wiiu");
    targetTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(consoleHome.IsVisible || !window.FindControl<Control>("LibraryView")!.IsVisible || ((ComboBoxItem)selector.SelectedItem!).Tag!.ToString()!="wiiu")throw new Exception("Clicking a console tile must open its catalogue.");
    if(snapshotDirectory is not null)
    {
        selector.IsDropDownOpen=true;Avalonia.Threading.Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        using var logoSelector=window.CaptureRenderedFrame();logoSelector!.Save(Path.Combine(snapshotDirectory,"console-logo-selector.png"));
        selector.IsDropDownOpen=false;
    }
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
    var transition=window.FindControl<ShelfTransitionControl>("ShelfTransition")!;
    var host=window.FindControl<Grid>("LibraryView")!;
    selector.SelectedIndex=1;
    if(!transition.IsRunning || cards.Items.Count!=13)throw new Exception("Console transition did not start with the new catalogue ready.");
    transition.Advance(ShelfTransitionControl.Duration/2);
    if(host.Opacity<=0 || host.Opacity>=1)throw new Exception("Transition did not reveal the incoming library progressively.");
    if(snapshotDirectory is not null)
    {
        Directory.CreateDirectory(snapshotDirectory);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var midpoint=window.CaptureRenderedFrame();
        midpoint!.Save(Path.Combine(snapshotDirectory,"transition-midpoint.png"));
    }
    selector.SelectedIndex=0;selector.SelectedIndex=2;
    transition.Advance(1);
    if(transition.IsRunning || transition.IsVisible || host.Opacity!=1 || host.RenderTransform is not null || cards.Items.Count!=16)
        throw new Exception("Rapid switching left stale content, opacity or transforms.");
    var decodes=CoverBitmapCache.DecodeCount;
    var search=window.FindControl<TextBox>("LibrarySearchBox")!;
    search.Text="Mario";search.Text="";
    if(CoverBitmapCache.DecodeCount!=decodes)throw new Exception("A warm search decoded cover images again.");
    var reduce=window.FindControl<CheckBox>("ReduceShelfMotionBox")!;
    reduce.IsChecked=true;selector.SelectedIndex=1;
    if(transition.IsRunning || host.Opacity!=1 || cards.Items.Count!=13)throw new Exception("Reduced motion did not switch immediately.");
    reduce.IsChecked=false;
    selector.SelectedIndex=2;
    window.Hide();
    if(transition.IsRunning || host.Opacity!=1)throw new Exception("Hiding the window did not release the transition.");
    window.Show();
    window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var stateChanges=0;
    void CountStateChanges(object? sender,AvaloniaPropertyChangedEventArgs args) { if(args.Property==Window.WindowStateProperty)stateChanges++; }
    window.PropertyChanged+=CountStateChanges;
    fullscreen.SelectedIndex=1;
    transition.Advance(1);
    window.PropertyChanged-=CountStateChanges;
    if(stateChanges!=0 || !window.FindControl<Control>("BigPictureView")!.IsVisible)
        throw new Exception("Changing console unnecessarily left fullscreen.");
    typeof(MainWindow).GetMethod("ExitBigPicture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [window,new RoutedEventArgs()]);
    selector.SelectedIndex=2;transition.Advance(1);
    if(cards.Items.Cast<MainWindow.DesktopGameCard>().Any(card=>card.Cover!.PixelSize.Height>512))
        throw new Exception("Library cards retained oversized images.");
    Console.WriteLine("OK  transition midpoint, rapid switching, reduced motion and hiding restore a clean view.");
    Console.WriteLine("OK  changing console keeps fullscreen; card thumbnails are bounded to 512 pixels.");
    Console.WriteLine($"OK  warm search: 0 additional cover decodes; estimated cover cache {CoverBitmapCache.EstimatedBytes/1048576d:0.0} MiB.");
    if(snapshotDirectory is not null)
    {
        Directory.CreateDirectory(snapshotDirectory);
        foreach(var console in new[]{0,1,2,3})
        {
            selector.SelectedIndex=console;
            transition.Advance(1);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame=window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(snapshotDirectory,$"library-{console}.png"));
        }
        foreach(var console in new[]{0,1,2,3})
        {
            selector.SelectedIndex=console;
            transition.Advance(1);
            var game=cards.Items.Cast<MainWindow.DesktopGameCard>().First().Game;
            typeof(MainWindow).GetMethod("SelectCatalogGame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window,[game,true]);
            var renderedCase=window.FindControl<CubeShelf.Desktop.Controls.GameCaseControl>("GameCase")!;
            if(renderedCase.CaseFormat!=CubeShelf.Desktop.Controls.GameCaseControl.FormatForConsole(game.ShelfConsole))throw new Exception("Wrong case format.");
            if(console==3)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                var origin=renderedCase.TranslatePoint(new Point(renderedCase.Bounds.Width/2,renderedCase.Bounds.Height/2),window)!.Value;
                window.MouseMove(origin);window.MouseDown(origin,MouseButton.Left,Avalonia.Input.RawInputModifiers.LeftMouseButton);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var destination=origin+new Vector(-110,100);
                window.MouseMove(destination,Avalonia.Input.RawInputModifiers.LeftMouseButton);window.MouseUp(destination,MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var pitch=(double)typeof(GameCaseControl).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(renderedCase)!;
                if(pitch<40)throw new Exception($"Dragging did not expose Nintendo 64 box edges: pitch {pitch}, origin {origin}, target {window.InputHitTest(origin)?.GetType().Name}, visible {renderedCase.IsEffectivelyVisible}.");
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame=window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(snapshotDirectory,$"case-{console}.png"));
        }
    }
    BigPictureControlsChecks.Run(window,snapshotDirectory);
    if (snapshotDirectory is not null)
    {
        foreach (var console in new[] { "nes", "gba", "ps1", "xbox360" })
        {
            selector.SelectedIndex = selector.Items.OfType<ComboBoxItem>().Select((item,index)=>(item,index)).Single(pair => pair.item.Tag?.ToString() == console).index;
            window.FindControl<ShelfTransitionControl>("ShelfTransition")!.Stop();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var capture = window.CaptureRenderedFrame(); capture!.Save(Path.Combine(snapshotDirectory, "platform-" + console + ".png"));
        }
    }
    void AssertBrand(string console)
    {
        if(window.FindControl<ConsoleBrandMark>("SidebarConsoleLogo")!.Platform!=console
            || window.FindControl<ConsoleBrandMark>("BigPictureConsoleLogo")!.Platform!=console
            || !window.Title!.StartsWith("DecompShelf · ")
            || window.FindControl<TextBlock>("SidebarAppName")!.Text!="DecompShelf"
            || window.FindControl<TextBlock>("BigPictureAppName")!.Text!="DecompShelf")
            throw new Exception("Application branding or catalogue logo did not follow the selected console.");
        if(new CubeShelf.Core.Platform.PlatformPaths().DataDirectory!=profile)
            throw new Exception("Renaming the application changed its existing profile location.");
    }
    void AssertCovers()
    {
        foreach(var card in cards.Items.Cast<MainWindow.DesktopGameCard>())
            if(card.Cover is null) throw new Exception("Missing game cover: " + card.Title);
    }
}
finally
{
    window.Close();
    Environment.SetEnvironmentVariable("CUBESHELF_DATA_DIR",null);
    // Keep temporary profile files until the process exits: background disposal may still finish writes.
}
