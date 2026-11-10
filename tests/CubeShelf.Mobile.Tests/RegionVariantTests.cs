using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using CubeShelf.Mobile;

internal static class RegionVariantTests
{
    internal static void Run(string root,MobileNativePort[] ports)
    {
        var family=ports.Where(p=>p.RegionOf=="gc-papermario-ttyd").ToArray();
        Check(family.Length==2&&family.Select(p=>p.Region).Order().SequenceEqual(["Europe","USA"])&&family.All(p=>p.Title=="Paper Mario: The Thousand-Year Door"),
            "Paper Mario's two regional packages share one family and one title");
        var games=new FakeGames(Path.Combine(root,"region-variants"),ports);
        var mobile=new MobileLibraryView(games);var host=new Window{Content=mobile,Width=390,Height=844};host.Show();Jobs();
        string[] Ids()=>((IEnumerable)Field("_allCatalog")).Cast<object>().Select(g=>(string)g.GetType().GetProperty("Id")!.GetValue(g)!).ToArray();
        var shown=Ids().Where(id=>id.StartsWith("gc-papermario-ttyd-",StringComparison.Ordinal)).ToArray();
        Check(shown.Length==1&&shown[0]==family[0].Id,"the shelf shows one Paper Mario, the first region by default");
        Invoke("SwitchRegion",ports.Single(p=>p.Id==shown[0]));Jobs();
        var switched=Ids().Where(id=>id.StartsWith("gc-papermario-ttyd-",StringComparison.Ordinal)).ToArray();
        Check(switched.Length==1&&switched[0]==family[1].Id,"choosing the other region replaces the game in place");
        host.Close();
        var restarted=new MobileLibraryView(games);host=new Window{Content=restarted,Width=390,Height=844};host.Show();Jobs();
        mobile=restarted;
        Check(Ids().Count(id=>id.StartsWith("gc-papermario-ttyd-",StringComparison.Ordinal))==1&&Ids().Contains(family[1].Id),"the chosen region survives a restart");
        host.Close();

        object Field(string name)=>typeof(MobileLibraryView).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mobile)!;
        void Invoke(string name,params object[] arguments)=>typeof(MobileLibraryView).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(mobile,arguments);
    }
    private static void Jobs(){for(var i=0;i<5;i++)Dispatcher.UIThread.RunJobs();}
    private static void Check(bool good,string text){if(!good)throw new Exception(text);Console.WriteLine("OK "+text);}
}
