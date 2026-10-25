using CubeShelf.Core.Social;

namespace CubeShelf.Core.Storage;

public sealed record SaveBackupInfo(string Id, DateTimeOffset CreatedAt, int FileCount, long Bytes);
public sealed record SaveBackupRestoreResult(int RestoredFiles, string? PreviousBackupId);

/// <summary>Restores the real memory-card backups created by the phone transfer; never guesses other games' save paths.</summary>
public sealed class SaveBackupService
{
    private const long MaximumFileBytes = 32 * 1024 * 1024;
    private readonly string _root;
    private readonly string _backups;

    public SaveBackupService(string savesDirectory)
    {
        _root = Path.GetFullPath(savesDirectory);
        _backups = Path.Combine(_root, "save-backups");
    }

    public IReadOnlyList<SaveBackupInfo> List()
    {
        if (!Directory.Exists(_backups)) return [];
        SafePath(_root, "save-backups");
        var result = new List<SaveBackupInfo>();
        foreach (var folder in Directory.EnumerateDirectories(_backups))
        {
            try
            {
                var id = Path.GetFileName(folder);
                var files = BackupFiles(id);
                if (files.Length > 0)
                    result.Add(new SaveBackupInfo(id, new DateTimeOffset(Directory.GetLastWriteTimeUtc(folder)), files.Length,
                        files.Sum(file => new FileInfo(SafePath(folder, file)).Length)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    /// <summary>Overlay selected backup files, first preserving replacements. Any failure or cancellation rolls back this operation.</summary>
    public SaveBackupRestoreResult Restore(string backupId, Func<bool> gameRunning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameRunning);
        if (gameRunning()) throw new InvalidOperationException("Ferme Mario Party 4 avant de restaurer ses sauvegardes.");
        var files = BackupFiles(backupId);
        if (files.Length == 0) throw new InvalidDataException("Cette sauvegarde ne contient aucune carte mémoire reconnue.");
        var source = SafePath(_backups, backupId);
        var beforeId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-restore-" + Guid.NewGuid().ToString("N");
        var before = SafePath(_backups, beforeId);
        var originals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SafePath(_root, relative);
            if (!File.Exists(target)) continue;
            if (new FileInfo(target).Length > MaximumFileBytes) throw new InvalidDataException("Une carte mémoire actuelle dépasse la taille autorisée.");
            var copy = SafePath(before, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(target, copy);
            originals.Add(relative);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (gameRunning()) throw new InvalidOperationException("Le jeu a démarré : restauration annulée, aucun fichier remplacé.");
        var changed = new List<string>();
        try
        {
            foreach (var relative in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (gameRunning()) throw new InvalidOperationException("Le jeu a démarré pendant la restauration.");
                var target = SafePath(_root, relative);
                changed.Add(relative);
                CopyAtomic(SafePath(source, relative), target);
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var relative in changed.AsEnumerable().Reverse())
                try
                {
                    var target = SafePath(_root, relative);
                    if (originals.Contains(relative)) CopyAtomic(SafePath(before, relative), target);
                    else File.Delete(target);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            if (failures.Count > 1) throw new AggregateException("Restauration interrompue ; les fichiers précédents restent dans " + before, failures);
            throw;
        }
        return new SaveBackupRestoreResult(files.Length, originals.Count == 0 ? null : beforeId);
    }

    private string[] BackupFiles(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Identifiant de sauvegarde invalide.", nameof(id));
        SafePath(_root, "save-backups");
        var directory = SafePath(_backups, id);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Sauvegarde introuvable.");
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory))
            if (PhoneLinkServer.IsSavePath(Path.GetFileName(file))) files.Add(Path.GetFileName(file));
        foreach (var region in new[] { "USA", "EUR", "JAP" })
        foreach (var card in new[] { "Card A", "Card B" })
        {
            var folder = SafePath(directory, region + "/" + card);
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var relative = region + "/" + card + "/" + Path.GetFileName(file);
                if (PhoneLinkServer.IsSavePath(relative)) files.Add(relative);
            }
        }
        if (files.Count > 64) throw new InvalidDataException("Trop de fichiers dans la sauvegarde.");
        foreach (var relative in files)
            if (new FileInfo(SafePath(directory, relative)).Length > MaximumFileBytes)
                throw new InvalidDataException("Une carte mémoire dépasse la taille autorisée.");
        return files.ToArray();
    }

    private static string SafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':')))
            throw new InvalidDataException("Chemin de sauvegarde invalide.");
        var target = fullRoot;
        if ((File.Exists(target) || Directory.Exists(target)) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Les liens symboliques ne sont pas acceptés dans les sauvegardes.");
        foreach (var part in parts)
        {
            target = Path.Combine(target, part);
            if ((File.Exists(target) || Directory.Exists(target)) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Les liens symboliques ne sont pas acceptés dans les sauvegardes.");
        }
        return target;
    }

    private static void CopyAtomic(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temporary); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
