using System.Text.Json;
using System.Text.RegularExpressions;

namespace CubeShelf.Core.Social;

/// <param name="Root">The folder the sync client watches, which exists on this machine.</param>
public sealed record DetectedSyncFolder(ShareLinkProvider Provider, string Label, string Root);

/// <summary>What the detector reads, injectable so it can be tested without any client installed.</summary>
/// <param name="Variable">Environment variables.</param>
/// <param name="Home">The user's home folder.</param>
/// <param name="DriveRoots">Mounted drives, where Google Drive puts its virtual one.</param>
public sealed record SyncEnvironment(
    Func<string, string?> Variable,
    string Home,
    IReadOnlyList<string> DriveRoots)
{
    public static SyncEnvironment Current => new(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        SafeDriveRoots());

    private static IReadOnlyList<string> SafeDriveRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Network)
                .Select(drive => drive.RootDirectory.FullName)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// Finds the folders sync clients already watch on this machine, so choosing where to publish is
/// a click instead of a hunt through the file system.
///
/// Every client records its folder somewhere it reads at start-up, and that is what is read here
/// -- nothing is guessed from folder names alone except Google Drive, whose client mounts a drive
/// rather than writing its path anywhere stable. Only folders that exist are returned.
/// </summary>
public static class SyncedFolderDetector
{
    private static readonly Regex NextcloudFolder = new(
        @"^\d+\\Folders(?:WithPlaceholders)?\\\d+\\localPath=(?<path>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static IReadOnlyList<DetectedSyncFolder> Detect() => Detect(SyncEnvironment.Current);

    public static IReadOnlyList<DetectedSyncFolder> Detect(SyncEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var found = new List<DetectedSyncFolder>();

        foreach (var folder in Dropbox(environment)) found.Add(folder);
        foreach (var folder in Nextcloud(environment)) found.Add(folder);
        foreach (var folder in OneDrive(environment)) found.Add(folder);
        foreach (var folder in GoogleDrive(environment)) found.Add(folder);

        return found
            .Where(folder => SafeExists(folder.Root))
            .GroupBy(folder => Normalize(folder.Root), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// Dropbox writes info.json beside its settings: {"personal": {"path": ...}, "business": ...}.
    /// </summary>
    private static IEnumerable<DetectedSyncFolder> Dropbox(SyncEnvironment environment)
    {
        var candidates = new[]
        {
            Combine(environment.Variable("APPDATA"), "Dropbox", "info.json"),
            Combine(environment.Variable("LOCALAPPDATA"), "Dropbox", "info.json"),
            Combine(environment.Home, ".dropbox", "info.json")
        };

        foreach (var file in candidates.OfType<string>().Where(SafeFileExists))
        {
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
            }
            if (document is null) continue;

            using (document)
            {
                foreach (var (account, label) in new[] { ("personal", "Dropbox"), ("business", "Dropbox (professionnel)") })
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty(account, out var entry) &&
                        entry.ValueKind == JsonValueKind.Object &&
                        entry.TryGetProperty("path", out var path) &&
                        path.ValueKind == JsonValueKind.String &&
                        path.GetString() is { Length: > 0 } root)
                        yield return new(ShareLinkProvider.Dropbox, label, root);
                }
            }
        }

        // macOS File Provider puts it here and keeps no info.json path in the old place.
        var cloud = Combine(environment.Home, "Library", "CloudStorage", "Dropbox");
        if (cloud is not null) yield return new(ShareLinkProvider.Dropbox, "Dropbox", cloud);
    }

