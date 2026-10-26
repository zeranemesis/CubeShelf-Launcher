using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
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
    var fullscreen=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
    var cards=window.FindControl<ItemsControl>("LibraryCards")!;
    var cubeCount=cards.Items.Count;
    AssertBrand("gamecube");
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
    Console.WriteLine($"OK  one window: GameCube ({cubeCount}) → Wii (13) → Switch (16) → GameCube ({cubeCount})");
    Console.WriteLine("OK  alternate port replaces the same card and persists; all Switch games have covers.");
    Console.WriteLine("OK  desktop and Big Picture selectors stay synchronised; last console persists.");
    Console.WriteLine("OK  switching Wii/Switch keeps Big Picture active in the same window.");
    typeof(MainWindow).GetMethod("ExitBigPicture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [window,new RoutedEventArgs()]);
    window.Width=1440;window.Height=1000;window.Show();
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
        foreach(var console in new[]{0,1,2})
        {
            selector.SelectedIndex=console;
            transition.Advance(1);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame=window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(snapshotDirectory,$"library-{console}.png"));
        }
        foreach(var console in new[]{0,1,2})
        {
            selector.SelectedIndex=console;
            transition.Advance(1);
            var game=cards.Items.Cast<MainWindow.DesktopGameCard>().First().Game;
            typeof(MainWindow).GetMethod("SelectCatalogGame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window,[game,true]);
            var renderedCase=window.FindControl<CubeShelf.Desktop.Controls.GameCaseControl>("GameCase")!;
            if(renderedCase.CaseFormat!=CubeShelf.Desktop.Controls.GameCaseControl.FormatForConsole(game.ShelfConsole))throw new Exception("Wrong case format.");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame=window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(snapshotDirectory,$"case-{console}.png"));
        }
    }
    BigPictureControlsChecks.Run(window,snapshotDirectory);
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
