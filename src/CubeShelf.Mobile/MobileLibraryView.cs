using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Mobile;

/// <summary>Phone/tablet shell sharing the desktop case renderer and catalogue.</summary>
public sealed class MobileLibraryView : UserControl
{
    private const string PartyBoardId = "GMPE01_00";
    private readonly IMobileGameService _games;
    private readonly GameShelfControl _shelf = new() { Height = 320 };
    private readonly TextBlock _title = new() { FontSize = 27, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _info = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _availability = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _invite = new() { Watermark = "Invitation privée PB4…", MaxLength = 80, MinHeight = 48 };
    private readonly ComboBox _picker = new() { MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _play = MakeButton("Jouer");
    private readonly Button _online = MakeButton("Salons en ligne");
    private readonly Button _download = MakeButton("Installer PartyBoard pour Android");
    private readonly Button _join = MakeButton("Rejoindre l’invitation");
    private readonly StackPanel _onlinePanel = new() { Spacing = 12 };
    private readonly Grid _body = new() { RowDefinitions = new RowDefinitions("Auto,Auto") };
    private readonly UniformGrid _navigation = new() { Columns = 2 };
    private readonly StackPanel _details = new() { Spacing = 16, Margin = new Thickness(20, 0, 20, 24) };
    private MobileGame[] _catalog = [];
    private int _selected;
    private bool _realized;
    private bool _ribbon = true;
    private string _cache = "";
    private IPointer? _touchPointer;
    private Point _touchStart;
    private int _touchSelection;
    private bool _horizontalDrag;
    private bool _stateLoaded;
    private string _rememberedGame = "";

    public MobileLibraryView(IMobileGameService games)
    {
        _games = games;
        Background = new SolidColorBrush(Color.Parse("#0B101B"));
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(20, 18, 20, 12) };
        heading.Children.Add(new TextBlock { Text = "CubeShelf", FontSize = 24, FontWeight = FontWeight.Bold });
        heading.Children.Add(new TextBlock { Text = "Tes jeux, en natif sur Android", FontSize = 14, Foreground = Brushes.LightGray });
        heading.Children.Add(_picker);
        _details.Children.Add(_title); _details.Children.Add(_info);
        var navigation = _navigation;
        AddAction(navigation, "Retourner", () => _shelf.FlipSelected());
        AddAction(navigation, "Vue", () => { _ribbon = !_ribbon; _shelf.SetSelection(_selected, _ribbon); SaveLibraryState(); });
        _details.Children.Add(navigation);
        _details.Children.Add(_availability);
        _play.Background = new SolidColorBrush(Color.Parse("#8B70F7"));
        _play.Click += (_, _) => Run(_games.OpenGame);
        _online.Click += (_, _) => Run(() => _games.OpenOnline());
        _download.Click += (_, _) => Run(_games.OpenDownloadPage);
        _join.Click += (_, _) => Run(() => _games.OpenOnline(_invite.Text?.Trim()));
        _details.Children.Add(_play); _details.Children.Add(_download);
        _onlinePanel.Children.Add(_online);
        _onlinePanel.Children.Add(new TextBlock { Text = "Entre téléphones avec le même APK et un disque compatible. La connexion à un PC est bloquée par le port actuel.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        _onlinePanel.Children.Add(_invite); _onlinePanel.Children.Add(_join);
        _details.Children.Add(_onlinePanel);
        _details.Children.Add(_status);
        _details.Children.Add(new TextBlock { Text = "Pour Mario Party 4, le premier choix du disque, les réglages, les sauvegardes et les mods se font dans PartyBoard. Aucun fichier de jeu n’est fourni.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        _body.Children.Add(_shelf); Grid.SetRow(_details, 1); _body.Children.Add(_details);
        var content = new StackPanel(); content.Children.Add(heading); content.Children.Add(_body);
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _shelf.SelectionRequested += Select;
        _shelf.AddHandler(PointerPressedEvent, ShelfTouchPressed, RoutingStrategies.Tunnel);
        _shelf.AddHandler(PointerMovedEvent, ShelfTouchMoved, RoutingStrategies.Tunnel);
        _shelf.AddHandler(PointerReleasedEvent, ShelfTouchReleased, RoutingStrategies.Tunnel);
        _shelf.PointerCaptureLost += (_, _) => ResetTouch();
        _picker.SelectionChanged += (_, _) => { if (_realized && _picker.SelectedIndex >= 0 && _picker.SelectedIndex != _selected) Select(_picker.SelectedIndex); };
        _invite.TextChanged += (_, _) => _join.IsEnabled = _invite.Text?.Trim().Length == 72 && _games.IsInstalled();
        AttachedToVisualTree += (_, _) => InitializeShelf();
        DetachedFromVisualTree += (_, _) => { ResetTouch(); _shelf.Clear(); CoverBitmapCache.ReleaseUnused(); _realized = false; };
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        KeyDown += (_, args) =>
        {
            if (_invite.IsKeyboardFocusWithin || _picker.IsKeyboardFocusWithin) return;
            if (args.Key == Key.Left) { Select(_selected - 1); args.Handled = true; }
            else if (args.Key == Key.Right) { Select(_selected + 1); args.Handled = true; }
        };
    }

    public void RefreshAvailability()
    {
        if (_catalog.Length == 0) return;
        var supported = _catalog[_selected].Id == PartyBoardId;
        var installed = supported && _games.IsInstalled();
        _availability.Text = !supported ? "Aucun port Android natif prêt à intégrer pour ce jeu."
            : installed ? "✓ PartyBoard Android installé"
            : !_games.DeviceSupportsPublishedGame() ? "L’APK PartyBoard publié nécessite un appareil ARM64. Tu peux parcourir la bibliothèque sur cet appareil."
            : "Installe le port natif PartyBoard pour jouer.";
        _play.IsVisible = installed;
        _download.IsVisible = supported && !installed && _games.DeviceSupportsPublishedGame();
        _onlinePanel.IsVisible = installed;
        _join.IsEnabled = installed && _invite.Text?.Trim().Length == 72;
    }

    public void SetActive(bool active)
    {
        if (active) { InitializeShelf(); RefreshAvailability(); }
        else { ResetTouch(); _shelf.Clear(); CoverBitmapCache.ReleaseUnused(); _realized = false; }
    }

    private void ShelfTouchPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Pointer.Type != PointerType.Touch) return;
        if (_touchPointer is not null) { args.Handled = true; return; }
        _touchPointer = args.Pointer;
        _touchStart = args.GetPosition(_shelf);
        _touchSelection = _selected;
        _horizontalDrag = false;
        // Defer the case's tap action until release, so a swipe does not flip it.
        args.Handled = true;
    }

    private void ShelfTouchMoved(object? sender, PointerEventArgs args)
    {
        if (args.Pointer != _touchPointer) return;
        var delta = args.GetPosition(_shelf) - _touchStart;
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
        var pointer = _touchPointer;
        _touchPointer = null;
        _horizontalDrag = false;
        if (pointer?.Captured == _shelf) pointer.Capture(null);
    }

    public void BrowseFromController(int step)
    {
        if (_invite.IsKeyboardFocusWithin || _picker.IsKeyboardFocusWithin) return;
        Select(_selected + step);
    }

    private void InitializeShelf()
    {
        if (_realized) return;
        try
        {
            _cache = Path.Combine(_games.DataDirectory, "covers-v1");
            Directory.CreateDirectory(_cache);
            using var source = AssetLoader.Open(Resource("games.json"));
            _catalog = (JsonSerializer.Deserialize<MobileGame[]>(source) ?? []).Where(game => string.IsNullOrWhiteSpace(game.VariantOf)).ToArray();
            if (!_stateLoaded)
            {
                _stateLoaded = true;
                try
                {
                    var statePath = Path.Combine(_games.DataDirectory, "library-view.json");
                    if (File.Exists(statePath) && new FileInfo(statePath).Length <= 16384)
                    {
                        var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(statePath));
                        _rememberedGame = state?.SelectedGame ?? "";
                        _ribbon = state?.Ribbon ?? true;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
            var remembered = Array.FindIndex(_catalog, game => game.Id == _rememberedGame);
            if (remembered >= 0) _selected = remembered;
            _picker.ItemsSource = _catalog.Select(game => game.Title).ToArray();
            _shelf.SetGames(_catalog.Select(game =>
            {
                var cover = game.Covers.FirstOrDefault();
                return new GameShelfItem(game.Title, Cover(cover?.Front), Cover(cover?.Back), Cover(cover?.Spine));
            }).ToArray());
            _realized = true;
            Select(_selected, false);
        }
        catch (Exception error) { _status.Text = "Impossible de charger la bibliothèque : " + error.Message; }
    }

    private void Select(int index) => Select(index, true);
    private void Select(int index, bool animate)
    {
        if (_catalog.Length == 0) return;
        _selected = (index % _catalog.Length + _catalog.Length) % _catalog.Length;
        var game = _catalog[_selected];
        _picker.SelectedIndex = _selected;
        _title.Text = game.Title;
        _info.Text = $"{game.Year} · {game.Genre} · {game.Players}";
        _status.Text = "";
        _shelf.SetSelection(_selected, _ribbon, animate);
        RefreshAvailability();
        SaveLibraryState();
    }

    private void SaveLibraryState()
    {
        if (_catalog.Length == 0) return;
        _rememberedGame = _catalog[_selected].Id;
        try
        {
            Directory.CreateDirectory(_games.DataDirectory);
            var path = Path.Combine(_games.DataDirectory, "library-view.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new LibraryState(_rememberedGame, _ribbon)));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _status.Text = "Impossible de mémoriser la bibliothèque : " + error.Message; }
    }

    private sealed record LibraryState(string SelectedGame, bool Ribbon);

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
    private void Run(Func<bool> action)
    {
        try
        {
            if (_catalog.Length == 0 || _catalog[_selected].Id != PartyBoardId) return;
            _status.Text = action() ? "" : _games.LastError ?? "Action impossible.";
        }
        catch (Exception error) { _status.Text = error.Message; }
        RefreshAvailability();
    }

    private void UpdateResponsiveLayout()
    {
        var wide = Bounds.Width >= 700;
        _body.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");
        Grid.SetRow(_details, wide ? 0 : 1); Grid.SetColumn(_details, wide ? 1 : 0);
        _shelf.Height = wide ? Math.Clamp(Bounds.Height - 120, 260, 500) : Math.Clamp(Bounds.Width * .85, 260, 410);
    }

    private static Button MakeButton(string text) => new() { Content = text, FontSize = 17, MinHeight = 52,
        CornerRadius = new CornerRadius(10), Padding = new Thickness(10),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private static void AddAction(Panel panel, string text, Action action)
    {
        var button = MakeButton(text); button.FontSize = 14; button.Margin = new Thickness(3);
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private sealed class MobileGame
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string VariantOf { get; set; } = "";
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
    }
}
