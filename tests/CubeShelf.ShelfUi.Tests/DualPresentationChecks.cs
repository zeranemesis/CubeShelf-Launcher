using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

internal static class DualPresentationChecks
{
    internal static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string text){if(!good)throw new Exception(text);Console.WriteLine("OK "+text);}
        void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
        var titleLogo=new GameTitleControl();
        using(var logoStream=Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf/Assets/Logos/catalog.json")))
        using(var logos=System.Text.Json.JsonDocument.Parse(logoStream))
        {
            var count=0;
            foreach(var logo in logos.RootElement.EnumerateArray())
            {
                titleLogo.SetGame("database-check",logo.GetProperty("Title").GetString()!,logo.GetProperty("Console").GetString());
                if(!titleLogo.HasArtwork)throw new Exception("Missing or undecodable bundled logo: "+logo.GetProperty("Title").GetString());
                var bitmap=(Avalonia.Media.Imaging.Bitmap)((Grid)titleLogo.Content!).Children.OfType<Image>().Single().Source!;
                if(bitmap.PixelSize.Width>640||bitmap.PixelSize.Height>260)throw new Exception("Unbounded logo decode");
                count++;
            }
            Check(count>=550,$"all {count} database logos decode offline with bounded bitmap sizes");
        }
        titleLogo.SetGame("n64-oot","The Legend of Zelda: Ocarina of Time","n64");var n64Logo=((Grid)titleLogo.Content!).Children.OfType<Image>().Single().Source;
        titleLogo.SetGame("3ds-oot","Ocarina of Time 3D","3ds");
        Check(titleLogo.HasArtwork&&!ReferenceEquals(n64Logo,((Grid)titleLogo.Content!).Children.OfType<Image>().Single().Source),"Zelda N64 and its 3DS remake resolve distinct console artwork");
        titleLogo.SetGame(null,"Aucun jeu trouvé");
        var sidebar=window.FindControl<Border>("DesktopSidebar")!;
        if(OperatingSystem.IsWindows())
        {
            var silence=Path.Combine(Path.GetTempPath(),"decompshelf-preview-"+Guid.NewGuid().ToString("N")+".wav");
            try
            {
                using(var writer=new BinaryWriter(File.Create(silence)))
                {writer.Write("RIFF"u8);writer.Write(1636);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(1600);writer.Write(new byte[1600]);}
                Exception? audioError=null;var audioThread=new Thread(()=>{try{using var player=new PreviewMusicPlayer();player.Play(silence);player.Play(null);}catch(Exception e){audioError=e;}}){IsBackground=true};
                audioThread.SetApartmentState(ApartmentState.STA);audioThread.Start();if(!audioThread.Join(TimeSpan.FromSeconds(5)))throw new Exception("Windows audio preview timed out.");if(audioError is not null)throw audioError;
                Check(true,"Windows native audio opens and stops a silent PCM preview on the app's STA thread model");
            }
            finally{File.Delete(silence);}
        }
        if(!sidebar.IsVisible)window.RaiseEvent(new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.F11});
        window.FindControl<ComboBox>("ConsoleShelfPicker")!.SelectedIndex=0;
        var picker=window.FindControl<ComboBox>("LibraryStylePicker")!;
        Check(picker.ItemCount==2,"Windows exposes two library themes");picker.SelectedIndex=1;Jobs();
        var dual=window.FindControl<DualScreenLibraryView>("DualScreenView")!;
        Check(dual.IsEffectivelyVisible&&dual.IsPlatforms&&sidebar.IsVisible,"the second Windows theme preserves the left menus and console choice");
        var catalog=(IEnumerable<CubeShelf.Core.Library.GameCatalogEntry>)typeof(MainWindow).GetField("_catalogGames",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        var played=catalog.First(g=>g.ShelfConsole=="n64");played.LastPlayedAt=DateTimeOffset.Now;
        window.FindControl<ComboBox>("ConsoleShelfPicker")!.SelectedIndex=1;picker.SelectedIndex=0;picker.SelectedIndex=1;Jobs();
        var homeItems=dual.GetVisualDescendants().OfType<ListBox>().Single().Items.Cast<DualScreenItem>().ToArray();
        Check(homeItems[0].Id==DualScreenLibraryView.ResumePrefix+played.Id&&homeItems[0].Title==played.Title&&dual.SelectedId!=homeItems[0].Id,
            "the last played game leads HOME as a Continue tile without stealing the current console selection");
        if(snapshots is not null){dual.Move(-999,0);Jobs();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-desktop-continue-light.png"));}
        played.LastPlayedAt=null;window.FindControl<ComboBox>("ConsoleShelfPicker")!.SelectedIndex=0;picker.SelectedIndex=0;picker.SelectedIndex=1;Jobs();
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-desktop-consoles-light.png"));}
        dual.Activate();Jobs();Check(dual.IsVisible&&!dual.IsPlatforms,"a console opens its native PC port grid");
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-desktop-games-light.png"));}
        window.Width=1920;window.Height=1080;
        window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        Check(dual.IsVisible&&dual.IsPlatforms&&!sidebar.IsVisible,"Big Picture uses the same optional dual theme");
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-bigpicture-consoles-light.png"));}
        window.HandleBigPictureControllerInput(5);Jobs();Check(!dual.IsPlatforms,"Big Picture controller confirmation opens the game grid");
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-bigpicture-games-light.png"));}
        var customize=dual.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Personnaliser");customize.Flyout!.ShowAt(customize);Jobs();
        var menu=(Control)((Flyout)customize.Flyout).Content!;var appearance=menu.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Affichage :",StringComparison.Ordinal)==true);
        appearance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();window.HandleBigPictureControllerInput(6);Jobs();
        Check(!dual.IsPopupOpen&&!dual.IsPlatforms,"controller Back closes appearance settings before leaving the game grid");
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-bigpicture-games-dark.png"));}
        var selected=dual.SelectedId;window.HandleBigPictureControllerInput(12);Jobs();
        Check(dual.SelectedId is not null,"Big Picture can move within the new game grid");
        window.HandleBigPictureControllerInput(6);Jobs();Check(dual.IsPlatforms,"controller Back returns to consoles in the second theme");
        if(snapshots is not null){using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-bigpicture-consoles-dark.png"));}
        window.RaiseEvent(new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.F11});Jobs();
        picker.SelectedIndex=0;Jobs();
        Check(!dual.IsVisible&&window.FindControl<PlatformHomeControl>("PlatformHome")!.IsVisible,"Classic restores the existing console interface");
    }
}
