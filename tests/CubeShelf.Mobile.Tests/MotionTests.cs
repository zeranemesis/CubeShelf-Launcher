using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;

internal static class MotionTests
{
    internal static void Run(string root,string cover)
    {
        var view=new DualScreenLibraryView();
        view.Configure(new LibraryPresentationStore(Path.Combine(root,"motion")),null);
        var window=new Window{Width=900,Height=800,Content=view};
        window.Show();
        view.SetItems([new("first","First","n64",cover,"Ready"),new("below","Below","n64",cover,"Ready"),
            new("bottom","Bottom","n64",cover,"Ready"),new("second","Second","gamecube",cover,"Ready")],"Games",false,"first");
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        var grid=Field<ListBox>(view,"_grid");
        var page=grid.ItemsSource;
        var tileTarget=grid.GetVisualDescendants().OfType<ListBoxItem>().First();
        var artwork=tileTarget.GetVisualDescendants().OfType<HomeArtworkControl>().Single();
        Check(tileTarget.Bounds.Size==artwork.Bounds.Size,
            "the gaps between covers are outside their pointer targets");
        var hero=Field<Grid>(view,"_heroContent");
        var heroRows=hero.RowDefinitions;
        var preview=Field<GameCaseControl>(view,"_casePreview");
        Check(preview is not null,"HOME uses a case preview for a valid cover");
        var launches=0;
        var selections=0;
        view.LaunchRequested+=_=>launches++;
        view.SelectionChanged+=_=>selections++;

        view.Move(1,0);
        var selected=grid.GetVisualDescendants().OfType<HomeArtworkControl>().Single(tile=>tile.IsSelected);
        Check(view.SelectedId=="second"&&selected.Title=="Second"&&hero.Opacity<1,
            "HOME updates the selected game immediately and starts the selection animation");
        var quick=Field<HomeArtworkControl>(view,"_quickPreview");
        Check(quick.Title=="Second"&&quick.IsEffectivelyVisible&&!preview!.IsEffectivelyVisible,
            "the new cover is visible immediately and the previous 3D case is hidden");
        WaitForPreview(view,"Second");
        Check(ReferenceEquals(preview,Field<GameCaseControl>(view,"_casePreview"))&&ReferenceEquals(heroRows,hero.RowDefinitions)
            &&AutomationProperties.GetName(preview!)=="Second"&&!Field<Grid>(view,"_art").Children.Contains(quick),
            "the delayed 3D case reuses the same control and shows the selected artwork");
        typeof(DualScreenLibraryView).GetMethod("AdvanceSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[0.045]);
        Check(hero.RenderTransform?.Value.M31>0&&selected.RenderTransform?.Value.M11>1,
            "the preview slides in the navigation direction and the selected cover grows");

        var trace=Field<Control>(selected,"_selection");
        var progress=trace.GetType().GetProperty("Progress")!;
        var advance=typeof(DualScreenLibraryView).GetMethod("AdvanceSelection",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Check(trace.RenderTransform?.Value.M31<0,"the selection frame travels from the previous tile to the next one");
        var start=(double)progress.GetValue(trace)!;
        advance.Invoke(view,[0.245]);
        Check((double)progress.GetValue(trace)!>start&&selected.RenderTransform is null&&hero.RenderTransform is null,
            "the highlight continues around the selected cover after the zoom and preview settle");

        advance.Invoke(view,[15d]);
        var looped=(double)progress.GetValue(trace)!;
        Check(looped is >=0 and <1&&hero.RenderTransform is null,
            "the ring keeps orbiting past several cycles without replaying the preview");
        view.SetPlaybackActive(false);
        Check(progress.GetValue(trace) is null&&trace.IsVisible,"leaving HOME stops the orbit and keeps the static outline");
        view.SetPlaybackActive(true);
        advance.Invoke(view,[0.05]);
        Check(progress.GetValue(trace) is double,"the orbit resumes without changing the selected game");

        view.SelectItem("first");
        Check(ReferenceEquals(page,grid.ItemsSource)&&view.SelectedId=="first"
            &&progress.GetValue(trace) is null&&hero.RenderTransform?.Value.M31<0,
            "rapid selection from the paired screen cancels previous motion and keeps its thumbnails");
        view.ReduceMotion=true;
        Dispatcher.UIThread.RunJobs();
        Check(hero.RenderTransform is null&&hero.Opacity==1
            &&grid.GetVisualDescendants().OfType<HomeArtworkControl>().All(tile=>tile.RenderTransform is null),"reduced motion cancels the current transition and restores the preview");
        var first=grid.GetVisualDescendants().OfType<HomeArtworkControl>().Single(tile=>tile.IsSelected);
        Check(Field<Control>(first,"_selection").IsVisible&&progress.GetValue(Field<Control>(first,"_selection")) is null,
            "reduced motion keeps the static selection frame visible");
        view.Move(-1,0);
        Check(selections==1&&launches==0,"movement at the grid boundary neither refreshes the selection nor launches a game");

        var rows=Field<int>(view,"_rows");
        Check(rows==3,"the HOME rail lays out three rows");
        var partial=Enumerable.Range(0,rows+2).Select(i=>new DualScreenItem("grid"+i,"Game "+i,"n64",cover,"Ready")).ToArray();
        view.SetItems(partial,"Games",false,partial[rows].Id);
        view.Move(0,1);view.Move(0,1);
        Check(view.SelectedId==partial[rows+1].Id,"Down reaches the last game and leaves its selection when the next slot is empty");
        view.Move(0,0);view.Move(0,-1);
        Check(view.SelectedId==partial[rows].Id,"Up returns to the same column");
        view.SelectItem(partial[0].Id);view.Move(1,0);view.Move(0,1);
        Check(quick.Title==partial[rows+1].Title,"rapid changes show only the latest front before the next 3D render");
        WaitForPreview(view,partial[rows+1].Title);
        Check(AutomationProperties.GetName(Field<GameCaseControl>(view,"_casePreview"))==partial[rows+1].Title,
            "the coalesced 3D refresh commits the latest selection");
        view.ReduceMotion=false;
        var items=Enumerable.Range(0,30).Select(i=>new DualScreenItem("rail"+i,"Rail game "+i,"n64",cover,"Ready")).ToArray();
        view.SetItems(items,"Games",false,items[0].Id);view.Move(0,0);
        var railItems=grid.ItemsSource;
        for(var column=0;column<7;column++)view.Move(1,0);
        var target=Field<double>(view,"_railTarget");
        advance.Invoke(view,[.045]);
        Check(view.SelectedId==items[21].Id&&target>0&&Field<double>(view,"_railOffset") is >0 and var position&&position<target,
            "horizontal movement follows the same row and scrolls to the selection over multiple frames");
        Check(grid.ItemCount==30&&ReferenceEquals(grid.ItemsSource,railItems),"scrolling retains a single list of tiles");
        view.ReduceMotion=true;
        view.StepColumn(1);
        Check(view.SelectedId==items[24].Id&&Field<double>(view,"_railOffset")==Field<double>(view,"_railTarget"),
            "reduced motion reveals the selected column immediately");
        view.SelectItem(items[0].Id);
        Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();
        var rail=Field<Border>(view,"_rail");
        var startPoint=rail.TranslatePoint(new Point(rail.Width/2,rail.Height/2),window)!.Value;
        var endPoint=startPoint-new Vector(92,0);
        window.MouseDown(startPoint,Avalonia.Input.MouseButton.Left);
        window.MouseMove(endPoint,Avalonia.Input.RawInputModifiers.LeftMouseButton);
        window.MouseUp(endPoint,Avalonia.Input.MouseButton.Left);
        Check(Field<double>(view,"_railOffset")>=90&&ReferenceEquals(grid.ItemsSource,railItems),
            "dragging moves the rail by the pointer distance without replacing any tiles");
        window.Close();
    }

    internal static void WaitForPreview(DualScreenLibraryView view,string title)
    {
        typeof(DualScreenLibraryView).GetMethod("FinishGamePreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        for(var i=0;i<150&&AutomationProperties.GetName(Field<GameCaseControl>(view,"_casePreview"))!=title;i++)
        {Thread.Sleep(8);Dispatcher.UIThread.RunJobs();}
    }

    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
}
