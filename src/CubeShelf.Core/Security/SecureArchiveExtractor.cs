using SharpCompress.Archives;

namespace CubeShelf.Core.Security;

public sealed record ArchiveExtractionLimits(
    int MaximumEntries,
    long MaximumUncompressedBytes,
    long MaximumSingleFileBytes);

public static class SecureArchiveExtractor
{
    public static void Extract(
        string archivePath,
        string destination,
        ArchiveExtractionLimits limits,
        Action<double>? progress = null)
    {
        if (limits.MaximumEntries <= 0 || limits.MaximumUncompressedBytes <= 0 ||
            limits.MaximumSingleFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits));

        var destinationRoot = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
        if (entries.Count == 0)
            throw new InvalidDataException("The archive contains no files.");
        if (entries.Count > limits.MaximumEntries)
            throw new InvalidDataException("The archive contains too many files.");

        var targetComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var targets = new HashSet<string>(targetComparer);
        long advertisedTotal = 0;

        foreach (var entry in entries)
        {
            if (entry.Size < 0 || entry.Size > limits.MaximumSingleFileBytes)
                throw new InvalidDataException("An archived file exceeds the size limit.");
            advertisedTotal = checked(advertisedTotal + entry.Size);
            if (advertisedTotal > limits.MaximumUncompressedBytes)
                throw new InvalidDataException("The expanded archive exceeds the size limit.");
            _ = ResolveTarget(destinationRoot, entry.Key, targets);
        }

        long extractedTotal = 0;
        var buffer = new byte[256 * 1024];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var target = ResolveTarget(destinationRoot, entry.Key, null);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.OpenEntryStream();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, buffer.Length);

            long fileBytes = 0;
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                fileBytes = checked(fileBytes + read);
                extractedTotal = checked(extractedTotal + read);
                if (fileBytes > limits.MaximumSingleFileBytes ||
                    extractedTotal > limits.MaximumUncompressedBytes)
                    throw new InvalidDataException("The archive exceeded its extraction limit.");
                output.Write(buffer, 0, read);
            }

            progress?.Invoke((index + 1d) / entries.Count);
        }
    }

    private static string ResolveTarget(
        string destinationRoot,
        string? entryKey,
        HashSet<string>? targets)
    {
        if (string.IsNullOrWhiteSpace(entryKey) || entryKey.IndexOf('\0') >= 0)
            throw new InvalidDataException("The archive contains an invalid file name.");

        var normalized = entryKey.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            throw new InvalidDataException("The archive contains an absolute path.");

        var target = Path.GetFullPath(Path.Combine(destinationRoot, normalized));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(destinationRoot, comparison))
            throw new InvalidDataException("The archive attempted to escape its destination.");
        if (targets is not null && !targets.Add(target))
            throw new InvalidDataException("The archive contains duplicate file targets.");
        return target;
    }
}
