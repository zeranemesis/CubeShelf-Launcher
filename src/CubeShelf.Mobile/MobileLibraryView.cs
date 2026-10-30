using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

/// <summary>Permanent Big Picture shell sharing the desktop case renderer and catalogue.</summary>
public sealed partial class MobileLibraryView : UserControl
{
    private const string PartyBoardId = "GMPE01_00";
    private readonly IMobileGameService _games;
    private readonly GameLaunchTransition _launchTransition = new() { ZIndex = 90 };
    private bool _launching;
    private CancellationTokenSource? _launchCancellation;
    private readonly GameShelfControl _shelf = new()
        { Height = 320, UseCompositorScaling = true, UseCompactTextureMesh = true };
    private readonly GameTitleControl _title = new() { FontSize = 27, Foreground = Brushes.White };
    private readonly TextBlock _info = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _availability = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _invite = new() { Watermark = "Invitation privée PB4…", MaxLength = 80, MinHeight = 48 };
    private readonly Button _play = MakeButton("Jouer");
    private readonly Button _online = MakeButton("Salons en ligne");
    private readonly Button _download = MakeButton("Installer PartyBoard");
    private readonly Button _join = MakeButton("Rejoindre l’invitation");
    private readonly StackPanel _onlinePanel = new() { Spacing = 12 };
    private readonly Grid _body = new() { RowDefinitions = new RowDefinitions("Auto,Auto") };
    private readonly TextBlock _position = new() { FontSize = 13, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _details = new() { Spacing = 12 };
    private readonly StackPanel _summary = new() { Spacing = 6 };
    private readonly Grid _heading = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), Margin = new Thickness(16, 10) };
    private readonly Grid _gamePanel = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 0) };
    private readonly Expander _invitationPanel = new() { Header = "Invitation privée", Focusable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _unavailableAction = new() { Text = "Port Android indisponible", FontSize = 15, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 16), Foreground = Brushes.LightGray };
    private readonly TextBlock _hint = new() { FontSize = 11, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 6) };
    private bool _controllerInput;
    private Button? _focusedButton;
    private MobileGame[] _catalog = [], _allCatalog = [];
    private string _activeConsole = "gamecube";
    private int _selected;
    private bool _realized;
    private bool _ribbon = true;
    private string _cache = "";
    private IPointer? _touchPointer;
    private Point _touchStart;
    private int _touchSelection;
    private bool _horizontalDrag;
    private bool _stateLoaded;
    private bool _startupPlatformChoiceShown;
    private readonly PlatformHomeControl _platformHome = new() { IsVisible=false,ZIndex=100 };
    private string _rememberedGame = "";
    private readonly DispatcherTimer _stateSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private LibraryState? _savedState;
    private bool _stateDirty;
    private (double Width, double Height, double Header, double Footer) _layoutKey;

    public MobileLibraryView(IMobileGameService games)
    {
        _games = games;
        _stateSaveTimer.Tick += (_, _) => FlushLibraryState();
        _holdTimer.Tick += (_, _) => InspectHeldCase();
        _launchTransition.DismissRequested += CancelLaunchTransition;
        Background = new SolidColorBrush(Color.Parse("#0B101B"));
        ConfigureButtonStyles();
        var heading = _heading;
        heading.Children.Add(BuildPlatformHeading());
        Grid.SetColumn(_position, 1); heading.Children.Add(_position);
        var help = MakeButton("?");
        help.Width = 48; help.MinHeight = 48; help.Padding = new Thickness(0); help.Margin = new Thickness(0, 0, 6, 0);
        _downloadsButton.MinHeight = _appUpdatesButton.MinHeight = 48;
        _downloadsButton.Padding = _appUpdatesButton.Padding = new Thickness(0);
        _downloadsButton.Margin = new Thickness(0, 0, 6, 0);
        _appUpdatesButton.Width = 48;
        AutomationProperties.SetName(help, "Aide locale LFM3");
        help.Click += (_, _) => MobileApp.HelpRequested?.Invoke(_catalog.ElementAtOrDefault(_selected)?.Title ?? "Jeu sélectionné");
        Grid.SetColumn(help, 2); heading.Children.Add(help);
        Grid.SetColumn(_downloadsButton, 3); heading.Children.Add(_downloadsButton);
        Grid.SetColumn(_appUpdatesButton, 4); heading.Children.Add(_appUpdatesButton);
        _summary.Children.Add(_title); _summary.Children.Add(_info); _summary.Children.Add(_availability);
        _play.Classes.Add("primary"); _download.Classes.Add("primary");
        _play.Content = ActionLabel("▶", "Jouer");
        _download.Content = ActionLabel("＋", "Préparer PartyBoard");
        _online.Content = ActionLabel("◎", "Salons en ligne");
        _play.Click += async (_, _) => await LaunchSelectedGameAsync();
        _online.Click += (_, _) => Run(() => _games.OpenOnline());
        _download.Click += async (_, _) => await PrepareSelectedPortAsync();
        _join.Click += (_, _) => Run(() => _games.OpenOnline(_invite.Text?.Trim()));
        _onlinePanel.Children.Add(_online);
        var invitation = new StackPanel { Spacing = 10 };
        invitation.Children.Add(new TextBlock { Text = "Entre téléphones avec le même APK et un disque compatible. Le port actuel ne permet pas de rejoindre un PC.", FontSize = 13, TextWrapping = TextWrapping.Wrap });
        invitation.Children.Add(_invite); invitation.Children.Add(_join);
        _invitationPanel.Content = invitation;
        _onlinePanel.Children.Add(_invitationPanel);
        _details.Children.Add(_nativeNotes);
        _details.Children.Add(_onlinePanel);
        _body.Children.Add(_shelf);
        _detailsScroll = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 12, 0, 8) };
        _gamePanel.Children.Add(_summary);
        Grid.SetRow(_detailsScroll, 1); _gamePanel.Children.Add(_detailsScroll);
        var primaryActions = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 4) };
        _status.IsVisible = false;
        primaryActions.Children.Add(_status);
        primaryActions.Children.Add(_nativePreparationProgress);
        primaryActions.Children.Add(_play); primaryActions.Children.Add(_download); primaryActions.Children.Add(_unavailableAction);
        _cancelNativePreparation.IsVisible=false;
        _cancelNativePreparation.Click+=(_,_)=>_nativePreparation?.Cancel();
        primaryActions.Children.Add(_cancelNativePreparation);
        Grid.SetRow(primaryActions, 2); _gamePanel.Children.Add(primaryActions);
        Grid.SetRow(_gamePanel, 1); _body.Children.Add(_gamePanel);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(8) };
        content.Children.Add(heading); Grid.SetRow(_body, 1); content.Children.Add(_body);
        Grid.SetRow(_hint, 2); content.Children.Add(_hint);
        var downloads = BuildDownloadsOverlay();
        Grid.SetRowSpan(downloads, 3); content.Children.Add(downloads);
        var inspection = BuildInspectionOverlay();
        Grid.SetRowSpan(inspection, 3); content.Children.Add(inspection);
        Grid.SetRowSpan(_launchTransition, 3); content.Children.Add(_launchTransition);
        Content = content;
        Grid.SetRowSpan(_platformHome,3);content.Children.Add(_platformHome);
        _platformHome.ConsoleChosen+=id=> { _consolePicker.SelectedIndex=Array.FindIndex(_mobilePlatforms,p=>p.Id==id);_platformHome.IsVisible=false;FocusPrimaryAction(); };
        UpdateInputHint();
        AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (args.Pointer.Type == PointerType.Touch) SetControllerInput(false);
        }, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => UpdateControllerFocus(), RoutingStrategies.Bubble);
        _invitationPanel.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty && _controllerInput && _invitationPanel.IsExpanded)
                _invite.Focus();
        };
        AutomationProperties.SetName(_download, "Préparer PartyBoard dans CubeShelf");
        _shelf.SelectionRequested += Select;
        _shelf.AddHandler(PointerPressedEvent, ShelfTouchPressed, RoutingStrategies.Tunnel);
        _shelf.AddHandler(PointerMovedEvent, ShelfTouchMoved, RoutingStrategies.Tunnel);
        _shelf.AddHandler(PointerReleasedEvent, ShelfTouchReleased, RoutingStrategies.Tunnel);
        _shelf.PointerCaptureLost += (_, args) =>
        {
            // Moving capture from a case to its shelf also bubbles a capture-lost
            // event from the case. Keep the gesture while the shelf owns it.
            if (args.Pointer == _touchPointer && args.Pointer.Captured != _shelf) ResetTouch();
        };
        _invite.TextChanged += (_, _) => _join.IsEnabled = _invite.Text?.Trim().Length == 72 && _games.IsInstalled();
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
            {
                insets.IsSystemBarVisible = false;
                insets.DisplayEdgeToEdgePreference = true;
                insets.SystemBarColor = Color.Parse("#0B101B");
            }
            InitializeShelf();
            SubscribeDownloads();
            SetAppUpdatesActive(true);
        };
        DetachedFromVisualTree += (_, _) => { SetAppUpdatesActive(false); UnsubscribeDownloads(); FlushLibraryState(); CancelLaunchTransition(); CloseDownloads(false); CloseInspection(false); ResetTouch(); _shelf.Clear(); CoverBitmapCache.ReleaseUnused(); _realized = false; };
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        LayoutUpdated += (_, _) => UpdateResponsiveLayout();
        KeyDown += (_, args) =>
        {
            if(_platformHome.IsVisible)
            {
                if(_platformHome.SortHasFocus)return;
                if(args.Key is Key.Left or Key.Right) _platformHome.Move(args.Key==Key.Left?-1:1);
                else if(args.Key==Key.Enter)_platformHome.Choose();
                else return;
                args.Handled=true;return;
            }
            if (_consolePicker.IsDropDownOpen) return;
            if (args.Key == Key.Escape) { BackFromController(); args.Handled = true; return; }
            if (_invite.IsKeyboardFocusWithin) return;
            if (args.Key == Key.Left) { BrowseFromController(-1); args.Handled = true; }
            else if (args.Key == Key.Right) { BrowseFromController(1); args.Handled = true; }
            else if (args.Key == Key.Up || args.Key == Key.Down) { MoveActionFocus(args.Key == Key.Up ? -1 : 1); args.Handled = true; }
        };
    }

    private readonly ScrollViewer _detailsScroll;

    public void ActivateFromController()
    {
        if(_platformHome.IsVisible){_platformHome.Choose();return;}
        SetControllerInput(true);
        if (_consolePicker.IsDropDownOpen) { _consolePicker.IsDropDownOpen = false; FocusPrimaryAction(); return; }
        if (_launching) return;
        if (_inspectionCase is not null) { CloseInspection(); return; }
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (_downloadsOverlay.IsVisible)
        {
            if (focused is Button transfer && transfer.IsEffectivelyVisible && transfer.IsEnabled)
                transfer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else FocusDownloadAction();
            return;
        }
        if(ReferenceEquals(focused,_consolePicker))_consolePicker.SelectedIndex=(_consolePicker.SelectedIndex+1)%_consolePicker.ItemCount;
        else if (ReferenceEquals(focused, _invitationPanel)) _invitationPanel.IsExpanded = !_invitationPanel.IsExpanded;
        else if (focused is Button button
            && button.IsEffectivelyVisible && button.IsEnabled)
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else if (!_invite.IsKeyboardFocusWithin) ActivatePrimaryAction();
    }

    public void ActivatePrimaryAction()
    {
        if (_platformHome.IsVisible || _launching || _inspectionCase is not null || _downloadsOverlay.IsVisible) return;
        if (_play.IsVisible && _play.IsEnabled) _ = LaunchSelectedGameAsync();
        else if (_download.IsVisible && _download.IsEnabled) _ = PrepareSelectedPortAsync();
    }

    public void FlipFromController() { SetControllerInput(true); if (!_platformHome.IsVisible && !_launching && !_downloadsOverlay.IsVisible) { if (_inspectionCase is { } cover) cover.Flip(); else _shelf.FlipSelected(); } }
    public void OpenOnlineFromController() { SetControllerInput(true); if (!_platformHome.IsVisible && !_launching && !_downloadsOverlay.IsVisible && _inspectionCase is null && _onlinePanel.IsVisible) Run(() => _games.OpenOnline()); }

    public void BackFromController()
    {
        if(_platformHome.IsVisible)return;
        SetControllerInput(true);
        DismissOverlay();
    }

    public void FocusPrimaryAction()
    {
        if (_play.IsVisible) _play.Focus();
        else if (_download.IsVisible) _download.Focus();
        else _downloadsButton.Focus();
    }

    public void MoveActionFocus(int step)
    {
        if(_platformHome.IsVisible){_platformHome.CycleSort();return;}
        SetControllerInput(true);
        if (_consolePicker.IsDropDownOpen)
        {
            _consolePicker.SelectedIndex = (_consolePicker.SelectedIndex + step + _consolePicker.ItemCount) % _consolePicker.ItemCount;
            return;
        }
        if (_launching) return;
        if (_downloadsOverlay.IsVisible) { MoveDownloadFocus(step); return; }
        if (_inspectionCase is not null) { _closeInspection.Focus(); return; }
        Control[] candidates = [_consolePicker,_downloadsButton, _appUpdatesButton, _online, _invitationPanel, _invite, _join, _play, _download,_cancelNativePreparation];
        var actions = candidates.Where(control => control.IsEffectivelyVisible && control.IsEnabled
            && (control != _invite && control != _join || _invitationPanel.IsExpanded)).ToArray();
        if (actions.Length == 0) return;
        var current = Array.IndexOf(actions, TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control);
        var next = actions[current < 0 ? (step > 0 ? 0 : actions.Length - 1) : (current + step + actions.Length) % actions.Length];
        next.Focus();
        if (_details.IsVisualAncestorOf(next)) next.BringIntoView();
    }

    private void SetControllerInput(bool controller)
    {
        if (_controllerInput == controller) return;
        _controllerInput = controller;
        UpdateInputHint();
        UpdateControllerFocus();
    }

    private void UpdateControllerFocus()
    {
        _focusedButton?.Classes.Remove("controller-focus");
        _focusedButton = _controllerInput ? TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Button : null;
        _focusedButton?.Classes.Add("controller-focus");
    }

    private void UpdateInputHint()
    {
        _hint.Text = _controllerInput
            ? "Stick : parcourir · A : choisir · X : retourner · R3 : agrandir" + (_onlinePanel.IsVisible ? " · Y : en ligne" : "")
            : "Balaye pour choisir · Touche pour retourner · Maintiens pour agrandir";
        _inspectionHint.Text = _controllerInput ? "X : retourner · B : fermer" : "Touche le boîtier pour le retourner";
        _transferHint.Text = _controllerInput ? "Haut/bas : commandes · A : choisir · B : retour" : "Tu peux revenir à ta bibliothèque pendant le téléchargement.";
    }

    private void SetStatus(string? text)
    {
        _status.Text = text;
        _status.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    public void RefreshAvailability()
    {
        if (_catalog.Length == 0) return;
        if(SelectedNativePort is { } native) { RefreshNativeAvailability(native);return; }
        _nativeNotes.IsVisible=false;
        var supported = _catalog[_selected].Id == PartyBoardId;
        var installed = supported && _games.IsInstalled();
        var compatible = supported && _games.DeviceSupportsPublishedGame();
        _availability.Text = !supported ? "Port Android indisponible"
            : installed ? "Prêt à jouer · PartyBoard"
            : !compatible ? "Port incompatible avec cet appareil"
            : "À préparer · PartyBoard";
        _availability.Foreground = new SolidColorBrush(Color.Parse(installed ? "#94D9B6" : compatible ? "#C1B2FF" : "#B2BACB"));
        _play.IsVisible = installed;
        _download.IsVisible = compatible && !installed;
        var state = _games.Downloads?.Snapshot.State;
        if (supported && !installed)
        {
            _availability.Text = state switch
            {
                RuntimeDownloadState.Downloading => "Téléchargement en cours",
                RuntimeDownloadState.Pausing or RuntimeDownloadState.Paused => "Téléchargement en pause",
                RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing => "Préparation en cours",
                RuntimeDownloadState.Failed => "Téléchargement à reprendre",
                _ => _availability.Text
            };
            _download.Content = ActionLabel("＋", state is RuntimeDownloadState.Downloading or RuntimeDownloadState.Pausing
                or RuntimeDownloadState.Paused or RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing or RuntimeDownloadState.Failed
                ? "Voir le téléchargement" : "Préparer PartyBoard");
        }
        _unavailableAction.IsVisible = !_play.IsVisible && !_download.IsVisible;
        _unavailableAction.Text = supported ? "Appareil ARM64 requis" : "Disponible dans la bibliothèque PC";
        _onlinePanel.IsVisible = installed;
        _join.IsEnabled = installed && _invite.Text?.Trim().Length == 72;
        ToolTip.SetTip(_download, "Télécharger et préparer les fichiers dans CubeShelf.");
        UpdateInputHint();
    }

    public void SetActive(bool active)
    {
        SetAppUpdatesActive(active);
        if (active) { InitializeShelf(); RefreshAvailability(); }
        else { FlushLibraryState(); CancelLaunchTransition(); CloseDownloads(false); CloseInspection(false); ResetTouch(); _shelf.Clear(); CoverBitmapCache.ReleaseUnused(); _realized = false; }
    }

    private void ShelfTouchPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Pointer.Type != PointerType.Touch) return;
        if (_touchPointer is not null) { args.Handled = true; return; }
        _touchPointer = args.Pointer;
        _touchStart = args.GetPosition(_shelf);
        _touchSelection = _selected;
        _horizontalDrag = false;
        _holdTimer.Start();
        args.Pointer.Capture(_shelf);
        // Defer the case's tap action until release, so a swipe does not flip it.
        args.Handled = true;
    }

    private void ShelfTouchMoved(object? sender, PointerEventArgs args)
    {
        if (args.Pointer != _touchPointer) return;
        var delta = args.GetPosition(_shelf) - _touchStart;
        if (Math.Abs(delta.X) > 12 || Math.Abs(delta.Y) > 12) _holdTimer.Stop();
        if (!_horizontalDrag && Math.Abs(delta.Y) > 18 && Math.Abs(delta.Y) > Math.Abs(delta.X))
        {
            ResetTouch(); // Leave vertical scrolling to the surrounding page.
            return;
        }
        if (!_horizontalDrag && Math.Abs(delta.X) > 18 && Math.Abs(delta.X) > Math.Abs(delta.Y) * 1.25)
        {
            _horizontalDrag = true;
            args.Pointer.Capture(_shelf);
        }
        if (_horizontalDrag) args.Handled = true;
    }

    private void ShelfTouchReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.Pointer != _touchPointer) return;
        var point = args.GetPosition(_shelf);
        var delta = point - _touchStart;
        var selected = _touchSelection;
        if (_horizontalDrag && Math.Abs(delta.X) >= 45)
            Select(selected + (delta.X < 0 ? 1 : -1));
        else if (!_horizontalDrag && Math.Abs(delta.X) < 18 && Math.Abs(delta.Y) < 18
            && _shelf.GameAtPoint(point) is { } hit)
        {
            if (hit == _selected) _shelf.FlipSelected();
            else Select(hit);
        }
        ResetTouch();
        args.Handled = true;
    }

    private void ResetTouch()
    {
        _holdTimer.Stop();
        var pointer = _touchPointer;
        _touchPointer = null;
        _horizontalDrag = false;
        if (pointer?.Captured == _shelf) pointer.Capture(null);
    }

    public void BrowseFromController(int step)
    {
        if(_platformHome.IsVisible){_platformHome.Move(step);return;}
        SetControllerInput(true);
        if (_launching) return;
        if (_inspectionCase is not null || _downloadsOverlay.IsVisible) return;
        if (_invite.IsKeyboardFocusWithin) return;
        Select(_selected + step);
        FocusPrimaryAction();
    }

    private void InitializeShelf()
    {
        if (_realized) return;
        try
        {
            _cache = Path.Combine(_games.DataDirectory, "covers-v1");
            Directory.CreateDirectory(_cache);
            using var source = AssetLoader.Open(Resource("games.json"));
            _allCatalog = (JsonSerializer.Deserialize<MobileGame[]>(source) ?? []).Where(game => game.Id == PartyBoardId).Concat(NativeCatalogGames()).ToArray();
            if (!_stateLoaded)
            {
                _stateLoaded = true;
                try
                {
                    var statePath = Path.Combine(_games.DataDirectory, "library-view.json");
                    if (File.Exists(statePath) && new FileInfo(statePath).Length <= 16384)
                    {
                        var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(statePath));
                        _savedState = state;
                        _rememberedGame = state?.SelectedGame ?? "";
                        _ribbon = true;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            _mobilePlatforms = CubeShelf.Core.Library.ShelfProduct.All.Where(product => _allCatalog.Any(game => game.ShelfConsole == product.Id)).ToArray();
            _consolePicker.ItemsSource = _mobilePlatforms.Select(product => product.Console).ToArray();
            _consolePicker.IsVisible = _mobilePlatforms.Length > 1;
            _activeConsole = _mobilePlatforms.Any(product => product.Id == _savedState?.Console) ? _savedState!.Console : _mobilePlatforms.FirstOrDefault()?.Id ?? "gamecube";
            _consolePicker.SelectedIndex = Array.FindIndex(_mobilePlatforms, product => product.Id == _activeConsole);
            _catalog=_allCatalog.Where(g=>g.ShelfConsole==_activeConsole).ToArray();
            UpdatePlatformCaption();
            var remembered = Array.FindIndex(_catalog, game => game.Id == _rememberedGame);
            if (remembered >= 0) _selected = remembered;
            RebuildMobileShelf();
            _realized = true;
            Select(_selected, false);
            if (!_startupPlatformChoiceShown && _mobilePlatforms.Length > 1)
            {
                _startupPlatformChoiceShown = true;
                Dispatcher.UIThread.Post(() => { if (_realized) OpenPlatformHome(); });
            }
        }
        catch (Exception error) { SetStatus("Impossible de charger la bibliothèque : " + error.Message); }
    }

    private void Select(int index) => Select(index, true);
    private void Select(int index, bool animate)
    {
        if (_launching || _nativePreparation is not null) return;
        if (_catalog.Length == 0) return;
        _selected = (index % _catalog.Length + _catalog.Length) % _catalog.Length;
        var game = _catalog[_selected];
        _position.Text = $"{_selected + 1} / {_catalog.Length}";
        _title.SetGame(game.Id, game.Title);
        _info.Text = $"{game.Year} · {game.Genre} · {game.Players}";
        SetStatus("");
        _invitationPanel.IsExpanded = false;
        _invite.Text = "";
        _shelf.SetSelection(_selected, _ribbon, animate);
        RefreshAvailability();
        _detailsScroll.Offset = default;
        SaveLibraryState();
    }

    private void SaveLibraryState()
    {
        if (_catalog.Length == 0) return;
        _rememberedGame = _catalog[_selected].Id;
        _stateSaveTimer.Stop();
        _stateDirty = _savedState != new LibraryState(_rememberedGame, _ribbon, _activeConsole);
        if (_stateDirty) _stateSaveTimer.Start();
    }

    private void FlushLibraryState()
    {
        _stateSaveTimer.Stop();
        if (!_stateDirty) return;
        var state = new LibraryState(_rememberedGame, _ribbon, _activeConsole);
        try
        {
            Directory.CreateDirectory(_games.DataDirectory);
            var path = Path.Combine(_games.DataDirectory, "library-view.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state));
            File.Move(path + ".tmp", path, true);
            _savedState = state;
            _stateDirty = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { SetStatus("Impossible de mémoriser la bibliothèque : " + error.Message); }
    }

    private sealed record LibraryState(string SelectedGame, bool Ribbon, string Console="gamecube");

    private string Cover(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return "";
        relative = relative.Replace('\\', '/');
        if (!relative.StartsWith("Assets/Covers/", StringComparison.Ordinal) || relative.Split('/').Contains("..")) return "";
        var target = Path.Combine(_cache, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = AssetLoader.Open(Resource(relative));
            var temporary = target + ".tmp";
            using (var output = File.Create(temporary)) source.CopyTo(output);
            File.Move(temporary, target, true);
        }
        return target;
    }

    private static Uri Resource(string path) => new($"avares://CubeShelf.Mobile/{path}");
    private void CancelLaunchTransition()
    {
        _launchCancellation?.Cancel();
        _launchTransition.Stop();
    }

    private async Task LaunchSelectedGameAsync()
    {
        if (_launching || !_play.IsVisible || _catalog.Length == 0) return;
        _launching = true;_consolePicker.IsEnabled=false;
        using var cancellation = new CancellationTokenSource();
        _launchCancellation = cancellation;
        try
        {
            var game = _catalog[_selected];
            var cover = game.Covers.FirstOrDefault();
            var native = SelectedNativePort;
            var openCase = game.ShelfConsole is "gamecube" or "wii" or "switch" or "n64";
            var media = game.Id == PartyBoardId ? "Assets/Covers/Media/gamecube-GMPE01.png" : cover?.Media;
            _launchTransition.Start(game.Title, Cover(cover?.Front), Cover(cover?.Back), Cover(cover?.Spine), "Lancement…", game.Id,
                game.ShelfConsole,openCase:openCase,media:Cover(media),right:Cover(cover?.Right),top:Cover(cover?.Top),bottom:Cover(cover?.Bottom));
            if(openCase)
            {
                if(!await _launchTransition.WaitForOpeningAsync(cancellation.Token))return;
            }
            else await Task.Delay(500,cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(native is not null) _games.NativePorts!.Launch(game.Id);
            else Run(_games.OpenGame);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { SetStatus(error.Message); }
        finally
        {
            _launchTransition.Stop(); _launchCancellation = null; _launching = false;_consolePicker.IsEnabled=true;
        }
    }

    private void Run(Func<bool> action)
    {
        try
        {
            if (_catalog.Length == 0 || _catalog[_selected].Id != PartyBoardId) return;
            SetStatus(action() ? "" : _games.LastError ?? "Action impossible.");
        }
        catch (Exception error) { SetStatus(error.Message); }
        RefreshAvailability();
    }

    private void UpdateResponsiveLayout()
    {
        if (Content is not Grid root || root.Bounds.Width <= 0 || root.Bounds.Height <= 0) return;
        var header = root.RowDefinitions[0].ActualHeight;
        var footer = root.RowDefinitions[2].ActualHeight;
        var key = (root.Bounds.Width, root.Bounds.Height, header, footer);
        if (_layoutKey == key) return;
        _layoutKey = key;
        var wide = root.Bounds.Width >= 600 && root.Bounds.Width > root.Bounds.Height;
        var narrow = root.Bounds.Width < 360;
        _heading.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumnSpan(_heading.Children[0], narrow ? 5 : 1);
        foreach (var control in _heading.Children.Skip(1)) Grid.SetRow(control, narrow ? 1 : 0);
        _downloadsButton.Content=wide?"Téléchargements":"↓";
        _downloadsButton.FontSize=wide?12:20;
        _downloadsButton.Width=wide?double.NaN:48;
        _downloadsButton.Padding=wide?new Thickness(14, 0):new Thickness(0);
        AutomationProperties.SetName(_downloadsButton,"Téléchargements");
        var available = Math.Max(80, root.Bounds.Height - header - footer);
        var compact = available < 500;
        var shelfHeight = wide ? available : Math.Min(available * (compact ? .36 : .52), root.Bounds.Width * 1.08);
        _body.Height = available;
        _body.ColumnDefinitions = new ColumnDefinitions(wide ? "3*,2*" : "*");
        _body.RowDefinitions = new RowDefinitions(wide ? "*" : "Auto,*");
        Grid.SetRow(_gamePanel, wide ? 0 : 1); Grid.SetColumn(_gamePanel, wide ? 1 : 0);
        _shelf.Height = shelfHeight;
        _shelf.MinHeight = 0;
        _gamePanel.Height = wide ? available : Math.Max(40, available - shelfHeight);
        _title.MaxHeight = compact ? 44 : wide ? 65 : 58;
        _info.FontSize = compact ? 13 : 15;
        _availability.FontSize = compact ? 14 : 16;
        _title.FontSize = wide ? 30 : 24;
    }

    public string DescribeLayout() => $"Root={Bounds}; Body={_body.Bounds}; Shelf={_shelf.Bounds}; Details={_detailsScroll.Bounds}; Viewport={_detailsScroll.Viewport}; Extent={_detailsScroll.Extent}";

    private static Button MakeButton(string text) => new() { Content = text, FontSize = 17, MinHeight = 56,
        FontWeight = FontWeight.SemiBold, CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 12),
        BorderThickness = new Thickness(2),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private sealed class MobileGame
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string VariantOf { get; set; } = "";
        public string ShelfConsole { get; set; } = "gamecube";
        public int Year { get; set; }
        public string Genre { get; set; } = "";
        public string Players { get; set; } = "";
        public MobileCover[] Covers { get; set; } = [];
    }
    private sealed class MobileCover
    {
        public string Front { get; set; } = "";
        public string Back { get; set; } = "";
        public string Spine { get; set; } = "";
        public string Right { get; set; } = "";
        public string Top { get; set; } = "";
        public string Bottom { get; set; } = "";
        public string Media { get; set; } = "";
    }
}
