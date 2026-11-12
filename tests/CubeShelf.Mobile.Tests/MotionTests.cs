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
        view.SetItems([new("first","First","n64",cover,"Ready"),new("second","Second","gamecube",cover,"Ready")],"Games",false,"first");
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
        var rows=hero.RowDefinitions;
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
        typeof(DualScreenLibraryView).GetMethod("FinishGamePreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        Check(ReferenceEquals(preview,Field<GameCaseControl>(view,"_casePreview"))&&ReferenceEquals(rows,hero.RowDefinitions)
            &&AutomationProperties.GetName(preview!)=="Second"&&!Field<Grid>(view,"_art").Children.Contains(quick),
            "the delayed 3D case reuses the same control and shows the selected artwork");
        typeof(DualScreenLibraryView).GetMethod("AdvanceSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[0.045]);
        Check(hero.RenderTransform?.Value.M31>0&&selected.RenderTransform?.Value.M11>1,
            "the preview slides in the navigation direction and the selected cover grows");

        var trace=Field<Control>(selected,"_selection");
        var progress=trace.GetType().GetProperty("Progress")!;
        var advance=typeof(DualScreenLibraryView).GetMethod("AdvanceSelection",BindingFlags.Instance|BindingFlags.NonPublic)!;
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

        var columns=Field<int>(view,"_columns");
        var partial=Enumerable.Range(0,columns+2).Select(i=>new DualScreenItem("grid"+i,"Game "+i,"n64",cover,"Ready")).ToArray();
        view.SetItems(partial,"Games",false,partial[columns-1].Id);
        view.Move(0,1);
        Check(view.SelectedId==partial[columns-1].Id,"Down stays on the current game if the next row has no tile in that column");
        view.SelectItem(partial[1].Id);view.Move(0,1);
        Check(view.SelectedId==partial[columns+1].Id,"Down reaches the game in the same column on an incomplete row");
        view.Move(0,-1);
        Check(view.SelectedId==partial[1].Id,"Up returns to the same column");
        view.Move(1,0);view.Move(1,0);
        Check(quick.Title==partial[3].Title,"rapid changes show only the latest front before the next 3D render");
        typeof(DualScreenLibraryView).GetMethod("FinishGamePreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        Check(AutomationProperties.GetName(Field<GameCaseControl>(view,"_casePreview"))==partial[3].Title,
            "the coalesced 3D refresh commits the latest selection");
        window.Close();
    }

    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK "+message);}
}
