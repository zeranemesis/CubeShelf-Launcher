using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CubeShelf.Core.Downloads;

public sealed class PortableDownloadQueueItem : INotifyPropertyChanged
{
    private double _progress;
    private string _status = "En attente";
    private readonly SynchronizationContext? _notificationContext;
    private DownloadActivityState _state = DownloadActivityState.Queued;
    private DateTimeOffset? _startedAt;
    private readonly bool _cancellable;

    internal PortableDownloadQueueItem(
        string uniqueKey,
        string title,
        string kind,
        string changes,
        string referenceId,
        Func<IProgress<double>, CancellationToken, Task>? work,
        Func<Task>? after,
        SynchronizationContext? notificationContext,
        bool cancellable = true)
    {
        Id = Guid.NewGuid().ToString("N");
        UniqueKey = uniqueKey;
        Title = title;
        Kind = kind;
        Changes = changes;
        ReferenceId = referenceId;
        Work = work;
        After = after;
        _notificationContext = notificationContext;
        _cancellable = cancellable;
        AddedAt = DateTimeOffset.UtcNow;
        UpdatedAt = AddedAt;
    }

    public string Id { get; }
    public string UniqueKey { get; }
    public string Title { get; }
    public string Kind { get; }
    public string Changes { get; }
    public string ReferenceId { get; }
    public DateTimeOffset AddedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public double Progress
    {
        get => _progress;
        internal set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (Math.Abs(_progress - normalized) < .000001) return;
            _progress = normalized;
            Touch();
            OnChanged();
            OnChanged(nameof(ProgressPercent));
            OnChanged(nameof(ProgressPercentText));
            OnChanged(nameof(ProgressEta));
            OnChanged(nameof(ProgressEtaText));
        }
    }

    public int ProgressPercent => (int)Math.Round(Progress * 100);
    public string ProgressPercentText => $"{ProgressPercent}%";

    public string ProgressEtaText
    {
        get
        {
            var remaining = ProgressEta;
            if (remaining is null) return "";
            if (remaining.Value.TotalHours >= 1)
                return $"Temps restant ≈ {(int)remaining.Value.TotalHours} h {remaining.Value.Minutes:00} min";
            return $"Temps restant ≈ {remaining.Value.Minutes:00}:{remaining.Value.Seconds:00}";
        }
    }

    public string StateText => State switch
    {
        DownloadActivityState.Queued => "En attente",
        DownloadActivityState.Running => "En cours",
        DownloadActivityState.Completed => "Terminé",
        DownloadActivityState.Failed => "Échec",
        DownloadActivityState.Cancelled => "Annulé",
        DownloadActivityState.Interrupted => "Interrompu",
        _ => State.ToString()
    };

    public TimeSpan? ProgressEta
    {
        get
        {
            if (State != DownloadActivityState.Running ||
                _startedAt is null || Progress <= 0 || Progress >= 1)
                return null;

            var elapsed = DateTimeOffset.UtcNow - _startedAt.Value;
            if (elapsed.TotalSeconds < 1) return null;
            var rate = Progress / elapsed.TotalSeconds;
            if (rate <= 0) return null;
            return TimeSpan.FromSeconds((1 - Progress) / rate);
        }
    }

    public string Status
    {
        get => _status;
        internal set
        {
            if (_status == value) return;
            _status = value;
            Touch();
            OnChanged();
        }
    }

    public DownloadActivityState State
    {
        get => _state;
        internal set
        {
            if (_state == value) return;
            _state = value;
            if (value == DownloadActivityState.Running)
                _startedAt ??= DateTimeOffset.UtcNow;
            else
                _startedAt = null;
            Touch();
            OnChanged();
            OnChanged(nameof(IsTerminal));
            OnChanged(nameof(CanCancel));
            OnChanged(nameof(CanRetry));
            OnChanged(nameof(CanResume));
            OnChanged(nameof(CanRemove));
            OnChanged(nameof(StateText));
            OnChanged(nameof(ProgressEta));
            OnChanged(nameof(ProgressEtaText));
        }
    }

    public bool IsTerminal => State is DownloadActivityState.Completed
        or DownloadActivityState.Failed
        or DownloadActivityState.Cancelled
        or DownloadActivityState.Interrupted;

    public bool CanCancel => _cancellable && !IsTerminal;
    public bool CanRetry => IsTerminal && State != DownloadActivityState.Completed && Work is not null;
    public bool CanResume => Work is null && IsTerminal && !string.IsNullOrWhiteSpace(ReferenceId) &&
        (Kind.Equals("RUNTIME", StringComparison.OrdinalIgnoreCase) ||
         Kind.Equals("GAME DATA", StringComparison.OrdinalIgnoreCase));
    public bool CanRemove => IsTerminal;

    internal Func<IProgress<double>, CancellationToken, Task>? Work { get; }
    internal Func<Task>? After { get; }
    internal CancellationTokenSource Cancellation { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
    private void OnChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler is null) return;
        var args = new PropertyChangedEventArgs(propertyName);
        if (_notificationContext is not null && SynchronizationContext.Current != _notificationContext)
            _notificationContext.Post(_ => handler(this, args), null);
        else
            handler(this, args);
    }
}

