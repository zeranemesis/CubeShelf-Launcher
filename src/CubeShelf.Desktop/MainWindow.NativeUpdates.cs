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
            while(!cancellation.IsCancellationRequested)
            {
                if(_preferences.CheckGamesOnStartup)await ScanInstalledNativeUpdatesAsync(cancellation);
                await Task.Delay(TimeSpan.FromHours(6),cancellation);
            }
        }
        catch(OperationCanceledException){}
    }

    private async Task ScanInstalledNativeUpdatesAsync(CancellationToken cancellation)
    {
        try
        {
            var library=new NativePortLibrary(_paths);
            using var service=new NativePortUpdateService();
            var notices=new List<(string Title,string Version)>();
            foreach(var (id,port) in _nativeShelfPorts.ToArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if(library.Installation(port) is not {} installed)continue;
                try
                {
                    var latest=await service.CheckAsync(port,cancellation);
                    if(latest is not null&&NativePortUpdateService.IsNewerRelease(latest.ReleaseTag,installed.Version)
                        &&latest.Assets.All(a=>!a.Sha256.Equals(installed.PackageSha256,StringComparison.OrdinalIgnoreCase)))
                    {
                        if(!_nativeReleaseUpdates.TryGetValue(id,out var known)||known.ReleaseTag!=latest.ReleaseTag)
                            notices.Add((port.Title,latest.ReleaseTag));
                        _nativeReleaseUpdates[id]=latest;
                    }
                    else _nativeReleaseUpdates.Remove(id);
                }
                catch(HttpRequestException error) when(error.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests){break;}
                catch(HttpRequestException){}
                catch(Exception error) when(error is IOException or System.Text.Json.JsonException or InvalidOperationException){}
            }
            if(_quickPlayClosing)return;
            RefreshQuickPlayUi();RefreshDualStatus();
            if(notices.Count>0&&_preferences.ShowGameUpdatePopup)
            {
                var message=notices.Count==1?$"{notices[0].Title} · {notices[0].Version}"
                    :P7($"{notices.Count} ports ont une nouvelle version.",$"{notices.Count} ports have a new version.");
                ShowToastParity(P7("Mise à jour de jeu disponible","Game update available"),
                    message+" "+P7("Ouvre Moteur et mises à jour dans les options du jeu.","Open Engine and updates in game options."));
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error) when(error is IOException or System.Text.Json.JsonException or InvalidOperationException){}
    }
    private async void OpenNativeEngineManager(GameCatalogEntry game)
    {
        try{await EnsureNativeShelfPortAsync(game,true);}
        catch(Exception error){ShowToastParity(game.Title,error.Message);}
    }
}
