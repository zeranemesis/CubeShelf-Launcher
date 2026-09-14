namespace CubeShelf.Core.Platform;

public enum PlatformFamily
{
    Windows,
    Linux,
    MacOS
}

public interface IPlatformPaths
{
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string ConfigurationDirectory { get; }
    string DownloadHistoryFile { get; }
}

public sealed class PlatformPaths : IPlatformPaths
{
    public PlatformPaths(
        string applicationName = "CubeShelf",
        IReadOnlyDictionary<string, string?>? environment = null,
        PlatformFamily? platform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        environment ??= ReadEnvironment();

        var selectedPlatform = platform ?? DetectPlatform();
        if (selectedPlatform == PlatformFamily.Windows)
        {
            var local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            DataDirectory = Path.Combine(local, applicationName);
            CacheDirectory = Path.Combine(DataDirectory, "Cache");
            ConfigurationDirectory = DataDirectory;
        }
        else
        {
            var userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            DataDirectory = ResolveXdg(
                environment, "XDG_DATA_HOME", userProfile, ".local", "share", applicationName);
            CacheDirectory = ResolveXdg(
                environment, "XDG_CACHE_HOME", userProfile, ".cache", applicationName);
            ConfigurationDirectory = ResolveXdg(
                environment, "XDG_CONFIG_HOME", userProfile, ".config", applicationName);
        }

        DownloadHistoryFile = Path.Combine(DataDirectory, "download-history.json");
    }

    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string ConfigurationDirectory { get; }
    public string DownloadHistoryFile { get; }

    private static string ResolveXdg(
        IReadOnlyDictionary<string, string?> environment,
        string variable,
        string userProfile,
        params string[] fallbackParts)
    {
        if (environment.TryGetValue(variable, out var configured) &&
            !string.IsNullOrWhiteSpace(configured) &&
            Path.IsPathRooted(configured))
        {
            return Path.Combine(configured, fallbackParts[^1]);
        }

        return Path.Combine(new[] { userProfile }.Concat(fallbackParts).ToArray());
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment()
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_DATA_HOME"] = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            ["XDG_CACHE_HOME"] = Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            ["XDG_CONFIG_HOME"] = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        };

    private static PlatformFamily DetectPlatform()
        => OperatingSystem.IsWindows()
            ? PlatformFamily.Windows
            : OperatingSystem.IsMacOS()
                ? PlatformFamily.MacOS
                : PlatformFamily.Linux;
}
