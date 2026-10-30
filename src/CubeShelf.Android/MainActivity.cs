using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using CubeShelf.Mobile;
using AndroidX.Core.View;

namespace CubeShelf.Android;

[Activity(Label = "DecompShelf", Theme = "@style/CubeShelfTheme", Icon = "@drawable/icon",
    MainLauncher = true, Exported = true,
    // Let Android recreate the native view/window on rotation. Avalonia's single-view
    // lifetime retains the library controls, while the replacement view gets a fresh
    // Vulkan surface instead of presenting to the window destroyed by the game.
    ConfigurationChanges = ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<MobileApp>
{
    private int _stickDirection;
    private bool _stickVertical;
    private long _nextStickBrowse;
    private AndroidAnimationFrames? _animationFrames;
    private RenderProfiler? _renderProfiler;

    public override bool DispatchKeyEvent(global::Android.Views.KeyEvent? keyEvent)
    {
        if (keyEvent is not null && View is { } view)
        {
            var gamepad = (keyEvent.Source & global::Android.Views.InputSourceType.Gamepad)
                == global::Android.Views.InputSourceType.Gamepad
                || (keyEvent.Source & global::Android.Views.InputSourceType.Dpad)
                == global::Android.Views.InputSourceType.Dpad;
            Action? action = keyEvent.KeyCode switch
            {
                global::Android.Views.Keycode.ButtonA => view.ActivateFromController,
                global::Android.Views.Keycode.ButtonB => view.BackFromController,
                global::Android.Views.Keycode.ButtonX => view.FlipFromController,
                global::Android.Views.Keycode.ButtonY => view.OpenOnlineFromController,
                global::Android.Views.Keycode.ButtonThumbr => view.InspectFromController,
                global::Android.Views.Keycode.DpadLeft when gamepad => () => view.BrowseFromController(-1),
                global::Android.Views.Keycode.DpadRight when gamepad => () => view.BrowseFromController(1),
                global::Android.Views.Keycode.DpadUp when gamepad => () => view.MoveActionFocus(-1),
                global::Android.Views.Keycode.DpadDown when gamepad => () => view.MoveActionFocus(1),
                _ => null
            };
            if (action is not null)
            {
                if (keyEvent.Action == global::Android.Views.KeyEventActions.Down && keyEvent.RepeatCount == 0) action();
                return true;
            }
        }
        return base.DispatchKeyEvent(keyEvent);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) ApplyImmersiveMode();
        if (hasFocus) Window?.DecorView.PostDelayed(() =>
            global::Android.Util.Log.Info("CubeShelf.Layout", View?.DescribeLayout() ?? "View not attached"), 1000);
    }

    private void ApplyImmersiveMode()
    {
        if (Window is not { } window) return;
        // Keep system navigation available with an edge swipe on Android 8+.
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        controller.Hide(WindowInsetsCompat.Type.SystemBars());
    }

    public override bool OnGenericMotionEvent(global::Android.Views.MotionEvent? motion)
    {
        if (motion is not null && (motion.Source & global::Android.Views.InputSourceType.Joystick)
            == global::Android.Views.InputSourceType.Joystick)
        {
            var horizontal = motion.GetAxisValue(global::Android.Views.Axis.HatX);
            if (Math.Abs(horizontal) < .5f) horizontal = motion.GetAxisValue(global::Android.Views.Axis.X);
            var vertical = motion.GetAxisValue(global::Android.Views.Axis.HatY);
            if (Math.Abs(vertical) < .5f) vertical = motion.GetAxisValue(global::Android.Views.Axis.Y);
            var isVertical = Math.Abs(vertical) > Math.Abs(horizontal);
            var axis = isVertical ? vertical : horizontal;
            var direction = Math.Abs(axis) < .55f ? 0 : axis < 0 ? -1 : 1;
            var now = SystemClock.UptimeMillis();
            if (direction != 0 && (direction != _stickDirection || isVertical != _stickVertical || now >= _nextStickBrowse))
            {
                if (isVertical) View?.MoveActionFocus(direction);
                else View?.BrowseFromController(direction);
                _nextStickBrowse = now + (direction == _stickDirection && isVertical == _stickVertical ? 180 : 450);
            }
            _stickDirection = direction;
            _stickVertical = isVertical;
            if (direction != 0) return true;
        }
        return base.OnGenericMotionEvent(motion);
    }
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        NativeGameService.CurrentActivity = this;
        _animationFrames = new AndroidAnimationFrames();
        MobileApp.AnimationFrameScheduler = _animationFrames.Request;
        base.OnCreate(savedInstanceState);
        // Controls allocate Avalonia objects in their static initializers. Configure
        // diagnostics only after Android has installed Avalonia's UI dispatcher.
        ConfigureRenderProfiling(Intent);
        OpenRequestedPanel(Intent);
        OnBackPressedDispatcher.AddCallback(this, new LibraryBackCallback(this));
        ApplyImmersiveMode();
        RequestHighRefreshRate();
    }

    private void RequestHighRefreshRate()
    {
        if (Window is not { } window || window.Attributes is not { } attributes) return;
        // A window-local hint: Android retains control of battery, thermal and user policies.
        // Do not pick a display mode, which could also change the user's resolution.
        attributes.PreferredRefreshRate = 120f;
        window.Attributes = attributes;
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        ConfigureRenderProfiling(intent);
        OpenRequestedPanel(intent);
    }

    private static void OpenRequestedPanel(global::Android.Content.Intent? intent)
    {
        if (intent?.GetBooleanExtra("cubeshelf_downloads", false) != true) return;
        // Consume the notification action: an orientation recreation must retain
        // the current screen, rather than reopen an already dismissed panel.
        intent.RemoveExtra("cubeshelf_downloads");
        View?.ShowDownloads();
    }

    private void ConfigureRenderProfiling(global::Android.Content.Intent? intent)
    {
        var enabled = intent?.GetBooleanExtra("cubeshelf_profile", false) == true;
        _renderProfiler = enabled ? new RenderProfiler() : null;
        var batch = intent?.GetBooleanExtra("cubeshelf_gpu_batch", true) != false;
        if (_renderProfiler is { } profiler)
        {
            MobileApp.ConfigureRenderDiagnostics(batch, profiler.Frontend, profiler.Backend);
            if (_animationFrames is { } frames) frames.FrameTiming = profiler.Vsync;
        }
        else
        {
            MobileApp.ConfigureRenderDiagnostics(batch, null, null);
            if (_animationFrames is { } frames) frames.FrameTiming = null;
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        MobileApp.Games = new AndroidGames();
        MobileApp.HelpRequested = title => { if (NativeGameService.CurrentActivity is { } activity) GameHelpActivity.Open(activity, title); };
        return base.CustomizeAppBuilder(builder).With(new AndroidPlatformOptions
        {
            RenderingMode = Intent?.GetBooleanExtra("cubeshelf_egl", false) == true
                ? [AndroidRenderingMode.Egl, AndroidRenderingMode.Vulkan, AndroidRenderingMode.Software]
                : [AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl, AndroidRenderingMode.Software]
        });
    }

    protected override void OnResume()
    {
        GameHelpOverlay.Stop(this);
        NativeGameService.CurrentActivity = this;
        base.OnResume();
        ApplyImmersiveMode();
        RequestHighRefreshRate();
        View?.SetActive(true);
    }

    protected override void OnPause()
    {
        _stickDirection = 0;
        View?.SetActive(false);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(NativeGameService.CurrentActivity, this))
        {
            NativeGameService.CurrentActivity = null;
            MobileApp.AnimationFrameScheduler = null;
            MobileApp.ConfigureRenderDiagnostics(true, null, null);
        }
        _animationFrames?.Stop();
        _animationFrames?.Dispose();
        _animationFrames = null;
        base.OnDestroy();
    }

    private static MobileLibraryView? View =>
        (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView as MobileLibraryView;

    private sealed class LibraryBackCallback(MainActivity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (View?.DismissOverlay() == true) return;
            Enabled = false;
            try { activity.OnBackPressedDispatcher.OnBackPressed(); }
            finally { Enabled = true; }
        }
    }

    private sealed class AndroidGames : IMobileGameService
    {
        public string DataDirectory => global::Android.App.Application.Context.FilesDir!.AbsolutePath;
        public string? LastError => NativeGameService.LastError;
        public IRuntimeDownloads Downloads => NativeGameService.Downloads;
        public LauncherAppUpdates AppUpdates => AndroidAppUpdates.Instance;
        public IMobileNativePorts NativePorts => AndroidNativePorts.Instance;
        public MobileDeviceStatus DeviceStatus => AndroidDeviceStatus.Read();
        public bool IsInstalled() => NativeGameService.IsInstalled();
        public bool DeviceSupportsPublishedGame() => NativeGameService.DeviceSupportsPublishedGame();
        public bool OpenGame() => NativeGameService.OpenGame();
        public bool OpenOnline(string? invitation = null) => NativeGameService.OpenOnline(invitation);
        public bool OpenDownloadPage() => NativeGameService.OpenDownloadPage();
    }
}
