using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Content.PM;
using Android.Views;

namespace CubeShelf.Android;

[Activity(Name = "io.decompshelf.GameHelpActivity", Label = "Aide locale · DecompShelf", Exported = false, Process = ":assistant",
    Theme = "@android:style/Theme.Material.Dialog.NoActionBar")]
public sealed class GameHelpActivity : Activity
{
    private GameHelpPanel? _panel;
    private AndroidGameHelpVoice? _voice;
    private TaskCompletionSource<bool>? _microphonePermission;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _voice = new AndroidGameHelpVoice(this, RequestMicrophoneAsync);
        _panel = new GameHelpPanel(this, Intent?.GetStringExtra("game") ?? "Jeu sélectionné", Finish, () =>
        {
            if (Settings.CanDrawOverlays(this))
            {
                global::Android.Widget.Toast.MakeText(this, "Bip sera là au prochain lancement. Un appui sur le compagnon ouvre les choix Indice et Solution.", global::Android.Widget.ToastLength.Long)?.Show();
                return;
            }
            using var intent = new Intent(Settings.ActionManageOverlayPermission, global::Android.Net.Uri.Parse("package:" + PackageName));
            StartActivity(intent);
        }, voice: _voice);
        SetContentView(_panel);
        SetFinishOnTouchOutside(true);
        Window?.SetGravity(GravityFlags.Top | GravityFlags.Right);
        Window?.SetDimAmount(.12f);
    }
    protected override void OnResume()
    {
        base.OnResume();
        var metrics = Resources!.DisplayMetrics!;
        Window?.SetLayout(Math.Min((int)(420 * metrics.Density), metrics.WidthPixels - (int)(16 * metrics.Density)),
            Math.Min((int)(620 * metrics.Density), metrics.HeightPixels - (int)(60 * metrics.Density)));
        if (Intent?.GetBooleanExtra("voice", false) == true)
        { Intent.RemoveExtra("voice"); _ = _panel!.ListenAsync(); }
    }
    protected override void OnStop() { _panel?.Cancel(); base.OnStop(); }
    protected override void OnDestroy() { _microphonePermission?.TrySetCanceled(); _panel?.Cancel(); _voice?.Dispose(); base.OnDestroy(); }
    private async Task<bool> RequestMicrophoneAsync(CancellationToken cancellation)
    {
        if (CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) == Permission.Granted) return true;
        _microphonePermission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => _microphonePermission?.TrySetCanceled(cancellation));
        RequestPermissions([global::Android.Manifest.Permission.RecordAudio], 73);
        try { return await _microphonePermission.Task; }
        finally { _microphonePermission = null; }
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 73) _microphonePermission?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
    internal static void Open(Context context, string game, bool voice = false)
    {
        using var intent = new Intent(context, typeof(GameHelpActivity)); intent.PutExtra("game", game); intent.PutExtra("voice", voice);
        if (context is not Activity) intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
