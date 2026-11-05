using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Storage;
using CubeShelf.Desktop.Controls;

internal static class GameStorageUiTests
{
    internal static void Run(string root,CubeShelf.Mobile.MobileNativePort[] ports)
    {
        var directory=Path.Combine(root,"storage-ui");var saves=Path.Combine(directory,"game/saves");Directory.CreateDirectory(saves);File.WriteAllBytes(Path.Combine(saves,"progress.sav"),new byte[8192]);
        var game=new GameStorageEntry{Id="ui-test",GameKey="n64:UITEST",Title="The Legend of Zelda: Ocarina of Time",OwnedRoot=directory,DataDirectory=Path.Combine(directory,"game"),RemovableDirectories=[Path.Combine(directory,"game")]};
        var large=game with{Id="large",GameKey="gc:LARGE",Title="Mario Party 4",DataDirectory=Path.Combine(directory,"large"),RemovableDirectories=[Path.Combine(directory,"large")]};
        var small=game with{Id="small",GameKey="n64:SMALL",Title="Super Mario 64",DataDirectory=Path.Combine(directory,"small")};
        Directory.CreateDirectory(large.DataDirectory!);File.WriteAllBytes(Path.Combine(large.DataDirectory!,"assets.bin"),new byte[12582912]);
        Directory.CreateDirectory(small.DataDirectory!);File.WriteAllBytes(Path.Combine(small.DataDirectory!,"assets.bin"),new byte[1048576]);
        var service=new GameStorageService(Path.Combine(directory,"settings"),[game,large,small],()=>false);
        string Artwork(string id,string relative)
        {
            var target=Path.Combine(directory,id+Path.GetExtension(relative));using var source=global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/"+relative));using var output=File.Create(target);source.CopyTo(output);return target;
        }
        var artwork=new Dictionary<string,string>{{game.Id,Artwork(game.Id,ports.Single(p=>p.Id=="n64-oot").Front)},
            {large.Id,Artwork(large.Id,"Assets/Covers/MarioParty4/pal_front.png")},{small.Id,Artwork(small.Id,ports.Single(p=>p.Id=="n64-sm64").Front)}};
        var view=new GameStorageView(service,coverPath:id=>artwork.GetValueOrDefault(id));var window=new Window{Content=view,Width=390,Height=844,Background=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B101B"))};window.Show();Jobs();
        var inventory=view.GetVisualDescendants().OfType<ListBox>().Single();
        for(var i=0;i<1000&&inventory.ItemCount!=3;i++){Jobs();Thread.Sleep(5);}Jobs();
        Check(inventory.ItemCount==3&&inventory.Items[0] is GameStorageItem {Game.Id:"large"},"storage opens on the largest installed games");
        var covers=view.GetVisualDescendants().OfType<Image>().Where(i=>i.Name=="StorageGameCover").ToArray();
        Check(covers.Length==3&&covers.All(i=>i.Source is global::Avalonia.Media.Imaging.Bitmap&&i.Stretch==global::Avalonia.Media.Stretch.Uniform),"storage rows show all three real catalogue covers without cropping");
        foreach(var width in new[]{320,390,900})
        {
            window.Width=width;Jobs();
            if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
            {using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"storage-inventory-{width}.png"));}
        }
        window.Width=390;Jobs();inventory.SelectedItem=inventory.Items.Cast<GameStorageItem>().Single(i=>i.Game.Id==game.Id);inventory.ScrollIntoView(inventory.SelectedItem!);Jobs();
        var selected=inventory.ContainerFromItem(inventory.SelectedItem!)!;var point=selected.TranslatePoint(new Point(selected.Bounds.Width/2,selected.Bounds.Height/2),window)!.Value;
        window.MouseDown(point,Avalonia.Input.MouseButton.Left);window.MouseUp(point,Avalonia.Input.MouseButton.Left);Jobs();
        Check(view.GetVisualDescendants().OfType<ComboBox>().First().IsEffectivelyVisible,"clicking a game's storage row opens its detailed cleanup actions");
        foreach(var size in new[]{new Size(320,700),new Size(390,844),new Size(900,740)})
        {
            window.Width=size.Width;window.Height=size.Height;Jobs();
            foreach(var combo in view.GetVisualDescendants().OfType<ComboBox>().Where(c=>c.IsVisible))
                Check(combo.Bounds.Width<=size.Width-20,"storage selectors fit the current viewport");
            var usage=view.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="StorageUsageCard");
            var progress=view.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="StorageSavesCard");
            Check(size.Width>=760?progress.Bounds.X>usage.Bounds.Right:progress.Bounds.Y>=usage.Bounds.Bottom,"storage sections reflow between phone and PC");
            var scroll=view.GetVisualDescendants().OfType<ScrollViewer>().First();scroll.ScrollToEnd();Jobs();
            var back=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Retour");
            var backPoint=back.TranslatePoint(new Point(0,0),window)!.Value;
            Check(backPoint.Y>=0&&backPoint.Y+back.Bounds.Height<=size.Height,"Back remains visible while scrolling storage actions");
            scroll.ScrollToHome();Jobs();
            if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
            {Directory.CreateDirectory(folder);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"storage-{size.Width}.png"));}
        }
        var create=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Créer une copie locale");
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var i=0;i<1000&&view.IsBusy;i++){Jobs();Thread.Sleep(5);}Jobs();
        Check(!view.IsBusy&&service.Backups(game).Count==1,"the shared PC/mobile storage view creates a real local save snapshot");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } snapshots)
        {
            window.Width=900;window.Height=740;window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;window.Background=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F3F4FA"));Jobs();
            using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(snapshots,"storage-desktop-light.png"));
            window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;window.Background=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B101B"));window.Width=390;window.Height=844;Jobs();
            var restore=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Restaurer cette copie");restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
            using(var frame=window.CaptureRenderedFrame())frame!.Save(Path.Combine(snapshots,"storage-confirm-phone.png"));
        }
        var uninstall=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Supprimer l’installation");uninstall.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        var keep=view.GetVisualDescendants().OfType<RadioButton>().Single(r=>r.Name=="StorageKeepSaves");var delete=view.GetVisualDescendants().OfType<RadioButton>().Single(r=>r.Name=="StorageDeleteSaves");
        Check(keep.IsChecked==true&&delete.IsChecked!=true,"uninstall explicitly asks about progress with preservation selected by default");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } choicesFolder)
        {using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(choicesFolder,"storage-save-choice-keep-phone.png"));}
        delete.Focus();view.ActivateFromController();Jobs();Check(delete.IsChecked==true&&keep.IsChecked==false,"controller can explicitly choose to delete the game's progress");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } deletionChoiceFolder)
        {using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(deletionChoiceFolder,"storage-save-choice-delete-phone.png"));}
        view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Annuler").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();Check(File.Exists(Path.Combine(saves,"progress.sav")),"cancelling the delete-progress choice leaves the real fixture save untouched");
        var gamePicker=view.GetVisualDescendants().OfType<ComboBox>().First();gamePicker.SelectedItem=large;Jobs();uninstall.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        var confirm=view.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Confirmer");
        Check(!confirm.IsEnabled,"removing an installation with unknown saves requires acknowledgement");
        var acknowledgement=view.GetVisualDescendants().OfType<CheckBox>().Single();acknowledgement.Focus();view.ActivateFromController();Jobs();Check(confirm.IsEnabled,"controller activation acknowledges the save-location check");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } deletionFolder)
        {using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(deletionFolder,"storage-remove-phone.png"));}
        var closed=false;view.CloseRequested+=()=>closed=true;view.BackFromController();Jobs();Check(!closed&&inventory.IsEffectivelyVisible,"Back returns from game details to the storage inventory");
        for(var i=0;i<1000&&view.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Analyse de l’espace occupé…");i++){Jobs();Thread.Sleep(5);}Jobs();
        Check(!view.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.StartsWith("Mesure impossible",StringComparison.Ordinal)==true),"returning to the inventory finishes its asynchronous refresh without an error");
        view.MoveFromController(1);view.ActivateFromController();Jobs();Check(!inventory.IsEffectivelyVisible,"controller selection opens a game from the storage inventory");view.BackFromController();Jobs();
        view.BackFromController();Check(closed,"controller Back closes the inventory");
        window.Close();
        var games=new FakeGames(root,ports){Storage=service};var mobile=new CubeShelf.Mobile.MobileLibraryView(games);var hostWindow=new Window{Width=390,Height=844,Content=mobile};hostWindow.Show();Jobs();mobile.ActivateFromController();Jobs();mobile.ShowDownloads();Jobs();
        T Field<T>(string name)=>(T)typeof(CubeShelf.Mobile.MobileLibraryView).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(mobile)!;
        var storageButton=Field<Button>("_storageButton");
        var storagePoint=storageButton.TranslatePoint(new Point(storageButton.Bounds.Width/2,storageButton.Bounds.Height/2),hostWindow)!.Value;
        hostWindow.MouseDown(storagePoint,Avalonia.Input.MouseButton.Left);hostWindow.MouseUp(storagePoint,Avalonia.Input.MouseButton.Left);Jobs();
        Check(Field<Border>("_storageOverlay").IsEffectivelyVisible,"mobile downloads opens the same storage and save manager");
        mobile.SetActive(false);mobile.SetActive(true);Jobs();
        Check(Field<Border>("_storageOverlay").IsVisible,"Android file-picker pause/resume preserves the storage view");
        mobile.BackFromController();Jobs();Check(!Field<Border>("_storageOverlay").IsVisible&&Field<Grid>("_downloadsOverlay").IsVisible,"controller Back returns from storage to downloads");hostWindow.Close();
    }
    private static void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
}
