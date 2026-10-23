using Avalonia.Controls;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async Task LaunchWithBigPictureTransitionAsync(GameCatalogEntry game)
    {
        if (!_bigPictureActive) { LaunchReadyGame(game); return; }
        var cover = game.Covers.FirstOrDefault();
        try
        {
            LaunchTransition.Start(game.Title, ResolveCoverPath(cover?.Front),
                ResolveCoverPath(cover?.Back), ResolveCoverPath(cover?.Spine), P7("Lancement…", "Launching…"));
            // Keep the fullscreen launcher behind the game's own fullscreen window.
            LaunchReadyGame(game);
            await Task.Delay(500);
            if (_bigPictureClosed || !_sessions.IsRunning(game.Id) || !IsActive) LaunchTransition.Stop();
        }
        catch { LaunchTransition.Stop(); throw; }
    }
}
