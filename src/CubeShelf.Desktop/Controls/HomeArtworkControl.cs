using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop.Controls;

/// <summary>Square menu icon; the selected preview retains the complete printed cover.</summary>
public sealed class HomeArtworkControl : UserControl
{
    public static readonly StyledProperty<string> CoverPathProperty = AvaloniaProperty.Register<HomeArtworkControl,string>(nameof(CoverPath),"");
    public static readonly StyledProperty<string> PlatformProperty = AvaloniaProperty.Register<HomeArtworkControl,string>(nameof(Platform),"gamecube");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<HomeArtworkControl,string>(nameof(Title),"");
    public static readonly StyledProperty<bool> IsPreviewProperty = AvaloniaProperty.Register<HomeArtworkControl,bool>(nameof(IsPreview));
    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<HomeArtworkControl,bool>(nameof(IsSelected));
    public static readonly StyledProperty<bool> LightPaletteProperty = AvaloniaProperty.Register<HomeArtworkControl,bool>(nameof(LightPalette));

    public string CoverPath { get=>GetValue(CoverPathProperty); set=>SetValue(CoverPathProperty,value); }
    public string Platform { get=>GetValue(PlatformProperty); set=>SetValue(PlatformProperty,value); }
    public string Title { get=>GetValue(TitleProperty); set=>SetValue(TitleProperty,value); }
    public bool IsPreview { get=>GetValue(IsPreviewProperty); set=>SetValue(IsPreviewProperty,value); }
    public bool IsSelected { get=>GetValue(IsSelectedProperty); set=>SetValue(IsSelectedProperty,value); }
    public bool LightPalette { get=>GetValue(LightPaletteProperty); set=>SetValue(LightPaletteProperty,value); }

