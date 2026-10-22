namespace CubeShelf.Mobile;

/// <summary>Native games are separate installed applications, never desktop executables.</summary>
public interface IMobileGameService
{
    string DataDirectory { get; }
    bool IsInstalled();
    bool DeviceSupportsPublishedGame();
    bool OpenGame();
    bool OpenOnline(string? invitation = null);
    bool OpenDownloadPage();
    string? LastError { get; }
}
