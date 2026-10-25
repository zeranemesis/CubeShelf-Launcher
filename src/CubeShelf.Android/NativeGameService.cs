using CubeShelf.Mobile;

namespace CubeShelf.Android;

/// <summary>PartyBoard activities and native engine are part of the CubeShelf package.</summary>
public static class NativeGameService
{
    private const string GameActivity = "com.mariopartyrd.partyboard.PartyBoardActivity";
    private const string OnlineActivity = "com.mariopartyrd.partyboard.online.LobbyActivity";
    private static WeakReference<global::Android.App.Activity>? _activity;
    private static PartyBoardDownloads? _downloads;
    private static RuntimeDownloadQueue? _queue;
    private static PartyBoardDownloads Engine => _downloads ??= new PartyBoardDownloads(
        global::Android.App.Application.Context.FilesDir!.AbsolutePath, DeviceSupportsPublishedGame());
    internal static RuntimeDownloadQueue Downloads => _queue ??= CreateDownloads();
    public static string? LastError { get; private set; }

    private static RuntimeDownloadQueue CreateDownloads()
    {
        var context = global::Android.App.Application.Context;
        var downloads = new RuntimeDownloadQueue(context.FilesDir!.AbsolutePath,
            new RuntimeDownloadSource("partyboard", "GMPE01_00", "Mario Party 4", PartyBoardDownloads.Version, Engine, Engine.InstalledAt));
        downloads.Changed += snapshot => RuntimeDownloadService.Observe(snapshot);
        return downloads;
    }

    public static global::Android.App.Activity? CurrentActivity
    {
        get
        {
            if (_activity is null || !_activity.TryGetTarget(out var activity)) return null;
            try { return !activity.IsFinishing && !activity.IsDestroyed ? activity : null; }
            catch (ObjectDisposedException) { return null; }
        }
        set => _activity = value is null ? null : new WeakReference<global::Android.App.Activity>(value);
    }

    public static bool DeviceSupportsPublishedGame()
    {
        var context = global::Android.App.Application.Context;
        return global::Android.OS.Build.SupportedAbis?.Contains("arm64-v8a") == true
            && File.Exists(Path.Combine(context.ApplicationInfo!.NativeLibraryDir!, "libdol.so"));
    }

    public static bool IsInstalled() => Engine.IsReady;
    public static bool OpenGame() => OpenNativeActivity(GameActivity, null);
    public static bool OpenOnline(string? invitation = null)
    {
        var code = invitation?.Trim();
        if (!string.IsNullOrEmpty(code) && !IsValidInvitation(code))
        {
            LastError = "L’invitation PB4 est incomplète. Copie le code entier depuis le salon de ton ami.";
            return false;
        }
        return OpenNativeActivity(OnlineActivity, code);
    }

    // Kept for platform-host compatibility: this now starts the internal download.
    public static bool OpenDownloadPage()
    {
        LastError = null;
        if (!DeviceSupportsPublishedGame()) { LastError = "Le moteur intégré nécessite Android ARM64."; return false; }
        Downloads.Start();
        return true;
    }

    private static bool OpenNativeActivity(string className, string? invitation)
    {
        LastError = null;
        if (CurrentActivity is not { } activity) { LastError = "Rouvre CubeShelf puis réessaie."; return false; }
        if (!IsInstalled()) { LastError = "Prépare PartyBoard dans Téléchargements avant de jouer."; return false; }
        try
        {
            using var intent = new global::Android.Content.Intent();
            intent.SetClassName(activity.PackageName!, className);
            if (!string.IsNullOrEmpty(invitation)) intent.PutExtra("invitation", invitation);
            intent.PutExtra("language", "fr");
            activity.StartActivity(intent);
            return true;
        }
        catch (Exception error)
        {
            global::Android.Util.Log.Error("CubeShelf.PartyBoard", error.ToString());
            LastError = "Impossible d’ouvrir le moteur intégré. Relance CubeShelf puis réessaie.";
            return false;
        }
    }

    private static bool IsValidInvitation(string code)
    {
        if (code.Length != 72 || !code.StartsWith("PB4.", StringComparison.Ordinal)) return false;
        try
        {
            var payload = Convert.FromBase64String(code[4..].Replace('-', '+').Replace('_', '/'));
            return payload.Length == 49 && payload[48] is >= 2 and <= 4;
        }
        catch (FormatException) { return false; }
    }
}
