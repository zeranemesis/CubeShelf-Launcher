#if ANDROID && DEBUG
using Android.App;
using Android.AccessibilityServices;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using Android.Runtime;
using Android.Views;

namespace CubeShelf.Android;

/// <summary>Debug-only device probe: exercises the real service, accessibility button and single tap.</summary>
[Instrumentation(Name = "io.decompshelf.HelpOverlayProbe", TargetPackage = "io.github.zeranemesis.cubeshelf")]
public sealed class HelpOverlayProbe : Instrumentation
{
    private string? _game;
    private string? _gamePackage, _gameActivity;
    private bool _interactive;
    private string _shareLabel = "DecompShelf · test visuel";
    public HelpOverlayProbe() { }
    public HelpOverlayProbe(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }
    public override void OnCreate(Bundle? arguments) { _interactive = arguments?.GetString("interactive") == "true"; _game = arguments?.GetString("game"); _gamePackage = arguments?.GetString("gamePackage"); _gameActivity = arguments?.GetString("gameActivity"); _shareLabel = arguments?.GetString("shareLabel") ?? _shareLabel; base.OnCreate(arguments); Start(); }
    public override void OnStart()
    {
        var result = new Bundle();
        try
        {
            if (_interactive)
            {
                using var start = new Intent(TargetContext!, typeof(GameHelpOverlay)); start.PutExtra("game", _game ?? "Mario Party 4");
                start.PutExtra("game-package", _gamePackage); start.PutExtra("game-activity", _gameActivity);
                TargetContext!.StartForegroundService(start);
                using var ready = new Bundle(); ready.PutString("phase", "Companion ready for manual ADB gameplay QA"); SendStatus(0, ready);
                for (var i = 0; i < 1440; i++) SystemClock.Sleep(500);
                Finish(Result.Ok, result); return;
            }
            var automation = UiAutomation!;
            var info = automation.ServiceInfo!;
            info.Flags |= AccessibilityServiceFlags.RetrieveInteractiveWindows | AccessibilityServiceFlags.ReportViewIds;
            automation.ServiceInfo = info;
            var visual = new CubeShelf.Assistance.LocalGameHelp(Path.Combine(TargetContext!.FilesDir!.AbsolutePath, "Assistance")).IsVisionInstalled;
            if (visual && _game is null)
            {
                using var fixture = new Intent(TargetContext, typeof(HelpCaptureFixture)); fixture.AddFlags(ActivityFlags.NewTask);
                TargetContext.StartActivity(fixture); SystemClock.Sleep(1200);
            }
            using var intent = new Intent(TargetContext!, typeof(GameHelpOverlay));
            intent.PutExtra("game", _game ?? (visual ? "Test visuel" : "Super Mario 64"));
            intent.PutExtra("game-package", _gamePackage ?? (visual ? TargetContext.PackageName : null));
            intent.PutExtra("game-activity", _gameActivity ?? (visual ? "io.decompshelf.HelpCaptureFixture" : null));
            TargetContext!.StartForegroundService(intent);
            AccessibilityNodeInfo? Find(Func<AccessibilityNodeInfo, bool> predicate)
            {
                AccessibilityNodeInfo? Walk(AccessibilityNodeInfo? node)
                {
                    if (node is null) return null;
                    if (predicate(node)) return node;
                    for (var i = 0; i < node.ChildCount; i++) if (Walk(node.GetChild(i)) is { } found) return found;
                    return null;
                }
                foreach (var window in automation.Windows ?? []) if (Walk(window.Root) is { } found) return found;
                return null;
            }
            AccessibilityNodeInfo? button = null;
            for (var i = 0; i < 40 && button is null; i++)
            { SystemClock.Sleep(250); button = Find(node => node.ContentDescription?.ToString()?.StartsWith("Aide DecompShelf") == true); }
            if (button is null) throw new Exception("The overlay button is missing from accessible windows.");
            using var bounds = new global::Android.Graphics.Rect();
            button.GetBoundsInScreen(bounds);
            for (var i = 0; i < 1; i++)
            {
                var time = SystemClock.UptimeMillis();
                using var down = MotionEvent.Obtain(time, time, MotionEventActions.Down, bounds.CenterX(), bounds.CenterY(), 0)!;
                using var up = MotionEvent.Obtain(time, time + 40, MotionEventActions.Up, bounds.CenterX(), bounds.CenterY(), 0)!;
                down.SetSource(InputSourceType.Touchscreen);
                up.SetSource(InputSourceType.Touchscreen);
                if (!automation.InjectInputEvent(down, true) || !automation.InjectInputEvent(up, true)) throw new Exception("Overlay touch rejected.");
                SystemClock.Sleep(150);
            }
            AccessibilityNodeInfo? heading = null;
            for (var i = 0; i < 24 && heading is null; i++)
            { SystemClock.Sleep(250); heading = Find(node => node.Text?.ToString()?.Equals("Indice", StringComparison.OrdinalIgnoreCase) == true); }
            if (heading is null) throw new Exception("One tap did not open the two choices.");
            if (Find(node => node.ContentDescription?.ToString()?.StartsWith("Aide DecompShelf") == true) is null)
                throw new Exception("Opening help removed the companion instead of keeping it beside the bubble.");
            if (Find(node => node.Text?.ToString()?.Equals("Réduire", StringComparison.OrdinalIgnoreCase) == true) is null)
                throw new Exception("The compact bubble has no accessible reduce action.");
            using (var image = automation.TakeScreenshot())
            using (var output = File.Create(Path.Combine(TargetContext.FilesDir!.AbsolutePath, "help-overlay-probe.png")))
                image!.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 100, output);
            if (Find(node => node.ClassName?.ToString() == "android.widget.EditText") is not null)
                throw new Exception("Opening the choices exposed a mandatory writing form.");
            if (Find(node => node.Text?.ToString()?.Equals("Solution", StringComparison.OrdinalIgnoreCase) == true) is null)
                throw new Exception("Solution choice missing.");
            if (visual)
            {
                heading.PerformAction(global::Android.Views.Accessibility.Action.Click);
                AccessibilityNodeInfo? done = null;
                for (var i = 0; i < 360 && done is null; i++)
                {
                    SystemClock.Sleep(500);
                    if (Find(node => node.Text?.ToString()?.Contains("Compatibilité des applis Android") == true) is not null)
                    {
                        Find(node => node.Text?.ToString()?.Equals("OK", StringComparison.OrdinalIgnoreCase) == true)?.PerformAction(global::Android.Views.Accessibility.Action.Click);
                        continue;
                    }
                    var allow = Find(node => node.PackageName?.ToString() == "com.android.systemui" && node.ClassName?.ToString()?.Contains("Button") == true &&
                        new[] { "Start now", "Démarrer", "Commencer", "Partager", "Continuer", "Suivant" }.Any(text => node.Text?.ToString()?.StartsWith(text, StringComparison.OrdinalIgnoreCase) == true));
                    if (allow is not null)
                    {
                        using var consentBounds = new global::Android.Graphics.Rect(); allow.GetBoundsInScreen(consentBounds);
                        var consentTime = SystemClock.UptimeMillis();
                        using var consentDown = MotionEvent.Obtain(consentTime, consentTime, MotionEventActions.Down, consentBounds.CenterX(), consentBounds.CenterY(), 0)!;
                        using var consentUp = MotionEvent.Obtain(consentTime, consentTime + 50, MotionEventActions.Up, consentBounds.CenterX(), consentBounds.CenterY(), 0)!;
                        consentDown.SetSource(InputSourceType.Touchscreen); consentUp.SetSource(InputSourceType.Touchscreen);
                        automation.InjectInputEvent(consentDown, true); automation.InjectInputEvent(consentUp, true); continue;
                    }
                    Find(node => node.PackageName?.ToString() == "com.android.systemui" && node.Text?.ToString() == _shareLabel)?.PerformAction(global::Android.Views.Accessibility.Action.Click);
                    done = Find(node => node.Text?.ToString()?.StartsWith("Terminé ·") == true);
                }
                if (done is null) throw new Exception("Local inference did not complete within three minutes.");
                using var image = automation.TakeScreenshot();
                using var output = File.Create(Path.Combine(TargetContext.FilesDir!.AbsolutePath, "help-response-probe.png"));
                image!.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 100, output);
                result.PutString("inference", "PASS: actual MediaProjection capture and local vision response, without writing.");
            }
            result.PutString("result", "PASS: real overlay service, one tap, two choices, no writing form.");
            Finish(Result.Ok, result);
        }
        catch (Exception error) { result.PutString("error", error.ToString()); Finish(Result.Canceled, result); }
    }
}

