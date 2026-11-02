using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

static class CoverLayoutChecks
{
    public static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string message) { if(!good)throw new Exception(message); }
        using var sources=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs/covers.json")));
        var samples=new[]{("snes","Super Mario World"),("snes","Dragon Ball Z: Super Saiya Densetsu"),
            ("ps1","Crash Bandicoot"),("n64","Banjo-Kazooie"),("n64","Virtual Pro Wrestling 2"),
            ("switch","Unicorn Overlord"),("nds","Kingdom Hearts 358/2 Days"),("nds","Pokémon SoulSilver"),
            ("ps4","MediEvil"),("virtualboy","Zero Racers"),("xbox","StarCraft: Ghost"),("arcade","Vampire Savior 2")};
        foreach(var (console,title) in samples)
        {
            var record=sources.RootElement.EnumerateArray().Single(p=>p.GetProperty("Console").GetString()==console && p.GetProperty("Title").GetString()==title);
            var front=Path.Combine(AppContext.BaseDirectory,record.GetProperty("Front").GetString()!);
            using var bitmap=new Bitmap(front);
            var format=GameCaseControl.FormatForConsole(console);
            var cover=new GameCoverControl { Cover=bitmap,Format=format };
            cover.Measure(new Size(232,260));
            Check(cover.DesiredSize.Width<=232.01 && cover.DesiredSize.Height<=260.01,"Cover exceeds its thumbnail well: "+title);
            var printed=format is GameCaseFormat.OriginalBox or GameCaseFormat.Nintendo64;
            var horizontalInset=printed?6:format==GameCaseFormat.Switch?14:16;
            var verticalInset=printed?8:14;
            // Layout rounds to device pixels; painting still uniformly fits the complete image.
            Check(Math.Abs((cover.DesiredSize.Width-horizontalInset)/(cover.DesiredSize.Height-verticalInset)
                -(double)bitmap.PixelSize.Width/bitmap.PixelSize.Height)<.012,"Thumbnail changed the printed cover aspect: "+title+" measured="+cover.DesiredSize+" source="+bitmap.PixelSize);
            cover.Measure(new Size(0,0));
            Check(cover.DesiredSize==default,"Empty layout constraints must not create an overflowing cover.");
            if(!printed)continue;
            using var box=new GameCaseControl { CaseFormat=format };
            box.SetCover(front,"","");box.SetShelfPose(14,true,animate:false);
            box.Measure(new Size(420,520));box.Arrange(new Rect(0,0,420,520));
            Check(Math.Abs(box.CaseAspectRatio-(double)bitmap.PixelSize.Width/bitmap.PixelSize.Height)<.003,
                "3D printed box changed the source aspect: "+title);
            foreach(var yaw in new[]{-38d,14d,38d})
            foreach(var pitch in new[]{-35d,0d,35d})
            {
                box.SetShelfPose(yaw,true,animate:false);
                typeof(GameCaseControl).GetField("_pitch",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(box,pitch);
                var vertices=(Array)typeof(GameCaseControl).GetMethod("BuildProjectedVertices",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(box,null)!;
                foreach(var vertex in vertices)
                {
                    var point=(Point)vertex!.GetType().GetProperty("Screen")!.GetValue(vertex)!;
                    Check(point.X>=0 && point.X<=420 && point.Y>=0 && point.Y<=520,"The complete 3D box does not fit its viewport: "+title);
                }
            }
        }
        if(snapshots is not null)
        {
            window.FindControl<Button>("BigPictureEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<PlatformHomeControl>("PlatformHome")!.Choose();
            try
            {
                var picker=window.FindControl<ComboBox>("BigPictureConsoleShelfPicker")!;
                foreach(var (console,title) in samples.Where(sample=>sample.Item1 is "snes" or "ps1" or "switch" or "ps4" or "virtualboy" or "xbox" or "arcade"))
                {
                    picker.SelectedIndex=picker.Items.OfType<ComboBoxItem>().Select((item,index)=>(item,index)).Single(p=>p.item.Tag?.ToString()==console).index;
                    window.FindControl<ShelfTransitionControl>("ShelfTransition")!.Stop();
                    var games=(GameCatalogEntry[])typeof(MainWindow).GetField("_bigPictureGames",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                    var index=Array.FindIndex(games,game=>game.Title==title);
                    Check(index>=0,"Cover-layout sample disappeared from its catalog: "+title);
                    typeof(MainWindow).GetMethod("SelectBigPictureGame",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[index]);
                    window.FindControl<GameShelfControl>("BigPictureShelf")!.SetSelection(index,false,animate:false);
                    Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                    using var frame=window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(snapshots,$"cover-fit-{console}-{(title.StartsWith("Dragon")?"portrait":"front")}.png"));
                }
            }
            finally { window.FindControl<Button>("BigPictureExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        }
        Console.WriteLine("OK  portrait, square and landscape covers fit bounded thumbnails; printed 3D boxes preserve source ratios and remain wholly in view.");
    }
}
