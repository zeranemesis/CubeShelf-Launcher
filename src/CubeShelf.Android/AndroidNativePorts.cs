using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia.Platform;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

internal sealed class AndroidNativePorts : IMobileNativePorts
{
    internal static AndroidNativePorts Instance { get; } = new();
    public IReadOnlyList<MobileNativePort> Catalog { get; }
    private readonly Dictionary<string,NativeApkDownload> _engines;
    private static Context Context => global::Android.App.Application.Context;
    private AndroidNativePorts()
    {
        using var stream=AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/Catalogs/android-ports.json"));
        Catalog=MobileNativePort.Load(stream);
        _engines=Catalog.Where(p=>!IsEmbedded(p.Id)).ToDictionary(p=>p.Id,p=>new NativeApkDownload(p,Context.FilesDir!.AbsolutePath,CompatibilityError(p.Id) is null));
    }
    internal IEnumerable<RuntimeDownloadSource> Sources => Catalog.Where(p=>!IsEmbedded(p.Id)).Select(p=>new RuntimeDownloadSource(p.Id,p.Id,p.Title,p.Version,_engines[p.Id]));
    private MobileNativePort Port(string id)=>Catalog.Single(p=>p.Id==id);
    private static PackageInfo? Installed(string package)
    {
        try {return Context.PackageManager!.GetPackageInfo(package,PackageInfoFlags.MetaData);}
        catch(PackageManager.NameNotFoundException) {return null;}
    }
    public bool IsEmbedded(string id)=>id==AndroidSm64.Id;
    public bool IsInstalled(string id)=>IsEmbedded(id)?AndroidSm64.IsPrepared:Installed(Port(id).PackageName) is not null;
    public bool IsUpdateAvailable(string id)=>!IsEmbedded(id)&&Installed(Port(id).PackageName) is { } info && VersionCode(info)<Port(id).VersionCode;
    public bool IsPackageReady(string id)=>IsEmbedded(id)?AndroidSm64.EngineAvailable:_engines[id].IsReady;
    public string? CompatibilityError(string id)
    {
        var port=Port(id);
        if(IsEmbedded(id)&&!AndroidSm64.EngineAvailable)return "Super Mario 64 intégré nécessite l’APK ARM64 de DecompShelf.";
        if((int)global::Android.OS.Build.VERSION.SdkInt<port.MinimumSdk)return "Version Android trop ancienne pour ce port.";
        if(!port.Abis.Intersect(global::Android.OS.Build.SupportedAbis??[]).Any())return "Architecture incompatible avec ce port Android.";
        if(port.RequiresVulkan12&&!Context.PackageManager!.HasSystemFeature("android.hardware.vulkan.version",0x00402000))
            return "Ce port nécessite un pilote Vulkan 1.2.";
        if(port.RequiresGles3 && (Context.GetSystemService(Context.ActivityService) as ActivityManager)?.DeviceConfigurationInfo?.ReqGlEsVersion<0x30000)
            return "Ce port nécessite OpenGL ES 3.0.";
        return null;
    }
    public async Task<string> PrepareAsync(string id, CancellationToken cancellation = default)
    {
        if(IsEmbedded(id))return await AndroidSm64.PrepareAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        var port=Port(id);
        if(CompatibilityError(id) is { } issue)throw new InvalidOperationException(issue);
        if(!_engines[id].IsReady)
        {
            NativeGameService.Downloads.Enqueue(id);
            if(NativeGameService.Downloads.IsPaused)NativeGameService.Downloads.SetPaused(false);
            return "Téléchargement du port ajouté à la file. Après vérification, touche Installer l’APK.";
        }
        var file=await _engines[id].VerifiedApkAsync();
        var activity=NativeGameService.CurrentActivity??throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
        var manager=activity.PackageManager!;
#pragma warning disable CS0618, CA1422
        var candidate=manager.GetPackageArchiveInfo(file,PackageInfoFlags.Signatures)??throw new InvalidDataException("APK invalide.");
        if(candidate.PackageName!=port.PackageName || VersionCode(candidate)!=port.VersionCode)throw new InvalidDataException("L’identité du port ne correspond pas au catalogue.");
        var installed=Installed(port.PackageName);
        if(installed is not null)
        {
            if(VersionCode(installed)>=port.VersionCode)return "Cette version ou une version plus récente est déjà installée. Utilise Jouer.";
            var current=manager.GetPackageInfo(port.PackageName,PackageInfoFlags.Signatures)!;
            var a=current.Signatures?.Select(s=>s.ToCharsString()).OrderBy(s=>s).ToArray();
            var b=candidate.Signatures?.Select(s=>s.ToCharsString()).OrderBy(s=>s).ToArray();
            if(a is not {Length:>0} || b is null || !a.SequenceEqual(b))throw new InvalidDataException("Signature différente du port installé. Aucune désinstallation automatique ; tes données sont conservées.");
        }
#pragma warning restore CS0618, CA1422
        if(!manager.CanRequestPackageInstalls())
        {
            using var settings=new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,global::Android.Net.Uri.Parse("package:"+activity.PackageName));
            activity.StartActivity(settings);
            return "Autorise DecompShelf à installer des applications, puis reviens et touche Installer l’APK.";
        }
        var uri=AndroidX.Core.Content.FileProvider.GetUriForFile(activity,activity.PackageName+".updates",new Java.IO.File(file));
        using var intent=new Intent(Intent.ActionView);
        intent.SetDataAndType(uri,"application/vnd.android.package-archive");intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        activity.StartActivity(intent);
        return "Confirme l’installation dans Android, puis reviens pour jouer. Le port demandera les fichiers de ta propre copie du jeu.";
    }
    public void Launch(string id)
    {
        if(IsEmbedded(id)) {AndroidSm64.Launch();return;}
        var activity=NativeGameService.CurrentActivity??throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
        using var intent=activity.PackageManager!.GetLaunchIntentForPackage(Port(id).PackageName)
            ??throw new InvalidOperationException("Installe d’abord le port Android.");
        activity.StartActivity(intent);
        GameHelpOverlay.StartForGame(activity, Port(id).Title, intent.Component?.PackageName, intent.Component?.ClassName);
    }
    private static long VersionCode(PackageInfo info)
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(28))return info.LongVersionCode;
#pragma warning disable CS0618
        return info.VersionCode;
#pragma warning restore CS0618
    }
}
