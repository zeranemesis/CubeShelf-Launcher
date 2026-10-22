using System;

namespace CubeShelf.Android;

/// <summary>Starts the official native Party Board Android port in its own app.</summary>
public static class NativeGameService
{
    public const string PackageName = "com.mariopartyrd.partyboard";
    public const string DownloadUrl = "https://github.com/zeranemesis/Marioparty4/releases/download/v0.20.0/PartyBoard-v0.20.0-phone-arm64-v8a.apk";

    private const string GameActivity = PackageName + ".PartyBoardActivity";
    private const string OnlineActivity = PackageName + ".online.LobbyActivity";
    private static WeakReference<global::Android.App.Activity>? _activity;

    /// <summary>Set by the launcher activity; no activity is retained after Android destroys it.</summary>
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

    /// <summary>The French explanation of the last failed action, or null after success.</summary>
    public static string? LastError { get; private set; }

    /// <summary>The official published APK contains ARM64 libraries only.</summary>
    public static bool DeviceSupportsPublishedGame()
    {
        var supported = global::Android.OS.Build.SupportedAbis;
        if (supported is null) return false;
        foreach (var abi in supported)
            if (string.Equals(abi, "arm64-v8a", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsInstalled()
    {
        LastError = null;
        var activity = GetActivity();
        if (activity is null) return false;
        try
        {
            var application = activity.PackageManager?.GetApplicationInfo(PackageName,
                (global::Android.Content.PM.PackageInfoFlags)0);
            if (application is { Enabled: true }) return true;
            LastError = application is null ? MissingGameMessage : "Party Board est désactivé. Active-le dans les paramètres Android.";
            return false;
        }
        catch (global::Android.Content.PM.PackageManager.NameNotFoundException)
        {
            LastError = MissingGameMessage;
            return false;
        }
        catch (Exception exception)
        {
            LastError = "Impossible de vérifier Party Board : " + exception.Message;
            return false;
        }
    }

    /// <summary>The native game keeps its own disc selection and file permissions.</summary>
    public static bool OpenGame() => OpenNativeActivity(GameActivity, null);

    /// <summary>Opens the native lobby, optionally with a complete PB4 invitation.</summary>
    public static bool OpenOnline(string? invitation = null)
    {
        LastError = null;
        var code = invitation?.Trim();
        if (!string.IsNullOrEmpty(code) && !IsValidInvitation(code))
        {
            LastError = "L’invitation PB4 est incomplète ou invalide. Copie le code entier depuis le salon de ton ami.";
            return false;
        }
        return OpenNativeActivity(OnlineActivity, string.IsNullOrEmpty(code) ? null : code);
    }

    /// <summary>Opens the official APK URL in a browser; installation remains with Android.</summary>
    public static bool OpenDownloadPage()
    {
        LastError = null;
        if (!DeviceSupportsPublishedGame())
        {
            LastError = UnsupportedDownloadMessage;
            return false;
        }
        var activity = GetActivity();
        if (activity is null) return false;
        try
        {
            using var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView,
                global::Android.Net.Uri.Parse(DownloadUrl));
            activity.StartActivity(intent);
            return true;
        }
        catch (global::Android.Content.ActivityNotFoundException)
        {
            LastError = "Aucun navigateur ne peut ouvrir GitHub. Installe un navigateur pour télécharger Party Board.";
            return false;
        }
        catch (Exception exception)
        {
            LastError = "Impossible d’ouvrir le téléchargement officiel : " + exception.Message;
            return false;
        }
    }

    private const string UnsupportedDownloadMessage = "L’APK officiel de Party Board est disponible uniquement pour Android ARM64. Cet appareil ne prend pas en charge arm64-v8a ; aucun téléchargement compatible n’est proposé.";
    private static string MissingGameMessage => DeviceSupportsPublishedGame()
        ? "Party Board natif n’est pas installé. Télécharge l’APK officiel, ouvre-le pour l’installer, puis reviens dans CubeShelf."
        : UnsupportedDownloadMessage;

    private static global::Android.App.Activity? GetActivity()
    {
        var activity = CurrentActivity;
        if (activity is null) LastError = "CubeShelf n’est plus actif. Rouvre le lanceur puis réessaie.";
        return activity;
    }

    private static bool OpenNativeActivity(string activityName, string? invitation)
    {
        LastError = null;
        var activity = GetActivity();
        if (activity is null) return false;
        try
        {
            using var intent = new global::Android.Content.Intent();
            intent.SetClassName(PackageName, activityName);
            if (invitation is not null) intent.PutExtra("invitation", invitation);
            activity.StartActivity(intent);
            return true;
        }
        catch (global::Android.Content.ActivityNotFoundException)
        {
            LastError = MissingGameMessage;
            return false;
        }
        catch (global::Java.Lang.SecurityException)
        {
            LastError = "Android a refusé d’ouvrir Party Board. Vérifie que l’APK officiel est installé et activé.";
            return false;
        }
        catch (Exception exception)
        {
            LastError = "Impossible d’ouvrir Party Board : " + exception.Message;
            return false;
        }
    }

    private static bool IsValidInvitation(string code)
    {
        if (code.Length != 72 || !code.StartsWith("PB4.", StringComparison.Ordinal)) return false;
        try
        {
            var payload = Convert.FromBase64String(code[4..].Replace('-', '+').Replace('_', '/'));
            // The native lobby checks expiry, peer address, TLS pin and build compatibility.
            return payload.Length == 49 && payload[48] is >= 2 and <= 4;
        }
        catch (FormatException) { return false; }
    }
}
