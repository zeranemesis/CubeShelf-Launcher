namespace CubeShelf.Core.Storage;

public static class DiskSpaceGuard
{
    public static void Require(string destination, long bytes)
    {
        long? available = null;
        try
        {
            var path = Path.GetFullPath(destination);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var drive = DriveInfo.GetDrives().OrderByDescending(item => item.RootDirectory.FullName.Length)
                .FirstOrDefault(item =>
                {
                    var root = item.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);
                    return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
                });
            if (drive?.IsReady == true) available = drive.AvailableFreeSpace;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        // An unmeasurable network/mounted volume remains usable; actual I/O errors
        // still surface normally. Never confuse an unavailable measurement with zero.
        if (available is { } free && free < Math.Max(0, bytes) + 16L * 1024 * 1024)
            throw new IOException($"Espace disque insuffisant : {Math.Max(0, bytes) / 1048576d:F0} Mo requis, {free / 1048576d:F0} Mo disponibles (hors marge de sécurité).");
    }
}
