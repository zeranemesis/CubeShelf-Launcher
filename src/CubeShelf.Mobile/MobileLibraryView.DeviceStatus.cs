using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly DispatcherTimer _deviceStatusTimer=new() {Interval=TimeSpan.FromSeconds(5)};
    private readonly TextBlock _deviceConnection=new() {FontSize=12,Foreground=Brushes.LightGray,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock _deviceBattery=new() {FontSize=12,Foreground=Brushes.LightGray,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock _deviceTime=new() {FontSize=17,FontWeight=FontWeight.SemiBold,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,0,0)};
    private readonly Border _batteryFill=new() {Height=6,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(1)};
    private readonly Avalonia.Controls.Shapes.Path _chargingMark=new() {Width=7,Height=13,Stretch=Stretch.Uniform,Fill=Brushes.LightGreen,Data=Geometry.Parse("M 5,0 L 0,7 L 3,7 L 2,13 L 8,5 L 4,5 Z")};
    private readonly Border _deviceStatusBar=new() {Padding=new Thickness(24,4,24,0),ZIndex=200,Background=new SolidColorBrush(Color.Parse("#0B101B"))};

    private Control BuildDeviceStatusBar()
    {
        var battery=new Border {Width=24,Height=12,BorderThickness=new Thickness(1.3),BorderBrush=Brushes.LightGray,
            CornerRadius=new CornerRadius(3),Padding=new Thickness(2),Child=_batteryFill,VerticalAlignment=VerticalAlignment.Center};
        var indicators=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center};
        indicators.Children.Add(battery);indicators.Children.Add(_chargingMark);indicators.Children.Add(_deviceBattery);
        var row=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right};
        row.Children.Add(_deviceConnection);row.Children.Add(indicators);row.Children.Add(_deviceTime);
        _deviceStatusBar.Child=row;
        _deviceStatusTimer.Tick+=(_,_)=>RefreshDeviceStatus();
        return _deviceStatusBar;
    }

    private void SetDeviceStatusActive(bool active)
    {
        _deviceStatusTimer.Stop();
        if(!active)return;
        RefreshDeviceStatus();_deviceStatusTimer.Start();
    }

    private void RefreshDeviceStatus()
    {
        var status=_games.DeviceStatus;
        var percent=status?.BatteryPercent is >=0 and <=100?status.BatteryPercent:null;
        _deviceTime.Text=status?.Time??DateTime.Now.ToString("HH:mm");
        _deviceConnection.Text=status?.Connection??"Réseau —";
        _deviceBattery.Text=percent is { } value?$"{value} %":"— %";
        _batteryFill.Width=18*(percent??0)/100d;
        _batteryFill.Background=percent<=15&&status?.IsCharging!=true?Brushes.Salmon:Brushes.LightGreen;
        _chargingMark.IsVisible=status?.IsCharging==true;
        var battery=percent is { } level?$"Batterie {level} %":"Batterie indisponible";
        var description=$"{_deviceConnection.Text}. {battery}{(status?.IsCharging==true?", en charge":"")}. Heure {_deviceTime.Text}.";
        AutomationProperties.SetName(_deviceStatusBar,description);
        ToolTip.SetTip(_deviceStatusBar,description);
    }
}
