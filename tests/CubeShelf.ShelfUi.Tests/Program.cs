using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CubeShelf.Desktop;

var profile = Path.Combine(Path.GetTempPath(), "cubeshelf-selector-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CUBESHELF_DATA_DIR",profile);
var snapshotDirectory = Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR");
var builder = AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = snapshotDirectory is null });
if(snapshotDirectory is not null) builder.UseSkia();
builder.SetupWithoutStarting();
var window = new MainWindow();
try
{
    var selector=window.FindControl<ComboBox>("ConsoleShelfPicker")!;
    var fullscreen=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
    var cards=window.FindControl<ItemsControl>("LibraryCards")!;
    var cubeCount=cards.Items.Count;
    if(cubeCount!=21)throw new Exception($"GameCube catalogue should contain 21 unique games, got {cubeCount}.");
    AssertCovers();
    selector.SelectedIndex=1;
    if(cards.Items.Count!=13 || fullscreen.SelectedIndex!=1 || !window.Title!.Contains("Wii"))throw new Exception("Wii selector did not update the existing library.");
    AssertCovers();
    selector.SelectedIndex=2;
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
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=13 || selector.SelectedIndex!=1)
        throw new Exception("Changing console left Big Picture or used the wrong catalogue.");
    fullscreen.SelectedIndex=2;
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=16)
        throw new Exception("Switch library did not remain in the existing Big Picture interface.");
    Console.WriteLine($"OK  one window: GameCube ({cubeCount}) → Wii (13) → Switch (16) → GameCube ({cubeCount})");
    Console.WriteLine("OK  alternate port replaces the same card and persists; all Switch games have covers.");
    Console.WriteLine("OK  desktop and Big Picture selectors stay synchronised; last console persists.");
    Console.WriteLine("OK  switching Wii/Switch keeps Big Picture active in the same window.");
    if(snapshotDirectory is not null)
    {
        Directory.CreateDirectory(snapshotDirectory);
        typeof(MainWindow).GetMethod("ExitBigPicture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [window,new RoutedEventArgs()]);
        window.Width=1440;window.Height=1000;window.Show();
        foreach(var console in new[]{0,1,2})
        {
            selector.SelectedIndex=console;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame=window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(snapshotDirectory,$"library-{console}.png"));
        }
        foreach(var console in new[]{0,1,2})
        {
            selector.SelectedIndex=console;
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
