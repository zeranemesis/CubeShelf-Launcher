using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Library;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

static class BigPictureControlsChecks
{
    public static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string message) { if(!good)throw new Exception(message); }
        Check(BigPictureGamepad.DirectionInput(0,1000,-3000)==0,"Stick drift escaped the dead zone.");
        Check(BigPictureGamepad.DirectionInput(0,-32768,0)==11 && BigPictureGamepad.DirectionInput(0,20000,0)==12
            && BigPictureGamepad.DirectionInput(0,0,32768)==13 && BigPictureGamepad.DirectionInput(0,0,-32768)==14,
            "Analog directions were merged with D-pad navigation.");
        Check(BigPictureGamepad.DirectionInput(4,-20000,0)==4,"D-pad input lost priority.");
        Check(BigPictureGamepad.MapNavigation(2,true)==12 && BigPictureGamepad.MapNavigation(13,true)==3
            && BigPictureGamepad.MapNavigation(5,true)==5,"Swapping navigation remapped action buttons.");
        window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var consoles=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
        var position=window.FindControl<TextBlock>("BigPicturePosition")!;
        var menu=window.FindControl<Border>("BigPictureMenuOverlay")!;
        var shelf=window.FindControl<GameShelfControl>("BigPictureShelf")!;
        Check(shelf.UseGpuTextures && shelf.UseCompositorScaling,"Big Picture did not enable the optimised renderer.");
        var selectedCase=shelf.Children.OfType<GameCaseControl>().Single(c=>c.IsInteractive);
        Check(selectedCase.UseGpuTextures,"Shelf cases did not inherit the renderer selection.");
        shelf.FlipSelected();
        var flipped=typeof(GameCaseControl).GetField("_flipped",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        Check((bool)flipped.GetValue(selectedCase)!,"Selected case did not flip.");
        var chosenIndex=int.Parse(position.Text!.Split('/')[0].Trim())-1;
        var beforeDecode=CoverBitmapCache.DecodeCount;
        shelf.SetSelection(chosenIndex,true);
        Check((bool)flipped.GetValue(selectedCase)! && CoverBitmapCache.DecodeCount==beforeDecode,
            "Refreshing the same selection reset inspection or decoded textures again.");
        var hiddenCard=window.FindControl<ItemsControl>("LibraryCards")!.Items.Cast<MainWindow.DesktopGameCard>().First(c=>c.Game.RuntimeOwnsSetup);
        var executable=hiddenCard.Game.Executable;var label=hiddenCard.PlayLabel;
        var placeholder=Path.Combine(new PlatformPaths().DataDirectory,"not-an-executable.txt");File.WriteAllText(placeholder,"test fixture, never executed");
        var refresh=typeof(MainWindow).GetMethod("RefreshQuickPlayUi",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        hiddenCard.Game.Executable=placeholder;refresh.Invoke(window,null);
        Check(hiddenCard.PlayLabel==label,"Background status refreshed the hidden desktop cards.");
        window.FindControl<Button>("BigPictureExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(hiddenCard.PlayLabel!=label,"Desktop status did not catch up when leaving Big Picture.");
        hiddenCard.Game.Executable=executable;refresh.Invoke(window,null);
        window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Input(int value)=>window.HandleBigPictureControllerInput(value);
        var console=consoles.SelectedIndex;Input(13);
        Check(consoles.SelectedIndex==(console+1)%3,"Stick up did not change platform.");
        var game=position.Text;Input(12);Check(game!=position.Text,"Stick right did not change game.");
        game=position.Text;Input(4);Check(menu.IsVisible && game==position.Text,"D-pad changed game instead of opening menus.");
        Input(15);Check(!menu.IsVisible,"Start did not close the menu.");
        Input(15);Input(4);Input(4);Input(4);Input(5);Input(5);
        Check(new UserPreferencesStore(new PlatformPaths()).Load().SwapBigPictureNavigation,"Control inversion did not persist.");
        Save("controller-menu.png");
        Input(6);Input(6);game=position.Text;Input(2);
        Check(!menu.IsVisible && game!=position.Text,"Swapped D-pad did not browse games.");
        console=consoles.SelectedIndex;Input(13);
        Check(menu.IsVisible && consoles.SelectedIndex==console,"Swapped stick still changed platform.");
        Save("main-menu.png");
        Input(15);Input(15);Input(14);Input(14);Input(14);Input(5);Input(5);
        Check(!new UserPreferencesStore(new PlatformPaths()).Load().SwapBigPictureNavigation,"Could not restore original navigation.");
        Input(6);Input(6);
        var launch=window.FindControl<GameLaunchTransition>("LaunchTransition")!;
        var currentGame=typeof(MainWindow).GetProperty("BigPictureGame",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        using var sources=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs/covers.json")));
        string PathFor(string? file)=>string.IsNullOrEmpty(file)?"":Path.Combine(AppContext.BaseDirectory,file.Replace('\\',Path.DirectorySeparatorChar));
        foreach(var index in new[]{0,1,2})
        {
            consoles.SelectedIndex=index;
            window.FindControl<ShelfTransitionControl>("ShelfTransition")!.Advance(1);
            Save($"catalogue-{index}.png");
            var selected=(GameCatalogEntry)currentGame.GetValue(window)!;
            var cover=selected.Covers.First();
            var art=sources.RootElement.EnumerateArray().FirstOrDefault(e=>e.GetProperty("Console").GetString()==selected.ShelfConsole && e.GetProperty("Title").GetString()==selected.Title);
            var media=art.ValueKind==JsonValueKind.Object && art.TryGetProperty("Media",out var source)?PathFor(source.GetString()):"";
            launch.Start(selected.Title,PathFor(cover.Front),PathFor(cover.Back),PathFor(cover.Spine),"Lancement…",selected.Id,
                selected.ShelfConsole,openCase:true,media:media);
            var pending=launch.WaitForOpeningAsync(CancellationToken.None);
            Check(!pending.IsCompleted,"The game would launch before the case opens.");
            launch.Advance(.85);Save($"opening-{index}-mid.png");
            launch.Advance(.5);Save($"opening-{index}-spinning.png");
            Check(!pending.IsCompleted,"The game would launch before the disc spins.");
            var animated=launch.GetLogicalDescendants().OfType<GameCaseControl>().Single();
            if(index!=2)
            {
                Check(animated.MediaVerticalOffset<0,"The disc moved up instead of down.");
                var first=animated.MediaRotationDegrees;launch.Advance(.05);
                var second=animated.MediaRotationDegrees;launch.Advance(.05);
                Check(second>first && animated.MediaRotationDegrees-second>second-first,"Disc rotation did not accelerate.");
            }
            launch.Advance(GameLaunchTransition.OpeningDuration);Check(pending.IsCompletedSuccessfully && pending.Result,"Opening animation never completed.");
            if(index!=2)
            {
                var rotation=animated.MediaRotationDegrees;
                Check(launch.Advance(.05),"Disc animation stopped while the launch screen remained visible.");
                Check(animated.MediaRotationDegrees!=rotation,"The disc stopped spinning while the game was still starting.");
            }
            else Check(!launch.Advance(.05),"The static cartridge kept scheduling unnecessary animation frames.");
            Save($"opening-{index}-open.png");launch.Stop();
        }
        launch.Start("Cancel test","","","","",openCase:true);
        var cancelled=launch.WaitForOpeningAsync(CancellationToken.None);Input(6);
        Check(cancelled.IsCompletedSuccessfully && !cancelled.Result && !launch.IsVisible,"B did not cancel opening before launch.");
        launch.Start("Token test","","","","",openCase:true);
        using(var cancellation=new CancellationTokenSource())
        {
            var pending=launch.WaitForOpeningAsync(cancellation.Token);cancellation.Cancel();
            Check(pending.IsCanceled,"A cancelled play intent could still launch after opening.");
        }
        launch.Stop();
        window.FindControl<Button>("BigPictureExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        console=consoles.SelectedIndex;Input(13);Input(15);
        Check(!menu.IsVisible && consoles.SelectedIndex==console,"Big Picture controls changed the classic interface.");
        Console.WriteLine("OK  stick / D-pad separation, dead zone, platform and game navigation, menu and persisted inversion.");
        Console.WriteLine("OK  three opening animations complete before launch; B and cancellation prevent launch.");
        Console.WriteLine("OK  discs descend and accelerate; repeated selection preserves inspection; hidden card updates resume on desktop.");
        void Save(string file)
        {
            if(snapshots is null)return;
            Directory.CreateDirectory(snapshots);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,file));
        }
    }
}
