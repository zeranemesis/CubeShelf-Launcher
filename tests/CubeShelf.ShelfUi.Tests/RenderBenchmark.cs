using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using CubeShelf.Desktop.Controls;

static class RenderBenchmark
{
    public static void Run()
    {
        using var registry=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Catalogs/covers.json")));
        var report=new List<object>();
        foreach(var console in new[]{"gamecube","wii","switch"})
        {
            string FilePath(JsonElement entry,string face)=>entry.TryGetProperty(face,out var path)
                ? Path.Combine(AppContext.BaseDirectory,path.GetString()!.Replace('\\',Path.DirectorySeparatorChar)):"";
            var games=registry.RootElement.EnumerateArray().Where(e=>e.GetProperty("Console").GetString()==console && e.TryGetProperty("Front",out _))
                .Take(10).Select(e=>new GameShelfItem(e.GetProperty("Title").GetString()!,FilePath(e,"Front"),FilePath(e,"Back"),FilePath(e,"Spine"),console)).ToArray();
            foreach(var (compositor,gpu) in new[]{(false,false),(true,false),(true,true)})
            {
                using var shelf=new GameShelfControl { UseCompositorScaling=compositor,UseGpuTextures=gpu };
                using var bitmap=new RenderTargetBitmap(new PixelSize(1000,600),new Vector(96,96));
                shelf.SetGames(games);shelf.SetSelection(0,true,false);
                shelf.Measure(new Size(1000,600));shelf.Arrange(new Rect(0,0,1000,600));bitmap.Render(shelf);
                var frames=new List<double>();var selections=new List<double>();long allocations=0;var backend=0;
                GameCaseControl.BackendTiming=_=>backend++;
                for(var game=1;game<8;game++)
                {
                    var selection=Stopwatch.StartNew();shelf.SetSelection(game,true);selections.Add(selection.Elapsed.TotalMilliseconds);
                    for(var frame=0;frame<8;frame++)
                    {
                        var before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                        shelf.InvalidateArrange();shelf.Arrange(new Rect(0,0,1000,600));bitmap.Render(shelf);
                        frames.Add(watch.Elapsed.TotalMilliseconds);allocations+=GC.GetAllocatedBytesForCurrentThread()-before;
                    }
                }
                GameCaseControl.BackendTiming=null;
                frames.Sort();
                selections.Sort();
                var result=new {console,compositor,gpu,frames=frames.Count,medianMs=frames[frames.Count/2],p95Ms=frames[(int)(frames.Count*.95)],allocatedKiBPerFrame=allocations/1024d/frames.Count,
                    selectionMedianMs=selections[selections.Count/2],selectionMaxMs=selections[^1],backendDraws=backend};
                report.Add(result);Console.WriteLine(JsonSerializer.Serialize(result));
                var snapshots=Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR");
                if(snapshots is not null) { Directory.CreateDirectory(snapshots);bitmap.Save(Path.Combine(snapshots,$"bench-{console}-{compositor}-{gpu}.png")); }
                shelf.Clear();CoverBitmapCache.ReleaseUnused();
            }
        }
        GameCaseControl.PreferGpuTextures=false;
        var destination=Environment.GetEnvironmentVariable("CUBESHELF_RENDER_BENCHMARK_FILE");
        if(destination is not null)File.WriteAllText(destination,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
    }
}
