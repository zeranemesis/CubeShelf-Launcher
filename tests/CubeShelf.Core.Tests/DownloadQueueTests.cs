using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Mods;

internal static class DownloadQueueTests
{
    public static void OrderAndLocalPause() => Run(async () =>
    {
        using var queue = new PortableDownloadQueue();
        queue.SetSuspended(true);
        var order = new ConcurrentQueue<string>();
        PortableDownloadQueueItem Add(string id) => queue.Enqueue(id, id, "RUNTIME", "",
            (_, _) => { order.Enqueue(id); return Task.CompletedTask; })!;
        var a = Add("a"); var b = Add("b"); var c = Add("c");
        Check(queue.MoveBefore(c, a));
        Check(queue.TogglePause(b));
        Check(queue.Items.All(i => i.State == DownloadActivityState.Queued));
        queue.SetSuspended(false);
        await Until(() => a.IsTerminal && c.IsTerminal);
        Check(order.SequenceEqual(new[] { "c", "a" }));
        Check(b.State == DownloadActivityState.Queued && b.IsPaused);
        queue.TogglePause(b);
        await Until(() => b.IsTerminal);
        Check(order.SequenceEqual(new[] { "c", "a", "b" }));
    });

    public static void ActivePauseResumeAndCancellation() => Run(async () =>
    {
        using var queue = new PortableDownloadQueue();
        var ready = Signal(); var enter = Signal(); var passed = Signal();
        var item = queue.Enqueue("active", "active", "RUNTIME", "", async (_, ct) =>
        {
            var transfer = DownloadTransfer.Current!;
            transfer.BeginDownload(100);
            ready.SetResult();
            await enter.Task.WaitAsync(ct);
            await transfer.WaitAsync(ct);
            passed.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        })!;
        await ready.Task;
        Check(queue.TogglePause(item));
        enter.SetResult();
        Check(await Task.WhenAny(passed.Task, Task.Delay(80)) != passed.Task);
        Check(item.State == DownloadActivityState.Running && item.IsPaused);
        queue.TogglePause(item);
        await passed.Task;
        queue.TogglePause(item);
        Check(queue.Cancel(item));
        await Until(() => item.IsTerminal);
        Check(item.State == DownloadActivityState.Cancelled && !item.IsPaused);
    });

    public static void SuspendedQueueSurvivesActiveCancellation() => Run(async () =>
    {
        using var queue = new PortableDownloadQueue();
        var ready = Signal();
        var first = queue.Enqueue("one", "one", "RUNTIME", "", async (_, ct) =>
        {
            DownloadTransfer.Current!.BeginDownload(100);
            ready.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        })!;
        await ready.Task;
        var next = queue.Enqueue("two", "two", "RUNTIME", "", (_, _) => Task.CompletedTask)!;
        queue.SetSuspended(true);
        queue.Cancel(first);
        await Until(() => first.IsTerminal);
        Check(next.State == DownloadActivityState.Queued);
        queue.SetSuspended(false);
        await Until(() => next.IsTerminal);
        Check(next.State == DownloadActivityState.Completed);
    });

    public static void BandwidthAndCancellation() => Run(async () =>
    {
        long limit = 8192;
        var transfer = new DownloadTransfer(() => Interlocked.Read(ref limit));
        transfer.BeginDownload(8192);
        var watch = Stopwatch.StartNew();
        await transfer.RecordNetworkAsync(8192, CancellationToken.None);
        Check(watch.Elapsed.TotalSeconds >= .85);
        Check(transfer.NetworkBytes == 8192 && transfer.ReceivedBytes == 8192);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        try { await transfer.RecordNetworkAsync(8192, cts.Token); throw new Exception("Throttle ignored cancellation"); }
        catch (OperationCanceledException) { }
        Interlocked.Exchange(ref limit, 0);
        await transfer.RecordNetworkAsync(32, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    });

    public static void PayloadTelemetryAndScopeIsolation() => Run(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-download-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var payload = new byte[300_000]; Random.Shared.NextBytes(payload);
            using var http = new HttpClient(new PayloadHandler(payload));
            var client = new GameBananaClient(http);
            using var queue = new PortableDownloadQueue();
            var item = queue.Enqueue("payload", "payload", "MOD", "", (p, ct) =>
                client.DownloadAsync(new GameBananaMod(42, "Board", 1, "https://gamebanana.com/dl/42", "board.zip"),
                    Path.Combine(root, "board.zip"), p, ct))!;
            await Until(() => item.IsTerminal);
            Check(item.State == DownloadActivityState.Completed);
            Check(item.Transfer.NetworkBytes == payload.Length && item.Transfer.DiskBytes == payload.Length);
            Check(item.Transfer.ExpectedBytes == payload.Length && item.Transfer.ReceivedBytes == payload.Length);
            Check(File.ReadAllBytes(Path.Combine(root, "board.zip")).SequenceEqual(payload));
            Check(DownloadTransfer.Current is null);
        }
        finally { Directory.Delete(root, true); }
    });

    public static void ProgressWaitsForInstallation() => Run(async () =>
    {
        using var queue = new PortableDownloadQueue();
        var downloaded = Signal(); var install = Signal(); var installing = Signal(); var finish = Signal();
        var item = queue.Enqueue("mod-progress", "Mod", "MOD", "", async (progress, ct) =>
        {
            DownloadTransfer.Current!.BeginDownload(100);
            progress.Report(1);
            downloaded.SetResult();
            await install.Task.WaitAsync(ct);
            DownloadTransfer.Current.SetPhase(DownloadPhase.Installing);
            progress.Report(.1);
            installing.SetResult();
            await finish.Task.WaitAsync(ct);
            progress.Report(1);
        })!;
        await downloaded.Task;
        var beforeInstall = item.Progress;
        Check(beforeInstall is > 0 and < 1 && !item.IsTerminal);
        install.SetResult(); await installing.Task;
        Check(item.Progress >= beforeInstall && item.Progress < 1 && !item.CanPause);
        queue.SetSuspended(true);
        Check(!item.IsPaused);
        finish.SetResult(); await Until(() => item.IsTerminal);
        Check(item.State == DownloadActivityState.Completed && item.Progress == 1);
    });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition) { if (!condition) throw new Exception("Download queue assertion failed"); }
    private static async Task Until(Func<bool> predicate)
    { while (!predicate()) await Task.Delay(5); }
    private static void Run(Func<Task> body) => body().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
    private sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(payload) });
    }
}
