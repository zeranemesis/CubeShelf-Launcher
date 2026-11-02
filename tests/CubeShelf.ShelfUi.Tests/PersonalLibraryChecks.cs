using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

static class PersonalLibraryChecks
{
    public static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string message) { if(!good)throw new Exception(message); }
        void Click(string name)=>window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var catalog=(List<GameCatalogEntry>)typeof(MainWindow).GetField("_catalogGames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var paths=new PlatformPaths();
        var personal=window.FindControl<Button>("NavMyGamesButton")!;
        var library=window.FindControl<ItemsControl>("LibraryCards")!;
        MainWindow.DesktopGameCard[] Cards()=>library.Items.Cast<MainWindow.DesktopGameCard>().ToArray();
        Click("NavMyGamesButton");
        Check(library.Items.Count==0 && window.FindControl<TextBlock>("EmptyLibraryText")!.IsVisible,
            "A clean profile must show an empty personal library, not hundreds of catalog games.");
        Check(personal.Classes.Contains("selected") && !window.FindControl<Button>("NavLibraryButton")!.Classes.Contains("selected"),
            "Personal library and catalog need distinct navigation states.");
        Check(!window.FindControl<ComboBox>("ConsoleShelfPicker")!.IsVisible && window.Title!.Contains("Mes jeux"),
            "The all-console library must not appear scoped to the last console.");
        var ports=catalog.Where(game=>game.ShelfConsole=="n64" && game.RuntimeOwnsSetup)
            .GroupBy(NativePortCatalog.GameKey).First(group=>group.Count()>1).ToArray();
        var owned=ports[^1];
        var configured=catalog.First(game=>game.ShelfConsole=="ps1" && game.NativePortId.Length>0);
        var discGame=catalog.First(game=>game.ShelfConsole=="gamecube" && !game.RuntimeOwnsSetup && !GameVariants.IsVariant(game));
        var unavailable=catalog.First(game=>game.ShelfConsole=="switch" && game.NativePortId.Length>0);
        var previousPaths=catalog.ToDictionary(game=>game.Id,game=>(game.Executable,game.DiscImage,game.IsFavorite));
        var fixture=Path.Combine(paths.DataDirectory,"personal-library-fixture.bin");File.WriteAllText(fixture,"never executed");
        var profile=Path.Combine(paths.ConfigurationDirectory,"desktop-profile.json");
        var nativeFile=Path.Combine(paths.ConfigurationDirectory,"native-ports.json");
        var profileBefore=File.Exists(profile)?File.ReadAllBytes(profile):null;
        var nativeBefore=File.Exists(nativeFile)?File.ReadAllBytes(nativeFile):null;
        var custom=new GameCatalogEntry {Id="CUSTOM_PERSONAL_TEST",Title="Mon port ajouté",ShelfConsole="wii",Executable=fixture+"-missing"};
        try
        {
            owned.Executable=fixture;
            var port=NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Catalogs/ps1.json")).Single(p=>p.Id==configured.NativePortId);
            new NativePortLibrary(paths).Configure(port,fixture);
            new GameProfileStore(paths.ConfigurationDirectory).Save(new GameProfileEntry(discGame.Id,"",fixture));
            catalog.Add(custom);unavailable.IsFavorite=true;
            Click("NavMyGamesButton");
            Check(Cards().Select(card=>card.Game.Id).ToHashSet().SetEquals(new[]{owned.Id,configured.Id,discGame.Id,custom.Id}),
                "Personal library must include installed, configured, saved-disc and manually added games across consoles only.");
            Check(Cards().All(card=>card.ShowConsole) && configured.Executable==fixture,
                "Personal cards must identify their console and restore configured native executables.");
            Check(Cards().Single(card=>card.Game.Id==owned.Id).Versions.All(choice=>choice.GameId==owned.Id),
                "Uninstalled alternative ports must not masquerade as owned versions.");
            Save("my-games-desktop.png");
            var width=window.Width;var height=window.Height;
            window.Width=960;window.Height=700;Dispatcher.UIThread.RunJobs();
            var heading=window.FindControl<StackPanel>("LibraryHeaderText")!;
            var tools=window.FindControl<StackPanel>("LibraryHeaderTools")!;
            Check(heading.TranslatePoint(default,window)!.Value.Y+heading.Bounds.Height<=tools.TranslatePoint(default,window)!.Value.Y
                && tools.TranslatePoint(default,window)!.Value.X+tools.Bounds.Width<=window.Bounds.Width,
                "Small personal-library windows must keep search and actions below the heading without clipping.");
            Save("my-games-compact.png");window.Width=width;window.Height=height;Dispatcher.UIThread.RunJobs();
            var search=window.FindControl<TextBox>("LibrarySearchBox")!;
            search.Text="PlayStation";
            Dispatcher.UIThread.RunJobs();
            Check(Cards().Length==1 && Cards()[0].Game.Id==configured.Id,"Personal library search must find a console as well as a title.");
            search.Text="no-such-personal-game";
            Dispatcher.UIThread.RunJobs();
            Check(Cards().Length==0 && window.FindControl<TextBlock>("EmptyLibraryText")!.IsVisible,"Empty searches need visible feedback.");
            search.Text="";Dispatcher.UIThread.RunJobs();ports[0].Executable=fixture;Click("NavMyGamesButton");
            Check(Cards().Length==4,"Installing another port duplicated its game in the personal library.");
            var multi=Cards().Single(card=>card.Game.ShelfConsole=="n64");
            var choice=multi.Versions.First(version=>version.GameId!=multi.Game.Id);
            typeof(MainWindow).GetMethod("SelectPortVersion",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[choice.GameId]);
            Check(Cards().Length==4 && Cards().Any(card=>card.Game.Id==choice.GameId),
                "A personal port choice from another console must replace the existing card.");
            File.Delete(fixture);Click("NavMyGamesButton");
            Check(Cards().Any(card=>card.Game.Id==configured.Id) && Cards().Any(card=>card.Game.Id==discGame.Id)
                && Cards().Any(card=>card.Game.Id==custom.Id),"Explicitly added games must remain findable when their files are temporarily unavailable.");
            File.WriteAllText(fixture,"never executed");
            Click("NavLibraryButton");
            Check(!personal.Classes.Contains("selected") && Cards().Length>4 && Cards().All(card=>!card.ShowConsole),
                "The port catalog must remain separately accessible.");
            Click("BigPictureEntryButton");
            window.HandleBigPictureControllerInput(7);
            Check(window.FindControl<Control>("LibraryView")!.IsVisible && !window.FindControl<PlatformHomeControl>("PlatformHome")!.IsVisible && Cards().Length==4,
                "X on Big Picture console home must open the personal library.");
            Save("my-games-big-picture.png");
            var visibleNative=Cards().First(card=>card.Game.Id==configured.Id);
            var label=visibleNative.PlayLabel;visibleNative.Game.Executable=fixture+"-missing";
            var refresh=typeof(MainWindow).GetMethod("RefreshQuickPlayUi",BindingFlags.Instance|BindingFlags.NonPublic)!;
            refresh.Invoke(window,null);
            Check(visibleNative.PlayLabel!=label,"Visible personal-library cards must update in Big Picture.");
            visibleNative.Game.Executable=fixture;refresh.Invoke(window,null);
            typeof(MainWindow).GetMethod("SelectLibraryGame",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,
                [new Button {Tag=Cards().First(card=>card.Game.Id==custom.Id)},new RoutedEventArgs()]);
            Check(window.FindControl<Control>("GameView")!.IsVisible,"Personal game details must open normally.");
            window.HandleBigPictureControllerInput(6);
            Check(window.FindControl<Control>("LibraryView")!.IsVisible && Cards().Length==4,"Back from details must preserve the personal library.");
            window.FindControl<Button>("BigPictureReturnButton")!.Focus();
            var before=window.FindControl<Button>("BigPictureReturnButton")!.IsFocused;
            window.HandleBigPictureControllerInput(4);
            Check(before && !window.FindControl<Button>("BigPictureReturnButton")!.IsFocused,"Controller navigation must reach personal-library controls.");
            window.HandleBigPictureControllerInput(6);
            Check(window.FindControl<PlatformHomeControl>("PlatformHome")!.IsVisible,"B must return from the personal library to console home.");
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.F2});
            Check(window.FindControl<Control>("LibraryView")!.IsVisible,"F2 must open personal games from console home.");
            window.HandleBigPictureControllerInput(6);window.HandleBigPictureControllerInput(15);
            var menu=window.FindControl<Border>("BigPictureMenuOverlay")!;
            Check(menu.IsVisible,"Start must work on console home.");
            Dispatcher.UIThread.RunJobs();
            var menuPoint=menu.TranslatePoint(new Point(menu.Bounds.Width/2,menu.Bounds.Height/2),window)!.Value;
            Check(window.InputHitTest(menuPoint) is Visual hit && (hit==menu || menu.IsVisualAncestorOf(hit)),
                "The console home must not cover or intercept the main menu.");
            Save("my-games-menu-from-consoles.png");
            window.HandleBigPictureControllerInput(4);window.HandleBigPictureControllerInput(5);
            Check(!menu.IsVisible && window.FindControl<Control>("LibraryView")!.IsVisible,"Personal library must be selectable through the controller menu.");
            window.HandleBigPictureControllerInput(6);window.HandleBigPictureControllerInput(5);
            Click("BigPictureExitButton");
            Check(!window.FindControl<TextBlock>("LibrarySectionTitle")!.Text!.Equals("Mes jeux"),"Choosing a console must leave the personal-library filter.");
            Console.WriteLine("OK  personal games across consoles, configured/profile/custom paths, missing files, owned variants without duplicates, search, desktop and Big Picture access, and B return.");
        }
        finally
        {
            catalog.Remove(custom);
            foreach(var game in catalog)if(previousPaths.TryGetValue(game.Id,out var previous))
                (game.Executable,game.DiscImage,game.IsFavorite)=previous;
            if(profileBefore is null)File.Delete(profile);else File.WriteAllBytes(profile,profileBefore);
            if(nativeBefore is null)File.Delete(nativeFile);else File.WriteAllBytes(nativeFile,nativeBefore);
            File.Delete(fixture);
            Click("NavLibraryButton");
            typeof(MainWindow).GetMethod("PersistCatalog",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        }
        void Save(string name)
        {
            if(snapshots is null)return;
            Directory.CreateDirectory(snapshots);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,name));
        }
    }
}
