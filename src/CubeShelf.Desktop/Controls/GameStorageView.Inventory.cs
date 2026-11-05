using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CubeShelf.Core.Storage;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameStorageView
{
    private readonly Grid _inventory=new(){RowDefinitions=new("Auto,*"),RowSpacing=14,Margin=new Thickness(20,0,20,20)};
    private readonly TextBlock _inventoryStatus=Text(14);
    private readonly ListBox _inventoryList=new(){Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    private CancellationTokenSource? _inventoryScan;

    private void InitializeInventory(Grid layout)
    {
        var toolbar=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=12};toolbar.Children.Add(_inventoryStatus);
        _inventoryStatus.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("Muted"));
        var refresh=MakeButton("Actualiser");Grid.SetColumn(refresh,1);toolbar.Children.Add(refresh);refresh.Click+=(_,_)=>_=RefreshInventory();_inventory.Children.Add(toolbar);
        _inventoryList.ItemTemplate=new FuncDataTemplate<GameStorageItem>((item,_)=>
        {
            if(item is null)return null;
            var row=new Grid{ColumnDefinitions=new("Auto,*,Auto"),RowDefinitions=new("Auto,Auto"),ColumnSpacing=12,RowSpacing=8};
            var cover=Artwork(item.Game);Grid.SetRowSpan(cover,2);row.Children.Add(cover);
            var title=Text(16,item.Game.Title);title.FontWeight=FontWeight.SemiBold;Grid.SetColumn(title,1);row.Children.Add(title);
            var size=Text(16,item.Usage is { } usage?Size(usage.TotalBytes):"À vérifier");size.FontWeight=FontWeight.SemiBold;Grid.SetColumn(size,2);row.Children.Add(size);
            var detail=Muted(item.Error??Breakdown(item.Usage!));detail.FontSize=13;Grid.SetRow(detail,1);Grid.SetColumn(detail,1);Grid.SetColumnSpan(detail,2);row.Children.Add(detail);
            var border=Card(row);border.Padding=new Thickness(14);border.CornerRadius=new CornerRadius(12);return border;
        });
        var style=new Style(s=>s.OfType<ListBoxItem>());style.Setters.Add(new Setter(ListBoxItem.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(ListBoxItem.MarginProperty,new Thickness(0,0,0,8)));style.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(ListBoxItem.CornerRadiusProperty,new CornerRadius(12)));Styles.Add(style);
        var selected=new Style(s=>s.OfType<ListBoxItem>().Class(":selected"));selected.Setters.Add(new Setter(ListBoxItem.BorderBrushProperty,new DynamicResourceExtension("Accent")));selected.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty,new Thickness(2)));Styles.Add(selected);
        _inventoryList.Tapped+=(_,_)=>{if(_inventoryList.SelectedItem is GameStorageItem item)OpenDetails(item.Game);};
        _inventoryList.KeyDown+=(_,e)=>{if(e.Key==global::Avalonia.Input.Key.Enter&&_inventoryList.SelectedItem is GameStorageItem item){OpenDetails(item.Game);e.Handled=true;}};
        Grid.SetRow(_inventoryList,1);_inventory.Children.Add(_inventoryList);Grid.SetRow(_inventory,1);layout.Children.Add(_inventory);
        global::Avalonia.Automation.AutomationProperties.SetName(_inventoryList,"Jeux classés du plus volumineux au plus léger");
    }

    private Border Artwork(GameStorageEntry game)
    {
        var missing=Muted("Sans jaquette");missing.FontSize=11;missing.TextAlignment=TextAlignment.Center;
        var frame=new Border{Width=64,MinHeight=64,Child=missing,CornerRadius=new CornerRadius(4),ClipToBounds=true,VerticalAlignment=VerticalAlignment.Center};
        frame.Bind(Border.BackgroundProperty,new DynamicResourceExtension("Panel2"));
        var image=new Image{Name="StorageGameCover",Tag=game.Id,Width=64,Stretch=Stretch.Uniform};Bitmap? bitmap=null;
        global::Avalonia.Automation.AutomationProperties.SetName(image,"Jaquette de "+game.Title);
        frame.AttachedToVisualTree+=(_,_)=>
        {
            if(bitmap is not null)return;
            try
            {
                var path=_coverPath?.Invoke(game.Id);if(string.IsNullOrEmpty(path)||!File.Exists(path))return;
                bitmap=CoverBitmapCache.Acquire(path,256);image.Source=bitmap;
                image.Height=Math.Min(96,64d*bitmap.PixelSize.Height/bitmap.PixelSize.Width);frame.MinHeight=0;frame.Child=image;
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException){frame.Child=missing;}
        };
        frame.DetachedFromVisualTree+=(_,_)=>{image.Source=null;CoverBitmapCache.Release(bitmap);bitmap=null;};
        return frame;
    }

    private static string Breakdown(GameStorageUsage usage)
    {
        var parts=new List<string>();
        if(usage.EngineBytes+usage.GameBytes>0)parts.Add($"Installation {Size(usage.EngineBytes+usage.GameBytes)}");
        if(usage.DownloadBytes>0)parts.Add($"Téléchargements {Size(usage.DownloadBytes)}");
        if(usage.SaveBytes>0)parts.Add($"Sauvegardes {Size(usage.SaveBytes)}");
        if(usage.BackupBytes>0)parts.Add($"Copies locales {Size(usage.BackupBytes)}");
        return string.Join(" · ",parts);
    }

    private async Task RefreshInventory()
    {
        _inventoryScan?.Cancel();_inventoryScan?.Dispose();_inventoryScan=new();var token=_inventoryScan.Token;
        _inventoryStatus.Text="Analyse de l’espace occupé…";
        try
        {
            var items=await Task.Run(()=>_service.Scan(token),token);if(token.IsCancellationRequested)return;
            _inventoryList.ItemsSource=items;
            _inventoryStatus.Text=items.Count==0?"Aucun fichier de jeu détecté dans les emplacements connus.":$"{items.Count} jeu(x) · du plus volumineux au plus léger.\nChoisis une installation connue de DecompShelf pour voir le détail et libérer de l’espace.";
        }
        catch(OperationCanceledException) { }
        catch(Exception error){_inventoryStatus.Text="Mesure impossible : "+error.Message;}
    }

    private void OpenDetails(GameStorageEntry game)
    {
        _inventoryScan?.Cancel();_inventory.IsVisible=false;_details.IsVisible=true;_heading.Text="Stockage et sauvegardes";
        _games.SelectedItem=game;_=Refresh();_details.ScrollToHome();_games.Focus();
    }
    private void ShowInventory()
    {
        _refresh?.Cancel();DiscardConfirmation();_confirmation.IsVisible=false;_details.IsVisible=false;_inventory.IsVisible=true;_heading.Text="Stockage des jeux";
        _inventoryList.SelectedIndex=-1;_=RefreshInventory();_inventoryList.Focus();
    }
}
