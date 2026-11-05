using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia.Platform;
using CubeShelf.Mobile;
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
    private readonly HashSet<string> _automaticPreparations=[];
    public event Action? Changed;
    public bool IsPreparing(string id){lock(_automaticPreparations)return _automaticPreparations.Contains(id);}
    private static Context Context => global::Android.App.Application.Context;
    private AndroidNativePorts()
    {
        using var stream=AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/Catalogs/android-ports.json"));
        Catalog=MobileNativePort.Load(stream);
        using var hosts=Context.Assets!.Open("nativehost/hosts.json");
        _hosts=(JsonSerializer.Deserialize<EmbeddedEngineSpec[]>(hosts)??[]).ToDictionary(s=>s.Id);
        if(!Catalog.Where(p=>p.Id!=AndroidSm64.Id).All(p=>_hosts.TryGetValue(p.Id,out var h)&&h.ApkSha256==p.ApkSha256&&h.PackageName==p.PackageName))
            throw new InvalidDataException("Les adaptateurs des moteurs ne correspondent pas au catalogue Android.");
        _modules=_hosts.ToDictionary(p=>p.Key,p=>new EmbeddedNativeModule(Context.FilesDir!.AbsolutePath,p.Value,name=>Context.Assets.Open(name)));
        _engines=Catalog.Where(p=>p.Id!=AndroidSm64.Id).ToDictionary(p=>p.Id,p=>new NativeApkDownload(p,Context.FilesDir!.AbsolutePath,CompatibilityError(p.Id) is null));
        foreach(var (id,engine) in _engines)
            engine.Changed+=snapshot=>{if(snapshot.State==RuntimeDownloadState.Ready&&IsUpdateAvailable(id))_ = PrepareKnownUpdateAsync(id);};
    }
    internal IEnumerable<RuntimeDownloadSource> Sources => Catalog.Where(p=>p.Id!=AndroidSm64.Id).Select(p=>new RuntimeDownloadSource(p.Id,p.Id,p.Title,p.Version,_engines[p.Id]));
    private MobileNativePort Port(string id)=>Catalog.Single(p=>p.Id==id);
    private static PackageInfo? Installed(string package)
    {
        try {return Context.PackageManager!.GetPackageInfo(package,PackageInfoFlags.MetaData);}
        catch(PackageManager.NameNotFoundException) {return null;}
    }
    public bool IsEmbedded(string id)=>id==AndroidSm64.Id||_hosts.ContainsKey(id);
    public bool IsInstalled(string id)=>id==AndroidSm64.Id?AndroidSm64.IsPrepared:_modules[id].IsReady&&(id!=AndroidMhguFiles.Id||AndroidMhguFiles.IsPrepared);
    public bool IsAdded(string id)=>IsInstalled(id)||Directory.Exists(Path.Combine(Context.FilesDir!.AbsolutePath,"native-games",id==AndroidSm64.Id?"sm64":id))
        ||id!=AndroidSm64.Id&&(_modules[id].HasPreviousVersion||_engines[id].IsReady||Installed(Port(id).PackageName) is not null);
    internal void StorageChanged(string id)
    {
        if(_engines.TryGetValue(id,out var engine)&&!File.Exists(engine.ApkFile)&&engine.Snapshot.State==RuntimeDownloadState.Ready)engine.Cancel();
    }
    public bool IsUpdateAvailable(string id)=>id!=AndroidSm64.Id&&!_modules[id].IsReady&&_modules[id].HasPreviousVersion;
    internal void ResumeKnownUpdates()
    {
        try
        {
            using var manager=Context.GetSystemService(Context.ConnectivityService) as global::Android.Net.ConnectivityManager;
            using var network=manager?.ActiveNetwork;
            using var capabilities=network is null?null:manager!.GetNetworkCapabilities(network);
            var unmetered=capabilities?.HasCapability(global::Android.Net.NetCapability.NotMetered)==true;
            foreach(var port in Catalog.Where(p=>IsUpdateAvailable(p.Id)&&CompatibilityError(p.Id) is null))
                if(_engines[port.Id].IsReady)_ = PrepareKnownUpdateAsync(port.Id);
                else if(unmetered)NativeGameService.Downloads.Enqueue(port.Id);
        }
        catch(Exception error){global::Android.Util.Log.Warn("DecompShelf","Mises à jour des moteurs : "+error.Message);}
    }
    private async Task PrepareKnownUpdateAsync(string id)
    {
        lock(_automaticPreparations)if(!_automaticPreparations.Add(id))return;
        Changed?.Invoke();
        await _preparation.WaitAsync();
        try
        {
            if(!IsUpdateAvailable(id))return;
            var file=await _engines[id].VerifiedApkAsync();
            var port=Port(id);var info=Context.PackageManager!.GetPackageArchiveInfo(file,PackageInfoFlags.MetaData);
            if(info?.PackageName!=port.PackageName||VersionCode(info)!=port.VersionCode)throw new InvalidDataException("Identité du moteur incorrecte.");
            await Task.Run(()=>_modules[id].PrepareAsync(file));
        }
        catch(Exception error){global::Android.Util.Log.Warn("DecompShelf","Mise à jour du moteur conservée pour réessai : "+error.Message);}
        finally{_preparation.Release();lock(_automaticPreparations)_automaticPreparations.Remove(id);Changed?.Invoke();}
    }
    public bool IsPackageReady(string id)=>id==AndroidSm64.Id?AndroidSm64.EngineAvailable:_engines[id].IsReady;
    public string? CompatibilityError(string id)
    {
        var port=Port(id);
        if(id==AndroidSm64.Id&&!AndroidSm64.EngineAvailable)return "Super Mario 64 intégré nécessite l’APK ARM64 de DecompShelf.";
        if(id!=AndroidSm64.Id)
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
            return "Téléchargement du moteur ajouté à la file. Après vérification, touche Préparer dans DecompShelf.";
        }
        if(!await _preparation.WaitAsync(0,cancellation))throw new InvalidOperationException("Une préparation est déjà en cours.");
        try
        {
            var file=await _engines[id].VerifiedApkAsync();cancellation.ThrowIfCancellationRequested();
            var port=Port(id);
            var candidate=Context.PackageManager!.GetPackageArchiveInfo(file,PackageInfoFlags.MetaData)??throw new InvalidDataException("Paquet du moteur invalide.");
            if(candidate.PackageName!=port.PackageName||VersionCode(candidate)!=port.VersionCode)throw new InvalidDataException("L’identité du moteur ne correspond pas au catalogue.");
            await Task.Run(()=>_modules[id].PrepareAsync(file,cancellation),cancellation);
            if(id==AndroidMhguFiles.Id&&!AndroidMhguFiles.IsPrepared)await AndroidMhguFiles.ImportAsync(cancellation);
            return "Moteur prêt dans DecompShelf. Au premier démarrage, importe les fichiers de ta propre copie du jeu.";
        }
        finally {_preparation.Release();}
    }
    public void Launch(string id)
    {
        if(id==AndroidSm64.Id){AndroidSm64.Launch();return;}
        if(CompatibilityError(id) is { } issue)throw new InvalidOperationException(issue);
        if(!IsInstalled(id))throw new InvalidOperationException("Prépare d’abord le moteur dans DecompShelf.");
        var activity=NativeGameService.CurrentActivity??throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
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