    /// <summary>Nextcloud (and ownCloud, its ancestor) list their folders in an ini file.</summary>
    private static IEnumerable<DetectedSyncFolder> Nextcloud(SyncEnvironment environment)
    {
        var candidates = new (string? File, string Label)[]
        {
            (Combine(environment.Variable("APPDATA"), "Nextcloud", "nextcloud.cfg"), "Nextcloud"),
            (Combine(environment.Home, ".config", "Nextcloud", "nextcloud.cfg"), "Nextcloud"),
            (Combine(environment.Home, "Library", "Preferences", "Nextcloud", "nextcloud.cfg"), "Nextcloud"),
            (Combine(environment.Variable("APPDATA"), "ownCloud", "owncloud.cfg"), "ownCloud"),
            (Combine(environment.Home, ".config", "ownCloud", "owncloud.cfg"), "ownCloud")
        };

        foreach (var (file, label) in candidates)
        {
            if (file is null || !SafeFileExists(file)) continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in NextcloudFolder.Matches(text))
            {
                var path = match.Groups["path"].Value.Trim().Trim('"');
                if (path.Length > 0) yield return new(ShareLinkProvider.Nextcloud, label, path);
            }
        }
    }

    /// <summary>
    /// The OneDrive client exports its folders as environment variables. A work account is often
    /// barred by its organisation from sharing to "anyone with the link", which the label says.
    /// </summary>
    private static IEnumerable<DetectedSyncFolder> OneDrive(SyncEnvironment environment)
    {
        if (environment.Variable("OneDriveConsumer") is { Length: > 0 } personal)
            yield return new(ShareLinkProvider.OneDrive, "OneDrive", personal);

        if (environment.Variable("OneDriveCommercial") is { Length: > 0 } work)
            yield return new(ShareLinkProvider.SharePoint, "OneDrive (professionnel)", work);

        if (environment.Variable("OneDrive") is { Length: > 0 } either)
            yield return new(ShareLinkProvider.OneDrive, "OneDrive", either);

        var cloud = Combine(environment.Home, "Library", "CloudStorage");
        if (cloud is not null && SafeExists(cloud))
        {
            foreach (var folder in SafeDirectories(cloud).Where(name => Path.GetFileName(name).StartsWith("OneDrive", StringComparison.Ordinal)))
                yield return new(ShareLinkProvider.OneDrive, "OneDrive", folder);
        }
    }

    /// <summary>
    /// Google Drive for desktop mounts a drive, usually G:, holding "My Drive" -- or "Mon Drive",
    /// since the folder takes the account's language.
    /// </summary>
    private static IEnumerable<DetectedSyncFolder> GoogleDrive(SyncEnvironment environment)
    {
        var names = new[] { "My Drive", "Mon Drive", "Mi unidad", "Meine Ablage" };
        foreach (var root in environment.DriveRoots)
        {
            foreach (var name in names)
            {
                var path = Combine(root, name);
                if (path is not null && SafeExists(path)) yield return new(ShareLinkProvider.GoogleDrive, "Google Drive", path);
            }
        }

        var cloud = Combine(environment.Home, "Library", "CloudStorage");
        if (cloud is null || !SafeExists(cloud)) yield break;
        foreach (var account in SafeDirectories(cloud).Where(name => Path.GetFileName(name).StartsWith("GoogleDrive", StringComparison.Ordinal)))
        {
            foreach (var name in names)
            {
                var path = Path.Combine(account, name);
                if (SafeExists(path)) yield return new(ShareLinkProvider.GoogleDrive, "Google Drive", path);
            }
        }
    }

    /// <summary>
    /// The folder CubeShelf publishes into, inside the one the sync client watches. Created on
    /// purpose here, by the user's own choice -- unlike the publisher, which never creates a
    /// folder, because one it had to create would be one nothing synchronises.
    /// </summary>
    public static string PreparePublishingFolder(string syncRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
        if (!Directory.Exists(syncRoot))
            throw new DirectoryNotFoundException($"Le dossier synchronisé est introuvable : {syncRoot}");

        var folder = Path.Combine(syncRoot, "CubeShelf");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string? Combine(string? root, params string[] parts) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.Combine(new[] { root }.Concat(parts).ToArray());

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
