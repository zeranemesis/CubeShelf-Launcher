using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using CubeShelf.Core.Achievements;

namespace CubeShelf.Desktop.Controls;

public sealed partial class DualScreenLibraryView
{
    public Func<string,CancellationToken,Task<GameAchievementProgress>>? FetchAchievements { get; set; }

    private readonly StackPanel _achievementSummary=new(){IsVisible=false,Spacing=5,MaxWidth=380};
    private readonly TextBlock _achievementSummaryText=Text(13);
    private readonly ProgressBar _achievementBar=new(){Height=6,Maximum=1,IsVisible=false};
    private readonly Border _achievementDetails=new(){IsVisible=false,CornerRadius=new CornerRadius(24),MaxWidth=900};
    private readonly TextBlock _achievementTitle=Text(23),_achievementDetailsStatus=Text(14);
    private readonly Button _achievementBack=Button("Retour"),_achievementPlay=Button("Jouer");
    private readonly StackPanel _achievementRows=new(){Spacing=8};
    private readonly ScrollViewer _achievementScroll=new(){HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
    private CancellationTokenSource? _achievementRequest;
    private string? _achievementRequestId;
    private GameAchievementProgress? _achievementProgress;

    private void BuildAchievementViews(Panel scene)
    {
        _achievementSummaryText.FontWeight=FontWeight.SemiBold;
        _achievementSummaryText.HorizontalAlignment=HorizontalAlignment.Center;
        _achievementSummaryText.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("HomeText"));
        _achievementBar.Bind(ProgressBar.ForegroundProperty,new DynamicResourceExtension("HomeAccent"));
        _achievementBar.Bind(ProgressBar.BackgroundProperty,new DynamicResourceExtension("HomeLine"));
        _achievementSummary.Children.Add(_achievementSummaryText);
        _achievementSummary.Children.Add(_achievementBar);

        _achievementDetails.Margin=new Thickness(16);
        _achievementDetails.Padding=new Thickness(16);
        _achievementDetails.Bind(Border.BackgroundProperty,new DynamicResourceExtension("HomeControl"));
        var content=new Grid{RowDefinitions=new("Auto,*,Auto"),RowSpacing=12};
        _achievementTitle.FontWeight=FontWeight.Bold;
        var header=new StackPanel{Spacing=3,Children={_achievementTitle,_achievementDetailsStatus}};
        content.Children.Add(header);
        _achievementScroll.Content=_achievementRows;
        Grid.SetRow(_achievementScroll,1);content.Children.Add(_achievementScroll);
        _achievementBack.FocusAdorner=null;_achievementPlay.FocusAdorner=null;
        _achievementBack.Click+=(_,_)=>CloseAchievementDetails();
        _achievementPlay.Classes.Add("home-primary");
        _achievementPlay.Click+=(_,_)=>{if(_selected is {} item){CloseAchievementDetails();LaunchRequested?.Invoke(item.Id);}};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={_achievementBack,_achievementPlay}};
        Grid.SetRow(actions,2);content.Children.Add(actions);
        _achievementDetails.Child=content;scene.Children.Add(_achievementDetails);
    }

    private void UpdateAchievementSelection()
    {
        _achievementSummary.IsVisible=_achievementPage&&ScreenRole!=HomeScreenRole.Preview&&_selected is not null;
        if(!_achievementSummary.IsVisible||_selected is not {} item)
        {
            ResetAchievementProgress();
            return;
        }
        if(FetchAchievements is null||_achievementRequestId==item.Id)return;
        ResetAchievementProgress();
        _achievementRequestId=item.Id;
        var request=_achievementRequest=new CancellationTokenSource();
        RenderAchievementProgress();
        _=LoadAchievementProgressAsync(item.Id,request);
    }

    private async Task LoadAchievementProgressAsync(string id,CancellationTokenSource request)
    {
        try
        {
            var progress=await FetchAchievements!(id,request.Token);
            if(!ReferenceEquals(_achievementRequest,request))return;
            _achievementProgress=progress;
            RenderAchievementProgress();
        }
        catch(OperationCanceledException){}
        finally
        {
            if(ReferenceEquals(_achievementRequest,request))_achievementRequest=null;
            request.Dispose();
        }
    }

    public void RefreshAchievementProgress()
    {
        if(!_achievementPage)return;
        ResetAchievementProgress();
        UpdateAchievementSelection();
    }

    private void ResetAchievementProgress()
    {
        var pending=_achievementRequest;_achievementRequest=null;
        pending?.Cancel();
        _achievementRequestId=null;_achievementProgress=null;
    }

    private void RenderAchievementProgress()
    {
        var progress=_achievementProgress;
        var total=progress?.Total;
        var unlocked=progress?.Unlocked;
        var summary=total.HasValue&&unlocked.HasValue
            ? $"{unlocked} / {total} succès débloqués"
            : progress?.Message??"Chargement des succès…";
        _achievementSummaryText.Text=summary;
        _achievementBar.IsVisible=total>0&&unlocked.HasValue;
        _achievementBar.Value=_achievementBar.IsVisible?(double)unlocked!.Value/total!.Value:0;
        AutomationProperties.SetName(_achievementBar,summary);
        if(!_achievementDetails.IsVisible)return;
        _achievementDetailsStatus.Text=progress?.Message is {} note&&note!=summary?summary+" · "+note:summary;
        _achievementRows.Children.Clear();
        if(progress is null)return;
        foreach(var (label,entries) in progress.Entries.GroupBy(entry=>entry.Unlocked)
            .Select(group=>(group.Key switch{true=>"Débloqués",false=>"À débloquer",null=>"Succès du jeu"},group)))
        {
            _achievementRows.Children.Add(new TextBlock{Text=label,FontSize=15,FontWeight=FontWeight.Bold,Margin=new Thickness(0,7,0,1)});
            foreach(var entry in entries)
            {
                var title=new TextBlock{Text=entry.Title,FontSize=16,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
                var description=new TextBlock{Text=entry.Description,FontSize=14,TextWrapping=TextWrapping.Wrap};
                description.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("HomeMuted"));
                var words=new StackPanel{Spacing=2,Children={title,description}};
                var points=new TextBlock{Text=$"{entry.Points} pts",FontSize=13,Margin=new Thickness(10,2,0,0)};
                var line=new Grid{ColumnDefinitions=new("*,Auto"),Children={words,points}};
                Grid.SetColumn(points,1);
                var card=new Border{Child=line,Padding=new Thickness(10),CornerRadius=new CornerRadius(10)};
                card.Bind(Border.BackgroundProperty,new DynamicResourceExtension(entry.Unlocked==true?"HomeFocus":"HomeHover"));
                _achievementRows.Children.Add(card);
            }
        }
    }

    private void OpenAchievementDetails()
    {
        if(_selected is not {} item||_achievementDetails.IsVisible)return;
        _achievementTitle.Text=item.Title;
        _root.IsVisible=false;_achievementDetails.IsVisible=true;
        _achievementScroll.Offset=default;
        RenderAchievementProgress();
        FocusHomeControl(_achievementBack);
    }

    private bool CloseAchievementDetails()
    {
        if(!_achievementDetails.IsVisible)return false;
        _achievementDetails.IsVisible=false;_root.IsVisible=true;
        _achievementRows.Children.Clear();
        if(_grid.IsEffectivelyVisible)_grid.Focus();
        return true;
    }

    private void MoveAchievementDetails(int horizontal,int vertical)
    {
        if(vertical!=0)
            _achievementScroll.Offset=new Vector(0,Math.Clamp(_achievementScroll.Offset.Y+vertical*96,0,
                Math.Max(0,_achievementScroll.Extent.Height-_achievementScroll.Viewport.Height)));
        if(horizontal<0)FocusHomeControl(_achievementBack);
        if(horizontal>0&&_achievementPlay.IsEnabled)FocusHomeControl(_achievementPlay);
    }
}
