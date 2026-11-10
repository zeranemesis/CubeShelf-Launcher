using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using CubeShelf.Assistance;

namespace CubeShelf.Android;

[Service(Name = "io.decompshelf.GameHelpOverlay", Exported = false, Process = ":assistant")]
public sealed class GameHelpOverlay : Service
{
    private IWindowManager? _windows;
    private CompanionView? _buddy;
    private GameHelpPanel? _panel;
    private string _game = "Jeu sélectionné";
    private string? _gamePackage, _gameActivity;
    private AndroidGameCapture? _capture;
    private bool _projecting;
    private bool _destroyed;
    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1));
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == "stop" || !Settings.CanDrawOverlays(this)) { StopSelf(); return StartCommandResult.NotSticky; }
        var nextGame = intent?.GetStringExtra("game");
        var nextPackage = intent?.GetStringExtra("game-package");
        var nextActivity = intent?.GetStringExtra("game-activity");
        if (nextGame is not null && (nextGame != _game || nextPackage != _gamePackage || nextActivity != _gameActivity))
        { HidePanel(); _capture?.Dispose(); _capture = null; }
        _game = nextGame ?? _game; _gamePackage = nextPackage ?? _gamePackage; _gameActivity = nextActivity ?? _gameActivity;
        _windows ??= GetSystemService(WindowService)!.JavaCast<IWindowManager>();
        try
        {
            if (intent?.Action == "capture-authorized") _projecting = true;
            UpdateForeground(false);
            if (_buddy is null) ShowBubble();
            if (intent?.Action == "listen") { ShowPanel(); _ = _panel!.ListenAsync(); }
            else if (intent?.Action == "voice-denied") _panel?.VoiceFeedback("Micro non autorisé. Tu peux continuer à écrire.");
            else if (intent?.Action == "capture-authorized") GameCapturePermission.CompleteRequest();
        }
        catch (Exception error) { global::Android.Util.Log.Warn("DecompShelf.Help", error.Message); StopSelf(); }
        finally { if (intent?.Action is "listen" or "voice-denied") GameHelpMicrophonePermission.CompleteRequest(); }
        return StartCommandResult.NotSticky;
    }

    private void UpdateForeground(bool microphone)
    {
        if (_destroyed) return;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("game-help", "Compagnon en jeu", NotificationImportance.Low));
        using var stop = new Intent(this, typeof(GameHelpOverlay)).SetAction("stop");
        var pending = PendingIntent.GetService(this, 0, stop, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(this, "game-help").SetSmallIcon(global::Android.Resource.Drawable.IcMenuHelp)
            .SetContentTitle("Bip · " + _game)
            .SetContentText(microphone ? "Micro actif pour cette question · Arrêter coupe l’écoute." : _projecting ? "Partage d’écran autorisé · analyse uniquement sur demande." : "Un appui sur Bip, puis Indice ou Solution.")
            .AddAction(new Notification.Action.Builder(null, "Masquer le compagnon", pending).Build()).SetOngoing(true).Build();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            // SPECIAL_USE was introduced after this .NET Android enum; its Android constant is bit 30.
            var type = OperatingSystem.IsAndroidVersionAtLeast(34) ? (ForegroundService)0x40000000 : ForegroundService.TypeNone;
            if (microphone && OperatingSystem.IsAndroidVersionAtLeast(30)) type |= ForegroundService.TypeMicrophone;
            if (_projecting) type |= ForegroundService.TypeMediaProjection;
            StartForeground(71, notification, type);
        }
        else StartForeground(71, notification);
    }
    private WindowManagerLayoutParams Layout(int width, int height, bool focusable, int top)
    {
        var flags = WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutInScreen;
        if (!focusable) flags |= WindowManagerFlags.NotFocusable;
        return new WindowManagerLayoutParams(width, height, WindowManagerTypes.ApplicationOverlay, flags, Format.Translucent)
        { Gravity = GravityFlags.Top | GravityFlags.Right, X = Dp(8), Y = Dp(top), SoftInputMode = SoftInput.AdjustResize };
    }
    private void ShowBubble()
    {
        if (_buddy is not null) return;
        _buddy = new CompanionView(this) { Clickable = true, LongClickable = true,
            ContentDescription = "Aide DecompShelf : un appui pour choisir Indice ou Solution" };
        _buddy.Click += (_, _) => { if (_panel is null) ShowPanel(); else HidePanel(); };
        _buddy.LongClick += (_, args) => { args.Handled = true; if (_panel is null) ShowPanel(); else HidePanel(); };
        _windows!.AddView(_buddy, Layout(Dp(64), Dp(80), false, 24));
    }
    private void HidePanel()
    {
        if (_panel is not null)
        {
            _panel.Cancel();
            _windows?.RemoveView(_panel); _panel.Dispose(); _panel = null;
        }
        if (_buddy is not null) _buddy.State = CompanionState.Idle;
    }
    private void ShowPanel()
    {
        if (_panel is not null) return;
        var voice = new AndroidGameHelpVoice(this, cancellation =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != Permission.Granted) return Task.FromResult(false);
            UpdateForeground(true); return Task.FromResult(true);
        }, () => UpdateForeground(false));
        _capture ??= new AndroidGameCapture(this, projecting => { _projecting = projecting; if (!projecting) _panel?.Cancel(); UpdateForeground(false); });
        _panel = new GameHelpPanel(this, _game, HidePanel, voice: voice,
            openVoice: () => GameHelpMicrophonePermission.Request(this), compact: true,
            stateChanged: state => { if (_buddy is not null) _buddy.State = state; }, capture: CaptureAsync,
            expanded: () => { if (_panel is not null) _windows!.UpdateViewLayout(_panel, PanelLayout(true)); });
        _windows!.AddView(_panel, PanelLayout(false));
    }
    private WindowManagerLayoutParams PanelLayout(bool expanded)
    {
        var metrics = Resources!.DisplayMetrics!;
        var layout = Layout(Math.Min(Dp(330), Math.Max(Dp(180), metrics.WidthPixels - Dp(100))),
            Math.Min(Dp(expanded ? 360 : 156), metrics.HeightPixels - Dp(48)), true, 24);
        layout.X = Dp(84); return layout;
    }
    private async Task<GameFrame> CaptureAsync(CancellationToken cancellation)
    {
        try { return await _capture!.CaptureAsync(cancellation, () =>
        {
            if (_buddy is not null) _buddy.Visibility = ViewStates.Invisible;
            if (_panel is not null) _panel.Visibility = ViewStates.Invisible;
        }); }
        finally
        {
            if (!_destroyed)
            {
                if (_buddy is not null) _buddy.Visibility = ViewStates.Visible;
                if (_panel is not null) _panel.Visibility = ViewStates.Visible;
            }
        }
    }
    public override void OnDestroy()
    {
        _destroyed = true;
        HidePanel();
        _capture?.Dispose(); _capture = null;
        if (_buddy is not null) { _windows?.RemoveView(_buddy); _buddy.Dispose(); _buddy = null; }
        base.OnDestroy();
    }
    internal static void StartForGame(Activity activity, string title, string? gamePackage = null, string? gameActivity = null)
    {
        if (!Settings.CanDrawOverlays(activity)) return;
        try
        {
            using var intent = new Intent(activity, typeof(GameHelpOverlay)); intent.PutExtra("game", title);
            intent.PutExtra("game-package", gamePackage); intent.PutExtra("game-activity", gameActivity);
            activity.StartForegroundService(intent);
        }
        catch (Exception error) { global::Android.Util.Log.Warn("DecompShelf.Help", error.Message); }
    }
    internal static void Stop(Context context) { using var intent = new Intent(context, typeof(GameHelpOverlay)); context.StopService(intent); }
}
