using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

internal static class BigPictureMenuChecks
{
    internal static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("OK "+message);}
        void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
        T Find<T>(string name) where T:Control=>window.FindControl<T>(name)!;
        void Click(string name){Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();}
        KeyEventArgs Press(Key key,KeyModifiers modifiers=KeyModifiers.None)
        {var input=new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key,KeyModifiers=modifiers};window.RaiseEvent(input);Jobs();return input;}
        var overlay=Find<Border>("BigPictureMenuOverlay");var header=Find<Border>("BigPictureSubnav");
        var sidebar=Find<Border>("DesktopSidebar");var dual=Find<DualScreenLibraryView>("DualScreenView");
        void Menu(string label)
        {
            Find<StackPanel>("BigPictureMenuRows").Children.OfType<Button>().Single(b=>AutomationProperties.GetName(b)==label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        }
        void Fits(Control control)
        {
            var at=control.TranslatePoint(new Point(0,0),window)!.Value;
            Check(control.IsEffectivelyVisible&&at.X>=0&&at.Y>=0&&at.X+control.Bounds.Width<=window.Bounds.Width+1
                &&at.Y+control.Bounds.Height<=window.Bounds.Height+1,"Big Picture action stays within the viewport: "+control.Name);
        }
        void Snapshot(string name){if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,name));}}
        var engines=new NativePortsWindow(CubeShelf.Core.Library.ShelfProduct.Resolve("n64"),busy:_=>true);
        engines.Show();Jobs();
        var engineButtons=engines.GetVisualDescendants().OfType<Button>().ToArray();
        Check(engineButtons.Any(b=>Equals(b.Content,"Rechercher une mise à jour"))
            &&engineButtons.Any(b=>Equals(b.Content,"Revenir au moteur précédent"))
            &&!engineButtons.Single(b=>Equals(b.Content,"Installer ce paquet")).IsEnabled,
            "the native engine manager exposes updates and rollback and blocks installation while the game is running");
        engines.Close();Jobs();
        if(!sidebar.IsVisible)Press(Key.F11);
        window.WindowState=WindowState.Normal;window.Width=1280;window.Height=800;
        Find<ComboBox>("LibraryStylePicker").SelectedIndex=0;Find<ComboBox>("ConsoleShelfPicker").SelectedIndex=0;Jobs();
        var previousSize=window.Bounds.Size;var previousState=window.WindowState;
        var previousDecorations=window.SystemDecorations;var previousResize=window.CanResize;
        Click("BigPictureEntryButton");
        Check(header.IsEffectivelyVisible&&Find<PlatformHomeControl>("PlatformHome").IsVisible,"Classic console choice has a persistent Big Picture toolbar");
        Fits(Find<Button>("BigPictureExitButton"));
        Find<Button>("BigPictureSettingsButton").Focus();Press(Key.Enter);
        Check(overlay.IsVisible&&Find<PlatformHomeControl>("PlatformHome").IsVisible,"Enter on the toolbar opens the menu without choosing a console");
        window.HandleBigPictureControllerInput(4);Jobs();
        Check(AutomationProperties.GetName(overlay.GetVisualDescendants().OfType<Button>().Single(b=>b.IsFocused))=="Mes jeux","controller Down follows the main navigation order");
        window.HandleBigPictureControllerInput(3);Jobs();
        Check(!Find<StackPanel>("BigPictureMenuRows").Children.OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Options du jeu"),"console selection does not offer actions for a stale game");
        Menu("Paramètres");Menu("Manette");
        Check(Find<TextBlock>("BigPictureMenuTitle").Text=="Manette","controller preferences have their own menu level");
        window.HandleBigPictureControllerInput(6);Jobs();Check(Find<TextBlock>("BigPictureMenuTitle").Text=="Paramètres","controller Back returns to its parent settings menu");
        window.HandleBigPictureControllerInput(6);Jobs();Check(Find<TextBlock>("BigPictureMenuTitle").Text=="Menu","the next Back returns to the main menu");
        Snapshot("bigpicture-menu-classic.png");
        Find<Button>("BigPictureMenuCloseButton").Focus();Press(Key.Enter);Check(!overlay.IsVisible&&!sidebar.IsVisible,"Enter on Close dismisses only the menu");
        Press(Key.F10);Menu("Téléchargements");Check(Find<Control>("DownloadsView").IsVisible&&header.IsVisible,"downloads remain inside Big Picture with the persistent toolbar");
        Click("BigPictureReturnButton");Check(Find<PlatformHomeControl>("PlatformHome").IsVisible,"Back from downloads restores the console chooser");
        Press(Key.X);Check(Find<Control>("LibraryView").IsVisible&&!overlay.IsVisible,"keyboard X on consoles preserves the personal-library shortcut");
        Click("BigPictureConsolesButton");
        Find<PlatformHomeControl>("PlatformHome").Choose();Jobs();Find<Button>("BigPicturePlayButton").Focus();Press(Key.X);
        Check(Find<TextBlock>("BigPictureMenuContext").Text==AutomationProperties.GetName(Find<GameTitleControl>("BigPictureTitle")),"game options identify the actual selected Classic game");
        Check(Find<TextBlock>("BigPictureMenuTitle").Text=="Options du jeu"
            &&new[]{"Paramètres du jeu","Mods du jeu"}.All(label=>Find<StackPanel>("BigPictureMenuRows").Children.OfType<Button>().Any(b=>AutomationProperties.GetName(b)==label)),
            "keyboard X opens the existing game menu with game settings and mods");
        window.HandleBigPictureControllerInput(1);window.HandleBigPictureControllerInput(2);Jobs();
        Check(overlay.IsVisible&&!Find<GameLaunchTransition>("LaunchTransition").IsVisible,"horizontal menu input cannot trigger a game launch without confirmation");
        Press(Key.Escape);Press(Key.Escape);
        window.HandleBigPictureControllerInput(7);Jobs();
        Check(overlay.IsVisible&&Find<TextBlock>("BigPictureMenuTitle").Text=="Options du jeu","controller X opens Classic game options instead of flipping the case");
        Menu("Paramètres du jeu");Check(Find<Control>("GameView").IsVisible&&!overlay.IsVisible,"game settings reuse the existing in-window game page");
        Click("BigPictureReturnButton");Check(Find<Control>("BigPictureView").IsVisible,"Back from game settings restores the selected Classic game");
        Press(Key.X);Menu("Mods du jeu");Check(Find<Control>("ModsView").IsVisible,"X exposes the existing game mods page");
        Click("BigPictureReturnButton");
        var raCatalog=(List<CubeShelf.Core.Library.GameCatalogEntry>)typeof(MainWindow).GetField("_catalogGames",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        var raDirectory=Path.Combine(Path.GetTempPath(),"decompshelf-ra-menu-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(raDirectory);
        var raExecutable=Path.Combine(raDirectory,"native.exe");File.WriteAllText(raExecutable,"test fixture; never executed");
        File.WriteAllText(Path.Combine(raDirectory,"manifest.json"),"{\"capabilities\":[\"retroachievements-login\"]}");
        var supported=new CubeShelf.Core.Library.GameCatalogEntry{Id="ra-menu-fixture",Title="RA native fixture",Executable=raExecutable};
        var unsupported=new CubeShelf.Core.Library.GameCatalogEntry{Id="ra-menu-no-support",Title="No native RA support",Executable=Path.Combine(raDirectory,"unverified","native.exe")};
        var duplicate=new CubeShelf.Core.Library.GameCatalogEntry{Id="ra-menu-alternate",Title=supported.Title,Executable=raExecutable};
        raCatalog.AddRange(new[]{supported,unsupported,duplicate});
        try
        {
            Click("BigPictureAchievements");
            var labels=Find<StackPanel>("BigPictureMenuRows").Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray();
            Check(overlay.IsVisible&&Find<TextBlock>("BigPictureMenuTitle").Text=="RetroAchievements"
                &&labels.Count(label=>label==supported.Title)==1&&!labels.Contains(unsupported.Title),
                "the colour RA button opens only declared native-compatible games without duplicate ports");
            Press(Key.Escape);Press(Key.Escape);
            Check(!Find<GameLaunchTransition>("LaunchTransition").IsVisible,"browsing RetroAchievements compatibility never launches a game");
        }
        finally{raCatalog.Remove(supported);raCatalog.Remove(unsupported);raCatalog.Remove(duplicate);}
        Find<ComboBox>("LibraryStylePicker").SelectedIndex=1;Jobs();
        Check(header.IsVisible&&dual.IsPlatforms,"the HOME console chooser retains the same toolbar");
        window.HandleBigPictureControllerInput(7);Jobs();
        Check(dual.IsVisible&&!dual.IsPlatforms&&!overlay.IsVisible&&Find<TextBlock>("BigPictureSubnavTitle").Text!.Contains("Mes jeux"),"controller X on HOME consoles opens the personal library");
        Click("BigPictureConsolesButton");
        Find<Button>("BigPictureSettingsButton").Focus();Press(Key.Enter);Check(overlay.IsVisible&&dual.IsPlatforms,"HOME toolbar Enter opens the menu instead of activating a grid tile");
        Menu("Paramètres");Menu("Affichage et audio");Menu("Apparence");
        var dark=Find<ThemeVariantScope>("BigPictureMenuTheme").RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Dark;
        if(!dark)Menu("Apparence");
        Check(Find<ThemeVariantScope>("BigPictureChromeTheme").RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Dark,"HOME appearance also updates the menu and toolbar palette");
        window.Width=960;window.Height=560;Jobs();
        Fits(Find<Button>("BigPictureMenuExitButton"));Fits(Find<Button>("BigPictureMenuCloseButton"));
        Snapshot("bigpicture-menu-appearance-small.png");
        Press(Key.Escape);Press(Key.Escape);Press(Key.Escape);window.Width=1280;window.Height=800;Jobs();
        dual.Activate();Jobs();dual.Move(0,0);Press(Key.X);
        Check(Find<TextBlock>("BigPictureMenuContext").Text==dual.GetVisualDescendants().OfType<GameTitleControl>().Select(AutomationProperties.GetName).Single(),"HOME game options target the selected grid game");
        Press(Key.Escape);Press(Key.Escape);window.HandleBigPictureControllerInput(7);Jobs();
        Check(overlay.IsVisible&&Find<TextBlock>("BigPictureMenuTitle").Text=="Options du jeu","controller X opens HOME game options");
        Press(Key.Escape);Press(Key.Escape);
        dual.GetVisualDescendants().OfType<RetroAchievementsButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        Check(overlay.IsVisible&&Find<TextBlock>("BigPictureMenuTitle").Text=="RetroAchievements","HOME uses the same clickable RetroAchievements game menu");
        Press(Key.Escape);Press(Key.Escape);
        var search=dual.GetVisualDescendants().OfType<TextBox>().Single();
        var searchButton=dual.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"search"));
        searchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();search.Focus();
        var typedX=Press(Key.X);window.HandleBigPictureControllerInput(7);Jobs();
        Check(!typedX.Handled&&!overlay.IsVisible&&search.IsKeyboardFocusWithin,"X remains available to HOME search and controller X does not interrupt typing");
        searchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();dual.Move(0,0);var cut=Press(Key.X,KeyModifiers.Control);
        Check(!cut.Handled&&!overlay.IsVisible,"Ctrl+X remains available to normal input handling");
        var customize=dual.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Personnaliser"));customize.Flyout!.ShowAt(customize);Jobs();
        Press(Key.X);window.HandleBigPictureControllerInput(7);Jobs();
        Check(dual.IsPopupOpen&&!overlay.IsVisible,"X does not replace the HOME customization popup");
        dual.DismissPopup();dual.Move(0,0);Jobs();
        var dialog=new Window{Width=300,Height=200};dialog.Show(window);Jobs();
        var dialogX=Press(Key.X);window.HandleBigPictureControllerInput(7);Jobs();
        Check(!dialogX.Handled&&!overlay.IsVisible&&dialog.IsVisible,"X leaves owned dialogs in control");dialog.Close();Jobs();dual.Move(0,0);
        dual.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"more")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        Check(overlay.IsVisible&&Find<TextBlock>("BigPictureMenuTitle").Text=="Options du jeu","HOME OptionsRequested and X use the same existing game menu");
        Press(Key.Escape);Snapshot("bigpicture-menu-home-dark.png");
        Press(Key.F10);Snapshot("bigpicture-toolbar-home-dark.png");
        Press(Key.F10);Menu("Paramètres");Menu("Paramètres avancés");
        Check(Find<Control>("SettingsView").IsVisible&&header.IsVisible,"advanced settings retain the Big Picture navigation");
        window.Width=960;window.Height=560;Jobs();
        foreach(var button in header.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible))
        {Fits(button);Check(button.Bounds.Height>=48,"toolbar actions retain large targets");}
        Press(Key.F10);Find<Button>("BigPictureMenuExitButton").Focus();window.HandleBigPictureControllerInput(5);Jobs();
        Check(sidebar.IsVisible&&!header.IsVisible&&!overlay.IsVisible&&window.WindowState==previousState
            &&window.SystemDecorations==previousDecorations&&window.CanResize==previousResize,"controller confirmation on Exit restores desktop decorations and closes the menu");
        Check(Math.Abs(window.Bounds.Width-previousSize.Width)<1&&Math.Abs(window.Bounds.Height-previousSize.Height)<1,"leaving Big Picture restores the desktop window size");
        Click("BigPictureEntryButton");Find<Button>("BigPictureExitButton").Focus();Press(Key.Enter);
        Check(sidebar.IsVisible&&!header.IsVisible,"Enter on the persistent Exit button leaves HOME Big Picture without opening a game");
        Click("BigPictureEntryButton");Press(Key.F10);Press(Key.F11);
        Check(sidebar.IsVisible&&!overlay.IsVisible,"F11 also exits while the Big Picture menu is open");
    }
}