    private readonly Border _frame=new(){CornerRadius=new CornerRadius(14),ClipToBounds=true,BorderThickness=new Thickness(1)};
    private readonly Image _image=new(){Stretch=Stretch.UniformToFill};
    private readonly ConsoleBrandMark _console=new(){Margin=new Thickness(14)};
    private readonly TextBlock _badgeText=new(){FontSize=8,FontWeight=FontWeight.Bold,Foreground=Brushes.White};
    private readonly Border _badge=new(){Padding=new Thickness(5,3),CornerRadius=new CornerRadius(0,0,7,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private readonly Border _sheen=new(){IsHitTestVisible=false};
    private readonly Border _selection=new(){IsHitTestVisible=false,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(3)};
    private Bitmap? _bitmap;
    private bool _attached;
    private readonly DemandAnimation _motion;
    private readonly MatrixTransform _pose=new();
    private double _scale=1,_lift,_targetScale=1,_targetLift;
    private bool _reduceMotion,_pressed;
    public bool ReduceMotion
    {
        get=>_reduceMotion;
        set{if(_reduceMotion==value)return;_reduceMotion=value;UpdateMotion(false);}
    }

    public HomeArtworkControl()
    {
        IsHitTestVisible=false;
        RenderTransformOrigin=RelativePoint.Center;RenderTransform=_pose;
        _motion=new DemandAnimation(this,AdvanceMotion);
        _badge.Child=_badgeText;
        var layers=new Grid();layers.Children.Add(_console);layers.Children.Add(_image);layers.Children.Add(_sheen);layers.Children.Add(_badge);
        _frame.Child=layers;Content=new Panel{Children={_frame,_selection}};
        AttachedToVisualTree+=(_,_)=>{_attached=true;LoadArtwork();UpdateMotion(false);};
        DetachedFromVisualTree+=(_,_)=>{_attached=false;_pressed=false;_motion.Stop();_image.Source=null;CoverBitmapCache.Release(_bitmap);_bitmap=null;};
        PropertyChanged+=(_,change)=>
        {
            if(change.Property==CoverPathProperty||change.Property==IsPreviewProperty)LoadArtwork();
            if(change.Property==IsSelectedProperty||change.Property==IsPreviewProperty)UpdateMotion(_attached);
            if(change.Property==CoverPathProperty||change.Property==PlatformProperty||change.Property==TitleProperty||change.Property==IsPreviewProperty||change.Property==IsSelectedProperty||change.Property==LightPaletteProperty)RefreshAppearance();
        };
        RefreshAppearance();
    }

    public void SetPressed(bool pressed){if(_pressed==pressed)return;_pressed=pressed;UpdateMotion(_attached);}
    private void UpdateMotion(bool animate)
    {
        _targetScale=IsPreview?1:_pressed ? .975 : IsSelected?1.045:1;
        _targetLift=!IsPreview&&IsSelected&&!_pressed?-2:0;
        if(!animate||ReduceMotion)
        {
            _motion.Stop();_scale=ReduceMotion?1:_targetScale;_lift=ReduceMotion?0:_targetLift;
            _pose.Matrix=new Matrix(_scale,0,0,_scale,0,_lift);return;
        }
        _motion.Start();
    }
    private bool AdvanceMotion(double elapsed)
    {
        var response=DemandAnimation.Response(.30,elapsed);
        _scale+=(_targetScale-_scale)*response;_lift+=(_targetLift-_lift)*response;
        var moving=Math.Abs(_scale-_targetScale)>.0005||Math.Abs(_lift-_targetLift)>.025;
        if(!moving){_scale=_targetScale;_lift=_targetLift;}
        _pose.Matrix=new Matrix(_scale,0,0,_scale,0,_lift);return moving;
    }

    private void LoadArtwork()
    {
        _image.Source=null;CoverBitmapCache.Release(_bitmap);_bitmap=null;
        if(_attached&&File.Exists(CoverPath))
        {
            try{_bitmap=CoverBitmapCache.Acquire(CoverPath,IsPreview?640:256);_image.Source=_bitmap;}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){ }
        }
        RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        var accent=Color.Parse(ShelfProduct.Resolve(Platform).Accent);
        _frame.Background=new SolidColorBrush(Color.Parse(LightPalette?"#E4E8EF":"#202636"));
        _frame.BorderBrush=new SolidColorBrush(Color.Parse(LightPalette?"#AEB8C8":"#5B647A"));
        _selection.IsVisible=IsSelected;
        _selection.BorderBrush=new SolidColorBrush(Color.Parse(LightPalette?"#1D6579":"#B4E4EC"));
        _frame.BoxShadow=BoxShadows.Parse(IsPreview?"0 5 16 0 #30000000":"0 2 6 0 #25000000");
        _console.Platform=Platform;_console.IsVisible=_bitmap is null;
        _console.MinWidth=_console.MinHeight=IsPreview?64:0;
        _console.Margin=new Thickness(12);
        _image.IsVisible=_bitmap is not null;
        _image.Stretch=IsPreview?Stretch.Uniform:Stretch.UniformToFill;
        _image.Margin=new Thickness(IsPreview?8:0);
        _badge.IsVisible=!IsPreview&&_bitmap is not null;
        _badge.Background=new SolidColorBrush(Color.FromArgb(235,(byte)(accent.R*.6),(byte)(accent.G*.6),(byte)(accent.B*.6)));
        _badgeText.Text=Platform switch
        {
            "gamecube"=>"GC", "n64"=>"N64", "switch"=>"NSW", "wiiu"=>"Wii U", "megadrive"=>"MD", "mastersystem"=>"SMS", "dreamcast"=>"DC", "virtualboy"=>"VB", "gamegear"=>"GG", _=>Platform.ToUpperInvariant()
        };
        _sheen.Background=new LinearGradientBrush
        {
            StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(0,1,RelativeUnit.Relative),
            GradientStops=[new GradientStop(Color.Parse(IsPreview?"#00FFFFFF":"#18FFFFFF"),0),new GradientStop(Colors.Transparent,.4),new GradientStop(Color.Parse(IsPreview?"#00000000":"#18000000"),1)]
        };
        AutomationProperties.SetName(this,Title);ToolTip.SetTip(this,Title);
    }
}
