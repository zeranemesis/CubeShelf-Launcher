using Android.Content;
using Android.Net;
using Android.OS;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

internal static class AndroidDeviceStatus
{
    public static MobileDeviceStatus Read()
    {
        var context=global::Android.App.Application.Context;
        using var now=new Java.Util.Date();
        using var format=global::Android.Text.Format.DateFormat.GetTimeFormat(context);
        var time=format?.Format(now)??DateTime.Now.ToString("HH:mm");
        using var filter=new IntentFilter(Intent.ActionBatteryChanged);
        using var battery=context.RegisterReceiver(null,filter);
        var level=battery?.GetIntExtra(BatteryManager.ExtraLevel,-1)??-1;
        var scale=battery?.GetIntExtra(BatteryManager.ExtraScale,-1)??-1;
        int? percent=scale>0&&level>=0&&level<=scale?(int)(100L*level/scale):null;
        var state=battery?.GetIntExtra(BatteryManager.ExtraStatus,-1)??-1;
        var charging=state==(int)BatteryStatus.Charging || state==(int)BatteryStatus.Full&&(battery?.GetIntExtra(BatteryManager.ExtraPlugged,0)??0)>0;
        return new(time,percent,charging,Connection(context));
    }

    private static string Connection(Context context)
    {
        try
        {
            using var manager=context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            using var network=manager?.ActiveNetwork;
            if(network is null)return "Hors ligne";
            using var capabilities=manager!.GetNetworkCapabilities(network);
            if(capabilities?.HasTransport(TransportType.Wifi)==true)return "Wi-Fi";
            if(capabilities?.HasTransport(TransportType.Cellular)==true)
            {
                // This API needs only ACCESS_NETWORK_STATE; newer telephony APIs require phone permission.
#pragma warning disable CS0618, CA1422
                using var info=manager.GetNetworkInfo(network);
                return MobileDeviceStatus.AndroidCellularLabel((int)(info?.Subtype??0));
#pragma warning restore CS0618, CA1422
            }
            if(capabilities?.HasTransport(TransportType.Ethernet)==true)return "Ethernet";
            if(capabilities?.HasTransport(TransportType.Vpn)==true)return "VPN";
            return "Réseau —";
        }
        catch(Java.Lang.SecurityException) {return "Réseau —";}
    }
}
