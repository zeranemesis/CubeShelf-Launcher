using System.Diagnostics;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Releases;

public sealed record UpdateInstallLaunchResult(bool ShutdownRequired, bool Automatic, string Message);

public interface ILauncherUpdateInstaller
{
    UpdateInstallLaunchResult Launch(string downloadedArtifact, string version, int currentProcessId);
}

public static class LauncherUpdateInstallerFactory
{
    public static ILauncherUpdateInstaller Create(LauncherUpdateService service, IPlatformPaths paths) =>
        OperatingSystem.IsWindows()
            ? new WindowsLauncherUpdateInstaller(service)
            : OperatingSystem.IsLinux()
                ? new LinuxLauncherUpdateInstaller(paths)
                : new MacLauncherUpdateInstaller();
}

internal sealed class WindowsLauncherUpdateInstaller : ILauncherUpdateInstaller
{
    private readonly LauncherUpdateService _service;
    public WindowsLauncherUpdateInstaller(LauncherUpdateService service) => _service = service;

    public UpdateInstallLaunchResult Launch(string downloadedArtifact, string version, int currentProcessId)
    {
        _ = _service.LaunchWindowsUpdater(downloadedArtifact, version);
        return new(true, true, "La mise à jour Windows va remplacer CubeShelf puis le relancer.");
    }
}

internal sealed class LinuxLauncherUpdateInstaller : ILauncherUpdateInstaller
{
    private readonly IPlatformPaths _paths;
    public LinuxLauncherUpdateInstaller(IPlatformPaths paths) => _paths = paths;

    public UpdateInstallLaunchResult Launch(string downloadedArtifact, string version, int currentProcessId)
    {
        var currentAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(currentAppImage) && File.Exists(currentAppImage) &&
            downloadedArtifact.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            var helper = Path.Combine(_paths.CacheDirectory, "Updates", "apply-appimage-update.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
            File.WriteAllText(helper,
                "#!/bin/sh\n" +
                "pid=\"$1\"\nnew=\"$2\"\ntarget=\"$3\"\n" +
                "while kill -0 \"$pid\" 2>/dev/null; do sleep 0.2; done\n" +
                "cp \"$new\" \"$target.new\" || exit 1\n" +
                "chmod +x \"$target.new\" || exit 1\n" +
                "mv -f \"$target.new\" \"$target\" || exit 1\n" +
                "\"$target\" >/dev/null 2>&1 &\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            info.ArgumentList.Add(helper);
            info.ArgumentList.Add(currentProcessId.ToString());
            info.ArgumentList.Add(Path.GetFullPath(downloadedArtifact));
            info.ArgumentList.Add(Path.GetFullPath(currentAppImage));
            _ = Process.Start(info) ?? throw new InvalidOperationException("Impossible de démarrer la mise à jour AppImage.");
            return new(true, true, "L’AppImage sera remplacée après la fermeture de CubeShelf.");
        }

        Open(downloadedArtifact, "xdg-open");
        return new(false, false, "La mise à jour vérifiée a été téléchargée. Le fichier a été ouvert pour installation manuelle.");
    }

    private static void Open(string path, string command)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        _ = Process.Start(info);
    }
}

internal sealed class MacLauncherUpdateInstaller : ILauncherUpdateInstaller
{
    public UpdateInstallLaunchResult Launch(string downloadedArtifact, string version, int currentProcessId)
    {
        var info = new ProcessStartInfo("open") { UseShellExecute = false };
        info.ArgumentList.Add(downloadedArtifact);
        _ = Process.Start(info) ?? throw new InvalidOperationException("Impossible d’ouvrir le paquet de mise à jour macOS.");
        return new(false, false, "Le paquet vérifié a été ouvert. macOS demande encore une validation utilisateur pour remplacer l’application.");
    }
}
