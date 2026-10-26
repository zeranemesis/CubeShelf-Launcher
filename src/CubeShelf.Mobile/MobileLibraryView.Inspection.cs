using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly Grid _inspectionOverlay = new()
    {
        RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        Background = new SolidColorBrush(Color.Parse("#0B101B")), IsVisible = false, ZIndex = 80
    };
    private readonly Border _inspectionStage = new() { Padding = new Thickness(12), ClipToBounds = true };
    private readonly TextBlock _inspectionTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inspectionHint = new() { FontSize = 12, Foreground = Brushes.LightGray,
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 10), TextWrapping = TextWrapping.Wrap };
    private readonly Button _closeInspection = MakeButton("Fermer");
    private GameCaseControl? _inspectionCase;

    private Grid BuildInspectionOverlay()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 12) };
        _inspectionTitle.Margin = new Thickness(0, 0, 16, 0);
        header.Children.Add(_inspectionTitle);
        _closeInspection.FontSize = 14;
        _closeInspection.MinHeight = 48;
        _closeInspection.Click += (_, _) => CloseInspection();
        Grid.SetColumn(_closeInspection, 1); header.Children.Add(_closeInspection);
        _inspectionOverlay.Children.Add(header);
        Grid.SetRow(_inspectionStage, 1); _inspectionOverlay.Children.Add(_inspectionStage);
        Grid.SetRow(_inspectionHint, 2); _inspectionOverlay.Children.Add(_inspectionHint);
        return _inspectionOverlay;
    }

    private void InspectHeldCase()
    {
        _holdTimer.Stop();
        if (_touchPointer is null || _horizontalDrag || _launching
            || _shelf.GameAtPoint(_touchStart) is not { } hit) return;
        ResetTouch();
        if (hit != _selected) Select(hit);
        OpenInspection();
    }

    public void InspectFromController()
    {
        SetControllerInput(true);
        if (_downloadsOverlay.IsVisible) return;
        if (_inspectionCase is not null) CloseInspection();
        else OpenInspection();
    }

    public bool DismissOverlay()
    {
        if(_nativePreparation is not null) {_nativePreparation.Cancel();return true;}
        if (_downloadsOverlay.IsVisible) { CloseDownloads(); return true; }
        if (_inspectionCase is not null) { CloseInspection(); return true; }
        if (!_invitationPanel.IsExpanded) return false;
        _invitationPanel.IsExpanded = false;
        if (_controllerInput) _invitationPanel.Focus();
        return true;
    }

    private void OpenInspection()
    {
        if (_launching || _inspectionCase is not null || _catalog.Length == 0) return;
        ResetTouch();
        var game = _catalog[_selected];
        var cover = game.Covers.FirstOrDefault();
        var inspection = new GameCaseControl
        {
            MinWidth = 0, MinHeight = 0, RealisticShell = true, UseImportedModel = true,
            UseCompactTextureMesh = true, ShadowOpacity = .8,CaseFormat=GameCaseControl.FormatForConsole(game.ShelfConsole)
        };
        inspection.SetCover(Cover(cover?.Front), Cover(cover?.Back), Cover(cover?.Spine),Cover(cover?.Right),Cover(cover?.Top),Cover(cover?.Bottom));
        inspection.SetShelfPose(28, true, false);
        _inspectionCase = inspection;
        _inspectionStage.Child = inspection;
        _inspectionTitle.Text = game.Title;
        // Hide the shelf while inspecting: only one case is rendered, with no
        // fullscreen blur or continuously running zoom animation.
        _body.IsVisible = false;
        _inspectionOverlay.IsVisible = true;
        UpdateInputHint();
        if (_controllerInput) _closeInspection.Focus();
    }

    private void CloseInspection(bool restoreFocus = true)
    {
        if (_inspectionCase is not { } inspection) return;
        _inspectionCase = null;
        _inspectionStage.Child = null;
        inspection.Dispose();
        _inspectionOverlay.IsVisible = false;
        _body.IsVisible = true;
        if (restoreFocus)
        {
            _shelf.SetSelection(_selected, _ribbon, false);
            if (_controllerInput) FocusPrimaryAction();
        }
    }

    private static StackPanel ActionLabel(string icon, string text)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new PathIcon
        {
            Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(icon switch
            {
                "▶" => "M5,2 L21,12 L5,22 Z",
                "＋" => "M10,3 H14 V10 H21 V14 H14 V21 H10 V14 H3 V10 H10 Z",
                _ => "M8,3 A4,4 0 1 0 8,11 A4,4 0 1 0 8,3 Z M2,21 V18 A6,6 0 0 1 14,18 V21 Z M17,5 A3,3 0 1 0 17,11 A3,3 0 1 0 17,5 Z M17,14 A5,5 0 0 1 22,19 V21 H17 Z"
            })
        });
        label.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        return label;
    }

    private void ConfigureButtonStyles()
    {
        var secondary = new SolidColorBrush(Color.Parse("#182235"));
        var border = new SolidColorBrush(Color.Parse("#344159"));
        var primary = new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft, EndPoint = RelativePoint.BottomRight,
            GradientStops = new GradientStops { new(Color.Parse("#947BFF"), 0), new(Color.Parse("#6448D8"), 1) }
        };
        Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(Button.BackgroundProperty, secondary), new Setter(Button.BorderBrushProperty, border),
                new Setter(Button.ForegroundProperty, Brushes.White) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("primary"))
        {
            Setters = { new Setter(Button.BackgroundProperty, primary),
                new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.Parse("#AD98FF"))) }
        });
        AddState(false, ":pointerover", "#24324A", "#657595");
        AddState(false, ":pressed", "#10192A", "#9BA9C2");
        AddState(true, ":pointerover", "#947BFF", "#C7B8FF");
        AddState(true, ":pressed", "#6549D3", "#D0C4FF");
        Styles.Add(new Style(x => x.OfType<Button>().Class("controller-focus").Template()
            .OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BorderBrushProperty, Brushes.White) }
        });

        void AddState(bool isPrimary, string state, string background, string outline)
        {
            Styles.Add(new Style(x =>
            {
                var button = x.OfType<Button>();
                if (isPrimary) button = button.Class("primary");
                return button.Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(background))),
                    new Setter(ContentPresenter.BorderBrushProperty, new SolidColorBrush(Color.Parse(outline))) }
            });
        }
    }
}
