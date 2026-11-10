using System.Diagnostics;

namespace CubeShelf.Core.Downloads;

public enum DownloadPhase { Resolving, Downloading, Verifying, Installing, Preparing }

/// <summary>
/// Scoped to one queue operation, including its awaited child calls. HTTP services opt in at
/// their streaming boundaries; calls made outside the queue retain their existing behaviour.
/// Pausing holds the open stream rather than cancelling and restarting the installation.
/// </summary>
public sealed class DownloadTransfer
{
    private static readonly AsyncLocal<DownloadTransfer?> Ambient = new();
    private readonly object _gate = new();
    private readonly Func<long> _bandwidthLimit;
    private TaskCompletionSource _resume = CompletedSignal();
    private bool _paused;
    private long _networkBytes, _diskBytes, _receivedBytes, _expectedBytes;
    private DownloadPhase _phase;
    private readonly Stopwatch _throttleClock = Stopwatch.StartNew();
    private long _throttleBytes, _lastLimit;

    internal DownloadTransfer(Func<long> bandwidthLimit) => _bandwidthLimit = bandwidthLimit;
    public static DownloadTransfer? Current => Ambient.Value;
    public DownloadPhase Phase { get { lock (_gate) return _phase; } }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public bool CanPause => Phase is DownloadPhase.Resolving or DownloadPhase.Downloading;
    public long NetworkBytes => Interlocked.Read(ref _networkBytes);
    public long DiskBytes => Interlocked.Read(ref _diskBytes);
    public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);
    public long? ExpectedBytes => Interlocked.Read(ref _expectedBytes) is > 0 and var size ? size : null;

    internal IDisposable Enter()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Scope(() => Ambient.Value = previous);
    }

    public void SetPhase(DownloadPhase phase) { lock (_gate) _phase = phase; }

    public static async Task EnterPhaseAsync(DownloadPhase phase, CancellationToken cancellationToken)
    {
        var current = Current;
        if (current is null) return;
        while (true)
        {
            await current.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (current._gate)
            {
                if (current._paused) continue;
                current._phase = phase;
                return;
            }
        }
    }

    public void BeginDownload(long? expectedBytes, long receivedBytes = 0)
    {
        SetPhase(DownloadPhase.Downloading);
        Interlocked.Exchange(ref _receivedBytes, receivedBytes);
        Interlocked.Exchange(ref _expectedBytes, expectedBytes.GetValueOrDefault());
    }

    internal bool SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused) return true;
            if (paused && _phase is not (DownloadPhase.Resolving or DownloadPhase.Downloading)) return false;
            _paused = paused;
            if (paused) _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            else
            {
                _throttleClock.Restart();
                _throttleBytes = 0;
                _resume.TrySetResult();
            }
            return true;
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate) { if (!_paused) break; wait = _resume.Task; }
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task RecordNetworkAsync(int bytes, CancellationToken cancellationToken)
    {
        Interlocked.Add(ref _networkBytes, bytes);
        Interlocked.Add(ref _receivedBytes, bytes);
        await WaitAsync(cancellationToken).ConfigureAwait(false);
        // Limit each payload stream in short, cancellation-aware slices. Changing the limit
        // or resuming takes effect without paying a debt accumulated while paused.
        while (true)
        {
            TimeSpan delay;
            lock (_gate)
            {
                var limit = Math.Max(0, _bandwidthLimit());
                if (limit != _lastLimit)
                {
                    _lastLimit = limit;
                    _throttleBytes = 0;
                    _throttleClock.Restart();
                }
                if (limit == 0) return;
                if (bytes > 0) { _throttleBytes += bytes; bytes = 0; }
                delay = TimeSpan.FromSeconds((double)_throttleBytes / limit) - _throttleClock.Elapsed;
                if (delay <= TimeSpan.Zero)
                {
                    if (_throttleClock.Elapsed.TotalSeconds >= 1)
                    { _throttleClock.Restart(); _throttleBytes = 0; }
                    return;
                }
            }
            await Task.Delay(delay > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : delay,
                cancellationToken).ConfigureAwait(false);
            await WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void RecordDisk(long bytes) => Interlocked.Add(ref _diskBytes, bytes);

    private static TaskCompletionSource CompletedSignal()
    { var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); signal.SetResult(); return signal; }
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
