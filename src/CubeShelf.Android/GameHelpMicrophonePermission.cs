using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace CubeShelf.Android;

/// <summary>No help page: only the system microphone prompt, then return to the existing overlay.</summary>
[Activity(Name = "io.decompshelf.GameHelpMicrophonePermission", Exported = false, Process = ":assistant",
    ExcludeFromRecents = true, TaskAffinity = "io.decompshelf.assistant", Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class GameHelpMicrophonePermission : Activity
{
    private static WeakReference<GameHelpMicrophonePermission>? _pending;
    private bool _requested;
    protected override void OnResume()
    {
        base.OnResume();
        if (_requested) return;
        _requested = true;
        _pending = new(this);
        if (CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) == Permission.Granted) Complete(true);
        else RequestPermissions([global::Android.Manifest.Permission.RecordAudio], 74);
    }
    public override void OnRequestPermissionsResult(int code, string[] permissions, Permission[] grants)
    {
        base.OnRequestPermissionsResult(code, permissions, grants);
        if (code == 74) Complete(grants.Length > 0 && grants[0] == Permission.Granted);
    }
    private void Complete(bool granted)
    {
        using var intent = new Intent(this, typeof(GameHelpOverlay)).SetAction(granted ? "listen" : "voice-denied");
        // Start while this transparent permission host is visible: Android requires foreground microphone access.
        try { StartForegroundService(intent); }
        catch (Exception error) { global::Android.Util.Log.Warn("DecompShelf.Help", error.Message); Finish(); }
    }
    internal static void CompleteRequest()
    {
        if (_pending?.TryGetTarget(out var activity) == true) activity.Finish();
        _pending = null;
    }
    protected override void OnDestroy()
    {
        if (_pending?.TryGetTarget(out var activity) == true && ReferenceEquals(activity, this)) _pending = null;
        base.OnDestroy();
    }
    internal static void Request(Context context)
    {
        using var intent = new Intent(context, typeof(GameHelpMicrophonePermission)); intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
