using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CubeShelf.Core.Games;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async void ShowLaunchRecovery(GameSessionEndedEventArgs session)
    {
        if (session.StopRequested || session.ExitCode is null or 0 || _quickPlayClosing) return;
        var game = _catalogGames.FirstOrDefault(item => item.Id.Equals(session.GameId, StringComparison.OrdinalIgnoreCase));
        if (game is null) return;
        var dialog = CreatePhase7Dialog(P7("Le jeu s’est fermé", "The game closed"), 570, 450);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = P7($"{game.Title} s’est arrêté de façon inattendue (code {session.ExitCode}).",
                    $"{game.Title} stopped unexpectedly (code {session.ExitCode})."), TextWrapping = TextWrapping.Wrap, FontSize = 18 },
                new TextBlock { Text = P7("Le journal peut aider à comprendre l’erreur. Tu peux réessayer sans modifier tes fichiers.",
                    "The log can help explain the error. You can retry without changing your files."), TextWrapping = TextWrapping.Wrap },
                CreateDialogButton(P7("Réessayer", "Retry"), (_, _) => dialog.Close(true)),
                CreateDialogButton(P7("Ouvrir les journaux", "Open logs"), (_, _) =>
                {
                    var folder = Path.Combine(_paths.DataDirectory, "Logs");
                    Directory.CreateDirectory(folder);
                    _processLauncher.OpenDirectory(folder);
                }),
                CreateDialogButton(P7("Retour à la bibliothèque", "Back to library"), (_, _) =>
                {
                    ShowParityView(_bigPictureActive ? BigPictureView : LibraryView);
                    dialog.Close(false);
                })
            }
        };
        try { if (await dialog.ShowDialog<bool>(this)) await RequestPlayAsync(game); }
        catch (Exception error) { ShowToastParity(P7("Lancement", "Launch"), error.Message); }
    }
}
