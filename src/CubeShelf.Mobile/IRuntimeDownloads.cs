namespace CubeShelf.Mobile;

public enum RuntimeDownloadState { Unavailable, NotInstalled, Downloading, Pausing, Paused, Verifying, Installing, Ready, Failed, Queued }

public sealed record RuntimeDownloadSnapshot(RuntimeDownloadState State, long ReceivedBytes, long TotalBytes,
    double BytesPerSecond = 0, string? Error = null)
{
    public bool IsBusy => State is RuntimeDownloadState.Downloading or RuntimeDownloadState.Pausing
        or RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing;
}

public sealed record RuntimeDownloadSource(string Id, string GameId, string Title, string Version, IRuntimeDownloads Engine,
    DateTimeOffset? InstalledAt = null);
public sealed record RuntimeQueueEntry(string Id, string GameId, string Title, string Version, RuntimeDownloadSnapshot Transfer, int Position);
public sealed record RuntimeDownloadHistory(string Id, string GameId, string Title, string Version, DateTimeOffset CompletedAt, long Bytes);
public sealed record DownloadRateSample(DateTimeOffset At, double BytesPerSecond);

public interface IRuntimeDownloadManager : IRuntimeDownloads
{
    IReadOnlyList<RuntimeQueueEntry> Entries { get; }
    IReadOnlyList<RuntimeDownloadHistory> History { get; }
    IReadOnlyList<DownloadRateSample> Samples { get; }
    bool IsPaused { get; }
    double PeakBytesPerSecond { get; }
    string? PersistenceError { get; }
    void Enqueue(string id);
    void Pause(string id);
    void Cancel(string id);
    void Move(string id, int direction);
    void SetPaused(bool paused);
    void ClearHistory();
}

public interface IRuntimeDownloads
{
    RuntimeDownloadSnapshot Snapshot { get; }
    event Action<RuntimeDownloadSnapshot>? Changed;
    void Start();
    void Pause();
    void Cancel();
}