[Activity(Name = "io.decompshelf.HelpCaptureFixture", Label = "DecompShelf · test visuel", Exported = false, Process = ":vision_fixture",
    TaskAffinity = "io.decompshelf.vision_fixture", ExcludeFromRecents = true,
    ScreenOrientation = global::Android.Content.PM.ScreenOrientation.SensorLandscape, Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class HelpCaptureFixture : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Window!.AddFlags(WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn);
        SetContentView(new Fixture(this));
    }
    private sealed class Fixture(Context context) : View(context)
    {
        protected override void OnDraw(global::Android.Graphics.Canvas canvas)
        {
            base.OnDraw(canvas); canvas.DrawColor(global::Android.Graphics.Color.Rgb(28, 39, 54));
            canvas.Save(); canvas.Scale(Width / 1100f, Height / 650f);
            using var paint = new global::Android.Graphics.Paint(global::Android.Graphics.PaintFlags.AntiAlias) { TextSize = 36, Color = global::Android.Graphics.Color.White };
            canvas.DrawText("TEST VISION", 60, 70, paint);
            paint.Color = global::Android.Graphics.Color.Red; canvas.DrawRect(70, 150, 300, 460, paint);
            paint.Color = global::Android.Graphics.Color.Blue; canvas.DrawRect(380, 150, 610, 460, paint);
            paint.Color = global::Android.Graphics.Color.Green; canvas.DrawRect(800, 150, 1000, 460, paint);
            paint.Color = global::Android.Graphics.Color.White;
            canvas.DrawText("ROUGE", 100, 510, paint); canvas.DrawText("BLEU", 420, 510, paint); canvas.DrawText("PORTE", 830, 510, paint);
            canvas.DrawText("Objectif : rejoindre la porte verte", 60, 600, paint); canvas.Restore();
        }
    }
}
#endif
