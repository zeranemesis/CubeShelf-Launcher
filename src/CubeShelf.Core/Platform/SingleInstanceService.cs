using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Platform;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _lockPath;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private FileStream? _lockStream;
    private Task? _listener;
    private int _disposed;

    public SingleInstanceService(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        _lockPath = Path.Combine(paths.ConfigurationDirectory, ".cubeshelf-instance.lock");
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(paths.ConfigurationDirectory))))[..20];
        _pipeName = $"CubeShelf.Activate.{identity}";
    }

    public bool IsPrimary { get; private set; }
    public event EventHandler? ActivationRequested;

    public bool TryAcquire()
    {
        ThrowIfDisposed();
        if (IsPrimary) return true;

        try
        {
            _lockStream = new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.None);
            _lockStream.SetLength(0);
            using var writer = new StreamWriter(_lockStream, Encoding.UTF8, 1024, leaveOpen: true);
            writer.Write(Environment.ProcessId);
            writer.Flush();
            _lockStream.Flush(true);
            IsPrimary = true;
            _listener = Task.Run(() => ListenAsync(_lifetime.Token));
            return true;
        }
        catch (IOException)
        {
            TrySignalPrimary();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // If the lock file cannot be created, do not make CubeShelf unusable.
            // The caller can still start, but duplicate protection is unavailable.
            IsPrimary = true;
            return true;
        }
    }

    private void TrySignalPrimary()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                client.Connect(300);
                client.WriteByte(1);
                client.Flush();
                return;
            }
            catch when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch
            {
                return;
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = server.ReadByte();
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(150, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        try { _listener?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _lifetime.Dispose();
        try { _lockStream?.Dispose(); } catch { }
        _lockStream = null;
        if (IsPrimary)
        {
            try { if (File.Exists(_lockPath)) File.Delete(_lockPath); } catch { }
        }
        IsPrimary = false;
    }
}
