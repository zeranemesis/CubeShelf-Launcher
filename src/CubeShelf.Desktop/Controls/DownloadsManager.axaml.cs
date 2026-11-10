using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CubeShelf.Core.Downloads;

namespace CubeShelf.Desktop.Controls;

public sealed partial class DownloadsManager : UserControl, IDisposable
{
    private PortableDownloadQueue? _queue;
    private Func<PortableDownloadQueueItem, string>? _coverPath;
    private Func<string, string, string> _translate = (fr, _) => fr;
    private Action<PortableDownloadQueueItem>? _resume;
    private Action<long>? _saveBandwidth;
    private string _dataDirectory = "";
    private readonly Dictionary<string, Bitmap> _covers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransferRow> _rows = new();
    private readonly List<Visual> _visibilityObservers = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastSample, _peak, _networkRate;
    private long _lastNetwork, _lastDisk;
    private PortableDownloadQueueItem? _dragged;
    private string _activeId = "", _queueIds = "", _historyIds = "", _languageStamp = "";
    private TransferRow? _activeRow;
    private bool _disposed, _settingBandwidth, _attached, _sampling, _idleSampled, _visibilityRefreshQueued;
    private DownloadTransfer? _sampledTransfer;
    private double _lastStorage = double.NegativeInfinity;
    private string _storageStamp = "";
    private readonly Dictionary<string, int> _pendingPositions = new();
    private int _pendingCount;
    private long _displayedLimit = -1;
    private static readonly long[] Limits = [0, 1024 * 1024, 5 * 1024 * 1024, 10 * 1024 * 1024,
        20 * 1024 * 1024, 50 * 1024 * 1024];

