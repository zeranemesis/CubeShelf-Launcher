using System.Diagnostics;
using CubeShelf.Core.Games;
using CubeShelf.Core.Platform;

internal static class GameCaptureSessionTests
{
    internal static void OnlyTrackedProcesses()
    {
        using var sessions = new GameSessionTracker(new ExistingProcess());
        if (sessions.GameIdForProcess(Environment.ProcessId) is not null) throw new Exception("Untracked window accepted.");
        sessions.Start("test-game", "unused", ".");
        if (sessions.GameIdForProcess(Environment.ProcessId) != "test-game" || sessions.GameIdForProcess(0) is not null)
            throw new Exception("Capture process attribution is incorrect.");
        sessions.Dispose();
        if (sessions.GameIdForProcess(Environment.ProcessId) is not null) throw new Exception("Closed session still authorizes capture.");
    }
    private sealed class ExistingProcess : IProcessLauncher
    {
        public Process Start(string executable, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null,
            IReadOnlyList<string>? arguments = null, string? outputLog = null) => Process.GetCurrentProcess();
        public void OpenDirectory(string directory) { }
    }
}
