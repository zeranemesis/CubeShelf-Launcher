using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;
using CubeShelf.Mobile;

internal static class FoundGamesTests
{
    internal static void Run(string root,MobileNativePort[] ports)
    {
        var games=new FakeGames(Path.Combine(root,"found-games"),ports);new LibraryPresentationStore(games.DataDirectory).SetStyle(true);
        var mobile=new MobileLibraryView(games);var host=new Window{Content=mobile,Width=390,Height=844};host.Show();Jobs();
        var dual=mobile.GetVisualDescendants().OfType<DualScreenLibraryView>().Single();
        Check(dual.IsPlatforms&&HintShown(dual),"the first start shows the « Chercher mes jeux » hint on the HOME console grid");
        mobile.ShowDownloads();Jobs();
        var scan=mobile.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ScanGamesButton");
        Check(scan.IsEffectivelyVisible&&scan.Content as string=="Chercher mes jeux…"&&scan.Bounds.Height>=48,"the settings offer a 48-pixel « Chercher mes jeux… » button");
        var n64=ports.Where(p=>p.ShelfConsole=="n64").Select(p=>p.Id).ToArray();var last=ports.Single(p=>p.Id==n64[^1]);
        var rom=new byte[0x400];rom[0]=0x80;rom[1]=0x37;rom[2]=0x12;rom[3]=0x40;Encoding.ASCII.GetBytes("SUPER MARIO 64".PadRight(20)).CopyTo(rom,0x20);
        var scanning=mobile.ScanGamesAsync(Files(
            new GameFileMatcher.ScannedFile("sm64.z64","content://test/sm64.z64",()=>Task.FromResult<Stream>(new MemoryStream(rom))),
            new GameFileMatcher.ScannedFile(last.Title.Replace(":"," -")+" (USA).z64","content://test/last.z64",()=>Task.FromResult<Stream>(new MemoryStream())),
            new GameFileMatcher.ScannedFile("notes.txt","content://test/notes.txt",()=>throw new InvalidOperationException("not a game"))));
        for(var i=0;i<200&&!scanning.IsCompleted;i++)Jobs();
        var found=new FoundGamesStore(games.DataDirectory).Load();
        Check(scanning.IsCompletedSuccessfully&&found["n64-sm64"]=="content://test/sm64.z64"&&found.ContainsKey(last.Id),"a scanned N64 header and a No-Intro file name are recorded in found-games.json");
        Check(mobile.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==$"2 jeux reconnus : Super Mario 64, {last.Title}"),"the settings list the recognised games");
        mobile.BackFromController();Jobs();
        Check(dual.IsPlatforms&&!HintShown(dual),"the hint disappears once a scan has run");
        dual.SelectItem("n64");dual.Activate();Jobs();
        var expected=n64.Where(found.ContainsKey).Concat(n64.Where(id=>!found.ContainsKey(id))).ToArray();
        var tiles=Tiles(dual);
        Check(!dual.IsPlatforms&&tiles.Length>2&&tiles.SequenceEqual(expected.Take(tiles.Length))&&tiles[1]==last.Id,"found games lead the HOME game grid, the rest keeps catalogue order");
        dual.Move(-99,0);dual.Move(0,1);Jobs();
        Check(dual.SelectedId==last.Id&&dual.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.StartsWith("Fichier trouvé · ",StringComparison.Ordinal)==true),"a found game's status starts with « Fichier trouvé »");
        host.Close();
        var restarted=new MobileLibraryView(games);host=new Window{Content=restarted,Width=390,Height=844};host.Show();Jobs();
        dual=restarted.GetVisualDescendants().OfType<DualScreenLibraryView>().Single();
        if(dual.IsPlatforms){dual.SelectItem("n64");dual.Activate();Jobs();}
        Check(!HintShown(dual)&&Tiles(dual).SequenceEqual(tiles),"found games keep leading the grid after a restart");
        host.Close();
    }
    private static async IAsyncEnumerable<GameFileMatcher.ScannedFile> Files(params GameFileMatcher.ScannedFile[] files){foreach(var file in files)yield return file;await Task.CompletedTask;}
    private static bool HintShown(DualScreenLibraryView view)=>view.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.StartsWith("Astuce : Réglages → Chercher mes jeux",StringComparison.Ordinal)==true&&t.IsEffectivelyVisible);
    private static string[] Tiles(DualScreenLibraryView view)=>view.GetVisualDescendants().OfType<ListBox>().Single().GetVisualDescendants().OfType<HomeArtworkControl>().Select(t=>(string)t.Tag!).ToArray();
    private static void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
    private static void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("OK "+message);}
}
