using Android.App;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

internal sealed class AndroidHomeDisplays : Java.Lang.Object,DisplayManager.IDisplayListener
{
    private readonly MainActivity _activity;
    private readonly MobileLibraryView _library;
    private readonly DisplayManager? _manager;
    private readonly Handler _handler=new(Looper.MainLooper!);
    private HomePresentation? _presentation;
    private bool _resumed,_refreshing,_disposed;

    internal AndroidHomeDisplays(MainActivity activity,MobileLibraryView library)
    {
        _activity=activity;_library=library;
        _manager=activity.GetSystemService(global::Android.Content.Context.DisplayService) as DisplayManager;
        library.SecondaryScreenStateChanged+=RefreshRequested;
    }
    internal void Resume()
    {
        if(_disposed)return;
        if(!_resumed){_resumed=true;_manager?.RegisterDisplayListener(this,_handler);}
        Refresh();
    }
    internal void Pause()
    {
        if(_resumed)_manager?.UnregisterDisplayListener(this);
        _resumed=false;Close();
    }
    // A touch handler can request a close while AvaloniaView is still dispatching that touch.
    private void RefreshRequested()=>_handler.Post(Refresh);
    public void OnDisplayAdded(int displayId)=>RefreshRequested();
    public void OnDisplayRemoved(int displayId)=>RefreshRequested();
    public void OnDisplayChanged(int displayId)=>RefreshRequested();
    private void Refresh()
    {
        if(_disposed||_refreshing)return;
        if(_resumed&&SecondaryDisplay() is not null)_library.PreferHomeForDualScreen();
        _refreshing=true;
        try
        {
            var display=_resumed&&_library.WantsSecondaryScreen?SecondaryDisplay():null;
            if(display is null){Close();return;}
            if(_presentation is {IsShowing:true} current&&current.Display?.DisplayId==display.DisplayId)return;
            Close();
            var next=new HomePresentation(_activity,display,_library.CreateSecondaryHome());
            _presentation=next;
            next.DismissEvent+=(_,_)=>
            {
                if(!ReferenceEquals(_presentation,next))return;
                _presentation=null;next.ReleaseSurface();_library.ReleaseSecondaryHome();_handler.Post(next.Dispose);
            };
            next.Show();
            _library.SetSecondaryScreenAttached(true);
        }
        catch(Exception error) when(error is WindowManagerInvalidDisplayException or WindowManagerBadTokenException or Java.Lang.SecurityException)
        {
            Close();global::Android.Util.Log.Warn("DecompShelf.Home","Second écran indisponible : "+error.Message);
        }
        finally{_refreshing=false;}
    }
    private Display? SecondaryDisplay()
    {
        var own=_activity.Window?.DecorView.Display?.DisplayId??Display.DefaultDisplay;
        return _manager?.GetDisplays(DisplayManager.DisplayCategoryPresentation)?.FirstOrDefault(d=>d.IsValid&&d.DisplayId!=own&&d.State!=DisplayState.Off);
    }
    private void Close()
    {
        var previous=_presentation;_presentation=null;
        _library.SetSecondaryScreenAttached(false);
        if(previous is null)return;
        previous.ReleaseSurface();_library.ReleaseSecondaryHome();previous.Dismiss();
        // Android queues OnDismiss; keep its managed peer alive until that callback has run.
        _handler.Post(previous.Dispose);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!_disposed)
        {
            Pause();_disposed=true;_library.SecondaryScreenStateChanged-=RefreshRequested;
            _handler.Dispose();_manager?.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class HomePresentation(MainActivity activity,Display display,Avalonia.Controls.Control content):Presentation(activity,display)
    {
        private AvaloniaView? _surface;
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _surface=new AvaloniaView(Context){Content=content};
            SetContentView(_surface);
            if(Window is {} window)
            {
                var insets=AndroidX.Core.View.WindowCompat.GetInsetsController(window,window.DecorView);
                insets.SystemBarsBehavior=AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                insets.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
            }
        }
        public override bool DispatchKeyEvent(KeyEvent? e)=>activity.DispatchKeyEvent(e);
        public override bool OnGenericMotionEvent(MotionEvent? e)=>activity.OnGenericMotionEvent(e);
        internal void ReleaseSurface()
        {
            if(_surface is not {} surface)return;
            var root=surface.Content is Control control?TopLevel.GetTopLevel(control) as EmbeddableControlRoot:null;
            _surface=null;surface.Visibility=ViewStates.Gone;surface.Content=null;root?.Dispose();surface.RemoveAllViews();
            ((IDisposable)surface).Dispose();
        }
    }
}
