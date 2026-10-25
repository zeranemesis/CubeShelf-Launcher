using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class RuntimeDownloadService : Service
{
    private const int NotificationId = 1802;
    private const string Channel = "cubeshelf-downloads";
    private static bool _requested;
    private long _lastNotification;

    internal static void Observe(RuntimeDownloadSnapshot snapshot)
    {
        var context = global::Android.App.Application.Context;
        try
        {
            if (snapshot.IsBusy && snapshot.State != RuntimeDownloadState.Pausing)
            {
                if (_requested) return;
                _requested = true;
                context.StartForegroundService(new Intent(context, typeof(RuntimeDownloadService)));
            }
            else if (!snapshot.IsBusy)
            {
                _requested = false;
                context.StopService(new Intent(context, typeof(RuntimeDownloadService)));
            }
        }
        catch (Exception error)
        {
            _requested = false;
            global::Android.Util.Log.Warn("CubeShelf.Download", error.ToString());
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        using var channel = new NotificationChannel(Channel, "Téléchargements CubeShelf", NotificationImportance.Low);
        manager.CreateNotificationChannel(channel);
        NativeGameService.Downloads.Changed += Update;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var snapshot = NativeGameService.Downloads.Snapshot;
        StartForeground(NotificationId, Notification(snapshot));
        if (!snapshot.IsBusy) StopSelf();
        return StartCommandResult.NotSticky;
    }

    private Notification Notification(RuntimeDownloadSnapshot snapshot)
    {
        using var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        intent.PutExtra("cubeshelf_downloads", true);
        using var pending = PendingIntent.GetActivity(this, NotificationId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        using var builder = new Notification.Builder(this, Channel);
        return builder.SetContentTitle("PartyBoard · CubeShelf")!
            .SetContentText(snapshot.State == RuntimeDownloadState.Downloading ? "Téléchargement des ressources" : "Préparation des fichiers")!
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)!
            .SetContentIntent(pending)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetProgress(100, (int)(100 * snapshot.ReceivedBytes / Math.Max(1, snapshot.TotalBytes)), snapshot.State != RuntimeDownloadState.Downloading)!
            .Build();
    }

    private void Update(RuntimeDownloadSnapshot snapshot)
    {
        if (!snapshot.IsBusy) { StopSelf(); return; }
        var now = SystemClock.UptimeMillis();
        if (now - _lastNotification < 1000) return;
        _lastNotification = now;
        using var notification = Notification(snapshot);
        ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, notification);
    }

    public override void OnDestroy()
    {
        NativeGameService.Downloads.Changed -= Update;
        _requested = false;
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
