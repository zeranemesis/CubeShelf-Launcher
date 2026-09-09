
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

    internal CancellationTokenSource Cancellation { get; } = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string UniqueKey { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Changes { get; init; } = "";
    public DateTimeOffset AddedAt { get; } = DateTimeOffset.Now;

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnChanged();
            OnChanged(nameof(ProgressPercent));
        }
    }

    public int ProgressPercent => (int)Math.Round(Progress * 100);

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
        internal set { _running = value; OnChanged(); NotifyActions(); }
    }

    public bool IsTerminal => Completed || Failed || Cancelled;
    public bool CanCancel => !IsTerminal;
    public bool CanRemove => IsTerminal;

    internal Func<IProgress<double>, CancellationToken, Task>? Work { get; init; }
    internal Func<Task>? After { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyActions()
    {
        OnChanged(nameof(IsTerminal));
        OnChanged(nameof(CanCancel));
        OnChanged(nameof(CanRemove));
    }

    private void OnChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DownloadQueueService
{
    private readonly SemaphoreSlim _signal = new(0);
    private bool _workerStarted;

    public ObservableCollection<DownloadQueueItem> Items { get; } = new();

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
                Work = work,
                After = after
            });

            added = true;
            QueueChanged?.Invoke(this, EventArgs.Empty);
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

            QueueChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Remove(DownloadQueueItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!item.IsTerminal)
                return;

            item.Cancellation.Dispose();
            Items.Remove(item);
            QueueChanged?.Invoke(this, EventArgs.Empty);
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

            QueueChanged?.Invoke(this, EventArgs.Empty);
        });
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
                    QueueChanged?.Invoke(this, EventArgs.Empty);
                });
                continue;
            }

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = true;
                    item.Status = "Téléchargement…";
                    QueueChanged?.Invoke(this, EventArgs.Empty);
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
                    QueueChanged?.Invoke(this, EventArgs.Empty);
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
                    QueueChanged?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.Running = false;
                    item.Failed = true;
                    item.Status = "Erreur : " + ex.Message;
                    QueueChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }
    }
}
