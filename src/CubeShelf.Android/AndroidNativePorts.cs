using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia.Platform;
using CubeShelf.Mobile;
using CubeShelf.Core.Updates;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CubeShelf.Android;

internal sealed class AndroidNativePorts : IMobileNativePorts
{
    internal static AndroidNativePorts Instance { get; } = new();
    public IReadOnlyList<MobileNativePort> Catalog { get; }
    private readonly Dictionary<string,NativeApkDownload> _engines;
    private readonly Dictionary<string,EmbeddedEngineSpec> _hosts;
    private readonly Dictionary<string,EmbeddedNativeModule> _modules;
    private readonly SemaphoreSlim _preparation=new(1);
    private readonly NativePortUpdateService _updates=new();
    private readonly SemaphoreSlim _updateCheck=new(1);
    private readonly ConcurrentDictionary<(string Id,string Version),(DateTimeOffset RetryAt,AndroidPortRelease? Release)> _releases=new();
    private volatile string? _selectedForUpdate;
    public event Action? Changed;
    private static Context Context => global::Android.App.Application.Context;
    private AndroidNativePorts()
    {
        using var stream=AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/Catalogs/android-ports.json"));
        Catalog=MobileNativePort.Load(stream);
        using var hosts=Context.Assets!.Open("nativehost/hosts.json");
        _hosts=(JsonSerializer.Deserialize<EmbeddedEngineSpec[]>(hosts)??[]).ToDictionary(s=>s.Id);
        if(!Catalog.Where(p=>p.Id!=AndroidSm64.Id&&!p.ExternalInstall).All(p=>_hosts.TryGetValue(p.Id,out var h)&&h.ApkSha256==p.ApkSha256&&h.PackageName==p.PackageName))
            throw new InvalidDataException("Les adaptateurs des moteurs ne correspondent pas au catalogue Android.");
        _modules=_hosts.ToDictionary(p=>p.Key,p=>new EmbeddedNativeModule(Context.FilesDir!.AbsolutePath,p.Value,name=>Context.Assets.Open(name)));
        _engines=Catalog.Where(p=>p.Id!=AndroidSm64.Id).ToDictionary(p=>p.Id,p=>new NativeApkDownload(p,Context.FilesDir!.AbsolutePath,CompatibilityError(p.Id) is null));
    }
    internal IEnumerable<RuntimeDownloadSource> Sources => Catalog.Where(p=>p.Id!=AndroidSm64.Id).Select(p=>new RuntimeDownloadSource(p.Id,p.Id,p.Title,p.Version,_engines[p.Id]));
    private MobileNativePort Port(string id)=>Catalog.Single(p=>p.Id==id);
    private static PackageInfo? Installed(string package)
    {
        try {return Context.PackageManager!.GetPackageInfo(package,PackageInfoFlags.MetaData);}
        catch(PackageManager.NameNotFoundException) {return null;}
    }
    public bool IsEmbedded(string id)=>id==AndroidSm64.Id||_hosts.ContainsKey(id);
    public bool IsInstalled(string id)
    {
        if(id==AndroidSm64.Id)return AndroidSm64.IsPrepared;
        if(_modules.TryGetValue(id,out var module))return module.IsReady&&(id!=AndroidMhguFiles.Id||AndroidMhguFiles.IsPrepared);
        var port=Port(id);
        using var installed=Installed(port.PackageName);
        return installed is not null&&VersionCode(installed)>=port.VersionCode;
    }
    public bool IsAdded(string id)=>IsInstalled(id)||Directory.Exists(Path.Combine(Context.FilesDir!.AbsolutePath,"native-games",id==AndroidSm64.Id?"sm64":id))
        ||id!=AndroidSm64.Id&&(_modules.TryGetValue(id,out var module)&&module.HasPreviousVersion||_engines[id].IsReady||Installed(Port(id).PackageName) is not null);
    internal void StorageChanged(string id)
    {
        if(_engines.TryGetValue(id,out var engine)&&!File.Exists(engine.ApkFile)&&engine.Snapshot.State==RuntimeDownloadState.Ready)engine.Cancel();
    }
    public bool IsUpdateAvailable(string id)=>_modules.TryGetValue(id,out var module)&&!module.IsReady&&module.HasPreviousVersion;
    private (string Repository,string Pinned,string Package,string? Installed)? Upstream(string id)
    {
        if(id=="GMPE01_00")return ("zeranemesis/Marioparty4",PartyBoardDownloads.Version,PartyBoardDownloads.AssetName,null);
        var port=Catalog.FirstOrDefault(p=>p.Id==id);
        if(port is null)return null;
        if(port.ExternalInstall)
        {
            using var installed=Installed(port.PackageName);
            if(installed?.VersionName is {} name&&NativePortUpdateService.IsNewerRelease(name,port.Version))
                return (port.Repository,port.Version,port.Name,name);
        }
        return (port.Repository,port.Version,port.Name,null);
    }

