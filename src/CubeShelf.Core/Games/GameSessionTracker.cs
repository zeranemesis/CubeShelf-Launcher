using System.Diagnostics;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Games;

public sealed record GameSessionEndedEventArgs(
    string GameId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    int? ExitCode);

/// <summary>
/// Tracks native game processes without depending on WPF or Avalonia.
/// The UI decides whether to minimize/restore itself and persists play statistics.
/// </summary>
public sealed class GameSessionTracker : IDisposable
{
    private sealed record RunningSession(Process Process, DateTimeOffset StartedAt);

    private readonly object _gate = new();
    private readonly Dictionary<string, RunningSession> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly IProcessLauncher _launcher;
    private bool _disposed;

    public GameSessionTracker(IProcessLauncher? launcher = null) =>
        _launcher = launcher ?? new ProcessLauncher();

    public event Action<string>? Started;
    public event Action<GameSessionEndedEventArgs>? Ended;

    public bool IsRunning(string gameId)
    {
        lock (_gate)
            return _running.TryGetValue(gameId, out var session) && IsAlive(session.Process);
    }

    public IReadOnlyCollection<string> RunningGameIds
    {
        get
        {
            lock (_gate)
                return _running.Where(pair => IsAlive(pair.Value.Process)).Select(pair => pair.Key).ToArray();
        }
    }

    public Process Start(
        string gameId,
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        lock (_gate)
        {
            if (_running.TryGetValue(gameId, out var existing) && IsAlive(existing.Process))
                return existing.Process;
        }

        var process = _launcher.Start(executable, workingDirectory, environment, arguments);
        var startedAt = DateTimeOffset.Now;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => FinalizeSession(gameId, process);

        lock (_gate)
            _running[gameId] = new RunningSession(process, startedAt);

        Started?.Invoke(gameId);
        return process;
    }

    public async Task<bool> StopAsync(
        string gameId,
        TimeSpan? gracefulTimeout = null,
        CancellationToken cancellationToken = default)
    {
        RunningSession? session;
        lock (_gate)
            _running.TryGetValue(gameId, out session);

        if (session is null || !IsAlive(session.Process)) return false;
        gracefulTimeout ??= TimeSpan.FromSeconds(2);

        try
        {
            try { _ = session.Process.CloseMainWindow(); } catch { }

            var exitTask = session.Process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(gracefulTimeout.Value, cancellationToken);
            if (await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false) != exitTask && IsAlive(session.Process))
            {
                session.Process.Kill(entireProcessTree: false);
                await session.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            FinalizeSession(gameId, session.Process);
            return true;
        }
        catch (InvalidOperationException)
        {
            FinalizeSession(gameId, session.Process);
            return true;
        }
    }

    private void FinalizeSession(string gameId, Process process)
    {
        RunningSession? tracked;
        lock (_gate)
        {
            if (!_running.TryGetValue(gameId, out tracked) || !ReferenceEquals(tracked.Process, process))
                return;
            _running.Remove(gameId);
        }

        var endedAt = DateTimeOffset.Now;
        int? exitCode = null;
        try { if (process.HasExited) exitCode = process.ExitCode; } catch { }

        Ended?.Invoke(new GameSessionEndedEventArgs(
            gameId,
            tracked.StartedAt,
            endedAt,
            endedAt - tracked.StartedAt,
            exitCode));

        try { process.Dispose(); } catch { }
    }

    private static bool IsAlive(Process process)
    {
        try { return !process.HasExited; }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RunningSession[] sessions;
        lock (_gate)
        {
            sessions = _running.Values.ToArray();
            _running.Clear();
        }
        foreach (var session in sessions)
            try { session.Process.Dispose(); } catch { }
    }
}
