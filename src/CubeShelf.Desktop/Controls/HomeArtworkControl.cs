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
    internal int PreviewDecodeHeight { get; init; }=640;

    private readonly Border _frame=new(){CornerRadius=new CornerRadius(CoverRadius),ClipToBounds=true,BorderThickness=new Thickness(1)};
    private readonly Image _image=new(){Stretch=Stretch.UniformToFill};
    private readonly ConsoleBrandMark _console=new(){Margin=new Thickness(14)};
    private readonly TextBlock _badgeText=new(){FontSize=10,FontWeight=FontWeight.Bold,Foreground=Brushes.White};
    private readonly Border _badge=new(){Padding=new Thickness(5,3),CornerRadius=new CornerRadius(0,0,7,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private readonly Border _sheen=new(){IsHitTestVisible=false};
    private const double CoverRadius=14;
    private readonly SelectionFrame _selection=new(){Name="SelectionFrame",IsHitTestVisible=false};
    private Bitmap? _bitmap;
    private bool _attached;
    public HomeArtworkControl()
    {
        IsHitTestVisible=false;
        _badge.Child=_badgeText;
        var layers=new Grid();layers.Children.Add(_console);layers.Children.Add(_image);layers.Children.Add(_sheen);layers.Children.Add(_badge);
        _frame.Child=layers;Content=new Panel{Children={_frame,_selection}};
        AttachedToVisualTree+=(_,_)=>{_attached=true;LoadArtwork();};
        DetachedFromVisualTree+=(_,_)=>{_attached=false;_image.Source=null;CoverBitmapCache.Release(_bitmap);_bitmap=null;};
        PropertyChanged+=(_,change)=>
        {
            if(change.Property==CoverPathProperty||change.Property==IsPreviewProperty)LoadArtwork();
            else if(change.Property==IsSelectedProperty)RefreshSelection();
            else if(change.Property==PlatformProperty||change.Property==TitleProperty||change.Property==LightPaletteProperty)RefreshAppearance();
        };
        RefreshAppearance();
    }

    private void RefreshSelection()
    {
        _selection.IsVisible=IsSelected&&!IsPreview;
        if(!_selection.IsVisible)SetSelectionSweep(null);
    }

    internal void SetSelectionSweep(double? progress)
    {
        if(_selection.Progress==progress)return;
        _selection.SetReflection(progress);
        _selection.InvalidateVisual();
    }

    private sealed class SelectionFrame : Control
    {
        private const double Inset=3.25;
        private const double Radius=CoverRadius-Inset;
        private static readonly Pen Outline=new(new SolidColorBrush(Color.Parse("#223D46")),5.5);
        private static readonly Pen Track=new(new SolidColorBrush(Color.Parse("#A3C1BC")),1.8);
        private LinearGradientBrush? _reflection;
        private Pen? _reflectionPen;
        public double? Progress { get; private set; }

        public void SetReflection(double? progress)
        {
            Progress=progress;
            if(progress is not { } phase)return;
            if(_reflection is null)
            {
                _reflection=new LinearGradientBrush
                {
                    GradientStops=[
                        new GradientStop(Colors.Transparent,0),
                        new GradientStop(Colors.Transparent,0.40),
                        new GradientStop(Color.Parse("#269DBFB6"),0.57),
                        new GradientStop(Color.Parse("#9ED5E7D8"),0.78),
                        new GradientStop(Color.Parse("#FFF6E5C4"),1)]
                };
                _reflectionPen=new Pen(_reflection,3.2);
            }

            var angle=phase*2*Math.PI;
            var x=Math.Cos(angle)*0.64;
            var y=Math.Sin(angle)*0.64;
            _reflection.StartPoint=new RelativePoint(0.5-x,0.5-y,RelativeUnit.Relative);
            _reflection.EndPoint=new RelativePoint(0.5+x,0.5+y,RelativeUnit.Relative);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if(Bounds.Width<=2*Inset||Bounds.Height<=2*Inset)return;
            // All strokes share the same centerline and follow the artwork's corner radius.
            var path=new Rect(Inset,Inset,Bounds.Width-2*Inset,Bounds.Height-2*Inset);
            context.DrawRectangle(null,Outline,path,Radius,Radius);
            context.DrawRectangle(null,Track,path,Radius,Radius);
            if(Progress.HasValue)context.DrawRectangle(null,_reflectionPen,path,Radius,Radius);
        }
    }

    private void LoadArtwork()
    {
        _image.Source=null;CoverBitmapCache.Release(_bitmap);_bitmap=null;
        if(_attached&&File.Exists(CoverPath))
        {
            try{_bitmap=CoverBitmapCache.Acquire(CoverPath,IsPreview?PreviewDecodeHeight:256);_image.Source=_bitmap;}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){ }
        }
        RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        var accent=Color.Parse(ShelfProduct.Resolve(Platform).Accent);
        _frame.Background=new SolidColorBrush(Color.Parse(LightPalette?"#E4E8EF":"#202636"));
        _frame.BorderBrush=new SolidColorBrush(Color.Parse(LightPalette?"#AEB8C8":"#5B647A"));
        RefreshSelection();
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
