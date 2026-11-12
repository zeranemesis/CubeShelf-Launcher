using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;
using CubeShelf.Mobile;

internal static class PresentationTests
{
    internal static void Run(string root,MobileNativePort[] ports)
    {
        var logoTitle=new GameTitleControl();
        foreach(var port in ports.Where(p=>!p.Title.Contains(" / ",StringComparison.Ordinal)))
        {logoTitle.SetGame(port.Id,port.Title,port.ShelfConsole);Check(logoTitle.HasArtwork,"Android bundles the exact-platform logo for "+port.Title);}
        logoTitle.SetGame("n64-oot","The Legend of Zelda: Ocarina of Time","n64");
        Check(logoTitle.HasArtwork,"the original Nintendo 64 Zelda logo is available offline");
        var originalLogo=((Grid)logoTitle.Content!).Children.OfType<Image>().Single().Source;
        logoTitle.SetGame("n64-oot","The Legend of Zelda: Ocarina of Time","n64");
        Check(ReferenceEquals(originalLogo,((Grid)logoTitle.Content!).Children.OfType<Image>().Single().Source),"refreshing a selected title reuses its decoded logo");
        logoTitle.SetGame("GMPE01_00","Mario Party 4","n64");
        Check(!logoTitle.HasArtwork,"a legacy GameCube ID cannot supply a logo for another console");
        logoTitle.SetGame("unmatched","Un jeu sans logo","n64");
        Check(!logoTitle.HasArtwork&&((Grid)logoTitle.Content!).Children.OfType<TextBlock>().Single().Text=="Un jeu sans logo","an unknown title remains readable without a substituted logo");
        var logoWindow=new Window{Content=logoTitle,Width=400,Height=200};logoTitle.SetGame("n64-sm64","Super Mario 64","n64");logoWindow.Show();Jobs();
        logoWindow.Content=null;Check(((Grid)logoTitle.Content!).Children.OfType<Image>().Single().Source is null,"detaching the title releases its native bitmap");
        logoWindow.Content=logoTitle;Jobs();Check(logoTitle.HasArtwork,"reattaching the title restores its logo");logoWindow.Close();
        var directory=Path.Combine(root,"dual-theme");var settings=new LibraryPresentationStore(directory);
        Check(!settings.IsDual&&!settings.HasStyleChoice,"existing installations keep the classic theme without recording a choice");settings.SetStyle(true);
        Check(new LibraryPresentationStore(directory).HasStyleChoice,"an explicit theme choice is remembered");
        Check(!settings.SwapFaceButtons&&settings.LastPlayed is null,"Xbox button layout and no last game by default");
        settings.SetSwapFaceButtons(true);settings.SetLastPlayed("n64-sm64");
        Check(new LibraryPresentationStore(directory) is {SwapFaceButtons:true,LastPlayed:"n64-sm64"},"the Nintendo layout and the last launched game survive restart");
        Check(new LibraryPresentationStore(directory).IsDual,"the second theme survives restart independently of light/dark");
        Check(settings.LightHome,"the sober HOME theme defaults to the requested white appearance");
        settings.ToggleHomeAppearance();Check(!new LibraryPresentationStore(directory).LightHome,"the dark HOME appearance is persisted separately from Classic");settings.ToggleHomeAppearance();
        var wav=new byte[1644];"RIFF"u8.CopyTo(wav);BitConverter.GetBytes(wav.Length-8).CopyTo(wav,4);"WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.GetBytes(16).CopyTo(wav,16);BitConverter.GetBytes((short)1).CopyTo(wav,20);BitConverter.GetBytes((short)1).CopyTo(wav,22);BitConverter.GetBytes(8000).CopyTo(wav,24);BitConverter.GetBytes(16000).CopyTo(wav,28);BitConverter.GetBytes((short)2).CopyTo(wav,32);BitConverter.GetBytes((short)16).CopyTo(wav,34);"data"u8.CopyTo(wav.AsSpan(36));BitConverter.GetBytes(1600).CopyTo(wav,40);
        settings.ImportMusicAsync("GMPE01_00",".wav",new MemoryStream(wav)).GetAwaiter().GetResult();
        var track=settings.Track("GMPE01_00");Check(track is not null&&File.ReadAllBytes(track).SequenceEqual(wav),"a local game track is copied into managed preview storage");
        try{settings.ImportMusicAsync("GMPE01_00",".wav",new MemoryStream(new byte[20])).GetAwaiter().GetResult();throw new Exception("Invalid audio accepted");}catch(InvalidDataException){}
        Check(File.ReadAllBytes(track!).SequenceEqual(wav),"a rejected replacement preserves the previous game track");
        try{settings.ImportMusicAsync("GMPE01_00",".wav",new MemoryStream(new byte[32*1024*1024+1])).GetAwaiter().GetResult();throw new Exception("Oversized audio accepted");}catch(InvalidDataException){}
        Check(File.ReadAllBytes(track!).SequenceEqual(wav),"oversized audio cannot replace an associated track");
        Task.WhenAll(settings.ImportMusicAsync("concurrent",".wav",new MemoryStream(wav)),Task.Run(()=>{for(var i=0;i<10;i++)settings.ToggleHomeAppearance();})).GetAwaiter().GetResult();
        var concurrent=new LibraryPresentationStore(directory);Check(concurrent.LightHome&&concurrent.Track("concurrent") is not null,"audio import and appearance changes preserve both settings without colliding temporary writes");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{settings.ImportMusicAsync("GMPE01_00",".wav",new MemoryStream(wav),cancelled.Token).GetAwaiter().GetResult();throw new Exception("Cancelled import accepted");}catch(OperationCanceledException){}}
        var cover=Path.Combine(directory,"mario-party.png");using(var input=Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/Assets/Covers/MarioParty4/pal_front.png")))using(var output=File.Create(cover))input.CopyTo(output);
        MotionTests.Run(root,cover);
        var back=Path.Combine(directory,"mario-party-back.png");var spine=Path.Combine(directory,"mario-party-spine.png");
        foreach(var (name,target) in new[]{("pal_back.png",back),("pal_spine.png",spine)})
        {using var input=Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/Assets/Covers/MarioParty4/"+name));using var output=File.Create(target);input.CopyTo(output);}
        var played=new List<string?>();var cues=new List<string?>();var view=new DualScreenLibraryView();view.Configure(settings,path=>played.Add(path),path=>cues.Add(path));
        var items=Enumerable.Range(0,30).Select(i=>new DualScreenItem(i==0?"GMPE01_00":"fixture-"+i,i==0?"Mario Party 4":"The Legend of Zelda: long fixture title "+i,i%2==0?"gamecube":"n64",cover,"Port natif · état de test","Jouer",Back:back,Spine:spine)).ToArray();
        var window=new Window{Content=view,Width=390,Height=844};window.Show();view.SetItems(items,"Mes jeux",false,items[0].Id);Jobs();
        var achievementsButton=view.GetVisualDescendants().OfType<RetroAchievementsButton>().Single();var requested=0;view.AchievementsRequested+=()=>requested++;
        achievementsButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(requested==1&&achievementsButton.GetVisualDescendants().OfType<Image>().Single().Source is not null,"one clickable RetroAchievements button carries the offline colour logo");
        Check(view.GetVisualDescendants().OfType<GameCaseControl>().Count()==1,"the selected game reuses one 3D case instead of rendering the whole grid in 3D");
        Check(view.GetVisualDescendants().OfType<GameCaseControl>().Single().CaseFormat==GameCaseFormat.GameCube,"the GameCube preview uses the existing GameCube case format");
        var grid=view.GetVisualDescendants().OfType<ListBox>().Single();Check(grid.ItemCount==items.Length,"HOME exposes the full collection in one rail");
        var selected="";view.SelectionChanged+=id=>selected=id;var source=grid.ItemsSource;
        view.Move(1,0);Check(selected==items[3].Id,"horizontal controller movement selects the next column");
        Check(ReferenceEquals(source,grid.ItemsSource),"moving between columns preserves thumbnail instances");
        MotionTests.WaitForPreview(view,items[3].Title);
        Check(view.GetVisualDescendants().OfType<GameCaseControl>().Single().CaseFormat==GameCaseFormat.Nintendo64,"changing console also changes the selected 3D box format");
        view.Move(0,1);Check(selected==items[4].Id,"vertical movement stays in the current column");
        Check(cues.Any(path=>path?.EndsWith("move.wav")==true),"moving the selection emits the original navigation cue");
        for(var i=0;i<30;i++)view.Move(1,0);Check(view.SelectedId==items[^2].Id&&grid.ItemCount==items.Length,"controller movement reaches the last column without duplicating games");
        var launched="";var libraryRequests=0;view.LaunchRequested+=id=>launched=id;view.LibraryRequested+=()=>libraryRequests++;
        var homeGames=items.Take(6).ToArray();
        view.SetItems(homeGames,"Mes jeux",false,homeGames[0].Id);Jobs();view.Move(0,0);
        view.Move(0,-1);
        var dock=view.GetVisualDescendants().OfType<Button>().Where(button=>button.Classes.Contains("home-dock")).ToArray();
        Check(dock.Any(button=>button.IsFocused)&&view.SelectedId==homeGames[0].Id,
            "up from the first game row focuses the top icons without changing the selection");
        for(var i=0;i<dock.Length;i++)view.Move(-1,0);
        view.Move(1,0);view.ActivateFocused();
        Check(libraryRequests==1&&launched=="","left and right navigate the top icons and confirm opens the focused command");
        view.Move(0,1);Check(grid.IsKeyboardFocusWithin,"down returns from the toolbar to the grid");
        view.SelectItem(homeGames[^1].Id);view.Move(0,1);
        var actions=view.GetVisualDescendants().OfType<Button>().Where(button=>button.Content?.ToString() is "Jouer" or "Options du jeu").ToArray();
        Check(actions.Any(button=>button.IsFocused)||achievementsButton.IsFocused,"down from the last row reaches the bottom actions");
        for(var i=0;i<3;i++)view.Move(-1,0);
        view.ActivateFocused();
        Check(launched==view.SelectedId&&libraryRequests==1,"confirm on the lower play action launches the selected game");
        view.Move(0,-1);Check(grid.IsKeyboardFocusWithin,"up returns from the bottom actions to the grid");
        Check(cues.Last()?.EndsWith("launch.wav")==true,"activation uses a different sound from navigation");
        var customize=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Personnaliser");
        customize.Focus();view.ActivateFocused();Jobs();
        Check(view.IsPopupOpen,"controller confirmation opens the customization menu");
        var menu=(Control)((Flyout)customize.Flyout!).Content!;
        var musicToggle=menu.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Musique :",StringComparison.Ordinal)==true);
        var beforeLaunch=launched;var beforeMusic=settings.MusicEnabled;musicToggle.Focus();view.ActivateFocused();Jobs();
        Check(settings.MusicEnabled!=beforeMusic&&launched==beforeLaunch,"confirming an audio popup choice changes its setting without launching a game");
        view.Move(0,1);view.ActivateFocused();Check(launched==beforeLaunch,"controller navigation stays within an open customization menu");
        Check(view.DismissPopup()&&!view.IsPopupOpen,"Back dismisses the customization popup before changing console");settings.ToggleMusic();if(!settings.SoundsEnabled)settings.ToggleSounds();
        var preview=new List<DualScreenItem>{new("GMPE01_00","Mario Party 4","gamecube",cover,"Exemple · aucune installation effectuée","Préparer",Back:back,Spine:spine)};
        foreach(var port in ports.Take(8))
        {
            var target=Path.Combine(directory,port.Id+".png");using(var input=Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/"+port.Front)))using(var output=File.Create(target))input.CopyTo(output);
            preview.Add(new(port.Id,port.Title,port.ShelfConsole,target,"Catalogue Android · aperçu","Préparer"));
        }
        view.SetItems(preview,"Catalogue Android",false,"GMPE01_00");
        foreach(var size in new[]{new Size(320,740),new Size(390,844),new Size(1280,800),new Size(1920,1080),new Size(844,390),new Size(700,320)})
        {
            window.Width=size.Width;window.Height=size.Height;Jobs();
            CheckSelectionFits(grid);
            Check(grid.Bounds.Height>=64,"the bottom screen retains a visible row at "+size.Width);
            Snapshot(window,"home-"+size.Width+"-light.png");
            Check(view.GetVisualDescendants().OfType<Button>().Where(b=>b.Content is string&&b.IsEffectivelyVisible).All(b=>b.Bounds.Height>=48),"touch actions retain 48-pixel targets at "+size.Width);
            foreach(var control in view.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible))
            {var at=control.TranslatePoint(new Point(0,0),view)!.Value;Check(at.X>=0&&at.Y>=0&&at.X+control.Bounds.Width<=view.Bounds.Width+1&&at.Y+control.Bounds.Height<=view.Bounds.Height+1,"visible actions remain inside the viewport at "+size);}
        }
        window.Width=1280;window.Height=800;Jobs();
        settings.ToggleHomeAppearance();view.Configure(settings,path=>played.Add(path),path=>cues.Add(path));Jobs();Snapshot(window,"home-1280-dark.png");
        window.Width=390;window.Height=844;Jobs();Snapshot(window,"home-390-dark.png");window.Width=1280;window.Height=800;Jobs();
        var corrupt=Path.Combine(directory,"corrupt-cover.png");File.WriteAllText(corrupt,"not an image");
        view.SetItems([new DualScreenItem("missing-art","Exemple sans illustration","n64",corrupt,"Associe un programme compatible avant de jouer.","Configurer")],"Mes jeux",false);Jobs();Snapshot(window,"home-fallback-dark.png");
        Check(view.SelectedId=="missing-art","a damaged cover retains a named, selectable fallback");
        var fallback=view.GetVisualDescendants().OfType<HomeArtworkControl>().Single(a=>a.IsPreview);
        Check(fallback.GetVisualDescendants().OfType<ConsoleBrandMark>().Single().Bounds.Height>=60,"the missing-art preview reserves a visible console logo instead of a blank capsule");
        var selectedArtwork=grid.GetVisualDescendants().OfType<HomeArtworkControl>().Single(a=>a.IsSelected);
        Check(selectedArtwork.GetVisualDescendants().OfType<Control>().Any(b=>b.Name=="SelectionFrame"&&b.IsVisible),"selected thumbnails keep their visible outline");
        CheckSelectionFits(grid);
        view.SetItems(preview,"Catalogue Android",false,"GMPE01_00");Jobs();
        window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;Jobs();Snapshot(window,"dual-pc-light.png");
        var search=view.GetVisualDescendants().OfType<TextBox>().Single();search.Text="not-a-real-game";Jobs();Check(grid.ItemCount==0,"search has a non-launchable empty state");
        Check(view.GetVisualDescendants().OfType<Button>().Where(b=>b.Content?.ToString()=="Options du jeu").All(b=>!b.IsEnabled),"empty results disable game-specific options");Snapshot(window,"home-empty.png");
        search.Text="Ocarina";Jobs();view.Activate();
        Check(selected=="n64-oot"&&launched=="n64-oot","search updates both the displayed selection and the activated game");CheckSelectionFits(grid);Snapshot(window,"home-oot-3d-dark.png");search.Text="";Jobs();
        view.SetItems(items,"Mes jeux",false,items[0].Id);view.SetPlaybackActive(true);
        using(var timerWindow=new CancellationTokenSource(700))Dispatcher.UIThread.MainLoop(timerWindow.Token);
        Jobs();
        Check(played.LastOrDefault()==track,"selecting a game starts its associated local preview after a debounce");
        view.SetItems(items,"Consoles",true);using(var timerWindow=new CancellationTokenSource(700))Dispatcher.UIThread.MainLoop(timerWindow.Token);
        Check(played.LastOrDefault()?.EndsWith("ambience.wav")==true,"console selection has bundled original ambience without a music import");
        settings.ToggleMusic();view.SetPlaybackActive(true);Check(played.LastOrDefault() is null,"muting ambience stops its playback immediately");settings.ToggleMusic();
        settings.ToggleSounds();Check(!new LibraryPresentationStore(directory).SoundsEnabled,"sound effects have a separate persisted mute control");var cueCount=cues.Count;view.Move(1,0);Check(cues.Count==cueCount,"muted navigation does not emit a sound");settings.ToggleSounds();
        view.SetPlaybackActive(false);Check(played[^1] is null,"leaving the app stops preview music");window.Close();
        var games=new FakeGames(directory,ports);new LibraryPresentationStore(games.DataDirectory).SetStyle(true);var mobile=new MobileLibraryView(games);var host=new Window{Content=mobile,Width=390,Height=844};host.Show();Jobs();
        var dual=mobile.GetVisualDescendants().OfType<DualScreenLibraryView>().Single();
        Check(dual.IsVisible&&dual.IsPlatforms,"Android restores the dual theme on its console selection screen");
        Snapshot(host,"home-mobile-shell-consoles.png");
        Check(mobile.GetVisualDescendants().OfType<GameShelfControl>().All(s=>!s.IsEffectivelyVisible),"the grid theme hides the original 3D scenes rather than rendering them underneath");
        mobile.ActivateFromController();Jobs();Check(!dual.IsPlatforms,"controller confirmation opens that console's game grid");
        Snapshot(host,"home-mobile-shell-games.png");
        dual.GetVisualDescendants().OfType<TextBox>().Single().Text="Metroid";Jobs();dual.Activate();Jobs();
        Check(games.Native.Prepared.LastOrDefault()=="gc-metroidprime"&&games.Opened==0,"Android prepares the searched result instead of the previously selected game");
        mobile.BackFromController();Jobs();
        mobile.BackFromController();Jobs();Check(dual.IsPlatforms,"Android Back returns to the console grid");
        mobile.ShowDownloads();Jobs();var picker=mobile.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="LibraryStylePicker");
        Check(picker.ItemCount==2,"the chooser offers exactly Classic and Dual screen");picker.SelectedIndex=0;Jobs();
        Check(!dual.IsVisible&&!new LibraryPresentationStore(games.DataDirectory).IsDual,"switching to Classic hides the new theme and saves the choice");host.Close();
    }
    private static void CheckSelectionFits(ListBox grid)
    {
        var tile=grid.GetVisualDescendants().OfType<HomeArtworkControl>().Single(a=>a.IsSelected);
        var outline=tile.GetVisualDescendants().OfType<Control>().Single(b=>b.Name=="SelectionFrame");
        var origin=outline.TranslatePoint(default,tile)!.Value;
        Check(Math.Abs(origin.X)<.01&&Math.Abs(origin.Y)<.01
            &&Math.Abs(outline.Bounds.Width-tile.Bounds.Width)<.01&&Math.Abs(outline.Bounds.Height-tile.Bounds.Height)<.01,
            "the selection outline follows the complete tile bounds at "+tile.Bounds.Size);
    }
    private static void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
    private static void Snapshot(Window window,string name)
    {if(Environment.GetEnvironmentVariable("CUBESHELF_DUAL_SNAPSHOTS") is {} folder){Directory.CreateDirectory(folder);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,name));}}
    private static void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("OK "+message);}
}
