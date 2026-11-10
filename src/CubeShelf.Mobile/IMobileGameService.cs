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
    LauncherAppUpdates? AppUpdates => null;
    IMobileNativePorts? NativePorts => null;
    MobileDeviceStatus? DeviceStatus => null;
    CubeShelf.Core.Storage.GameStorageService? Storage => null;
    void StorageChanged(string id) { }
    void PlayPreviewMusic(string? file) { }
    void PlayMenuSound(string? file) { }
    bool SupportsRetroAchievements(string gameId)=>false;
}
