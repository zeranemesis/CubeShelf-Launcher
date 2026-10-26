using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;

namespace CubeShelf.Desktop;

public sealed class NativePortsWindow : Window
{
    private readonly ShelfProduct _product;
    private readonly NativePortLibrary _library;
    private readonly IReadOnlyList<NativePort> _ports;
    private readonly ListBox _list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBox _search = new() { Watermark = "Rechercher un jeu ou un projet…", MinHeight = 44 };
    private readonly CheckBox _compatible = new() { Content = "Paquets pour ce PC uniquement", IsChecked = false };
    private readonly TextBlock _count = Text(13), _title = Text(29), _project = Text(15), _notes = Text(15),
        _sources = Text(12), _status = Text(13), _configured = Text(12);
    private readonly ComboBox _packages = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
    private readonly Button _install = Button("Installer ce paquet"), _launch = Button("Jouer"),
        _configure = Button("Associer le programme"), _cancel = Button("Annuler"), _folder = Button("Ouvrir les fichiers"),
        _releases = Button("Versions officielles"), _source = Button("Projet et instructions"), _macApp = Button("Associer une application .app");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
    private CancellationTokenSource? _installation;
    private NativePort? _selected;
    private string? _downloadDirectory;
    private readonly StackPanel _details = new() { Spacing = 18, Margin = new Thickness(28), MaxWidth = 850 };

