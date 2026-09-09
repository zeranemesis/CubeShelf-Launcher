namespace CubeShelf.Launcher;

public sealed class LauncherConfig
{
    public string GitHubOwner { get; set; } = "zeranemesis";
    public string GitHubRepo { get; set; } = "CubeShelf-Launcher";
    public string ReleaseAssetName { get; set; } = "CubeShelf-win-x64.zip";
    public string ChecksumAssetName { get; set; } = "checksums.txt";
    public string ModsRoot { get; set; } = "Mods";
    public bool AutoCheckLauncherUpdates { get; set; } = true;
    public bool AutoInstallLauncherUpdates { get; set; } = true;

    public static LauncherConfig Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return JsonSerializer.Deserialize<LauncherConfig>(
            doc.RootElement.GetProperty("Launcher").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }
}
