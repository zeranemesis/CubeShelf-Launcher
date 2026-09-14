using System.Diagnostics;

namespace CubeShelf.Core.Platform;

public interface IProcessLauncher
{
    Process Start(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null);

    void OpenDirectory(string directory);
}

public sealed class ProcessLauncher : IProcessLauncher
{
    public Process Start(
        string executable,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyList<string>? arguments = null)
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

        return Process.Start(info) ??
            throw new InvalidOperationException("The process could not be started.");
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
