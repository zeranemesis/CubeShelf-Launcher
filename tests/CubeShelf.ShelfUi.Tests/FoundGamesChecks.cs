using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

internal static class FoundGamesChecks
{
    internal static void Run(MainWindow window,string? snapshots)
    {
        void Check(bool good,string text){if(!good)throw new Exception(text);Console.WriteLine("OK "+text);}
        void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
        T Wait<T>(Task<T> task){using var stop=new CancellationTokenSource();task.ContinueWith(_=>stop.Cancel(),TaskScheduler.Default);Dispatcher.UIThread.MainLoop(stop.Token);return task.GetAwaiter().GetResult();}
        static async IAsyncEnumerable<GameFileMatcher.ScannedFile> Files(string directory)
        {foreach(var f in Directory.EnumerateFiles(directory)){await Task.Yield();yield return new(Path.GetFileName(f),f,()=>Task.FromResult<Stream>(File.OpenRead(f)));}}
        var paths=new CubeShelf.Core.Platform.PlatformPaths();
        var folder=Path.Combine(Path.GetTempPath(),"decompshelf-roms-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var rom=new byte[0x400];rom[0]=0x80;rom[1]=0x37;rom[2]=0x12;rom[3]=0x40;System.Text.Encoding.ASCII.GetBytes("SUPER MARIO 64".PadRight(20)).CopyTo(rom,0x20);
            File.WriteAllBytes(Path.Combine(folder,"sm64.z64"),rom);File.WriteAllText(Path.Combine(folder,"notes.txt"),"not a game");
            var statuses=new List<string>();
            var storage=Wait(window.StorageProvider.TryGetFolderFromPathAsync(new Uri(folder)));
            // Headless has no real storage provider: feed the same files through the internal overload instead.
            var summary=Wait(storage is not null?window.ScanGameFolderAsync(storage,statuses.Add)
                :window.ScanGameFilesAsync(Files(folder),statuses.Add));
            Console.WriteLine($"   scan through {(storage is not null?"the storage provider":"the internal overload")}: {summary}");
            var found=new FoundGamesStore(paths.DataDirectory).Load();
            Check(found.Count>0&&found.All(f=>f.Value.EndsWith("sm64.z64",StringComparison.Ordinal))&&summary.Contains("Super Mario 64")&&statuses.Count>0,
                "a fake N64 ROM is recognised as Super Mario 64 and kept in found-games.json; notes.txt is ignored");
            window.FindControl<ComboBox>("ConsoleShelfPicker")!.SelectedIndex=3;
            var picker=window.FindControl<ComboBox>("LibraryStylePicker")!;picker.SelectedIndex=0;picker.SelectedIndex=1;Jobs();
            var dual=window.FindControl<DualScreenLibraryView>("DualScreenView")!;dual.Activate();Jobs();
            var items=dual.GetVisualDescendants().OfType<ListBox>().Single().Items.Cast<DualScreenItem>().ToArray();
            var mario=Array.FindIndex(items,i=>i.Title=="Super Mario 64");var firstToInstall=Array.FindIndex(items,i=>i.Status=="À installer");
            Check(!dual.IsPlatforms&&mario>=0&&items[mario].Status=="Fichier trouvé"&&firstToInstall>mario,
                "HOME lists the found game as « Fichier trouvé » before the catalogue games still to install");
            if(snapshots is not null){Jobs();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"home-found-games.png"));}
            picker.SelectedIndex=0;Jobs();
            var store=new CubeShelf.Core.Platform.UserPreferencesStore(paths);var preferences=new CubeShelf.Core.Platform.UserPreferences(Language:"fr");
            var wizard=new FirstRunWizardWindow(paths,store,preferences,scanGames:(_,_)=>Task.FromResult(""));
            typeof(FirstRunWizardWindow).GetMethod("ShowPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(wizard,[5]);
            var texts=wizard.GetLogicalDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();var buttons=wizard.GetLogicalDescendants().OfType<Button>().Select(b=>b.Content as string).ToArray();
            Check(texts.Contains("DECOMPSHELF")&&texts.Contains("Tes jeux")&&buttons.Contains("Choisir un dossier de jeux…")&&buttons.Contains("Terminer"),
                "the wizard ends on « Tes jeux » with a folder button and a « Terminer » button");
            var plain=new FirstRunWizardWindow(paths,store,preferences);
            typeof(FirstRunWizardWindow).GetMethod("ShowPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(plain,[5]);
            Check(!plain.GetLogicalDescendants().OfType<Button>().Any(b=>b.Content as string=="Choisir un dossier de jeux…"),"without a scanner the wizard hides the folder button");
        }
        finally
        {
            Directory.Delete(folder,true);File.Delete(Path.Combine(paths.DataDirectory,"found-games.json"));
        }
    }
}
