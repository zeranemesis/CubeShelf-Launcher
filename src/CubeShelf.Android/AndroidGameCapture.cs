using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using CubeShelf.Assistance;
using OperationCanceledException = System.OperationCanceledException;

namespace CubeShelf.Android;

/// <summary>One projection per game session; pixels are read only for an explicit help request.</summary>
internal sealed class AndroidGameCapture(Context context, Action<bool> projectionChanged) : IDisposable
{
    private MediaProjection? _projection;
    private ImageReader? _reader;
    private VirtualDisplay? _display;
    private ProjectionCallback? _callback;
    private int _width, _height;
    private bool _disposed;

    internal async Task<GameFrame> CaptureAsync(CancellationToken cancellation, Action hideOverlay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_projection is null)
        {
            using var consent = await GameCapturePermission.RequestAsync(context, cancellation);
            cancellation.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var manager = (MediaProjectionManager)context.GetSystemService(Context.MediaProjectionService)!;
            _projection = manager.GetMediaProjection((int)Result.Ok, consent) ?? throw new InvalidOperationException("Capture d’écran non autorisée.");
            _callback = new ProjectionCallback(ReleaseProjection);
            _projection.RegisterCallback(_callback, new Handler(Looper.MainLooper!));
        }
        hideOverlay();
        var metrics = context.Resources!.DisplayMetrics!;
        var scale = Math.Min(1d, 960d / Math.Max(metrics.WidthPixels, metrics.HeightPixels));
        var width = Math.Max(1, (int)(metrics.WidthPixels * scale)); var height = Math.Max(1, (int)(metrics.HeightPixels * scale));
        if (_reader is null || width != _width || height != _height)
        {
            _display?.Surface?.Dispose();
            _reader?.Close(); _reader?.Dispose();
            _reader = ImageReader.NewInstance(width, height, (global::Android.Graphics.ImageFormatType)1, 3);
            _width = width; _height = height;
            if (_display is null)
                _display = _projection.CreateVirtualDisplay("DecompShelf help", width, height, (int)metrics.DensityDpi,
                    (DisplayFlags)(int)VirtualDisplayFlags.AutoMirror, _reader.Surface, null, new Handler(Looper.MainLooper!));
            else { _display.Resize(width, height, (int)metrics.DensityDpi); _display.Surface = _reader.Surface; }
        }
        using (var stale = _reader!.AcquireLatestImage()) { stale?.Close(); }
        await Task.Delay(350, cancellation);
        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_reader is null) throw new InvalidOperationException("Le partage d’écran a été arrêté. Relance ta demande.");
            using var image = _reader.AcquireLatestImage();
            if (image is not null)
            {
                byte[]? pixels = null;
                try
                {
                var plane = image.GetPlanes()![0];
                using var buffer = plane.Buffer!;
                pixels = new byte[buffer.Remaining()]; buffer.Get(pixels);
                var rgb = new byte[_width * _height * 3];
                for (var y = 0; y < _height; y++) for (var x = 0; x < _width; x++)
                {
                    var from = y * plane.RowStride + x * plane.PixelStride; var to = (y * _width + x) * 3;
                    if (from + 2 >= pixels.Length) throw new InvalidDataException("Capture Android incomplète.");
                    rgb[to] = pixels[from]; rgb[to + 1] = pixels[from + 1]; rgb[to + 2] = pixels[from + 2];
                }
                if (rgb.All(value => value < 4)) throw new InvalidOperationException("Capture noire : le jeu protège peut-être son affichage. Réessaie après le chargement.");
                return new GameFrame(_width, _height, rgb);
                }
                finally { if (pixels is not null) Array.Clear(pixels); image.Close(); }
            }
            await Task.Delay(50, cancellation);
        }
        throw new TimeoutException("Aucune image du jeu reçue. Relance le partage d’écran.");
    }
    private void ReleaseProjection()
    {
        var projection = _projection; _projection = null;
        if (projection is not null && _callback is not null) projection.UnregisterCallback(_callback);
        _display?.Release(); _display?.Dispose(); _display = null;
        _reader?.Close(); _reader?.Dispose(); _reader = null;
        projection?.Stop(); projection?.Dispose();
        _callback?.Dispose(); _callback = null;
        projectionChanged(false);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; ReleaseProjection(); }
    private sealed class ProjectionCallback(Action stopped) : MediaProjection.Callback
    {
        public override void OnStop() => stopped();
    }
}

[Activity(Name = "io.decompshelf.GameCapturePermission", Exported = false, Process = ":assistant",
    ExcludeFromRecents = true, TaskAffinity = "io.decompshelf.assistant",
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class GameCapturePermission : Activity
{
    private static TaskCompletionSource<Intent>? _pending;
    private static Intent? _consent;
    private static WeakReference<GameCapturePermission>? _host;
    private bool _requested;
    internal static async Task<Intent> RequestAsync(Context context, CancellationToken cancellation)
    {
        if (_pending is not null) throw new InvalidOperationException("Une autorisation de capture est déjà ouverte.");
        var pending = new TaskCompletionSource<Intent>(TaskCreationOptions.RunContinuationsAsynchronously); _pending = pending;
        using var registration = cancellation.Register(() => pending.TrySetCanceled(cancellation));
        try
        {
            using var intent = new Intent(context, typeof(GameCapturePermission)); intent.AddFlags(ActivityFlags.NewTask); context.StartActivity(intent);
            return await pending.Task;
        }
        finally
        {
            _pending = null;
            if (_host?.TryGetTarget(out var activity) == true) activity.Finish();
            _host = null; _consent?.Dispose(); _consent = null;
        }
    }
    protected override void OnResume()
    {
        base.OnResume();
        if (_requested) return; _requested = true; _host = new(this);
        if (_pending is null) { Finish(); return; }
        var manager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
        StartActivityForResult(manager.CreateScreenCaptureIntent(), 75);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 75) return;
        if (resultCode != Result.Ok || data is null) { _pending?.TrySetException(new OperationCanceledException("Partage d’écran refusé.")); Finish(); return; }
        _consent = new Intent(data);
        using var start = new Intent(this, typeof(GameHelpOverlay)).SetAction("capture-authorized");
        try { StartForegroundService(start); }
        catch (Exception error) { _pending?.TrySetException(error); Finish(); }
    }
    internal static void CompleteRequest()
    {
        if (_consent is not null && _pending is { } pending) { var consent = _consent; _consent = null; if (!pending.TrySetResult(consent)) consent.Dispose(); }
    }
}
