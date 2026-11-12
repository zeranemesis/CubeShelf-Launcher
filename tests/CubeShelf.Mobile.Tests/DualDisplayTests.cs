using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;
using CubeShelf.Mobile;

internal static class DualDisplayTests
{
    internal static void Run(string root,MobileNativePort[] ports)
    {
        var games=new FakeGames(Path.Combine(root,"physical-home-displays"),ports);
        var settings=new LibraryPresentationStore(games.DataDirectory);settings.SetStyle(true);settings.SetSecondaryDisplay(false);
        Check(!new LibraryPresentationStore(games.DataDirectory).SecondaryDisplay,"the secondary-display choice survives restart");
        settings.SetSecondaryDisplay(true);
        var mobile=new MobileLibraryView(games);
        var top=new Window{Content=mobile,Width=960,Height=540};top.Show();Jobs();
        var preview=Field<DualScreenLibraryView>(mobile,"_dualLibrary");
        var navigation=(DualScreenLibraryView)mobile.CreateSecondaryHome();
        var bottom=new Window{Content=navigation,Width=620,Height=540};bottom.Show();mobile.SetSecondaryScreenAttached(true);Jobs();
        try
        {
            Check(mobile.WantsSecondaryScreen&&preview.ScreenRole==HomeScreenRole.Preview&&navigation.ScreenRole==HomeScreenRole.Navigation,
                "HOME splits the preview and touch navigation into distinct visual trees");
            mobile.OptionsFromController();Jobs();
            Check(!Field<Border>(mobile,"_gameOptionsOverlay").IsVisible,"X does not open game options on the console chooser");
            var grid=navigation.GetVisualDescendants().OfType<ListBox>().Single();
            grid.SelectedItem=grid.Items.Cast<DualScreenItem>().Single(item=>item.Id=="n64");Jobs();
            Check(preview.SelectedId=="n64"&&navigation.SelectedId=="n64","a bottom-screen console selection updates the upper preview");
            navigation.Move(0,0);mobile.ActivateFromController();Jobs();
            Check(!preview.IsPlatforms&&!navigation.IsPlatforms,"controller confirmation opens the same console on both displays");
            var previousCase=preview.GetVisualDescendants().OfType<GameCaseControl>().Single();
            var visibleTitle=Avalonia.Automation.AutomationProperties.GetName(previousCase);
            var targets=ports.Where(port=>port.ShelfConsole=="n64"&&port.Id!=navigation.SelectedId)
                .Where(port=>grid.Items.Cast<DualScreenItem>().Any(item=>item.Id==port.Id)).Take(2).ToArray();
            foreach(var port in targets)
                grid.SelectedItem=grid.Items.Cast<DualScreenItem>().Single(item=>item.Id==port.Id);
            var target=targets[^1];
            Check(preview.SelectedId==target.Id&&navigation.SelectedId==target.Id,
                "a bottom-screen game selection updates the upper game and its launch context");
            Check(previousCase.IsEffectivelyVisible
                &&Avalonia.Automation.AutomationProperties.GetName(previousCase)==visibleTitle
                &&Avalonia.Automation.AutomationProperties.GetName(Field<GameTitleControl>(preview,"_title"))==visibleTitle
                &&!preview.GetVisualDescendants().OfType<HomeArtworkControl>().Any(),
                "the upper display keeps its previous 3D case and matching logo while the latest case is prepared");
            for(var i=0;i<100&&Avalonia.Automation.AutomationProperties.GetName(previousCase)!=target.Title;i++)
            {Thread.Sleep(8);Jobs();}
            Check(ReferenceEquals(previousCase,preview.GetVisualDescendants().OfType<GameCaseControl>().Single())
                &&Avalonia.Automation.AutomationProperties.GetName(previousCase)==target.Title
                &&Avalonia.Automation.AutomationProperties.GetName(Field<GameTitleControl>(preview,"_title"))==target.Title,
                "the upper display swaps to the latest 3D case and matching logo together");
            games.Native.Releases[target.Id]="v99.1";
            Invoke(mobile,"OfferUpdate");Jobs();
            var offer=Field<Border>(mobile,"_gameOptionsOverlay");
            Check(offer.IsVisible&&offer.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Plus tard"),
                "a detected Android port update asks for a decision");
            mobile.ActivateFromController();Jobs();
            Invoke(mobile,"OfferUpdate");Jobs();
            Check(!offer.IsVisible&&games.Native.Updated.Count==0,
                "the default action defers the update and the same version is not asked again");
            games.Native.Releases[target.Id]="v99.2";
            Invoke(mobile,"OfferUpdate");Jobs();
            mobile.MoveActionFocus(1);mobile.ActivateFromController();Jobs();
            Check(!offer.IsVisible&&games.Native.Updated.SequenceEqual(new[]{target.Id}),
                "confirming a new version starts the managed update for the selected game");
            games.Native.Releases.Clear();

            using(var http=new HttpClient(new UpdateCatalogHttp()))
            {
                games.AppUpdates=new("0.19.0","arm64",games.DataDirectory,_=>throw new Exception("Unexpected installation"),http);
                games.AppUpdates.CheckAsync().GetAwaiter().GetResult();
                Check(games.AppUpdates.Snapshot.State==AppUpdateState.Available,
                    "the updater accepts a verified release the size of the current Android APK");
                Invoke(mobile,"OfferUpdate");Jobs();
                Check(offer.IsVisible&&offer.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Mettre à jour"),
                    "a launcher update asks before downloading");
                mobile.ActivateFromController();Jobs();
                Check(games.AppUpdates.Snapshot.State==AppUpdateState.Available,
                    "deferring a launcher update keeps it available without downloading");
                games.AppUpdates=null;
            }
            using(var http=new HttpClient(new UpdateCatalogHttp(small:true)))
            {
                var installs=0;
                games.AppUpdates=new("0.19.0","arm64",games.DataDirectory,file=>
                {
                    Check(File.ReadAllText(file)=="APK fixture","only the verified launcher file reaches the installer");
                    installs++;
                    return "Confirmation Android";
                },http);
                games.AppUpdates.CheckAsync().GetAwaiter().GetResult();
                Invoke(mobile,"OfferUpdate");Jobs();
                mobile.MoveActionFocus(1);mobile.ActivateFromController();Jobs();
                for(var i=0;i<100&&installs==0;i++){Thread.Sleep(10);Jobs();}
                Check(installs==1,"one confirmation downloads, verifies and opens the launcher installer");
                Invoke(mobile,"CloseDownloads",true);
                games.AppUpdates=null;
            }
            var achievements=navigation.GetVisualDescendants().OfType<RetroAchievementsButton>().Single();
            achievements.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
            Check(grid.ItemCount==0&&Field<TextBlock>(navigation,"_pageLabel").Text=="Aucun jeu compatible",
                "the Android RetroAchievements library has a useful empty state");
            mobile.BackFromController();Jobs();
            games.RetroAchievementsGames.Add(target.Id);
            navigation.FetchAchievements=(_,_)=>Task.FromResult(new CubeShelf.Core.Achievements.GameAchievementProgress(1,2,
                [new(2,"Premier succès","Objectif terminé",10,true),new(1,"Deuxième succès","Objectif restant",5,false)]));
            achievements.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
            Check(!Field<Border>(mobile,"_gameOptionsOverlay").IsVisible
                &&Field<TextBlock>(navigation,"_heading").Text=="RetroAchievements"
                &&grid.Items.Cast<DualScreenItem>().Select(item=>item.Id).SequenceEqual(new[]{target.Id})
                &&preview.SelectedId==target.Id&&games.Native.Launched.Count==0,
                "RetroAchievements uses both HOME displays and shows only declared compatible games without launching");
            Check(Field<ProgressBar>(navigation,"_achievementBar").Value==.5,"the selected game's progress bar uses unlocked over total");
            mobile.ActivateFromController();Jobs();
            var details=Field<Border>(navigation,"_achievementDetails");
            var sections=details.GetVisualDescendants().OfType<TextBlock>().Select(text=>text.Text).ToArray();
            Check(details.IsVisible&&Array.IndexOf(sections,"Débloqués")>=0
                &&Array.IndexOf(sections,"Débloqués")<Array.IndexOf(sections,"À débloquer")
                &&sections.Contains("Objectif terminé")&&games.Native.Launched.Count==0,
                "confirm opens the detail page, with earned achievements and their objectives first");
            mobile.BackFromController();Jobs();
            Check(!details.IsVisible&&grid.IsEffectivelyVisible&&grid.ItemCount==1,"Back from details returns to the achievements library");
            Click(navigation,"_back");Jobs();
            Check(grid.ItemCount>1&&preview.SelectedId==target.Id&&navigation.SelectedId==target.Id,
                "the RetroAchievements back button restores the original shelf and selection");
            Check(preview.GetVisualDescendants().OfType<GameCaseControl>().Count()==1
                &&!navigation.GetVisualDescendants().OfType<GameCaseControl>().Any(),
                "two physical displays render only one selected 3D case");
            Check(!Field<DispatcherTimer>(navigation,"_musicDelay").IsEnabled,"the navigation display does not start another music timer");
            preview.StopMusic();games.PreviewMusic.Clear();Invoke(preview,"PlaySelectedTrack");
            Check(games.PreviewMusic.Count(path=>path is not null)==1,"the upper display is the sole preview-music owner");
            Click(navigation,"_appearance");Jobs();
            Check(preview.GetVisualDescendants().OfType<ThemeVariantScope>().Single().RequestedThemeVariant
                ==navigation.GetVisualDescendants().OfType<ThemeVariantScope>().Single().RequestedThemeVariant,
                "appearance changes on the touch display also update the upper display");
            Click(navigation,"_mute");Jobs();
            Check(!new LibraryPresentationStore(games.DataDirectory).MusicEnabled&&games.PreviewMusic.LastOrDefault() is null,
                "the bottom-screen mute control stops the upper display's music");
            Click(navigation,"_mute");Jobs();
            foreach(var (upper,lower) in new[]{(new Size(960,540),new Size(620,540)),(new Size(768,432),new Size(496,432)),(new Size(640,360),new Size(413,360))})
            {
                top.Width=upper.Width;top.Height=upper.Height;bottom.Width=lower.Width;bottom.Height=lower.Height;Jobs();
                Check(preview.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="HomePreview").IsEffectivelyVisible,
                    "the upper display retains its preview at logical size "+upper);
                Check(grid.Bounds.Height>=64,"the bottom display retains a usable grid row at logical size "+lower);
                Fits(navigation);
                Click(navigation,"_find");Jobs();Fits(navigation);
                Check(navigation.GetVisualDescendants().OfType<TextBox>().Single().IsEffectivelyVisible,
                    "search remains accessible on the lower screen at "+lower);
                Click(navigation,"_find");Jobs();
            }
            bottom.Width=538;bottom.Height=468;Jobs();
            var visibleTile=grid.GetVisualDescendants().OfType<HomeArtworkControl>().First(tile=>tile.IsSelected);
            Check(visibleTile.Bounds.Width>=82&&grid.Bounds.Height>=180,
                "the Thor's lower screen fits two rows of larger game covers");
            Fits(navigation);
            mobile.SetActive(false);Jobs();
            Check(!mobile.WantsSecondaryScreen&&!Field<DispatcherTimer>(preview,"_musicDelay").IsEnabled
                &&!Field<DispatcherTimer>(navigation,"_musicDelay").IsEnabled&&games.PreviewMusic.LastOrDefault() is null,
                "backgrounding the launcher stops both displays' playback and secondary-display demand");
            mobile.SetSecondaryScreenAttached(false);Jobs();
            Check(preview.ScreenRole==HomeScreenRole.Combined&&!navigation.IsVisible,
                "losing the secondary display restores the complete single-screen HOME interface");
            mobile.SetActive(true);mobile.SetSecondaryScreenAttached(true);Jobs();
            Check(mobile.WantsSecondaryScreen&&preview.SelectedId==navigation.SelectedId,
                "resuming and reconnecting keeps a single consistent selection");
            var retired=navigation;
            bottom.Content=null;mobile.ReleaseSecondaryHome();bottom.Close();
            navigation=(DualScreenLibraryView)mobile.CreateSecondaryHome();
            bottom=new Window{Content=navigation,Width=620,Height=540};bottom.Show();mobile.SetSecondaryScreenAttached(true);Jobs();
            Check(!ReferenceEquals(retired,navigation)&&preview.SelectedId==navigation.SelectedId,
                "a recreated native window gets a fresh navigation tree and retains the current selection");
            mobile.OptionsFromController();Jobs();
            var options=Field<Border>(mobile,"_gameOptionsOverlay");var selectedGame=preview.SelectedId;
            Check(options.IsVisible&&mobile.WantsSecondaryScreen&&!navigation.IsEnabled&&options.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==target.Title),
                "controller X opens options for the selected game and keeps the bottom screen up but inert");
            var optionRows=Field<List<Button>>(mobile,"_gameOptionButtons");
            var selectedBrush=optionRows[0].BorderBrush;
            Check(!Equals(selectedBrush,optionRows[1].BorderBrush),"X highlights the first action before the next input");
            mobile.BrowseFromController(1);Jobs();
            Check(preview.SelectedId==selectedGame&&games.Native.Launched.Count==0,"moving within game options never changes or launches the selected game");
            Check(!Equals(selectedBrush,optionRows[0].BorderBrush)&&Equals(selectedBrush,optionRows[1].BorderBrush),
                "the menu highlight follows controller input across displays");
            var closeOption=options.GetVisualDescendants().OfType<Button>().Single(b=>Avalonia.Automation.AutomationProperties.GetName(b)=="Fermer");
            Check(closeOption.MinHeight>=48&&!closeOption.GetVisualAncestors().OfType<ScrollViewer>().Any(),
                "the X menu keeps its close button outside the scrolling list with a full touch target");
            mobile.BackFromController();Jobs();
            Check(!options.IsVisible&&mobile.WantsSecondaryScreen&&navigation.IsEnabled,"B returns from game options to HOME without losing the selection");
            mobile.OptionsFromController();mobile.ActivateFromController();Jobs();
            Check(!options.IsVisible&&!preview.IsVisible,"confirming game parameters opens the existing selected-game details");
            mobile.BackFromController();Jobs();
            Check(preview.IsVisible&&preview.SelectedId==selectedGame,"Back from game details restores the same HOME selection");
            mobile.OptionsFromController();mobile.OptionsFromController();Jobs();
            Check(!options.IsVisible,"pressing X again closes the game options");
            target=ports.Single(port=>port.Id==navigation.SelectedId);games.Native.Installed.Add(target.Id);mobile.RefreshAvailability();
            games.Native.OnLaunch=()=>mobile.SetActive(false);
            navigation.Move(0,0);mobile.ActivateFromController();Jobs();
            Check(!mobile.WantsSecondaryScreen,"the secondary launcher surface is released during the game opening animation");
            mobile.SetSecondaryScreenAttached(false);
            Invoke(Field<GameLaunchTransition>(mobile,"_launchTransition"),"Advance",2.2);
            for(var i=0;i<100&&games.Native.Launched.Count==0;i++){Jobs();Thread.Sleep(5);}
            Check(games.Native.Launched.SequenceEqual(new[]{target.Id})&&games.Native.Prepared.Count==0,
                "confirmation from the bottom screen launches exactly the selected native game");
            mobile.SetActive(true);mobile.SetSecondaryScreenAttached(true);Jobs();mobile.BackFromController();Jobs();
            Check(preview.IsPlatforms&&navigation.IsPlatforms&&preview.SelectedId=="n64"&&navigation.SelectedId=="n64",
                "Back returns both displays to the console just browsed rather than the first console");
            navigation.Move(-999,0);Jobs();
            Check(navigation.SelectedId==DualScreenLibraryView.ResumePrefix+target.Id&&preview.SelectedId==navigation.SelectedId,
                "the last launched game leads HOME as a Continue tile on both displays");
            mobile.ShowDownloads();Jobs();
            Check(mobile.WantsSecondaryScreen&&!navigation.IsEnabled&&mobile.GetVisualDescendants().OfType<ComboBox>().Single(picker=>picker.Name=="LibraryStylePicker").SelectedIndex==1,
                "settings keep the bottom screen up but inert, and the theme picker shows HOME");
            mobile.SetSecondaryScreenAttached(false);
            mobile.GetVisualDescendants().OfType<ComboBox>().Single(picker=>picker.Name=="LibraryStylePicker").SelectedIndex=0;Jobs();
            Check(!mobile.WantsSecondaryScreen&&!preview.IsVisible&&!new LibraryPresentationStore(games.DataDirectory).IsDual,
                "switching to Classic restores a single-screen launcher and persists the choice");
            mobile.ActivateFromController();mobile.OptionsFromController();Jobs();
            Check(Field<Border>(mobile,"_gameOptionsOverlay").IsVisible,"controller X also opens game options in the Classic catalogue");
            mobile.BackFromController();Jobs();
            mobile.PreferHomeForDualScreen();
            Check(!new LibraryPresentationStore(games.DataDirectory).IsDual,"an explicit Classic choice is never overridden by a second screen");
            var freshGames=new FakeGames(Path.Combine(root,"thor-first-start"),ports);var fresh=new MobileLibraryView(freshGames);
            var freshWindow=new Window{Content=fresh,Width=960,Height=540};freshWindow.Show();Jobs();
            Check(!Field<DualScreenLibraryView>(fresh,"_dualLibrary").IsVisible,"a single-screen first start keeps the Classic theme");
            fresh.PreferHomeForDualScreen();Jobs();
            Check(Field<DualScreenLibraryView>(fresh,"_dualLibrary").IsVisible&&Field<DualScreenLibraryView>(fresh,"_dualLibrary").IsPlatforms
                &&new LibraryPresentationStore(freshGames.DataDirectory).HasStyleChoice,
                "a dual-screen device without a theme choice opens HOME on the consoles and remembers it");
            freshWindow.Close();
        }
        finally{mobile.SetSecondaryScreenAttached(false);bottom.Content=null;bottom.Close();top.Close();}
    }
    private sealed class UpdateCatalogHttp(bool small=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            const string name="CubeShelf-Android-arm64-preview.apk";
            const string fixture="APK fixture";
            var tag=small?"v99.0.1":"v99.0.0";
            var release="https://github.com/zeranemesis/CubeShelf-Launcher/releases/download/"+tag+"/";
            var hash=small?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("APK fixture"u8)):new string('a',64);
            var body=request.RequestUri!.Host=="api.github.com"
                ?System.Text.Json.JsonSerializer.Serialize(new {draft=false,prerelease=false,tag_name=tag,
                    assets=new[]{new{name,browser_download_url=release+name,size=small?fixture.Length:286_510_909,digest="sha256:"+hash}}})
                :request.RequestUri.AbsolutePath.EndsWith("/checksums.txt",StringComparison.Ordinal)?hash+"  "+name:fixture;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {RequestMessage=request,Content=new StringContent(body)});
        }
    }

    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Invoke(object instance,string method,params object[] arguments)=>instance.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,arguments);
    private static void Click(DualScreenLibraryView view,string field)=>Field<Button>(view,field).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
    private static void Fits(DualScreenLibraryView view)
    {
        foreach(var button in view.GetVisualDescendants().OfType<Button>().Where(button=>button.IsEffectivelyVisible))
        {
            var at=button.TranslatePoint(default,view)!.Value;
            Check(button.Bounds.Height>=48&&at.X>=0&&at.Y>=0&&at.X+button.Bounds.Width<=view.Bounds.Width+1&&at.Y+button.Bounds.Height<=view.Bounds.Height+1,
                "lower-screen touch actions stay at least 48 pixels high and inside "+view.Bounds.Size+": "+button.Content);
        }
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
}
