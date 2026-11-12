using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

public enum HomeScreenRole { Combined, Preview, Navigation }

public sealed partial class DualScreenLibraryView
{
    private HomeScreenRole _screenRole;
    private readonly Border _rail=new(){ClipToBounds=true,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,Background=Brushes.Transparent};
    private readonly TranslateTransform _railPose=new();
    private readonly Dictionary<int,HomeArtworkControl> _thumbnails=[];
    private double _railWidth,_railOffset,_railTarget,_maxRailOffset;
    private int _coverFirst=-1,_coverEnd=-1;
    private IPointer? _railPointer;
    private double _dragOrigin,_dragOffset;
    private double _dragVelocity;
    private long _lastDragMove;
    private bool _draggingRail;
    public HomeScreenRole ScreenRole
    {
        get=>_screenRole;
        set
        {
            if(_screenRole==value)return;
            StopSelectionMotion();CancelQuickPreview();_screenRole=value;
            if(value!=HomeScreenRole.Navigation&&_selected is {} item)SetTitle(item);
            ResizeHome();ShowGrid();UpdateAchievementSelection();
        }
    }
    public bool SelectItem(string id)
    {
        if(_selected?.Id==id)return true;
        var index=Array.FindIndex(_filtered,item=>item.Id==id);if(index<0)return false;
        SelectInGrid(_filtered[index],false);
        return true;
    }
    private void SelectInGrid(DualScreenItem item,bool notify)
    {
        if(ScreenRole!=HomeScreenRole.Preview)
        {
            _building=true;
            try{_grid.SelectedItem=item;}
            finally{_building=false;}
        }
        Select(item,notify);
    }

    private void BuildRail()
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(_grid,Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_grid,Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _grid.RenderTransform=_railPose;
        _rail.Child=new Canvas{Children={_grid}};
        _rail.AddHandler(PointerPressedEvent,(_,args)=>
        {
            if(_maxRailOffset<=0||args.Pointer.Type==PointerType.Mouse&&!args.GetCurrentPoint(_rail).Properties.IsLeftButtonPressed)return;
            _railPointer=args.Pointer;
            _dragOrigin=args.GetPosition(_rail).X;
            _dragOffset=_railOffset;
            _dragVelocity=0;
            _lastDragMove=Environment.TickCount64;
            _draggingRail=false;
        },RoutingStrategies.Tunnel);
        _rail.AddHandler(PointerMovedEvent,(_,args)=>
        {
            if(_railPointer!=args.Pointer)return;
            var movement=args.GetPosition(_rail).X-_dragOrigin;
            if(!_draggingRail&&Math.Abs(movement)<8)return;
            if(!_draggingRail){_draggingRail=true;args.Pointer.Capture(_rail);}
            _railTarget=Math.Clamp(_dragOffset-movement,0,_maxRailOffset);
            var now=Environment.TickCount64;
            _dragVelocity=Math.Clamp((_railTarget-_railOffset)/Math.Max(1,now-_lastDragMove),-2,2);
            _lastDragMove=now;
            SetRailOffset(_railTarget);
            args.Handled=true;
        },RoutingStrategies.Tunnel,handledEventsToo:true);
        _rail.AddHandler(PointerReleasedEvent,(_,args)=>
        {
            if(_railPointer!=args.Pointer)return;
            _railPointer=null;
            if(!_draggingRail)return;
            _draggingRail=false;
            args.Pointer.Capture(null);
            if(!ReduceMotion&&Environment.TickCount64-_lastDragMove<65)
                SetRailTarget(_railOffset+_dragVelocity*85);
            args.Handled=true;
        },RoutingStrategies.Tunnel,handledEventsToo:true);
        _rail.PointerCaptureLost+=(_,args)=>
        {
            if(_railPointer==args.Pointer&&args.Pointer.Captured!=_rail){_railPointer=null;_draggingRail=false;}
        };
        _rail.AddHandler(PointerWheelChangedEvent,(_,args)=>
        {
            if(_maxRailOffset<=0)return;
            var delta=Math.Abs(args.Delta.X)>Math.Abs(args.Delta.Y)?args.Delta.X:-args.Delta.Y;
            SetRailTarget(_railTarget+delta*_tiles.ItemWidth);
            args.Handled=true;
        },RoutingStrategies.Tunnel);
    }

    private void SizeRail(bool reset=false)
    {
        var hasItems=ScreenRole!=HomeScreenRole.Preview&&_filtered.Length>0;
        _rail.IsVisible=hasItems;
        if(!hasItems)return;
        var columns=(_filtered.Length+_rows-1)/_rows;
        _grid.Width=columns*_tiles.ItemWidth;
        _grid.Height=Math.Min(_rows,_filtered.Length)*_tiles.ItemHeight;
        _rail.Width=Math.Min(_grid.Width,_railWidth);
        _rail.Height=_grid.Height;
        _maxRailOffset=Math.Max(0,_grid.Width-_rail.Width);
        if(reset)_railOffset=_railTarget=0;
        _railTarget=Math.Clamp(_railTarget,0,_maxRailOffset);
        RevealSelection(animate:false);
        SetRailOffset(_railTarget);
    }

    private void RevealSelection(bool animate)
    {
        var index=Array.IndexOf(_filtered,_selected);
        if(!_rail.IsVisible||index<0)return;
        var column=index/_rows*_tiles.ItemWidth;
        var target=Math.Clamp(_railTarget,column+_tiles.ItemWidth-_rail.Width,column);
        SetRailTarget(target,animate);
    }

    private void SetRailTarget(double target,bool animate=true)
    {
        _railTarget=Math.Clamp(target,0,_maxRailOffset);
        if(animate&&CanAnimateSelection&&Math.Abs(_railTarget-_railOffset)>.3)_selectionAnimation.Start();
        else SetRailOffset(_railTarget);
    }

    private void SetRailOffset(double offset)
    {
        _railOffset=offset;
        _railPose.X=-offset;
        if(!_rail.IsVisible)return;
        var first=Math.Max(0,(int)(offset/_tiles.ItemWidth)-1)*_rows;
        var end=Math.Min(_filtered.Length,((int)Math.Ceiling((offset+_rail.Width)/_tiles.ItemWidth)+1)*_rows);
        if(first==_coverFirst&&end==_coverEnd)return;
        for(var index=_coverFirst;index<_coverEnd;index++)
        {
            if((index<first||index>=end)&&_thumbnails.TryGetValue(index,out var tile))tile.CoverPath="";
        }
        _coverFirst=first;_coverEnd=end;
        for(var index=first;index<end;index++)
            if(_thumbnails.TryGetValue(index,out var tile))tile.CoverPath=_filtered[index].Cover;
    }

    public void StepColumn(int direction)
    {
        if(!IsPopupOpen&&!_achievementDetails.IsVisible&&ScreenRole!=HomeScreenRole.Preview&&_filtered.Length>0)
        {
            _grid.Focus();
            Move(direction,0);
        }
    }
    public void RefreshPreferences(){ApplyPalette();UpdateMusic();}
}