    public DownloadsManager()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            if (_disposed) return;
            _attached = true;
            ObserveVisibility();
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _timer.Stop();
            _sampling = false;
            StopObservingVisibility();
        };
    }

    public void Configure(PortableDownloadQueue queue, string dataDirectory,
        Func<PortableDownloadQueueItem, string> coverPath, Func<string, string, string> translate,
        Action<PortableDownloadQueueItem> resume, Action<long> saveBandwidth)
    {
        _queue = queue;
        _dataDirectory = dataDirectory;
        _coverPath = coverPath;
        _translate = translate;
        _resume = resume;
        _saveBandwidth = saveBandwidth;
        Refresh();
    }

    private string L(string fr, string en) => _translate(fr, en);

    private bool CanRefresh => _attached && IsEffectivelyVisible
        && TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized };

    private void ObserveVisibility()
    {
        StopObservingVisibility();
        foreach (var visual in this.GetVisualAncestors().Prepend(this))
        {
            _visibilityObservers.Add(visual);
            visual.PropertyChanged += VisibilityChanged;
        }
    }

    private void StopObservingVisibility()
    {
        foreach (var visual in _visibilityObservers) visual.PropertyChanged -= VisibilityChanged;
        _visibilityObservers.Clear();
    }

    private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty || args.Property == Window.WindowStateProperty)
        {
            // A hidden/minimized view does no timer work, chart updates or filesystem reads.
            _timer.Stop();
            _sampling = false;
            _lastStorage = double.NegativeInfinity;
            // Read effective visibility after Avalonia has propagated the ancestor change.
            if (_visibilityRefreshQueued) return;
            _visibilityRefreshQueued = true;
            Dispatcher.UIThread.Post(() => { _visibilityRefreshQueued = false; Refresh(); });
        }
    }

    public void Refresh()
    {
        if (_queue is null || _disposed) return;
        if (!CanRefresh)
        {
            _timer.Stop();
            _sampling = false;
            return;
        }
        var items = _queue.Items;
        var active = items.FirstOrDefault(i => i.State == DownloadActivityState.Running);
        var pending = _queue.PendingItems;
        _pendingPositions.Clear();
        for (var index = 0; index < pending.Count; index++) _pendingPositions[pending[index].Id] = index;
        _pendingCount = pending.Count;
        var history = items.Where(i => i.IsTerminal).OrderByDescending(i => i.UpdatedAt).ToArray();
        var stamp = L("fr", "en");
        if (stamp != _languageStamp || _displayedLimit != _queue.BandwidthLimitBytesPerSecond)
        {
            _languageStamp = stamp;
            _displayedLimit = _queue.BandwidthLimitBytesPerSecond;
            _activeId = _queueIds = _historyIds = "";
            _rows.Clear();
            Eyebrow.Text = L("GESTION DES TRANSFERTS", "TRANSFER MANAGER");
            Heading.Text = L("Téléchargements", "Downloads");
            NetworkLabel.Text = L("Réseau", "Network");
            DiskLabel.Text = L("Écriture disque", "Disk writes");
            PeakLabel.Text = L("Pic réseau", "Network peak");
            BandwidthLabel.Text = L("Limite du débit", "Bandwidth limit");
            ChartStart.Text = L("Il y a 60 secondes", "60 seconds ago");
            ChartEnd.Text = L("Maintenant", "Now");
            ReorderHint.Text = L("Glisser ou utiliser les flèches pour réorganiser", "Drag or use arrows to reorder");
            ClearHistory.Content = L("Effacer les terminés", "Clear completed");
            _settingBandwidth = true;
            var options = Limits.Select(v => v == 0 ? L("Illimité", "Unlimited") : Rate(v)).ToList();
            if (!Limits.Contains(_queue.BandwidthLimitBytesPerSecond)) options.Add(Rate(_queue.BandwidthLimitBytesPerSecond));
            Bandwidth.ItemsSource = options;
            Bandwidth.SelectedIndex = Array.IndexOf(Limits, _queue.BandwidthLimitBytesPerSecond) is var index && index >= 0 ? index : options.Count - 1;
            _settingBandwidth = false;
        }

        var seconds = _clock.Elapsed.TotalSeconds;
        var moving = active is { IsPaused: false };
        _timer.IsEnabled = moving;
        if (!_sampling || !ReferenceEquals(_sampledTransfer, active?.Transfer))
        {
            if (!_sampling) SpeedChart.ResetSamples();
            _sampledTransfer = active?.Transfer;
            _lastNetwork = _sampledTransfer?.NetworkBytes ?? 0;
            _lastDisk = _sampledTransfer?.DiskBytes ?? 0;
            _lastSample = seconds;
            _sampling = true;
            _idleSampled = false;
            _networkRate = 0;
            NetworkRate.Text = DiskRate.Text = Rate(0);
            ChartScale.Text = L("Échelle : ", "Scale: ") + Rate(SpeedChart.Ceiling);
        }
        if (seconds - _lastSample >= .95 || (!moving && !_idleSampled))
        {
            var network = active?.Transfer.NetworkBytes ?? 0;
            var disk = active?.Transfer.DiskBytes ?? 0;
            var elapsed = seconds - _lastSample;
            _networkRate = moving && elapsed > 0 ? Math.Max(0, network - _lastNetwork) / elapsed : 0;
            var diskRate = moving && elapsed > 0 ? Math.Max(0, disk - _lastDisk) / elapsed : 0;
            _lastSample = seconds; _lastNetwork = network; _lastDisk = disk;
            _peak = Math.Max(_peak, _networkRate);
            SpeedChart.AddSample(_networkRate, diskRate);
            _idleSampled = !moving;
            NetworkRate.Text = Rate(_networkRate); DiskRate.Text = Rate(diskRate); PeakRate.Text = Rate(_peak);
            ChartScale.Text = L("Échelle : ", "Scale: ") + Rate(SpeedChart.Ceiling);
        }

        var storageStamp = stamp + "|" + active?.Id + "|" + history.FirstOrDefault()?.Id;
        if (double.IsNegativeInfinity(_lastStorage) || _storageStamp != storageStamp || (moving && seconds - _lastStorage >= 15))
        {
            _storageStamp = storageStamp;
            _lastStorage = seconds;
            RefreshStorage();
        }

        GlobalPause.Content = _queue.IsSuspended ? L("▶  Tout reprendre", "▶  Resume all") : L("Ⅱ  Tout suspendre", "Ⅱ  Pause all");
        GlobalPause.IsEnabled = _queue.IsSuspended || _queue.PendingCount > 0;
        QueueHeading.Text = L($"En attente · {pending.Count}", $"Queued · {pending.Count}");
        HistoryHeading.Text = L($"Terminés et interrompus · {history.Length}", $"Completed and interrupted · {history.Length}");
        ClearHistory.IsEnabled = history.Any(i => i.State == DownloadActivityState.Completed);
        var activeId = active?.Id ?? "none";
        if (_activeId != activeId)
        {
            _activeId = activeId;
            ActiveContent.Children.Clear();
            _activeRow = active is null ? null : CreateRow(active, true);
            if (_activeRow is not null) ActiveContent.Children.Add(_activeRow.View);
            else ActiveContent.Children.Add(Text(L("Aucun transfert actif", "No active transfer"), 17));
        }
        _activeRow?.Update();
        Reconcile(QueueContent, pending, ref _queueIds, L("Aucun téléchargement en attente.", "No downloads queued."));
        Reconcile(HistoryContent, history, ref _historyIds, L("Tes téléchargements terminés apparaîtront ici.", "Completed downloads will appear here."));
        var retained = items.Select(i => i.Id).ToHashSet();
        var removed = _rows.Keys.Where(id => !retained.Contains(id)).ToArray();
        foreach (var id in removed) _rows.Remove(id);
        if (removed.Length > 0)
        {
            // Clearing history also releases covers that no remaining row uses.
            var used = _rows.Values.Select(row => row.Cover.Source).OfType<Bitmap>().ToHashSet();
            if (_activeRow?.Cover.Source is Bitmap activeCover) used.Add(activeCover);
            foreach (var entry in _covers.Where(entry => !used.Contains(entry.Value)).ToArray())
            { entry.Value.Dispose(); _covers.Remove(entry.Key); }
        }
    }

    private void Reconcile(StackPanel host, IReadOnlyList<PortableDownloadQueueItem> items, ref string previous, string empty)
    {
        var ids = string.Join(',', items.Select(i => i.Id));
        if (ids != previous || host.Children.Count == 0)
        {
            previous = ids;
            host.Children.Clear();
            if (items.Count == 0) host.Children.Add(Text(empty, 13, "Muted"));
            foreach (var item in items)
            {
                if (!_rows.TryGetValue(item.Id, out var row)) _rows[item.Id] = row = CreateRow(item, false);
                if (row.View.Parent is Panel parent) parent.Children.Remove(row.View);
                host.Children.Add(row.View);
            }
        }
        foreach (var item in items) _rows[item.Id].Update();
    }

    private TransferRow CreateRow(PortableDownloadQueueItem item, bool active)
    {
        var nextCoverCheck = double.NegativeInfinity;
        var cover = new Image { Width = active ? 100 : 48, Height = active ? 142 : 68, Stretch = Stretch.Uniform };
        var placeholder = Text("▣", active ? 38 : 25, "Muted");
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var art = new Grid { Width = cover.Width, Height = cover.Height, Children = { placeholder, cover } };
        var title = Text(item.Title, active ? 20 : 14);
        title.FontWeight = FontWeight.SemiBold;
        var subtitle = Text(item.Changes, 12, "Muted");
        var status = Text("", 12, "DownloadAccent");
        var bytes = Text("", 12, "Muted");
        var progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = active ? 8 : 4, Margin = new Thickness(0, 4, 0, 2) };
        progress.Bind(ProgressBar.ForegroundProperty, new DynamicResourceExtension("DownloadAccent"));
        var eta = Text("", 11, "Muted");
        var phases = Text("", 12, "DownloadAccent");
        var info = new StackPanel { Spacing = 5, Children = { title, subtitle, status, bytes, progress, eta, phases } };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MaxWidth = active ? 230 : 300 };
        Button Action(string text, string hint, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 6), Margin = new Thickness(3), MinHeight = 32 };
            ToolTip.SetTip(button, hint); AutomationProperties.SetName(button, hint);
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        var pause = Action(active ? L("Ⅱ  Suspendre", "Ⅱ  Pause") : "Ⅱ", L("Suspendre ou reprendre ce transfert", "Pause or resume this transfer"), () => _queue?.TogglePause(item));
        var first = Action("⇡", L("Placer en premier après l’opération active", "Run next, after the active operation"), () => _queue?.MoveBefore(item, null));
        var up = Action("↑", L("Monter dans la file", "Move up"), () => Move(item, -1));
        var down = Action("↓", L("Descendre dans la file", "Move down"), () => Move(item, 1));
        var retry = Action(L("Réessayer", "Retry"), L("Réessayer cette opération", "Retry this operation"), () => _queue?.Retry(item));
        var resume = Action(L("Reprendre", "Resume"), L("Reprendre l’opération interrompue", "Resume the interrupted operation"), () => _resume?.Invoke(item));
        var cancel = Action(active ? L("Annuler", "Cancel") : "×", L("Annuler ce téléchargement", "Cancel this download"), async () => await ConfirmCancelAsync(item));
        var remove = Action("×", L("Retirer de l’historique", "Remove from history"), () => _queue?.Remove(item));
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions(active ? "100,18,*" : "48,14,*,Auto"), ColumnSpacing = 0 };
        content.Children.Add(art); Grid.SetColumn(info, 2); content.Children.Add(info);
        if (active)
        {
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            info.Children.Remove(title); heading.Children.Add(title); Grid.SetColumn(actions, 1); heading.Children.Add(actions); info.Children.Insert(0, heading);
        }
        else { Grid.SetColumn(actions, 3); content.Children.Add(actions); }
        var view = new Border { Child = content, Padding = active ? new Thickness(0) : new Thickness(0, 13), BorderThickness = active ? new Thickness(0) : new Thickness(0, 0, 0, 1) };
        view.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Border"));

        if (!active)
        {
            // Only our own row handle can initiate a queue move. File drops from outside
            // CubeShelf are not interpreted as download commands.
            cover.Cursor = new Cursor(StandardCursorType.SizeAll);
            cover.PointerPressed += async (_, e) =>
            {
                if (item.State != DownloadActivityState.Queued || !e.GetCurrentPoint(cover).Properties.IsLeftButtonPressed) return;
                _dragged = item;
                try
                {
#pragma warning disable CS0618
                    var data = new DataObject(); data.Set("CubeShelfDownloadId", item.Id);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618
                }
                finally { _dragged = null; }
            };
            DragDrop.SetAllowDrop(view, true);
            DragDrop.AddDragOverHandler(view, (_, e) => { e.DragEffects = _dragged is not null && item.State == DownloadActivityState.Queued ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; });
            DragDrop.AddDropHandler(view, (_, e) => { if (_dragged is not null) _queue?.MoveBefore(_dragged, item); e.Handled = true; });
        }
        return new TransferRow(view, () =>
        {
            var transfer = item.Transfer;
            var pending = item.State == DownloadActivityState.Queued;
            var running = item.State == DownloadActivityState.Running;
            var phase = transfer.Phase;
            if (cover.Source is null && _clock.Elapsed.TotalSeconds >= nextCoverCheck)
            {
                nextCoverCheck = _clock.Elapsed.TotalSeconds + 10;
                var path = _coverPath?.Invoke(item) ?? "";
                try
                {
                    if (!_covers.TryGetValue(path, out var bitmap) && File.Exists(path))
                    { using var stream = File.OpenRead(path); _covers[path] = bitmap = Bitmap.DecodeToWidth(stream, 200); }
                    if (bitmap is not null) { cover.Source = bitmap; placeholder.IsVisible = false; }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
            }
            var position = _pendingPositions.GetValueOrDefault(item.Id, -1);
            status.Text = item.IsPaused ? L("En pause · progression conservée", "Paused · progress saved")
                : pending ? (_queue?.IsSuspended == true ? L("File suspendue", "Queue suspended")
                    : L($"En attente · priorité {position + 1}", $"Queued · priority {position + 1}"))
                : running ? (item.Kind.Contains("UNINSTALL", StringComparison.OrdinalIgnoreCase)
                    ? L("Suppression en cours", "Removing files") : PhaseName(phase)) : item.State switch
                {
                    DownloadActivityState.Completed => L("Terminé", "Completed"),
                    DownloadActivityState.Failed => L("Échec · ", "Failed · ") + item.Status,
                    DownloadActivityState.Interrupted => item.CanResume
                        ? L("Interrompu · reprise disponible", "Interrupted · resume available")
                        : L("Interrompu par la fermeture de CubeShelf", "Interrupted when CubeShelf closed"),
                    _ => L("Annulé", "Cancelled")
                };
            bytes.Text = transfer.ExpectedBytes is { } expected
                ? $"{Size(transfer.ReceivedBytes)} / {Size(expected)} · {item.ProgressPercent} %"
                : transfer.ReceivedBytes > 0 ? L($"{Size(transfer.ReceivedBytes)} reçus", $"{Size(transfer.ReceivedBytes)} received")
                : item.Progress > 0 ? $"{item.ProgressPercent} %" : "";
            bytes.IsVisible = !string.IsNullOrEmpty(bytes.Text) && !item.IsTerminal;
            progress.IsVisible = running || (pending && item.Progress > 0);
            progress.Value = item.Progress;
            progress.IsIndeterminate = running && item.Progress <= 0 && transfer.ExpectedBytes is null;
            eta.Text = running && !item.IsPaused && phase == DownloadPhase.Downloading && _networkRate > 0 && transfer.ExpectedBytes is { } total
                ? L("Transfert restant ≈ ", "Transfer remaining ≈ ") + Duration((total - transfer.ReceivedBytes) / _networkRate) : "";
            eta.IsVisible = !string.IsNullOrEmpty(eta.Text);
            phases.Text = active ? phase switch
            {
                DownloadPhase.Downloading => L("↓ Téléchargement  →  Vérification  →  Installation", "↓ Download  →  Verify  →  Install"),
                DownloadPhase.Verifying => L("✓ Téléchargement  →  Vérification en cours  →  Installation", "✓ Download  →  Verifying  →  Install"),
                DownloadPhase.Installing => L("✓ Transfert terminé  →  Installation en cours", "✓ Transfer complete  →  Installing"),
                DownloadPhase.Preparing => L("Préparation des données sur le disque", "Preparing game data on disk"),
                _ => L("Recherche du paquet et de sa version…", "Resolving package and version…")
            } : "";
            phases.IsVisible = active;
            pause.IsVisible = !item.IsTerminal;
            pause.IsEnabled = item.CanPause || item.IsPaused;
            pause.Content = item.IsPaused ? (active ? L("▶  Reprendre", "▶  Resume") : "▶") : (active ? L("Ⅱ  Suspendre", "Ⅱ  Pause") : "Ⅱ");
            ToolTip.SetTip(pause, pause.IsEnabled ? L("Suspendre ou reprendre", "Pause or resume") : L("Cette étape doit se terminer avant la pause de la file.", "This step must finish before the queue pauses."));
            first.IsVisible = up.IsVisible = down.IsVisible = pending;
            first.IsEnabled = up.IsEnabled = position > 0;
            down.IsEnabled = position >= 0 && position < _pendingCount - 1;
            retry.IsVisible = item.CanRetry; resume.IsVisible = item.CanResume;
            cancel.IsVisible = !item.IsTerminal; cancel.IsEnabled = item.CanCancel;
            remove.IsVisible = item.CanRemove;
        }, cover);
    }

    private void Move(PortableDownloadQueueItem item, int direction)
    {
        if (_queue is null) return;
        var items = _queue.PendingItems.ToList();
        var index = items.IndexOf(item);
        if (index < 0 || index + direction < 0 || index + direction >= items.Count) return;
        if (direction < 0) _queue.MoveBefore(item, items[index - 1]);
        else _queue.MoveBefore(items[index + 1], item);
    }

    private async Task ConfirmCancelAsync(PortableDownloadQueueItem item)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new Window { Title = L("Annuler le téléchargement", "Cancel download"), Width = 480,
            SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        body.Children.Add(Text(item.Title, 18));
        body.Children.Add(Text(L("Retirer cette opération ? Le jeu déjà installé et les sauvegardes sont conservés.", "Remove this operation? The installed game and saves are preserved."), 13));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var keep = new Button { Content = L("Conserver", "Keep") };
        var cancel = new Button { Content = L("Annuler le téléchargement", "Cancel download") };
        keep.Click += (_, _) => dialog.Close(false); cancel.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(keep); actions.Children.Add(cancel); body.Children.Add(actions); dialog.Content = body;
        if (await dialog.ShowDialog<bool>(owner)) _queue?.Cancel(item);
    }

    private void ToggleGlobalPause(object? sender, RoutedEventArgs e)
    { if (_queue is not null) _queue.SetSuspended(!_queue.IsSuspended); }

    private void ClearCompleted(object? sender, RoutedEventArgs e)
    { if (_queue is not null) foreach (var item in _queue.Items.Where(i => i.State == DownloadActivityState.Completed)) _queue.Remove(item); }

    private void BandwidthChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingBandwidth || _queue is null || Bandwidth.SelectedIndex < 0 || Bandwidth.SelectedIndex >= Limits.Length) return;
        _queue.BandwidthLimitBytesPerSecond = Limits[Bandwidth.SelectedIndex];
        _saveBandwidth?.Invoke(_queue.BandwidthLimitBytesPerSecond);
    }

    private void RefreshStorage()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_dataDirectory));
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            StorageText.Text = L($"Bibliothèque : {_dataDirectory} · {Size(drive.AvailableFreeSpace)} disponibles", $"Library: {_dataDirectory} · {Size(drive.AvailableFreeSpace)} free");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { StorageText.Text = _dataDirectory; }
    }

    private string PhaseName(DownloadPhase phase) => phase switch
    {
        DownloadPhase.Downloading => L("Téléchargement en cours", "Downloading"),
        DownloadPhase.Verifying => L("Vérification de l’intégrité", "Verifying integrity"),
        DownloadPhase.Installing => L("Installation en cours · ne pas fermer CubeShelf", "Installing · keep CubeShelf open"),
        DownloadPhase.Preparing => L("Préparation des données", "Preparing game data"),
        _ => L("Connexion au serveur…", "Connecting to server…")
    };

    private string Rate(double bytes) => $"{bytes / (1024 * 1024):0.#} {L("Mio/s", "MiB/s")}";
    private string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.#} {L("Gio", "GiB")}" : $"{bytes / (1024d * 1024):0.#} {L("Mio", "MiB")}";
    private static string Duration(double seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 365 * 86400)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
    private static TextBlock Text(string text, double size, string brush = "Text")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush));
        return block;
    }

    public void Dispose()
    {
        _disposed = true; _timer.Stop();
        StopObservingVisibility();
        ActiveContent.Children.Clear(); QueueContent.Children.Clear(); HistoryContent.Children.Clear();
        foreach (var bitmap in _covers.Values) bitmap.Dispose();
        _covers.Clear(); _rows.Clear();
    }

    private sealed record TransferRow(Control View, Action Update, Image Cover);
}
