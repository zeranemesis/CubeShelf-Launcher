using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CubeShelf.Desktop;

var profile = Path.Combine(Path.GetTempPath(), "cubeshelf-selector-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CUBESHELF_DATA_DIR",profile);
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var window = new MainWindow();
try
{
    var selector=window.FindControl<ComboBox>("ConsoleShelfPicker")!;
    var fullscreen=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
    var cards=window.FindControl<ItemsControl>("LibraryCards")!;
    var cubeCount=cards.Items.Count;
    if(cubeCount<23)throw new Exception("GameCube catalogue is incomplete.");
    selector.SelectedIndex=1;
    if(cards.Items.Count!=13 || fullscreen.SelectedIndex!=1 || !window.Title!.Contains("Wii"))throw new Exception("Wii selector did not update the existing library.");
    selector.SelectedIndex=2;
    if(cards.Items.Count!=20 || fullscreen.SelectedIndex!=2 || !window.Title!.Contains("Switch"))throw new Exception("Switch selector did not update the existing library.");
    fullscreen.SelectedIndex=0;
    if(cards.Items.Count!=cubeCount || selector.SelectedIndex!=0)throw new Exception("Big Picture selector did not restore the GameCube library.");
    if(File.ReadAllText(Path.Combine(profile,"selected-console.txt"))!="gamecube")throw new Exception("Last console was not persisted.");
    window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    fullscreen.SelectedIndex=1;
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=13 || selector.SelectedIndex!=1)
        throw new Exception("Changing console left Big Picture or used the wrong catalogue.");
    fullscreen.SelectedIndex=2;
    if(!window.FindControl<Control>("BigPictureView")!.IsVisible || cards.Items.Count!=20)
        throw new Exception("Switch library did not remain in the existing Big Picture interface.");
    Console.WriteLine($"OK  one window: GameCube ({cubeCount}) → Wii (13) → Switch (20) → GameCube ({cubeCount})");
    Console.WriteLine("OK  desktop and Big Picture selectors stay synchronised; last console persists.");
    Console.WriteLine("OK  switching Wii/Switch keeps Big Picture active in the same window.");
}
finally
{
    window.Close();
    Environment.SetEnvironmentVariable("CUBESHELF_DATA_DIR",null);
    // Keep temporary profile files until the process exits: background disposal may still finish writes.
}
