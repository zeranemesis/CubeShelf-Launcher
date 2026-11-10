using System.Text.Json;

namespace CubeShelf.Mobile;

/// <summary>Serial scheduler for registered native engines, independent of the download screen.</summary>
public sealed class RuntimeDownloadQueue : IRuntimeDownloadManager
{
    private readonly object _gate = new();
    private readonly RuntimeDownloadSource[] _sources;
    private readonly Dictionary<string, RuntimeDownloadSource> _byId;
    private readonly List<string> _order = [];
    private readonly List<RuntimeDownloadHistory> _history = [];
    private readonly Queue<DownloadRateSample> _samples = new(240);
    private readonly Dictionary<string, RuntimeDownloadState> _states = [];
    private readonly string _path;
    private string? _active;
    private bool _paused;
    private double _peak;
    public event Action<RuntimeDownloadSnapshot>? Changed;
    public string? PersistenceError { get; private set; }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public double PeakBytesPerSecond { get { lock (_gate) return _peak; } }
    public RuntimeDownloadSnapshot Snapshot
    {
        get { lock (_gate) return _byId[_active ?? _sources[0].Id].Engine.Snapshot; }
    }
    public IReadOnlyList<RuntimeQueueEntry> Entries
    {
        get
        {
            lock (_gate) return _sources.Select(source =>
            {
                var position = _order.IndexOf(source.Id);
                var snapshot = source.Engine.Snapshot;
                if (position >= 0 && source.Id != _active)
                    snapshot = snapshot with { State = RuntimeDownloadState.Queued, BytesPerSecond = 0 };
                return new RuntimeQueueEntry(source.Id, source.GameId, source.Title, source.Version, snapshot, position);
            }).OrderBy(entry => entry.Position < 0 ? int.MaxValue : entry.Position).ToArray();
        }
    }
    public IReadOnlyList<RuntimeDownloadHistory> History { get { lock (_gate) return _history.ToArray(); } }
    public IReadOnlyList<DownloadRateSample> Samples { get { lock (_gate) return _samples.ToArray(); } }

    public RuntimeDownloadQueue(string directory, params RuntimeDownloadSource[] sources)
    {
        if (sources.Length == 0) throw new ArgumentException("At least one registered engine is required.", nameof(sources));
        _sources = sources;
        _byId = sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        _path = Path.Combine(directory, "download-queue.json");
        var firstRun = !File.Exists(_path);
        Load();
        foreach (var source in sources)
        {
            _states[source.Id] = source.Engine.Snapshot.State;
            source.Engine.Changed += snapshot => OnEngineChanged(source, snapshot);
        }
        if (firstRun)
        {
            // Migrate an existing installation using its real marker timestamp,
            // never invent a download or reimport after the user clears history.
            foreach (var source in sources)
                if (source.Engine.Snapshot.State == RuntimeDownloadState.Ready && source.InstalledAt is { } completed)
                    _history.Add(new(source.Id, source.GameId, source.Title, source.Version, completed, source.Engine.Snapshot.TotalBytes));
            Save();
        }
    }

    // The compatibility methods target the primary engine; the manager uses IDs.
    public void Start() => Enqueue(_sources[0].Id);
    public void Pause() => Pause(_sources[0].Id);
    public void Cancel() => Cancel(_sources[0].Id);

