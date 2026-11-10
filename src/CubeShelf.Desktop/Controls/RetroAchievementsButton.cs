using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CubeShelf.Desktop.Controls;

public sealed class RetroAchievementsButton : Button
{
    private static readonly Bitmap Logo=LoadLogo();
    private readonly TextBlock _label=new(){Text="RetroAchievements",VerticalAlignment=VerticalAlignment.Center};
    protected override Type StyleKeyOverride=>typeof(Button);
    public RetroAchievementsButton()
    {
        MinHeight=48;FontSize=13;Padding=new Thickness(12,6);CornerRadius=new CornerRadius(14);
        HorizontalContentAlignment=HorizontalAlignment.Center;
        Content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center,
            Children={new Image{Source=Logo,Width=32,Height=20,Stretch=Stretch.Uniform},_label}};
        AutomationProperties.SetName(this,"RetroAchievements");ToolTip.SetTip(this,"RetroAchievements");
    }
    public void SetCompact(bool compact){_label.IsVisible=!compact;Width=compact?48:double.NaN;Padding=new Thickness(compact?6:12,6);}
    private static Bitmap LoadLogo(){using var stream=AssetLoader.Open(new Uri($"avares://{typeof(RetroAchievementsButton).Assembly.GetName().Name}/Assets/Home/Brand/retroachievements.webp"));return new Bitmap(stream);}
}
