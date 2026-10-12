namespace CubeShelf.Core.Social;

/// <summary>
/// The friends list, or another profile file, could not be read right now: something else holds
/// it (an antivirus, a backup, a sync client). Nothing was changed; trying again later is safe.
/// </summary>
public sealed class ProfileFileUnavailableException(string path, Exception? inner = null)
    : IOException($"{Path.GetFileName(path)} is in use by another program; nothing was changed.", inner)
{
    public string FilePath { get; } = path;
}

/// <summary>
/// Reading and writing the small JSON files of a profile without ever confusing "busy" with
/// "empty". A file another program holds for a moment is retried briefly and then reported,
/// never read as nothing -- reading it as nothing and saving the result is how a whole friends
/// list disappears. A file that is there but does not parse is set aside under another name
/// before anything replaces it, so a later save cannot destroy what a person might recover.
/// </summary>
internal static class ProfileFiles
{
    private const int Attempts = 4;

    /// <summary>The file's text, null when there is no file, or throws <see cref="ProfileFileUnavailableException"/>.</summary>
    public static string? ReadText(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Attempts) throw new ProfileFileUnavailableException(path, exception);
                Thread.Sleep(25 * attempt);
            }
        }
    }

    /// <summary>Writes through a temporary file in the same folder, retrying a target held for a moment.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(temporary, text);
                File.Move(temporary, path, true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Attempts)
                {
                    try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                    throw new ProfileFileUnavailableException(path, exception);
                }
                Thread.Sleep(25 * attempt);
            }
        }
    }

    /// <summary>Moves an unparseable file out of the way, keeping it. Best effort.</summary>
    public static void SetAside(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, $"{path}.unreadable-{DateTime.UtcNow:yyyyMMdd-HHmmss}", true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
