using CubeShelf.Core.Library;
using CubeShelf.Core.Updates;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string,NativePort> _nativeReleaseUpdates=new(StringComparer.OrdinalIgnoreCase);
    private async Task CheckInstalledNativeUpdatesAsync(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12),cancellation);
            if(!_preferences.CheckGamesOnStartup)return;
            var library=new NativePortLibrary(_paths);
            using var service=new NativePortUpdateService();
            foreach(var (id,port) in _nativeShelfPorts)
            {
                cancellation.ThrowIfCancellationRequested();
                if(library.Installation(port) is not {} installed)continue;
                try
                {
                    var latest=await service.CheckAsync(port,cancellation);
                    if(latest is not null&&latest.Assets.All(a=>!a.Sha256.Equals(installed.PackageSha256,StringComparison.OrdinalIgnoreCase)))
                        _nativeReleaseUpdates[id]=latest;
                }
                catch(HttpRequestException){break;}
                catch(Exception error) when(error is IOException or System.Text.Json.JsonException or InvalidOperationException){}
            }
            if(!_quickPlayClosing){RefreshQuickPlayUi();RefreshDualStatus();}
        }
        catch(OperationCanceledException){}
        catch(Exception error) when(error is IOException or System.Text.Json.JsonException or InvalidOperationException){}
    }
    private async void OpenNativeEngineManager(GameCatalogEntry game)
    {
        try{await EnsureNativeShelfPortAsync(game,true);_nativeReleaseUpdates.Remove(game.Id);RefreshQuickPlayUi();RefreshDualStatus();}
        catch(Exception error){ShowToastParity(game.Title,error.Message);}
    }
}