    public void Enqueue(string id)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var source) || source.Engine.Snapshot.State
                is RuntimeDownloadState.Ready or RuntimeDownloadState.Unavailable) return;
            if (!_order.Contains(id)) _order.Add(id);
            Save();
        }
        Notify(); Pump();
    }

    public void Pause(string id)
    {
        RuntimeDownloadSource? source;
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out source)) return;
            _order.Remove(id); Save();
        }
        source.Engine.Pause();
        Notify(); Pump();
    }

    public void Cancel(string id)
    {
        RuntimeDownloadSource? source;
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out source) || source.Engine.Snapshot.State
                is RuntimeDownloadState.Ready or RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing) return;
            _order.Remove(id); Save();
        }
        source.Engine.Cancel();
        Notify(); Pump();
    }

    public void Move(string id, int direction)
    {
        lock (_gate)
        {
            var index = _order.IndexOf(id);
            if (index < 0 || id == _active) return;
            var minimum = _active is not null && _order.Contains(_active) ? 1 : 0;
            var next = Math.Clamp(index + Math.Sign(direction), minimum, _order.Count - 1);
            if (index == next) return;
            _order.RemoveAt(index); _order.Insert(next, id); Save();
        }
        Notify();
    }

    public void SetPaused(bool paused)
    {
        IRuntimeDownloads? engine;
        lock (_gate)
        {
            _paused = paused;
            engine = _active is { } active ? _byId[active].Engine : null;
            Save();
        }
        if (paused) engine?.Pause();
        Notify();
        if (!paused) Pump();
    }

    public void ClearHistory()
    {
        lock (_gate) { _history.Clear(); Save(); }
        Notify();
    }

    private void OnEngineChanged(RuntimeDownloadSource source, RuntimeDownloadSnapshot snapshot)
    {
        lock (_gate)
        {
            var previous = _states[source.Id];
            _states[source.Id] = snapshot.State;
            if (source.Id == _active || snapshot.IsBusy)
            {
                var now = DateTimeOffset.UtcNow;
                if (snapshot.State == RuntimeDownloadState.Downloading && double.IsFinite(snapshot.BytesPerSecond))
                    _peak = Math.Max(_peak, snapshot.BytesPerSecond);
                // A bounded, real timeline. No timer or fake samples when idle.
                if (_samples.Count == 0 || now - _samples.Last().At >= TimeSpan.FromMilliseconds(200))
                {
                    if (_samples.Count == 240) _samples.Dequeue();
                    _samples.Enqueue(new(now, snapshot.State == RuntimeDownloadState.Downloading
                        ? Math.Max(0, snapshot.BytesPerSecond) : 0));
                }
            }
            if (!snapshot.IsBusy)
            {
                if (_active == source.Id) _active = null;
                if (!_paused || snapshot.State != RuntimeDownloadState.Paused) _order.Remove(source.Id);
                if (snapshot.State == RuntimeDownloadState.Ready && previous != RuntimeDownloadState.Ready)
                {
                    _history.Insert(0, new(source.Id, source.GameId, source.Title, source.Version, DateTimeOffset.UtcNow, snapshot.TotalBytes));
                    if (_history.Count > 32) _history.RemoveRange(32, _history.Count - 32);
                }
                Save();
            }
        }
        // Start the next registered transfer before publishing the aggregate
        // status, keeping the foreground service alive across queue handoffs.
        Pump(); Notify();
    }

    private void Pump()
    {
        IRuntimeDownloads? engine = null;
        lock (_gate)
        {
            if (_paused || _active is not null) return;
            while (_order.Count > 0)
            {
                var id = _order[0];
                var candidate = _byId[id].Engine;
                if (candidate.Snapshot.State is RuntimeDownloadState.Ready or RuntimeDownloadState.Unavailable)
                { _order.RemoveAt(0); continue; }
                _active = id; engine = candidate; break;
            }
        }
        engine?.Start();
    }

    private void Notify() => Changed?.Invoke(Snapshot);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 128 * 1024) return;
            var record = JsonSerializer.Deserialize<QueueState>(File.ReadAllText(_path));
            if (record is null) return;
            foreach (var id in (record.Order ?? []).Distinct().Take(_sources.Length))
                if (_byId.TryGetValue(id, out var source) && source.Engine.Snapshot.State
                    is not RuntimeDownloadState.Ready and not RuntimeDownloadState.Unavailable) _order.Add(id);
            _history.AddRange((record.History ?? []).Where(item => item is not null).Take(32));
            // Interrupted work is retained but resumed explicitly from a visible UI.
            _paused = record.Paused || _order.Count > 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { PersistenceError = "La file précédente n’a pas pu être restaurée."; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new QueueState(_order.ToArray(), _history.ToArray(), _paused)));
            File.Move(_path + ".tmp", _path, true);
            PersistenceError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { PersistenceError = "La file n’a pas pu être mémorisée. Le transfert peut continuer."; }
    }

    private sealed record QueueState(string[] Order, RuntimeDownloadHistory[] History, bool Paused);
}
