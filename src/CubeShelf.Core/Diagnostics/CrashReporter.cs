using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Diagnostics;

public sealed class CrashReporter
{
    private readonly string _logsDirectory;

    public CrashReporter(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logsDirectory = Path.Combine(Path.GetFullPath(paths.DataDirectory), "Logs");
    }

    public string LatestCrashPath => Path.Combine(_logsDirectory, "CubeShelf-crash-latest.log");

    public string Write(Exception exception, string source, bool terminating)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            var now = DateTimeOffset.Now;
            var path = Path.Combine(_logsDirectory, $"CubeShelf-crash-{now:yyyyMMdd-HHmmss-fff}.log");
            var text = BuildReport(exception, source, terminating, now);
            File.WriteAllText(path, text, Encoding.UTF8);
            File.WriteAllText(LatestCrashPath, text, Encoding.UTF8);
            CleanupOldReports();
            return path;
        }
        catch
        {
            return "";
        }
    }

    public static string WriteFallback(Exception exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CubeShelf", "Logs");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "CubeShelf-startup-crash.log");
            File.WriteAllText(path, $"CubeShelf startup crash - {DateTimeOffset.Now:O}\n\n{exception}", Encoding.UTF8);
            return path;
        }
        catch { return ""; }
    }

    private static string BuildReport(Exception exception, string source, bool terminating, DateTimeOffset now)
    {
        var entry = Assembly.GetEntryAssembly();
        var version = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? entry?.GetName().Version?.ToString() ?? "unknown";
        var builder = new StringBuilder();
        builder.AppendLine("CubeShelf crash report");
        builder.AppendLine($"Time local: {now:O}");
        builder.AppendLine($"Time UTC: {now.ToUniversalTime():O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Terminating: {terminating}");
        builder.AppendLine($"Version: {version}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"PID: {Environment.ProcessId}");
        builder.AppendLine($"Thread: {Environment.CurrentManagedThreadId}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private void CleanupOldReports()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_logsDirectory, "CubeShelf-crash-*.log")
                         .Where(path => !path.EndsWith("latest.log", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(20))
                File.Delete(file);
        }
        catch { }
    }
}
