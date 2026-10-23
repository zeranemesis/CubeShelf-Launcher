using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using CubeShelf.Mobile;
using AndroidX.Core.View;

namespace CubeShelf.Android;

[Activity(Label = "CubeShelf", Theme = "@style/CubeShelfTheme", Icon = "@drawable/icon",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<MobileApp>
{
    private int _stickDirection;
    private bool _stickVertical;
    private long _nextStickBrowse;

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
                global::Android.Views.Keycode.ButtonB => view.FocusPrimaryAction,
                global::Android.Views.Keycode.ButtonX => view.FlipFromController,
                global::Android.Views.Keycode.ButtonY => view.OpenOnlineFromController,
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
        base.OnCreate(savedInstanceState);
        ApplyImmersiveMode();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        MobileApp.Games = new AndroidGames();
        return base.CustomizeAppBuilder(builder);
    }

    protected override void OnResume()
    {
        NativeGameService.CurrentActivity = this;
        base.OnResume();
        ApplyImmersiveMode();
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
        if (ReferenceEquals(NativeGameService.CurrentActivity, this)) NativeGameService.CurrentActivity = null;
        base.OnDestroy();
    }

    private static MobileLibraryView? View =>
        (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView as MobileLibraryView;

    private sealed class AndroidGames : IMobileGameService
    {
        public string DataDirectory => global::Android.App.Application.Context.FilesDir!.AbsolutePath;
        public string? LastError => NativeGameService.LastError;
        public bool IsInstalled() => NativeGameService.IsInstalled();
        public bool DeviceSupportsPublishedGame() => NativeGameService.DeviceSupportsPublishedGame();
        public bool OpenGame() => NativeGameService.OpenGame();
        public bool OpenOnline(string? invitation = null) => NativeGameService.OpenOnline(invitation);
        public bool OpenDownloadPage() => NativeGameService.OpenDownloadPage();
    }
}