/// <summary>
/// UI-framework-independent serial work queue. It contains no WPF/Avalonia dispatcher
/// references. Consumers can marshal Changed/PropertyChanged events to their UI thread.
/// </summary>
public sealed class PortableDownloadQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly List<PortableDownloadQueueItem> _items = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly DownloadActivityStore? _history;
    private readonly SynchronizationContext? _notificationContext;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _worker;
    private bool _disposed;

    public PortableDownloadQueue(
        DownloadActivityStore? history = null,
        SynchronizationContext? notificationContext = null)
    {
        _history = history;
        _notificationContext = notificationContext;
        _history?.MarkInterruptedOperations();
        if (_history is not null)
        {
            foreach (var activity in _history.Load().OrderBy(item => item.CreatedAt).TakeLast(100))
            {
                var item = new PortableDownloadQueueItem(
                    $"history:{activity.Id}",
                    activity.Title,
                    activity.Kind,
                    activity.Message,
                    activity.ReferenceId,
                    null,
                    null,
                    _notificationContext)
                {
                    Progress = activity.Progress,
                    Status = activity.Message,
                    State = activity.State
                };
                _items.Add(item);
                _historyIds[item.Id] = activity.Id;
            }
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<PortableDownloadQueueItem> Items
    {
        get
        {
            lock (_gate)
                return _items.OrderByDescending(item => item.AddedAt).ToArray();
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _items.Count(item => !item.IsTerminal);
        }
    }

    public PortableDownloadQueueItem? Enqueue(
        string uniqueKey,
        string title,
        string kind,
        string changes,
        Func<IProgress<double>, CancellationToken, Task> work,
        Func<Task>? after = null,
        string referenceId = "",
        bool cancellable = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);

        PortableDownloadQueueItem item;
        lock (_gate)
        {
            if (_items.Any(existing =>
                    !existing.IsTerminal &&
                    existing.UniqueKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase)))
                return null;

            item = new PortableDownloadQueueItem(
                uniqueKey, title, kind, changes, referenceId, work, after, _notificationContext, cancellable);
            _items.Add(item);
        }

        PersistCreate(item);
        RaiseChanged();
        EnsureWorker();
        _signal.Release();
        return item;
    }

    public bool Cancel(PortableDownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (!_items.Contains(item) || !item.CanCancel) return false;
            item.Cancellation.Cancel();
            if (item.State == DownloadActivityState.Queued)
            {
                item.State = DownloadActivityState.Cancelled;
                item.Status = "Annulé";
                Persist(item);
            }
            else
            {
                item.Status = "Annulation…";
            }
        }
        RaiseChanged();
        return true;
    }

    public bool Retry(PortableDownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (!_items.Contains(item) || !item.CanRetry) return false;
            item.Cancellation.Dispose();
            item.Cancellation = new CancellationTokenSource();
            item.Progress = 0;
            item.Status = "En attente";
            item.State = DownloadActivityState.Queued;
            Persist(item);
        }
        RaiseChanged();
        EnsureWorker();
        _signal.Release();
        return true;
    }

    public bool Remove(PortableDownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string? historyId = null;
        lock (_gate)
        {
            if (!item.IsTerminal || !_items.Remove(item)) return false;
            item.Cancellation.Dispose();
            if (_historyIds.TryGetValue(item.Id, out historyId))
                _historyIds.Remove(item.Id);
        }
        if (historyId is not null) _history?.Remove(historyId);
        RaiseChanged();
        return true;
    }

    public int RemoveTerminalByKey(string uniqueKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueKey);
        List<PortableDownloadQueueItem> removed;
        var historyIds = new List<string>();
        lock (_gate)
        {
            removed = _items.Where(item => item.IsTerminal &&
                    item.UniqueKey.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var item in removed)
            {
                _items.Remove(item);
                item.Cancellation.Dispose();
                if (_historyIds.TryGetValue(item.Id, out var historyId))
                {
                    historyIds.Add(historyId);
                    _historyIds.Remove(item.Id);
                }
            }
        }
        foreach (var historyId in historyIds) _history?.Remove(historyId);
        if (removed.Count > 0) RaiseChanged();
        return removed.Count;
    }

    public int ClearFinished()
    {
        List<PortableDownloadQueueItem> removed;
        lock (_gate)
        {
            removed = _items.Where(item => item.IsTerminal).ToList();
            foreach (var item in removed)
            {
                _items.Remove(item);
                item.Cancellation.Dispose();
                _historyIds.Remove(item.Id);
            }
        }
        _history?.ClearFinished();
        if (removed.Count > 0) RaiseChanged();
        return removed.Count;
    }

    private void EnsureWorker()
    {
        lock (_gate)
        {
            if (_worker is null || _worker.IsCompleted)
                _worker = Task.Run(ProcessLoopAsync);
        }
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await _signal.WaitAsync(_lifetime.Token).ConfigureAwait(false);

                PortableDownloadQueueItem? item;
                lock (_gate)
                {
                    item = _items.FirstOrDefault(candidate =>
                        candidate.State == DownloadActivityState.Queued && !candidate.Cancellation.IsCancellationRequested);
                }
                if (item is null) continue;
                if (item.Work is null) continue;

                item.State = DownloadActivityState.Running;
                item.Status = item.Kind.Contains("UNINSTALL", StringComparison.OrdinalIgnoreCase)
                    ? "Suppression…"
                    : item.Kind.Contains("DATA", StringComparison.OrdinalIgnoreCase)
                        ? "Préparation…"
                        : "Téléchargement…";
                Persist(item);
                RaiseChanged();

                var progressGate = new object();
                var lastProgress = -1d;
                var lastPush = DateTimeOffset.MinValue;
                var progress = new InlineProgress<double>(value =>
                {
                    var normalized = Math.Clamp(value, 0, 1);
                    lock (progressGate)
                    {
                        var now = DateTimeOffset.UtcNow;
                        if (normalized < 1 && lastProgress >= 0 &&
                            now - lastPush < TimeSpan.FromMilliseconds(120) &&
                            Math.Abs(normalized - lastProgress) < .005)
                            return;
                        lastProgress = normalized;
                        lastPush = now;
                    }
                    item.Progress = normalized;
                    Persist(item);
                    RaiseChanged();
                });

                try
                {
                    await item.Work(progress, item.Cancellation.Token).ConfigureAwait(false);
                    item.Cancellation.Token.ThrowIfCancellationRequested();
                    item.Progress = 1;
                    item.Status = "Terminé";
                    item.State = DownloadActivityState.Completed;
                    Persist(item);
                    RaiseChanged();
                    if (item.After is not null)
                        await item.After().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    item.Status = "Annulé";
                    item.State = DownloadActivityState.Cancelled;
                    Persist(item);
                    RaiseChanged();
                }
                catch (Exception exception)
                {
                    item.Status = "Erreur : " + exception.Message;
                    item.State = DownloadActivityState.Failed;
                    Persist(item);
                    RaiseChanged();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void PersistCreate(PortableDownloadQueueItem item)
    {
        if (_history is null) return;
        var activity = _history.Create(item.Kind, item.Title,
            string.IsNullOrWhiteSpace(item.ReferenceId) ? item.UniqueKey : item.ReferenceId);
        lock (_gate)
            _historyIds[item.Id] = activity.Id;
    }

    private readonly Dictionary<string, string> _historyIds = new(StringComparer.Ordinal);

    private void Persist(PortableDownloadQueueItem item)
    {
        if (_history is null) return;
        string? id;
        lock (_gate)
            _historyIds.TryGetValue(item.Id, out id);
        if (id is null) return;
        _history.Update(id, item.State, item.Progress, item.Status);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        public void Report(T value) => _callback(value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        lock (_gate)
        {
            foreach (var item in _items.Where(item => !item.IsTerminal))
                item.Cancellation.Cancel();
        }

        try { _signal.Release(); } catch (ObjectDisposedException) { }
        var worker = _worker;
        if (worker is null || worker.IsCompleted)
            CleanupResources();
        else
            _ = worker.ContinueWith(_ => CleanupResources(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CleanupResources()
    {
        lock (_gate)
        {
            foreach (var item in _items)
                item.Cancellation.Dispose();
        }
        _signal.Dispose();
        _lifetime.Dispose();
    }
}
