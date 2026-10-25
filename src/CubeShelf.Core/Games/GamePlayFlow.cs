using System.Collections.Concurrent;

namespace CubeShelf.Core.Games;

public enum GamePlayResult { Started, Cancelled, AlreadyPending }

/// <summary>
/// One explicit Play request owns its target throughout preparation. The operations ensure
/// their prerequisite only when missing, so a ready game starts without an update check.
/// </summary>
public sealed record GamePlayActions(
    Func<CancellationToken, Task<bool>> EnsureDisc,
    Func<CancellationToken, Task> EnsureRuntime,
    Func<CancellationToken, Task> EnsureData,
    Func<CancellationToken, Task> Launch);

public sealed class GamePlayFlow
{
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    public async Task<GamePlayResult> RunAsync(string gameId, GamePlayActions actions, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(actions);
        if (!_pending.TryAdd(gameId, 0)) return GamePlayResult.AlreadyPending;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await actions.EnsureDisc(cancellationToken)) return GamePlayResult.Cancelled;
            cancellationToken.ThrowIfCancellationRequested();
            await actions.EnsureRuntime(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await actions.EnsureData(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await actions.Launch(cancellationToken);
            return GamePlayResult.Started;
        }
        finally { _pending.TryRemove(gameId, out _); }
    }
}
