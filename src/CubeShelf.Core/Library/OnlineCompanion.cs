namespace CubeShelf.Core.Library;

/// <summary>
/// Finds the runtime's online companion beside the runtime CubeShelf installed.
///
/// This is the single place that decides whether a game can be played together at all, and the
/// answer is deliberately narrow: only a game whose catalog entry names a companion, and only
/// once that companion is actually on disk. Today that is Mario Party 4 and PartyBoard's
/// PartyBoardOnline.exe. Soulcalibur II and Super Mario Strikers name none, so CubeShelf offers
/// no invitation for them rather than one that would fail at the last step.
///
/// CubeShelf does not add multiplayer to anything. It carries an invitation for a runtime that
/// already has it.
/// </summary>
public static class OnlineCompanion
{
    /// <summary>
    /// The companion beside <paramref name="executablePath"/>, or false with an empty path when
    /// the game declares none, the runtime is not installed, or the file is simply not there --
    /// a companion may ship in one release and not the next.
    /// </summary>
    public static bool TryResolve(string? executablePath, string? companionName, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(companionName))
            return false;

        // A companion name is a file name beside the runtime, never a path out of it: the
        // catalog is a file on disk and this value ends up in Process.Start.
        var name = companionName.Trim();
        if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name is "." or "..")
            return false;

        string candidate;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            if (string.IsNullOrEmpty(directory)) return false;
            candidate = Path.Combine(directory, name);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        if (!File.Exists(candidate)) return false;
        path = candidate;
        return true;
    }

    /// <summary>
    /// Whether this game could ever offer an invitation, regardless of what is installed right
    /// now. Used to decide whether the invitation card belongs on the page at all, so that a
    /// game with no netplay never shows one that explains why it is empty.
    /// </summary>
    public static bool IsSupported(GameCatalogEntry? game) =>
        !string.IsNullOrWhiteSpace(game?.OnlineCompanion);
}
