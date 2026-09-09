
using System.Windows;

namespace CubeShelf.Launcher.Services;

public sealed class DownloadQueueItem : INotifyPropertyChanged
{
    private double _progress;
    private string _status = "En attente";
    private bool _completed;
    private bool _failed;
    private bool _cancelled;
    private bool _running;
    private DateTimeOffset? _startedAt;

    internal CancellationTokenSource Cancellation { get; set; } = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string UniqueKey { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Changes { get; init; } = "";
    public string CoverPath { get; init; } = "";
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnChanged();
            OnChanged(nameof(ProgressPercent));
            OnChanged(nameof(ProgressEtaText));
        }
    }

    public int ProgressPercent => (int)Math.Round(Progress * 100);

    public string ProgressEtaText
    {
        get
        {
            if (!Running || _startedAt is null || Progress <= 0 || Progress >= 1)
                return "";

            var elapsed = DateTimeOffset.Now - _startedAt.Value;
            if (elapsed.TotalSeconds < 1)
                return "";

            var rate = Progress / elapsed.TotalSeconds;
            if (rate <= 0)
                return "";

            var remaining = TimeSpan.FromSeconds((1 - Progress) / rate);
            if (remaining.TotalHours >= 1)
                return $"Temps restant ≈ {(int)remaining.TotalHours} h {remaining.Minutes:00} min";

            return $"Temps restant ≈ {remaining.Minutes:00}:{remaining.Seconds:00}";
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnChanged(); }
    }

    public bool Completed
    {
        get => _completed;
        internal set { _completed = value; OnChanged(); NotifyActions(); }
    }

    public bool Failed
    {
        get => _failed;
        internal set { _failed = value; OnChanged(); NotifyActions(); }
    }

    public bool Cancelled
    {
        get => _cancelled;
        internal set { _cancelled = value; OnChanged(); NotifyActions(); }
    }

    public bool Running
    {
        get => _running;
        internal set
        {
            _running = value;
            if (value)
                _startedAt ??= DateTimeOffset.Now;
            else
                _startedAt = null;

            OnChanged();
            OnChanged(nameof(ProgressEtaText));
            NotifyActions();
        }
    }

    public bool IsTerminal => Completed || Failed || Cancelled;
    public bool CanCancel => !IsTerminal;
    public bool CanRemove => IsTerminal;
    public bool CanRetry => IsTerminal && !Completed && Work is not null;
    public bool CanShowStart =>
        UniqueKey.StartsWith("runtime:", StringComparison.OrdinalIgnoreCase) ||
        UniqueKey.StartsWith("gamedata:", StringComparison.OrdinalIgnoreCase);
    public bool CanStart =>
        IsTerminal && CanShowStart && (Completed || Work is null);
    public Visibility StartVisibility =>
        CanStart ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => CanRetry ? Visibility.Visible : Visibility.Collapsed;

    internal Func<IProgress<double>, CancellationToken, Task>? Work { get; init; }
    internal Func<Task>? After { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyActions()
    {
        OnChanged(nameof(IsTerminal));
        OnChanged(nameof(CanCancel));
        OnChanged(nameof(CanRemove));
        OnChanged(nameof(CanRetry));
        OnChanged(nameof(CanStart));
        OnChanged(nameof(StartVisibility));
        OnChanged(nameof(RetryVisibility));
    }

    private void OnChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DownloadQueueService
{
    private sealed record PersistedItem(
        string UniqueKey,
        string Title,
        string Kind,
        string Changes,
        string CoverPath,
        double Progress,
        string Status,
        bool Completed,
        bool Failed,
        bool Cancelled,
        DateTimeOffset AddedAt);

    private readonly SemaphoreSlim _signal = new(0);
    private readonly string _historyPath;
    private bool _workerStarted;

    public ObservableCollection<DownloadQueueItem> Items { get; } = new();

    public DownloadQueueService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf");

        Directory.CreateDirectory(dataDir);
        _historyPath = Path.Combine(dataDir, "download-history.json");
        LoadHistory();
    }

    public int PendingCount => Items.Count(x => !x.IsTerminal);
    public event EventHandler? QueueChanged;

    public bool Enqueue(
        string uniqueKey,
        string title,
        string kind,
        string changes,
        Func<IProgress<double>, CancellationToken, Task> work,
        Func<Task>? after = null)
    {
        var added = false;

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (Items.Any(x =>
                    !x.IsTerminal &&
                    string.Equals(x.UniqueKey, uniqueKey, StringComparison.OrdinalIgnoreCase)))
                return;

            Items.Add(new DownloadQueueItem
            {
                UniqueKey = uniqueKey,
                Title = title,
                Kind = kind,
                Changes = changes,
                CoverPath = ResolveCoverPath(uniqueKey),
                Work = work,
                After = after
            });

            added = true;
            NotifyQueueChanged();
        });

        if (!added)
            return false;

        if (!_workerStarted)
        {
            _workerStarted = true;
            _ = Task.Run(ProcessLoopAsync);
        }

        _signal.Release();
        return true;
    }

    public void Cancel(DownloadQueueItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (item.IsTerminal)
                return;

            item.Cancellation.Cancel();

            if (item.Running)
                item.Status = "Annulation…";
            else
            {
                item.Cancelled = true;
                item.Status = "Annulé";
            }

            NotifyQueueChanged();
        });
    }

    public void Retry(DownloadQueueItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!item.CanRetry || item.Work is null)
                return;

            item.Cancellation.Dispose();
            item.Cancellation = new CancellationTokenSource();
            item.Completed = false;
            item.Failed = false;
            item.Cancelled = false;
            item.Running = false;
            item.Progress = 0;
            item.Status = "En attente";
            NotifyQueueChanged();
        });

        if (!_workerStarted)
        {
            _workerStarted = true;
            _ = Task.Run(ProcessLoopAsync);
        }

        _signal.Release();
    }

    public void Remove(DownloadQueueItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!item.IsTerminal)
                return;

            item.Cancellation.Dispose();
            Items.Remove(item);
            NotifyQueueChanged();
        });
    }

    public void RemoveTerminalByKey(string uniqueKey)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var item in Items
                         .Where(x =>
                             x.IsTerminal &&
                             string.Equals(
                                 x.UniqueKey,
                                 uniqueKey,
                                 StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                item.Cancellation.Dispose();
                Items.Remove(item);
            }

            NotifyQueueChanged();
        });
    }

    public void ClearFinished()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var item in Items.Where(x => x.IsTerminal).ToList())
            {
                item.Cancellation.Dispose();
                Items.Remove(item);
            }

            NotifyQueueChanged();
        });
    }


    private void NotifyQueueChanged()
    {
        PersistHistory();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PersistHistory()
    {
        try
        {
            var snapshot = Items
                .OrderByDescending(x => x.AddedAt)
                .Take(100)
                .Select(x => new PersistedItem(
                    x.UniqueKey,
                    x.Title,
                    x.Kind,
                    x.Changes,
                    x.CoverPath,
                    x.Progress,
                    x.Status,
                    x.Completed,
                    x.Failed,
                    x.Cancelled,
                    x.AddedAt))
                .ToList();

            File.WriteAllText(
                _historyPath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Download history must never make the launcher fail.
        }
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyPath))
                return;

            var saved = JsonSerializer.Deserialize<List<PersistedItem>>(
                File.ReadAllText(_historyPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            foreach (var entry in saved.OrderBy(x => x.AddedAt))
            {
                var terminal = entry.Completed || entry.Failed || entry.Cancelled;

                Items.Add(new DownloadQueueItem
                {
                    UniqueKey = entry.UniqueKey,
                    Title = entry.Title,
                    Kind = entry.Kind,
                    Changes = entry.Changes,
                    CoverPath = entry.CoverPath,
                    Progress = terminal ? entry.Progress : 0,
                    Status = terminal
                        ? entry.Status
                        : "Interrompu lors de la fermeture de CubeShelf",
                    Completed = entry.Completed,
                    Failed = terminal ? entry.Failed : true,
                    Cancelled = entry.Cancelled,
                    AddedAt = entry.AddedAt
                });
            }
        }
        catch
        {
            // Ignore stale/corrupt history and start with an empty queue.
        }
    }

    private static string ResolveCoverPath(string uniqueKey)
    {
        var parts = uniqueKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "";

        var gameId = parts[1];
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CubeShelf",
                "games.json"),
            Path.Combine(AppContext.BaseDirectory, "games.json")
        };

        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(file))
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var game in doc.RootElement.EnumerateArray())
                {
                    if (!game.TryGetProperty("Id", out var idElement) ||
                        !string.Equals(idElement.GetString(), gameId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!game.TryGetProperty("Covers", out var covers) ||
                        covers.ValueKind != JsonValueKind.Array ||
                        covers.GetArrayLength() == 0)
                        break;

                    var coverEnumerator = covers.EnumerateArray();
                    if (!coverEnumerator.MoveNext())
                        break;

                    var first = coverEnumerator.Current;
                    if (!first.TryGetProperty("Front", out var frontElement))
                        break;

                    var front = frontElement.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(front))
                        break;

                    return Path.IsPathRooted(front)
                        ? Path.GetFullPath(front)
                        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, front));
                }
            }
            catch
            {
                // A missing/invalid cover must never block a download.
            }
        }

        var placeholder = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Covers",
            "Common",
            "placeholder.png");

        return File.Exists(placeholder) ? placeholder : "";
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync();

            DownloadQueueItem? item = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                item = Items.FirstOrDefault(x => !x.IsTerminal && !x.Running);
            });

            if (item is null || item.Work is null)
                continue;

            if (item.Cancellation.IsCancellationRequested)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Cancelled = true;
                    item.Status = "Annulé";
                    NotifyQueueChanged();
                });
                continue;
            }

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = true;
                    item.Status = "Téléchargement…";
                    NotifyQueueChanged();
                });

                var progress = new Progress<double>(p =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Progress = Math.Clamp(p, 0, 1);
                    });
                });

                await item.Work(progress, item.Cancellation.Token);
                item.Cancellation.Token.ThrowIfCancellationRequested();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = false;
                    item.Progress = 1;
                    item.Status = "Terminé";
                    item.Completed = true;
                    NotifyQueueChanged();
                });

                if (item.After is not null)
                    await Application.Current.Dispatcher.InvokeAsync(item.After);
            }
            catch (OperationCanceledException)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = false;
                    item.Cancelled = true;
                    item.Status = "Annulé";
                    NotifyQueueChanged();
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = false;
                    item.Failed = true;
                    item.Status = "Erreur : " + ex.Message;
                    NotifyQueueChanged();
                });
            }
        }
    }
}
