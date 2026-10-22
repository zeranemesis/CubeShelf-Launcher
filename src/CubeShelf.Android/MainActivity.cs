using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

[Activity(Label = "CubeShelf", Theme = "@style/CubeShelfTheme", Icon = "@drawable/icon",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<MobileApp>
{
    private int _stickDirection;
    private long _nextStickBrowse;

    public override bool OnGenericMotionEvent(global::Android.Views.MotionEvent? motion)
    {
        if (motion is not null && (motion.Source & global::Android.Views.InputSourceType.Joystick) != 0)
        {
            var horizontal = motion.GetAxisValue(global::Android.Views.Axis.HatX);
            if (Math.Abs(horizontal) < .5f) horizontal = motion.GetAxisValue(global::Android.Views.Axis.X);
            var direction = Math.Abs(horizontal) < .55f ? 0 : horizontal < 0 ? -1 : 1;
            var now = SystemClock.UptimeMillis();
            if (direction != 0 && (direction != _stickDirection || now >= _nextStickBrowse))
            {
                View?.BrowseFromController(direction);
                _nextStickBrowse = now + (direction == _stickDirection ? 180 : 450);
            }
            _stickDirection = direction;
            if (direction != 0) return true;
        }
        return base.OnGenericMotionEvent(motion);
    }
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        NativeGameService.CurrentActivity = this;
        base.OnCreate(savedInstanceState);
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
