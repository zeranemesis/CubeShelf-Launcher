using System.Diagnostics;
using CubeShelf.Core.Games;
using CubeShelf.Core.Platform;

internal static class GameSessionTrackerTests
{
    internal static void ExitedLookups()
    {
        foreach (var queryByProcess in new[] { true, false })
        {
            using var sessions = new GameSessionTracker(new ChildProcess());
            var ended = 0;
            sessions.Ended += _ => ended++;
            var process = sessions.Start("exiting-game", "unused", AppContext.BaseDirectory);
            var pid = process.Id;
            // Force HasExited to deliver the exit event synchronously inside the lookup.
            process.EnableRaisingEvents = false;
            process.StandardInput.WriteLine("exit");
            process.StandardInput.Flush();
            if (!process.WaitForExit(10000)) throw new Exception("Session fixture did not exit.");
            if (queryByProcess)
            {
                if (sessions.GameIdForProcess(pid) is not null) throw new Exception("Exited process still attributed to a game.");
            }
            else if (sessions.RunningGameIds.Count != 0) throw new Exception("Exited game still listed as running.");
            if (ended != 1 || sessions.IsRunning("exiting-game") || sessions.GameIdForProcess(pid) is not null)
                throw new Exception("Session exit was not finalized exactly once.");
        }
    }

    private sealed class ChildProcess : IProcessLauncher
    {
        public Process Start(string executable, string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment = null, IReadOnlyList<string>? arguments = null, string? outputLog = null)
        {
            var host = Environment.ProcessPath ?? throw new Exception("Test host path unavailable.");
            var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(typeof(GameSessionTrackerTests).Assembly.Location);
            info.ArgumentList.Add("--session-exit-fixture");
            return Process.Start(info) ?? throw new Exception("Session fixture did not start.");
        }
        public void OpenDirectory(string directory) { }
    }
}
