using Android.Content;
using Android.Content.PM;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

internal static class AndroidAppUpdates
{
    private static LauncherAppUpdates? _instance;
    internal static LauncherAppUpdates Instance => _instance ??= Create();

    private static LauncherAppUpdates Create()
    {
        var context = global::Android.App.Application.Context;
        var info = context.PackageManager!.GetPackageInfo(context.PackageName!, PackageInfoFlags.MetaData)!;
        // Match the process ABI, rather than the device's list of supported ABIs.
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            _ => "unsupported"
        };
        return new(info.VersionName ?? "0.0.0", architecture, context.CacheDir!.AbsolutePath, Install);
    }

    private static string Install(string file)
    {
        var activity = NativeGameService.CurrentActivity ?? throw new InvalidOperationException("Rouvre CubeShelf puis réessaie.");
        var manager = activity.PackageManager!;
        // Android also checks the APK's signature during installation. Check identity,
        // version and signing certificate first to avoid suggesting an uninstall.
#pragma warning disable CS0618, CA1422
        var candidate = manager.GetPackageArchiveInfo(file, PackageInfoFlags.Signatures)
            ?? throw new InvalidDataException("APK Android invalide.");
        var installed = manager.GetPackageInfo(activity.PackageName!, PackageInfoFlags.Signatures)!;
        if (candidate.PackageName != installed.PackageName || VersionCode(candidate) <= VersionCode(installed))
            throw new InvalidDataException("Ce paquet ne met pas à jour l'application installée.");
        var offered = candidate.Signatures?.Select(signature => signature.ToCharsString()).OrderBy(value => value).ToArray();
        var current = installed.Signatures?.Select(signature => signature.ToCharsString()).OrderBy(value => value).ToArray();
#pragma warning restore CS0618, CA1422
        if (offered is not { Length: > 0 } || current is null || !offered.SequenceEqual(current))
            throw new InvalidDataException("La signature diffère de celle de ton aperçu. Conserve tes données : ne désinstalle pas CubeShelf pour forcer cette mise à jour.");
        if (!manager.CanRequestPackageInstalls())
        {
            using var settings = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse("package:" + activity.PackageName));
            activity.StartActivity(settings);
            return "Autorise CubeShelf à installer ses mises à jour dans l'écran Android, puis reviens et touche Installer la mise à jour.";
        }
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".updates", new Java.IO.File(file));
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        activity.StartActivity(intent);
        return "Confirme la mise à jour dans l'installateur Android. Tes données restent conservées.";
    }

    private static long VersionCode(PackageInfo info)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28)) return info.LongVersionCode;
#pragma warning disable CS0618
        return info.VersionCode;
#pragma warning restore CS0618
    }
}
