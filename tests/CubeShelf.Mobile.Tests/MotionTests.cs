using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Library;
using CubeShelf.Desktop.Controls;

internal static class MotionTests
{
    internal static void Run(string root,string cover)
    {
        var tile=new HomeArtworkControl{Width=80,Height=80};
        var window=new Window{Width=240,Height=240,Content=tile};window.Show();Jobs();
        tile.IsSelected=true;Advance(tile,"AdvanceMotion",.025);
        var scale=Pose(tile).M11;
        Check(scale>1&&scale<1.045,"selection moves progressively instead of jumping");
        tile.SetPressed(true);Settle(tile);
        Check(Math.Abs(Pose(tile).M11-.975)<.001,"press feedback settles at its bounded target");
        tile.SetPressed(false);tile.IsSelected=false;tile.IsSelected=true;Settle(tile);
        Check(Math.Abs(Pose(tile).M11-1.045)<.001,"rapid selection changes settle on the current selection");
        tile.ReduceMotion=true;
        Check(Pose(tile)==Matrix.Identity&&!Running(tile,"_motion"),"reduced motion cancels selection transforms immediately");
        window.Close();Check(!Running(tile,"_motion"),"detaching a tile stops its frame requests");

        var view=new DualScreenLibraryView();view.Configure(new LibraryPresentationStore(Path.Combine(root,"motion")),null);
        window=new Window{Width=900,Height=800,Content=view};window.Show();
        view.SetItems(new[]{new DualScreenItem("first","First","n64",cover,"Ready"),new DualScreenItem("second","Second","n64",cover,"Ready")},"Games",false,"first");Jobs();
        var launches=0;view.LaunchRequested+=_=>launches++;
        var grid=Field<ListBox>(view,"_grid");var thumbnail=grid.GetVisualDescendants().OfType<HomeArtworkControl>().First();
        var point=thumbnail.TranslatePoint(new Point(30,30),window)!.Value;
        window.MouseDown(point,Avalonia.Input.MouseButton.Left);Settle(thumbnail);
        Check(Math.Abs(Pose(thumbnail).M11-.975)<.001,"pointer input reaches the artwork press feedback");
        window.MouseUp(point,Avalonia.Input.MouseButton.Left);Settle(thumbnail);
        Check(Pose(thumbnail).M11>1,"releasing the pointer restores selection feedback");
        view.Move(1,0);var hero=Field<Grid>(view,"_heroContent");var offset=Field<TranslateTransform>(view,"_bannerOffset");
        Check(view.SelectedId=="second"&&hero.Opacity>=.85&&hero.Opacity<1&&offset.X>0,"the new game is immediately readable during its directional transition");
        Advance(view,"AdvanceSelection",.11);
        Check(offset.X>0&&offset.X<8&&hero.Opacity>.9,"preview transitions decelerate toward the selected game");
        view.Move(-1,0);Advance(view,"AdvanceSelection",.22);
        Check(view.SelectedId=="first"&&offset.X==0&&hero.Opacity==1&&launches==0,"interrupting the preview preserves the latest selection without launching it");
        view.Move(1,0);view.ReduceMotion=true;
        Check(hero.Opacity==1&&offset.X==0&&!Running(view,"_selectionAnimation"),"reducing motion mid-transition restores a fully visible preview");
        view.ReduceMotion=false;view.Move(-1,0);view.IsVisible=false;
        Check(hero.Opacity==1&&offset.X==0&&!Running(view,"_selectionAnimation"),"hiding the library cancels its transition without leaving an offset");
        window.Close();
    }
    private static Matrix Pose(HomeArtworkControl tile)=>((MatrixTransform)tile.RenderTransform!).Matrix;
    private static void Settle(HomeArtworkControl tile){for(var i=0;i<120;i++)if(!Advance(tile,"AdvanceMotion",1d/120))break;}
    private static bool Advance(object target,string name,double elapsed)=>(bool)target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,new object[]{elapsed})!;
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static bool Running(object target,string name)=>Field<bool>(Field<object>(target,name),"_running");
    private static void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("OK motion: "+message);}
}