    private AndroidPortRelease? UpstreamRelease(string id)=>
        Upstream(id) is {} source&&_releases.TryGetValue((id,source.Installed??source.Pinned),out var cached)?cached.Release:null;

    public (string Version,bool CanInstall)? UpstreamUpdate(string id)
    {
        if(UpstreamRelease(id) is not {} release)return null;
        var port=Catalog.FirstOrDefault(p=>p.Id==id);
        var external=port?.ExternalInstall==true;
        if(external&&!IsInstalled(id))return null;
        return (release.Version,release.Apk is not null&&external);
    }

    public void CheckSelectedUpdate(string id)
    {
        if(_selectedForUpdate==id)return;
        _selectedForUpdate=id;
        if(id!="GMPE01_00"&&!Catalog.Any(port=>port.Id==id))return;
        _=CheckUpstreamAsync(id,true);
    }

    internal async Task CheckInstalledUpdatesAsync()
    {
        foreach(var port in Catalog)
            if(port.Id==_selectedForUpdate||IsInstalled(port.Id))await CheckUpstreamAsync(port.Id).ConfigureAwait(false);
        await CheckUpstreamAsync("GMPE01_00").ConfigureAwait(false);
    }

    private async Task CheckUpstreamAsync(string id,bool selectedOnly=false)
    {
        if(selectedOnly)await Task.Delay(450).ConfigureAwait(false);
        await _updateCheck.WaitAsync().ConfigureAwait(false);
        try
        {
            if(selectedOnly&&_selectedForUpdate!=id)return;
            if(Upstream(id) is not {} source)return;
            var key=(id,source.Installed??source.Pinned);
            if(_releases.TryGetValue(key,out var cached)&&cached.RetryAt>DateTimeOffset.UtcNow)return;
            try
            {
                var release=await _updates.CheckAndroidAsync(source.Repository,source.Pinned,source.Package,source.Installed,allowPrereleases:id=="GMPE01_00").ConfigureAwait(false);
                _releases[key]=(DateTimeOffset.UtcNow.AddHours(6),release);
                if(release!=cached.Release)Changed?.Invoke();
            }
            catch(Exception error) when(error is HttpRequestException or IOException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
            {
                _releases[key]=(DateTimeOffset.UtcNow.AddMinutes(10),cached.Release);
                global::Android.Util.Log.Debug("DecompShelf","Vérification du port "+id+" : "+error.Message);
            }
        }
        finally{_updateCheck.Release();}
    }

    public async Task<string> UpdateAsync(string id,CancellationToken cancellation=default)
    {
        var port=Port(id);
        if(CompatibilityError(id) is { } issue)throw new InvalidOperationException(issue);
        if(!await _preparation.WaitAsync(0,cancellation))throw new InvalidOperationException("Une préparation est déjà en cours.");
        try
        {
            var queue=NativeGameService.Downloads;
            var transferId=id;
            var engine=_engines[id];
            if(port.ExternalInstall)
            {
                if(UpstreamRelease(id) is not {Apk:{} apk} release||!IsInstalled(id))
                    throw new InvalidOperationException("Aucune mise à jour vérifiable disponible pour ce port.");
                var package=new MobileNativePort
                {
                    Id=id,ShelfConsole=port.ShelfConsole,Repository=port.Repository,PackageName=port.PackageName,
                    Version=release.Version,VersionCode=port.VersionCode,MinimumSdk=port.MinimumSdk,Abis=port.Abis,
                    Name=apk.Name,Url=apk.Url,Size=apk.Size,ApkSize=apk.Size,Sha256=apk.Sha256,ApkSha256=apk.Sha256
                };
                transferId=id+"-update-"+apk.Sha256[..12];
                engine=(NativeApkDownload)queue.Register(new(transferId,id,port.Title,release.Version,
                    new NativeApkDownload(package,Context.FilesDir!.AbsolutePath,true)));
            }
            if(!engine.IsReady)
                await queue.DownloadAsync(transferId,engine,cancellation);
            return await InstallVerifiedAsync(port,engine,port.ExternalInstall,cancellation);
        }
        finally{_preparation.Release();}
    }

    public bool IsPackageReady(string id)=>id==AndroidSm64.Id?AndroidSm64.EngineAvailable:_engines[id].IsReady;
    public string? CompatibilityError(string id)
    {
        var port=Port(id);
        if(id==AndroidSm64.Id&&!AndroidSm64.EngineAvailable)return "Super Mario 64 intégré nécessite l’APK ARM64 de DecompShelf.";
        if(id!=AndroidSm64.Id&&!port.ExternalInstall)
        {
            if(!OperatingSystem.IsAndroidVersionAtLeast(28))return "Les moteurs hébergés nécessitent Android 9 ou plus récent.";
            if(RuntimeInformation.ProcessArchitecture!=Architecture.Arm64)return "Les moteurs hébergés nécessitent l’APK ARM64 de DecompShelf.";
        }
        if((int)global::Android.OS.Build.VERSION.SdkInt<port.MinimumSdk)return "Version Android trop ancienne pour ce port.";
        if(!port.Abis.Intersect(global::Android.OS.Build.SupportedAbis??[]).Any())return "Architecture incompatible avec ce port Android.";
        if(port.RequiresVulkan13&&!Context.PackageManager!.HasSystemFeature("android.hardware.vulkan.version",0x00403000))return "Ce port nécessite un pilote Vulkan 1.3.";
        if(port.RequiresVulkan12&&!Context.PackageManager!.HasSystemFeature("android.hardware.vulkan.version",0x00402000))return "Ce port nécessite un pilote Vulkan 1.2.";
        if(port.RequiresVulkan11&&!Context.PackageManager!.HasSystemFeature("android.hardware.vulkan.version",0x00401000))return "Ce port nécessite un pilote Vulkan 1.1.";
        if(port.RequiresGles3 && (Context.GetSystemService(Context.ActivityService) as ActivityManager)?.DeviceConfigurationInfo?.ReqGlEsVersion<0x30000)return "Ce port nécessite OpenGL ES 3.0.";
        return null;
    }
    public async Task<string> PrepareAsync(string id,CancellationToken cancellation=default)
    {
        if(id==AndroidSm64.Id)return await AndroidSm64.PrepareAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if(CompatibilityError(id) is { } issue)throw new InvalidOperationException(issue);
        if(!_engines[id].IsReady)
        {
            NativeGameService.Downloads.Enqueue(id);
            if(NativeGameService.Downloads.IsPaused)NativeGameService.Downloads.SetPaused(false);
            return Port(id).ExternalInstall
                ? "Téléchargement ajouté à la file. Touche Installer l’APK après vérification."
                : "Téléchargement du moteur ajouté à la file. Après vérification, touche Préparer dans DecompShelf.";
        }
        if(!await _preparation.WaitAsync(0,cancellation))throw new InvalidOperationException("Une préparation est déjà en cours.");
        try {return await InstallVerifiedAsync(Port(id),_engines[id],false,cancellation);}
        finally {_preparation.Release();}
    }

    private async Task<string> InstallVerifiedAsync(MobileNativePort port,NativeApkDownload engine,bool update,CancellationToken cancellation)
    {
        var file=await engine.VerifiedApkAsync();
        cancellation.ThrowIfCancellationRequested();
        using var candidate=Context.PackageManager!.GetPackageArchiveInfo(file,PackageInfoFlags.MetaData)
            ??throw new InvalidDataException("Paquet du moteur invalide.");
        if(candidate.PackageName!=port.PackageName||!update&&VersionCode(candidate)!=port.VersionCode)
            throw new InvalidDataException("L’identité du moteur ne correspond pas au catalogue.");
        if(port.ExternalInstall)
        {
            var activity=NativeGameService.CurrentActivity??throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
            if(update)
            {
                if(activity is MainActivity {IsResumedForInstall:false})
                    return "APK vérifié. Rouvre les options du jeu pour lancer l’installation.";
                AndroidAppUpdates.ValidateUpdate(activity,file,port.PackageName);
            }
            return AndroidAppUpdates.RequestInstall(activity,file)
                ? "Confirme l’installation du port dans Android."
                : "Autorise DecompShelf à installer des applications, puis reviens et relance l’installation.";
        }
        await Task.Run(()=>_modules[port.Id].PrepareAsync(file,cancellation),cancellation);
        if(port.Id==AndroidMhguFiles.Id&&!AndroidMhguFiles.IsPrepared)await AndroidMhguFiles.ImportAsync(cancellation);
        return "Moteur prêt dans DecompShelf. Au premier démarrage, importe les fichiers de ta propre copie du jeu.";
    }
    public void Launch(string id)
    {
        if(id==AndroidSm64.Id){AndroidSm64.Launch();return;}
        if(CompatibilityError(id) is { } issue)throw new InvalidOperationException(issue);
        if(!IsInstalled(id))throw new InvalidOperationException("Prépare d’abord le moteur dans DecompShelf.");
        var activity=NativeGameService.CurrentActivity??throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
        var port=Port(id);
        if(port.ExternalInstall)
        {
            using var external=activity.PackageManager!.GetLaunchIntentForPackage(port.PackageName)
                ??throw new InvalidOperationException("Le port installé ne propose aucune activité de lancement.");
            activity.StartActivity(external);
            return;
        }
        var host=_hosts[id];var main=host.Components.Single(c=>c.Name==host.MainActivity&&c.Kind=="activity");
        using var intent=new Intent().SetClassName(activity.PackageName!,main.Bridge);
        intent.AddFlags(ActivityFlags.NewTask);
        activity.StartActivity(intent);
    }
    private static long VersionCode(PackageInfo info)
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(28))return info.LongVersionCode;
#pragma warning disable CS0618
        return info.VersionCode;
#pragma warning restore CS0618
    }
}
