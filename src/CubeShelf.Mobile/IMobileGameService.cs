namespace CubeShelf.Mobile;

/// <summary>Platform host for native engines and their resource downloads.</summary>
public interface IMobileGameService
{
    string DataDirectory { get; }
    bool IsInstalled();
    bool DeviceSupportsPublishedGame();
    bool OpenGame();
    bool OpenOnline(string? invitation = null);
    bool OpenDownloadPage();
    string? LastError { get; }
    IRuntimeDownloads? Downloads => null;
}
