using System.Text.Json;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Downloads;

public enum DownloadActivityState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
    Interrupted
}

public sealed record DownloadActivity(
    string Id,
    string Kind,
    string Title,
    DownloadActivityState State,
    double Progress,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ReferenceId = "");

/// <summary>
/// Durable history for the portable queue. Persistence is deliberately independent
/// from any UI framework so interrupted work can be represented on every platform.
/// </summary>
public sealed class DownloadActivityStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public DownloadActivityStore(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = paths.DownloadHistoryFile;
    }

    public IReadOnlyList<DownloadActivity> Load()
    {
        lock (_gate)
            return LoadUnlocked().OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    public DownloadActivity Create(string kind, string title, string referenceId = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var now = DateTimeOffset.UtcNow;
        var item = new DownloadActivity(Guid.NewGuid().ToString("N"), kind, title,
            DownloadActivityState.Queued, 0, "En attente", now, now, referenceId);
        Upsert(item);
        return item;
    }

    public void Update(string id, DownloadActivityState state, double progress, string message)
    {
        lock (_gate)
        {
            var items = LoadUnlocked();
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) return;
            items[index] = items[index] with
            {
                State = state,
                Progress = Math.Clamp(progress, 0, 1),
                Message = message,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveUnlocked(items);
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var items = LoadUnlocked();
            var removed = items.RemoveAll(item => item.Id == id) > 0;
            if (removed) SaveUnlocked(items);
            return removed;
        }
    }

    public int MarkInterruptedOperations()
    {
        lock (_gate)
        {
            var items = LoadUnlocked();
            var count = 0;
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].State is not (DownloadActivityState.Queued or DownloadActivityState.Running))
                    continue;
                items[index] = items[index] with
                {
                    State = DownloadActivityState.Interrupted,
                    Message = "Interrompu par la fermeture de CubeShelf",
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                count++;
            }
            if (count > 0) SaveUnlocked(items);
            return count;
        }
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            var items = LoadUnlocked().Where(item =>
                item.State is DownloadActivityState.Queued or DownloadActivityState.Running).ToList();
            SaveUnlocked(items);
        }
    }

    private void Upsert(DownloadActivity item)
    {
        lock (_gate)
        {
            var items = LoadUnlocked();
            items.RemoveAll(existing => existing.Id == item.Id);
            items.Add(item);
            SaveUnlocked(items);
        }
    }

    private List<DownloadActivity> LoadUnlocked()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<DownloadActivity>>(File.ReadAllText(_path), _json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void SaveUnlocked(IReadOnlyList<DownloadActivity> items)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items, _json));
        File.Move(temporary, _path, true);
    }
}
