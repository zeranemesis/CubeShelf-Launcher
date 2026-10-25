using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly Button _downloadsButton = MakeButton("Téléchargements");
    private readonly Grid _downloadsOverlay = new()
    {
        RowDefinitions = new RowDefinitions("Auto,*,Auto"), IsVisible = false, ZIndex = 70,
        Background = new SolidColorBrush(Color.Parse("#0B101B"))
    };
    private readonly Button _closeDownloads = MakeButton("Fermer");
    private readonly Button _pauseQueue = MakeButton("Tout mettre en pause");
    private readonly Button _clearHistory = MakeButton("Effacer l’historique");
    private readonly Button _transferStart = MakeButton("Télécharger");
    private readonly Button _transferPause = MakeButton("Pause");
    private readonly Button _transferCancel = MakeButton("Annuler");
    private readonly TextBlock _transferState = DownloadText(17), _transferDetails = DownloadText(13),
        _transferExplanation = DownloadText(12), _transferName = DownloadText(19), _transferVersion = DownloadText(12),
        _transferHeading = DownloadText(15), _queueHeading = DownloadText(17), _networkRate = DownloadText(22),
        _peakRate = DownloadText(22), _remaining = DownloadText(22), _chartCaption = DownloadText(11), _managerNote = DownloadText(12);
    private readonly DownloadRateChart _rateChart = new() { Height = 82 };
    private readonly Image _transferCover = new() { Width = 82, Height = 116, Stretch = Stretch.UniformToFill };
    private readonly StackPanel _pendingTransfers = new() { Spacing = 10 }, _completedTransfers = new() { Spacing = 10 };
    private readonly ProgressBar _transferProgress = new() { Minimum = 0, Maximum = 100, Height = 7,
        Foreground = new SolidColorBrush(Color.Parse("#A18AFF")) };
    private readonly TextBlock _transferHint = new() { FontSize = 12, Foreground = Brushes.LightGray,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 8) };
    private readonly Dictionary<string, Bitmap> _downloadCovers = [];
    private RuntimeDownloadState? _lastDownloadState;
    private bool _downloadsSubscribed;
    private string _displayedDownloadId = "partyboard", _rowsStamp = "";
    private IRuntimeDownloadManager? DownloadManager => _games.Downloads as IRuntimeDownloadManager;

    private static TextBlock DownloadText(double size) => new()
        { FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private static Border DownloadCard(Control content, int padding = 16) => new()
    {
        Child = content, Padding = new Thickness(padding), CornerRadius = new CornerRadius(18),
        Background = new SolidColorBrush(Color.Parse("#151D2D")),
        BorderBrush = new SolidColorBrush(Color.Parse("#2B3750")), BorderThickness = new Thickness(1)
    };

    private Grid BuildDownloadsOverlay()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 10) };
        header.Children.Add(new TextBlock { Text = "Téléchargements", FontSize = 21,
            FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _closeDownloads.MinHeight = 48; _closeDownloads.FontSize = 14;
        _closeDownloads.Click += (_, _) => CloseDownloads();
        Grid.SetColumn(_closeDownloads, 1); header.Children.Add(_closeDownloads); _downloadsOverlay.Children.Add(header);
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(BuildAppUpdateCard());
        var telemetry = new StackPanel { Spacing = 12 };
        var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        var labels = new[] { "RÉSEAU", "PIC", "RESTANT" };
        var values = new[] { _networkRate, _peakRate, _remaining };
        for (var index = 0; index < 3; index++)
        {
            var metric = new StackPanel { Spacing = 5 };
            var label = DownloadText(10); label.Text = labels[index]; metric.Children.Add(label);
            values[index].Foreground = index == 0 ? new SolidColorBrush(Color.Parse("#B8A6FF")) : Brushes.White;
            metric.Children.Add(values[index]); Grid.SetColumn(metric, index); metrics.Children.Add(metric);
        }
        telemetry.Children.Add(metrics); telemetry.Children.Add(_rateChart); telemetry.Children.Add(_chartCaption);
        AutomationProperties.SetName(_rateChart, "Débit réseau mesuré pendant les transferts, fenêtre de 60 secondes");
        content.Children.Add(DownloadCard(telemetry));
        _pauseQueue.FontSize = 14;
        _pauseQueue.Click += (_, _) => { if (DownloadManager is { } manager) manager.SetPaused(!manager.IsPaused); };
        content.Children.Add(_pauseQueue);
        _transferHeading.FontWeight = FontWeight.SemiBold; content.Children.Add(_transferHeading);
        var card = new StackPanel { Spacing = 14 };
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        identity.Children.Add(new Border { Child = _transferCover, CornerRadius = new CornerRadius(8), ClipToBounds = true });
        var title = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        _transferName.FontWeight = FontWeight.SemiBold; _transferName.Foreground = Brushes.White;
        _transferState.Foreground = new SolidColorBrush(Color.Parse("#B8A6FF"));
        title.Children.Add(_transferName); title.Children.Add(_transferVersion); title.Children.Add(_transferState);
        Grid.SetColumn(title, 1); identity.Children.Add(title); card.Children.Add(identity);
        card.Children.Add(_transferProgress); card.Children.Add(_transferDetails);
        _transferStart.Classes.Add("primary");
        _transferStart.Click += async (_, _) =>
        {
            var entry = DownloadManager?.Entries.FirstOrDefault(item => item.Id == _displayedDownloadId);
            var state = entry?.Transfer.State ?? _games.Downloads?.Snapshot.State;
            if (state == RuntimeDownloadState.Ready)
            {
                CloseDownloads(false);
                var index = Array.FindIndex(_catalog, game => game.Id == (entry?.GameId ?? PartyBoardId));
                if (index >= 0) { Select(index, false); await LaunchSelectedGameAsync(); }
            }
            else if (DownloadManager is { } manager)
            {
                manager.Enqueue(_displayedDownloadId);
                if (manager.IsPaused) manager.SetPaused(false);
            }
            else _games.Downloads?.Start();
        };
        _transferPause.Click += (_, _) =>
        {
            if (DownloadManager is { } manager) manager.Pause(_displayedDownloadId);
            else _games.Downloads?.Pause();
        };
        _transferCancel.Click += (_, _) =>
        {
            if (DownloadManager is { } manager) manager.Cancel(_displayedDownloadId);
            else _games.Downloads?.Cancel();
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _transferStart, _transferPause, _transferCancel })
        { button.FontSize = 14; button.MinHeight = 48; button.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(button); }
        card.Children.Add(actions); card.Children.Add(_transferExplanation); content.Children.Add(DownloadCard(card));
        _queueHeading.FontWeight = FontWeight.SemiBold; content.Children.Add(_queueHeading); content.Children.Add(_pendingTransfers);
        content.Children.Add(new TextBlock { Text = "Terminés", FontSize = 17, FontWeight = FontWeight.SemiBold });
        _clearHistory.FontSize = 12; _clearHistory.HorizontalAlignment = HorizontalAlignment.Left;
        _clearHistory.Click += (_, _) => DownloadManager?.ClearHistory();
        content.Children.Add(_clearHistory); content.Children.Add(_completedTransfers); content.Children.Add(_managerNote);
        var scroll = new ScrollViewer { Content = content, Margin = new Thickness(16, 8), MaxWidth = 1100,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); _downloadsOverlay.Children.Add(scroll);
        Grid.SetRow(_transferHint, 2); _downloadsOverlay.Children.Add(_transferHint);
        _downloadsButton.MinHeight = 48; _downloadsButton.FontSize = 12;
        _downloadsButton.Padding = new Thickness(10, 6); _downloadsButton.CornerRadius = new CornerRadius(14);
        _downloadsButton.Margin = new Thickness(12, 0, 0, 0);
        _downloadsButton.Click += (_, _) => ShowDownloads(); return _downloadsOverlay;
    }

    public void ShowDownloads()
    {
        if (_launching) return;
        CloseInspection(false); ResetTouch(); _body.IsVisible = false; _downloadsOverlay.IsVisible = true; _rowsStamp = "";
        ApplyDownloadSnapshot(_games.Downloads?.Snapshot ?? new(RuntimeDownloadState.Unavailable, 0, 0));
        UpdateInputHint(); if (_controllerInput) FocusDownloadAction();
    }
    private void PreparePartyBoard()
    {
        var start = _games.Downloads?.Snapshot.State == RuntimeDownloadState.NotInstalled;
        ShowDownloads(); if (start) _games.Downloads?.Start();
    }
    private void CloseDownloads(bool restoreFocus = true)
    {
        if (!_downloadsOverlay.IsVisible) return;
        _downloadsOverlay.IsVisible = false; _transferProgress.IsIndeterminate = false;
        _transferCover.Source = null; _pendingTransfers.Children.Clear(); _completedTransfers.Children.Clear();
        foreach (var bitmap in _downloadCovers.Values) bitmap.Dispose();
        _downloadCovers.Clear(); _rowsStamp = ""; _body.IsVisible = true;
        if (restoreFocus)
        {
            _shelf.SetSelection(_selected, _ribbon, false);
            if (_controllerInput) FocusPrimaryAction();
        }
    }
    private void SubscribeDownloads()
    {
        if (_downloadsSubscribed || _games.Downloads is not { } downloads) return;
        downloads.Changed += DownloadChanged; _downloadsSubscribed = true; ApplyDownloadSnapshot(downloads.Snapshot);
    }
    private void UnsubscribeDownloads()
    {
        if (_downloadsSubscribed && _games.Downloads is { } downloads) downloads.Changed -= DownloadChanged;
        _downloadsSubscribed = false;
    }
    private void DownloadChanged(RuntimeDownloadSnapshot snapshot) => Dispatcher.UIThread.Post(() =>
    { if (_downloadsSubscribed) ApplyDownloadSnapshot(snapshot); });
    private static string TransferLabel(RuntimeDownloadState state) => state switch
    {
        RuntimeDownloadState.Downloading => "Téléchargement en cours",
        RuntimeDownloadState.Pausing => "Mise en pause…", RuntimeDownloadState.Paused => "En pause",
        RuntimeDownloadState.Queued => "En attente", RuntimeDownloadState.Verifying => "Vérification des fichiers…",
        RuntimeDownloadState.Installing => "Préparation des fichiers…", RuntimeDownloadState.Ready => "Prêt à jouer",
        RuntimeDownloadState.Failed => "Transfert interrompu", RuntimeDownloadState.Unavailable => "Moteur indisponible sur cet appareil",
        _ => "Prêt à télécharger"
    };
    private static string DataSize(long bytes) => $"{Math.Max(0, bytes) / 1048576d:F1} Mio";
    private static string DataRate(double rate) => $"{Math.Max(0, rate) / 1048576d:F1} Mio/s";

    private void ApplyDownloadSnapshot(RuntimeDownloadSnapshot snapshot)
    {
        var entries = DownloadManager?.Entries ?? Array.Empty<RuntimeQueueEntry>();
        var primaryState = entries.FirstOrDefault(item => item.GameId == PartyBoardId)?.Transfer.State ?? snapshot.State;
        var changed = _lastDownloadState != primaryState; _lastDownloadState = primaryState;
        if (changed) RefreshAvailability();
        if (!_downloadsOverlay.IsVisible) return;
        var manager = DownloadManager;
        var entry = entries.FirstOrDefault(item => item.Transfer.IsBusy)
            ?? entries.FirstOrDefault(item => item.Position >= 0) ?? entries.FirstOrDefault();
        if (entry is not null) snapshot = entry.Transfer;
        _displayedDownloadId = entry?.Id ?? "partyboard";
        var previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _transferName.Text = entry?.Title ?? "Mario Party 4";
        _transferVersion.Text = entry is null ? "PartyBoard · v0.20.0" : $"Moteur · {entry.Version}";
        _transferCover.Source = DownloadCover(entry?.GameId ?? PartyBoardId);
        _transferState.Text = TransferLabel(snapshot.State);
        _transferHeading.Text = snapshot.IsBusy ? "EN COURS" : snapshot.State == RuntimeDownloadState.Ready ? "INSTALLÉ" : "À TÉLÉCHARGER";
        var percent = Math.Clamp(100d * snapshot.ReceivedBytes / Math.Max(1, snapshot.TotalBytes), 0, 100);
        _transferProgress.Value = percent;
        _transferProgress.IsIndeterminate = snapshot.State is RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing;
        _transferProgress.IsVisible = snapshot.State is not RuntimeDownloadState.NotInstalled and not RuntimeDownloadState.Unavailable;
        _transferDetails.Text = snapshot.State == RuntimeDownloadState.Ready ? $"{DataSize(snapshot.TotalBytes)} · Fichiers vérifiés"
            : $"{DataSize(snapshot.ReceivedBytes)} / {DataSize(snapshot.TotalBytes)} · {percent:F0} %";
        _transferStart.IsVisible = snapshot.State is RuntimeDownloadState.NotInstalled or RuntimeDownloadState.Paused
            or RuntimeDownloadState.Failed or RuntimeDownloadState.Ready || snapshot.State == RuntimeDownloadState.Queued && manager?.IsPaused == true;
        var action = snapshot.State switch
        {
            RuntimeDownloadState.Ready => "Jouer", RuntimeDownloadState.Paused => "Reprendre",
            RuntimeDownloadState.Queued => "Reprendre la file", RuntimeDownloadState.Failed => "Réessayer", _ => "Télécharger"
        };
        if ((_transferStart.Tag as string) != action)
        { _transferStart.Tag = action; _transferStart.Content = action == "Jouer" ? ActionLabel("▶", action) : action; }
        _transferPause.IsVisible = snapshot.State is RuntimeDownloadState.Downloading or RuntimeDownloadState.Pausing;
        _transferPause.IsEnabled = snapshot.State == RuntimeDownloadState.Downloading;
        _transferCancel.IsVisible = snapshot.State is RuntimeDownloadState.Downloading or RuntimeDownloadState.Paused or RuntimeDownloadState.Failed or RuntimeDownloadState.Queued;
        _transferExplanation.Text = snapshot.Error ?? (snapshot.State == RuntimeDownloadState.Ready
            ? "Ton image disque personnelle se sélectionne au premier lancement."
            : snapshot.State == RuntimeDownloadState.Queued && manager?.IsPaused == true ? "La file est en pause. Reprends-la quand tu es prêt."
            : "Les fichiers sont préparés dans CubeShelf. Tu peux continuer à parcourir ta bibliothèque.");
        var rate = snapshot.State == RuntimeDownloadState.Downloading ? snapshot.BytesPerSecond : 0;
        var samples = manager?.Samples ?? Array.Empty<DownloadRateSample>();
        _networkRate.Text = DataRate(rate); _peakRate.Text = DataRate(manager?.PeakBytesPerSecond ?? 0);
        var seconds = rate > 0 ? Math.Max(0, snapshot.TotalBytes - snapshot.ReceivedBytes) / rate : double.NaN;
        _remaining.Text = snapshot.State == RuntimeDownloadState.Ready ? "Terminé"
            : snapshot.State is RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing ? "Préparation"
            : double.IsFinite(seconds) ? seconds < 60 ? $"{Math.Max(1, Math.Ceiling(seconds)):F0} s" : $"{Math.Ceiling(seconds / 60):F0} min" : "—";
        _rateChart.SetSamples(samples);
        _chartCaption.Text = samples.Count < 2 ? "Le graphique apparaîtra pendant un téléchargement."
            : "Débit mesuré · 60 s avant le dernier relevé · pic de cette session";
        _pauseQueue.IsVisible = manager is not null && entries.Any(item => item.Transfer.IsBusy || item.Position >= 0);
        _pauseQueue.Content = manager?.IsPaused == true ? "Reprendre la file" : "Tout mettre en pause";
        _queueHeading.Text = "File d’attente" + (manager?.IsPaused == true ? " · en pause" : "");
        ReconcileDownloadRows(entries, manager?.History ?? Array.Empty<RuntimeDownloadHistory>());
        _managerNote.Text = manager?.PersistenceError ?? "Un transfert à la fois · PartyBoard est actuellement le seul moteur Android proposé. Aucun disque de jeu fourni.";
        if (_controllerInput && (previousFocus is null || !previousFocus.IsEffectivelyVisible || !previousFocus.IsEnabled
            || !previousFocus.GetVisualAncestors().Contains(_downloadsOverlay))) FocusDownloadAction();
    }

    private Bitmap? DownloadCover(string gameId)
    {
        if (_downloadCovers.TryGetValue(gameId, out var image)) return image;
        var relative = _catalog.FirstOrDefault(game => game.Id == gameId)?.Covers.FirstOrDefault()?.Front;
        if (string.IsNullOrEmpty(relative)) return null;
        try
        {
            using var stream = File.OpenRead(Cover(relative)); image = Bitmap.DecodeToWidth(stream, 256);
            _downloadCovers[gameId] = image; return image;
        }
        catch (Exception error) when (error is IOException or ArgumentException) { return null; }
    }
    private void ReconcileDownloadRows(IReadOnlyList<RuntimeQueueEntry> entries, IReadOnlyList<RuntimeDownloadHistory> history)
    {
        var pending = entries.Where(item => item.Id != _displayedDownloadId && item.Transfer.State
            is not RuntimeDownloadState.Ready and not RuntimeDownloadState.Unavailable).ToArray();
        var stamp = string.Join('|', pending.Select(item => $"{item.Id}:{item.Position}:{item.Transfer.State}"))
            + ";" + string.Join('|', history.Select(item => $"{item.Id}:{item.CompletedAt:O}"));
        if (_rowsStamp == stamp) return;
        _rowsStamp = stamp; _pendingTransfers.Children.Clear(); _completedTransfers.Children.Clear();
        if (pending.Length == 0)
        {
            var empty = DownloadText(13); empty.Text = "Rien en attente. Les prochains transferts démarreront dans l’ordre choisi.";
            _pendingTransfers.Children.Add(DownloadCard(empty));
        }
        foreach (var entry in pending)
        {
            var info = new StackPanel { Spacing = 8 };
            info.Children.Add(DownloadIdentity(entry.GameId, entry.Title, $"{entry.Version} · {TransferLabel(entry.Transfer.State)}"));
            var actions = new WrapPanel();
            if (entry.Position < 0)
                actions.Children.Add(DownloadRowButton(entry.Transfer.State == RuntimeDownloadState.Paused && DownloadManager?.IsPaused != true ? "Reprendre" : "Ajouter à la file",
                    () => DownloadManager?.Enqueue(entry.Id)));
            else
            {
                var priority = DownloadRowButton("Priorité +", () => DownloadManager?.Move(entry.Id, -1));
                priority.IsEnabled = entries.FirstOrDefault(item => item.Position >= 0 && !item.Transfer.IsBusy)?.Id != entry.Id;
                actions.Children.Add(priority);
            }
            if (entry.Position >= 0 || entry.Transfer.State is RuntimeDownloadState.Paused or RuntimeDownloadState.Failed)
                actions.Children.Add(DownloadRowButton("Annuler", () => DownloadManager?.Cancel(entry.Id)));
            info.Children.Add(actions); _pendingTransfers.Children.Add(DownloadCard(info, 12));
        }
        _clearHistory.IsVisible = history.Count > 0;
        if (history.Count == 0)
        {
            var empty = DownloadText(13); empty.Text = "Les prochains téléchargements terminés apparaîtront ici.";
            _completedTransfers.Children.Add(DownloadCard(empty));
        }
        foreach (var item in history)
            _completedTransfers.Children.Add(DownloadCard(DownloadIdentity(item.GameId, item.Title,
                $"{item.Version} · {DataSize(item.Bytes)} · {item.CompletedAt.ToLocalTime():dd/MM HH:mm}"), 12));
    }
    private Grid DownloadIdentity(string gameId, string title, string detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(new Image { Source = DownloadCover(gameId), Width = 48, Height = 68, Stretch = Stretch.UniformToFill });
        var text = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var name = DownloadText(15); name.Text = title; name.Foreground = Brushes.White;
        var details = DownloadText(12); details.Text = detail;
        text.Children.Add(name); text.Children.Add(details); Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }
    private static Button DownloadRowButton(string text, Action action)
    {
        var button = MakeButton(text); button.FontSize = 12; button.MinHeight = 48; button.Margin = new Thickness(0, 0, 6, 0);
        button.Click += (_, _) => action(); return button;
    }
    private void FocusDownloadAction()
    {
        var control = _transferStart.IsVisible ? _transferStart
            : _transferPause.IsVisible && _transferPause.IsEnabled ? _transferPause : _closeDownloads;
        control.Focus(); control.BringIntoView();
    }
    private void MoveDownloadFocus(int step)
    {
        var visible = _downloadsOverlay.GetVisualDescendants().OfType<Button>()
            .Where(control => control.IsEffectivelyVisible && control.IsEnabled).ToArray();
        if (visible.Length == 0) return;
        var current = Array.IndexOf(visible, TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Button);
        var next = visible[current < 0 ? step > 0 ? 0 : visible.Length - 1 : (current + step + visible.Length) % visible.Length];
        next.Focus(); next.BringIntoView();
    }
}
