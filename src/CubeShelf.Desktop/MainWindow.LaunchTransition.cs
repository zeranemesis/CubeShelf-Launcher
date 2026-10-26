using Avalonia.Controls;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async Task LaunchWithBigPictureTransitionAsync(GameCatalogEntry game,CancellationToken cancellationToken)
    {
        if (!_bigPictureActive) { LaunchReadyGame(game); return; }
        var cover = game.Covers.FirstOrDefault();
        try
        {
            LaunchTransition.Start(game.Title, ResolveCoverPath(cover?.Front),
                ResolveCoverPath(cover?.Back), ResolveCoverPath(cover?.Spine), P7("Lancement… · B pour annuler", "Launching… · B to cancel"), game.Id,
                game.ShelfConsole,openCase:true,media:ResolveCoverPath(_shelfMediaArtwork.GetValueOrDefault(NativePortCatalog.GameKey(GameVariants.BaseOf(_catalogGames,game)))));
            if(!await LaunchTransition.WaitForOpeningAsync(cancellationToken))throw new OperationCanceledException(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if(_bigPictureClosed || !_bigPictureActive || !LaunchTransition.IsVisible)throw new OperationCanceledException(cancellationToken);
            // Keep the fullscreen launcher behind the game's own fullscreen window.
            if(game.RuntimeOwnsSetup && OperatingSystem.IsMacOS() && Directory.Exists(game.Executable))
                new NativePortLibrary(_paths).Launch(_nativeShelfPorts[game.Id]);
            else LaunchReadyGame(game);
            await Task.Delay(500);
            if (_bigPictureClosed || !_sessions.IsRunning(game.Id) || !IsActive) LaunchTransition.Stop();
        }
        catch { LaunchTransition.Stop(); throw; }
    }
}
