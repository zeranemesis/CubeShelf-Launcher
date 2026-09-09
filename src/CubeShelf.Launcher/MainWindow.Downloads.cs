using System.Windows;
using System.Windows.Controls;
using CubeShelf.Launcher.Services;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadQueueItem item } || !item.CanStart)
            return;

        var parts = item.UniqueKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return;

        var game = _games.FirstOrDefault(x =>
            string.Equals(x.Id, parts[1], StringComparison.OrdinalIgnoreCase));

        if (game is null)
            return;

        // Reuse the normal game start flow:
        // - if no ISO/RVZ is selected, CubeShelf asks for it;
        // - if game data still needs preparation, that is queued separately;
        // - otherwise PartyBoard starts immediately.
        SelectGame(game);
        PlayButton_Click(this, new RoutedEventArgs());
    }
}