    public NativePortsWindow(ShelfProduct product, string? selectedPortId = null, IPlatformPaths? paths = null)
    {
        _product = product;
        paths ??= product.CreatePaths();
        _library = new NativePortLibrary(paths);
        _ports = NativePortCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Catalogs", product.Id + ".json"));
        Title = product.Name + " · Ports natifs";
        Width = 1210; Height = 800; MinWidth = 820; MinHeight = 560;
        Background = Brush("#0B0E16"); Foreground = Brushes.White;
        var accent = Brush(product.Accent);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(28, 24) };
        var brand = new StackPanel { Spacing = 7 };
        brand.Children.Add(new TextBlock { Text = product.Name, FontSize = 32, FontWeight = FontWeight.Bold, Foreground = accent });
        brand.Children.Add(new TextBlock { Text = $"{product.Console} sur PC · Ports natifs et recompilations", FontSize = 14, Foreground = Brush("#ADB5CA") });
        header.Children.Add(brand);
        var close = Button(product.Id == "gamecube" ? "Retour à CubeShelf" : "Quitter");
        close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        root.Children.Add(header);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("350,*"), Margin = new Thickness(20, 0, 20, 20), ColumnSpacing = 16 };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        left.Children.Add(_search); Grid.SetRow(_compatible, 1); left.Children.Add(_compatible);
        _compatible.Margin = new Thickness(0, 12); Grid.SetRow(_count, 2); left.Children.Add(_count);
        Grid.SetRow(_list, 3); _list.Margin = new Thickness(0, 12, 0, 0); left.Children.Add(_list);
        body.Children.Add(left);
        _title.FontWeight = FontWeight.Bold; _project.Foreground = accent;
        _details.Children.Add(_title); _details.Children.Add(_project); _details.Children.Add(_notes);
        _details.Children.Add(_sources); _details.Children.Add(_packages);
        _install.Background = accent; _install.Foreground = Brush("#121521");
        _launch.Background = accent; _launch.Foreground = Brush("#121521");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _install, _cancel, _launch, _configure, _macApp, _folder, _releases, _source }) actions.Children.Add(button);
        _details.Children.Add(actions); _details.Children.Add(_progress); _details.Children.Add(_status); _details.Children.Add(_configured);
        var notice = Text(12);
        notice.Text = "Les fichiers de ton jeu se préparent dans le port lui-même, selon ses instructions. Le launcher télécharge uniquement les paquets du projet. La présence d'une release ne garantit pas une partie complète sans bugs.";
        _details.Children.Add(notice);
        var detailBorder = new Border { Background = Brush("#131925"), CornerRadius = new CornerRadius(18),
            Child = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        Grid.SetColumn(detailBorder, 1); body.Children.Add(detailBorder); Grid.SetRow(body, 1); root.Children.Add(body);
        Content = root;
        _search.TextChanged += (_, _) => Filter();
        _compatible.IsCheckedChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => Select((_list.SelectedItem as ListBoxItem)?.Tag as NativePort);
        _packages.SelectionChanged += (_, _) => RefreshActions();
        _source.Click += (_, _) => OpenUrl(_selected?.Homepage);
        _releases.Click += (_, _) => OpenUrl(_selected?.ReleaseUrl.Length > 0 ? _selected.ReleaseUrl : _selected?.Homepage + "/releases");
        _configure.Click += async (_, _) => await ConfigureAsync(false);
        _macApp.Click += async (_, _) => await ConfigureAsync(true);
        _launch.Click += (_, _) => Run(() => { if (_selected is { } port) { _library.Launch(port); _status.Text = "Programme démarré. La préparation des fichiers se fait dans le port."; } });
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => _installation?.Cancel();
        _folder.Click += (_, _) => Run(() => { if (_downloadDirectory is { } directory) new ProcessLauncher().OpenDirectory(directory); });
        Closing += (_, args) =>
        {
            if (_installation is null) return;
            _installation.Cancel(); args.Cancel = true;
            _status.Text = "Annulation en cours… Ferme la fenêtre une fois le transfert arrêté.";
        };
        Filter();
        if (selectedPortId is not null)
            _list.SelectedItem = _list.ItemsSource!.Cast<ListBoxItem>().FirstOrDefault(i => (i.Tag as NativePort)?.Id == selectedPortId);
    }

    private void Filter()
    {
        var term = _search.Text?.Trim() ?? "";
        var ports = _ports.Where(p => (p.Title + " " + p.Project).Contains(term, StringComparison.OrdinalIgnoreCase)
            && (_compatible.IsChecked != true || p.Assets.Any(a => a.MatchesCurrentPlatform))).OrderBy(p => p.Title).ThenBy(p => p.Project).ToList();
        var selectedId = _selected?.Id;
        _list.ItemsSource = ports.Select(port =>
        {
            var title = Text(15); title.Text = port.Title; title.Foreground = Brushes.White; title.FontWeight = FontWeight.SemiBold;
            var detail = Text(12); detail.Text = port.Project + " · " + port.Status;
            return new ListBoxItem { Tag = port, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 5),
                Content = new StackPanel { Spacing = 6, Children = { title, detail } } };
        }).ToList();
        _count.Text = $"{ports.Count} projets · {_ports.Select(p => p.Title).Distinct().Count()} jeux recensés · 26/10/2026";
        var items = _list.ItemsSource.Cast<ListBoxItem>().ToList();
        _list.SelectedItem = items.FirstOrDefault(i => (i.Tag as NativePort)?.Id == selectedId) ?? items.FirstOrDefault();
        if (items.Count == 0) Select(null);
    }

    private void Select(NativePort? port)
    {
        _selected = port; _downloadDirectory = null;
        _details.IsVisible = port is not null;
        if (port is null) return;
        _title.Text = port.Title;
        _project.Text = port.Project + " · " + port.Method + " · " + port.Status;
        _notes.Text = port.Notes;
        _sources.Text = $"Source : {port.Repository}\nRévision du README : {port.SourceCommit}\nVérifié le {port.VerifiedAt} · Release : {(port.ReleaseTag.Length > 0 ? port.ReleaseTag : "pas de paquet public vérifié")}\nPlateformes déclarées : {string.Join(", ", port.Platforms)}";
        var assets = port.Assets.Where(a => a.MatchesCurrentPlatform).ToArray();
        _packages.ItemsSource = assets; _packages.SelectedIndex = assets.Length > 0 ? 0 : -1;
        _packages.IsVisible = assets.Length > 0;
        _status.Text = assets.Length > 0 ? "Choisis un paquet. Il sera vérifié par SHA-256 avant extraction."
            : "Aucun paquet vérifié pour cette architecture. Consulte les instructions du projet ; tu peux associer un port natif déjà compilé.";
        RefreshActions();
    }

    private void RefreshActions()
    {
        var busy = _installation is not null;
        _install.IsVisible = _packages.SelectedItem is NativePortAsset;
        _install.IsEnabled = !busy; _cancel.IsVisible = busy;
        _launch.IsEnabled = !busy && _selected is { } port && _library.IsConfigured(port);
        _configure.IsEnabled = _macApp.IsEnabled = !busy;
        _macApp.IsVisible = OperatingSystem.IsMacOS();
        _folder.IsVisible = _downloadDirectory is not null;
        _list.IsEnabled = _search.IsEnabled = _compatible.IsEnabled = !busy;
        _configured.Text = _selected is { } selected && _library.Executable(selected) is { } path ? "Programme associé : " + path : "Aucun programme associé.";
    }

    private async Task InstallAsync()
    {
        if (_installation is not null || _selected is not { } port || _packages.SelectedItem is not NativePortAsset asset) return;
        using var cancellation = new CancellationTokenSource();
        _installation = cancellation; _progress.IsVisible = true; _progress.Value = 0; RefreshActions();
        _status.Text = "Téléchargement de " + asset.Name + "…";
        try
        {
            _downloadDirectory = await _library.InstallAsync(port, asset,
                new Progress<double>(value => _progress.Value = value * 100), cancellation.Token);
            _status.Text = "Paquet vérifié et préparé. Associe son programme, puis touche Jouer. Les instructions restent disponibles sur la page du projet.";
        }
        catch (OperationCanceledException) { _status.Text = "Téléchargement annulé. Tu peux réessayer."; }
        catch (Exception error) { _status.Text = "Installation interrompue : " + error.Message; }
        finally { _installation = null; _progress.IsVisible = false; RefreshActions(); }
    }

    private async Task ConfigureAsync(bool applicationFolder)
    {
        if (_selected is not { } port) return;
        try
        {
            var start = _downloadDirectory is { } directory ? await StorageProvider.TryGetFolderFromPathAsync(directory) : null;
            string? path;
            if (applicationFolder)
                path = (await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choisir le dossier .app du port", AllowMultiple = false, SuggestedStartLocation = start })).FirstOrDefault()?.TryGetLocalPath();
            else
                path = (await StorageProvider.OpenFilePickerAsync(new() { Title = "Choisir le programme natif de " + port.Title,
                    AllowMultiple = false, SuggestedStartLocation = start, FileTypeFilter = [new FilePickerFileType("Programme natif")
                    { Patterns = OperatingSystem.IsWindows() ? ["*.exe"] : ["*"] }] })).FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;
            _library.Configure(port, path); _status.Text = "Programme associé. Au premier lancement, fournis tes fichiers dans le port."; RefreshActions();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void Run(Action action) { try { action(); } catch (Exception error) { _status.Text = error.Message; } }
    private void OpenUrl(string? url) => Run(() =>
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    });
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    private static TextBlock Text(double size) => new() { FontSize = size, Foreground = Brush("#B3BED3"), TextWrapping = TextWrapping.Wrap };
    private static Button Button(string text) => new() { Content = text, MinHeight = 44, Padding = new Thickness(16, 10), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10) };
}
