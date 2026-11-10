using CubeShelf.Core.Downloads;
using CubeShelf.Core.Games;

internal static class GamePlayFlowTests
{
    public static void DismissedDiscStopsPreparation() => Run(async () =>
    {
        var touched = false;
        var result = await new GamePlayFlow().RunAsync("GAME", new GamePlayActions(
            _ => Task.FromResult(false), _ => { touched = true; return Task.CompletedTask; },
            _ => { touched = true; return Task.CompletedTask; }, _ => { touched = true; return Task.CompletedTask; }));
        Check(result == GamePlayResult.Cancelled && !touched);
    });

    public static void PreparationIsOrderedAndAwaited() => Run(async () =>
    {
        var order = new List<string>();
        var ready = Signal(); var finish = Signal();
        var flow = new GamePlayFlow();
        var play = flow.RunAsync("GAME", new GamePlayActions(_ => { order.Add("disc"); return Task.FromResult(true); },
            _ => { order.Add("runtime"); return Task.CompletedTask; }, async ct => { order.Add("data"); ready.SetResult(); await finish.Task.WaitAsync(ct); },
            _ => { order.Add("launch"); return Task.CompletedTask; }));
        await ready.Task;
        Check(!play.IsCompleted && order.SequenceEqual(new[] { "disc", "runtime", "data" }));
        finish.SetResult();
        Check(await play == GamePlayResult.Started && order.Last() == "launch");
    });

    public static void DoubleClickDoesNotQueueTwoGames() => Run(async () =>
    {
        var flow = new GamePlayFlow(); var ready = Signal(); var finish = Signal(); var installs = 0; var launches = 0;
        var actions = new GamePlayActions(_ => Task.FromResult(true), async ct => { installs++; ready.TrySetResult(); await finish.Task.WaitAsync(ct); },
            _ => Task.CompletedTask, _ => { launches++; return Task.CompletedTask; });
        var first = flow.RunAsync("GAME", actions); await ready.Task;
        Check(await flow.RunAsync("game", actions) == GamePlayResult.AlreadyPending);
        Check(installs == 1 && launches == 0);
        finish.SetResult(); await first;
        Check(launches == 1);
    });

    public static void CancellationPreventsDelayedLaunch() => Run(async () =>
    {
        var flow = new GamePlayFlow(); var ready = Signal(); var finish = Signal(); var launched = false;
        using var cancellation = new CancellationTokenSource();
        var play = flow.RunAsync("GAME", new GamePlayActions(_ => Task.FromResult(true),
            async _ => { ready.SetResult(); await finish.Task; }, _ => Task.CompletedTask,
            _ => { launched = true; return Task.CompletedTask; }), cancellation.Token);
        await ready.Task; cancellation.Cancel(); finish.SetResult();
        try { await play; throw new Exception("Cancelled Play continued"); } catch (OperationCanceledException) { }
        Check(!launched);
    });

    public static void FailureAllowsAnotherAttempt() => Run(async () =>
    {
        var flow = new GamePlayFlow(); var data = 0; var launched = 0;
        var actions = new GamePlayActions(_ => Task.FromResult(true), _ => throw new IOException("Download failed"),
            _ => { data++; return Task.CompletedTask; }, _ => { launched++; return Task.CompletedTask; });
        try { await flow.RunAsync("GAME", actions); throw new Exception("Failure was swallowed"); } catch (IOException) { }
        Check(data == 0 && launched == 0);
        Check(await flow.RunAsync("GAME", actions with { EnsureRuntime = _ => Task.CompletedTask }) == GamePlayResult.Started);
        Check(data == 1 && launched == 1);
    });

    public static void QueueWaitCancellationDoesNotCancelInstallation() => Run(async () =>
    {
        using var queue = new PortableDownloadQueue(); var ready = Signal(); var finish = Signal();
        var item = queue.Enqueue("runtime:GAME", "Game", "RUNTIME", "", async (_, ct) => { ready.SetResult(); await finish.Task.WaitAsync(ct); })!;
        await ready.Task;
        using var cancellation = new CancellationTokenSource();
        var wait = queue.WaitForCompletionAsync(item, cancellation.Token);
        cancellation.Cancel();
        try { await wait; throw new Exception("Wait ignored cancellation"); } catch (OperationCanceledException) { }
        Check(item.State == DownloadActivityState.Running);
        finish.SetResult();
        Check(await queue.WaitForCompletionAsync(item) == DownloadActivityState.Completed);
        Check(await queue.WaitForCompletionAsync(item) == DownloadActivityState.Completed);
        var closedQueue = new PortableDownloadQueue();
        closedQueue.SetSuspended(true);
        var pending = closedQueue.Enqueue("pending", "Game", "RUNTIME", "", (_, _) => Task.CompletedTask)!;
        var waitingAtShutdown = closedQueue.WaitForCompletionAsync(pending);
        closedQueue.Dispose();
        try { await waitingAtShutdown; throw new Exception("Shutdown left a launch waiting"); } catch (OperationCanceledException) { }
    });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition) { if (!condition) throw new Exception("Quick Play assertion failed"); }
    private static void Run(Func<Task> test) => test().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
}
