using System.Diagnostics;

namespace CubeShelf.Core.Platform;

public interface IProcessLauncher
{
    Process Start(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null,
        string? outputLog = null);

    void OpenDirectory(string directory);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public Process Start(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null,
        string? outputLog = null)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("Executable not found.", executable);

        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in arguments ?? Array.Empty<string>())
            info.ArgumentList.Add(argument);

        foreach (var pair in environment ?? new Dictionary<string, string?>())
        {
            if (pair.Value is null)
                info.Environment.Remove(pair.Key);
            else
                info.Environment[pair.Key] = pair.Value;
        }

        // A console program's output goes to outputLog instead of a console window: a terminal
        // that falls behind blocks the game's writes, and closing it would end the game.
        if (outputLog is not null)
        {
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputLog))!);
        }

        var process = Process.Start(info) ??
            throw new InvalidOperationException("The process could not be started.");
        if (outputLog is not null) WriteOutput(process, outputLog);
        return process;
    }

    private static void WriteOutput(Process process, string outputLog)
    {
        // Readable while the game runs.
        var log = new StreamWriter(new FileStream(outputLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            { AutoFlush = true };
        void Write(object _, DataReceivedEventArgs line)
        {
            if (line.Data is null) return;
            lock (log)
                try { log.WriteLine(line.Data); } catch (ObjectDisposedException) { }
        }
        process.OutputDataReceived += Write;
        process.ErrorDataReceived += Write;
        // Reading starts first: a program that has already ended raises Exited at once, and
        // WaitForExit only waits for the last lines of streams that are being read.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.Exited += (_, _) =>
        {
            process.WaitForExit();
            lock (log) log.Dispose();
        };
        process.EnableRaisingEvents = true;
    }

    public void OpenDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var (executable, argument) = OperatingSystem.IsWindows()
            ? ("explorer.exe", directory)
            : OperatingSystem.IsMacOS()
                ? ("open", directory)
                : ("xdg-open", directory);

        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add(argument);
        _ = Process.Start(info) ??
            throw new InvalidOperationException("The directory could not be opened.");
    }
}
