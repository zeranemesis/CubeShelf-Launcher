using System.Windows;
using System.Windows.Media.Animation;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private GameDefinition? _pendingRepositoryDeleteGame;

    private void ShowRepositoryDeleteConfirmation(GameDefinition game)
    {
        _pendingRepositoryDeleteGame = game;

        RepositoryDeleteTitle.Text =
            game.GitHubLocalRepositoryManaged
                ? (IsEnglish
                    ? "Delete local repository?"
                    : "Supprimer le dépôt local ?")
                : (IsEnglish
                    ? "External repository detected"
                    : "Dépôt externe détecté");

        RepositoryDeletePath.Text =
            string.IsNullOrWhiteSpace(game.GitHubLocalRepositoryPath)
                ? "—"
                : game.GitHubLocalRepositoryPath;

        if (game.GitHubLocalRepositoryManaged)
        {
            RepositoryDeleteText.Text =
                IsEnglish
                    ? "This deletes only the GitHub source copy downloaded by CubeShelf. "
                      + "Your ISO/RVZ, prepared game data and mods are kept. "
                      + "If PartyBoard.exe is inside this repository, the game will immediately switch to Not installed."
                    : "Cette action supprime uniquement la copie des sources GitHub téléchargée par CubeShelf. "
                      + "Ton ISO/RVZ, les données de jeu préparées et les mods sont conservés. "
                      + "Si PartyBoard.exe se trouve dans ce dépôt, l'état du jeu passera immédiatement à Non installé.";

            RepositoryDeleteWarning.Text =
                IsEnglish
                    ? "This action cannot be undone. Are you sure?"
                    : "Cette action est irréversible. Es-tu sûr de vouloir continuer ?";

            RepositoryDeleteConfirmButton.Visibility = Visibility.Visible;
            RepositoryDeleteCancelButton.Content =
                IsEnglish ? "Cancel" : "Annuler";
        }
        else
        {
            RepositoryDeleteText.Text =
                IsEnglish
                    ? "CubeShelf detected this repository, but it was not downloaded by CubeShelf. "
                      + "For safety, CubeShelf will not delete a manual/external repository."
                    : "CubeShelf a détecté ce dépôt, mais il n'a pas été téléchargé par CubeShelf. "
                      + "Par sécurité, CubeShelf ne supprimera jamais automatiquement un dépôt manuel ou externe.";

            RepositoryDeleteWarning.Text =
                IsEnglish
                    ? "Delete it manually in File Explorer if that is really what you want."
                    : "Supprime-le manuellement dans l'Explorateur si c'est réellement ce que tu souhaites.";

            RepositoryDeleteConfirmButton.Visibility = Visibility.Collapsed;
            RepositoryDeleteCancelButton.Content =
                IsEnglish ? "Close" : "Fermer";
        }

        RepositoryDeletePopup.Opacity = 0;
        RepositoryDeletePopup.Visibility = Visibility.Visible;
        RepositoryDeletePopup.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(150)));
    }

    private void CancelRepositoryDelete_Click(
        object sender,
        RoutedEventArgs e)
    {
        _pendingRepositoryDeleteGame = null;
        RepositoryDeletePopup.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmRepositoryDelete_Click(
        object sender,
        RoutedEventArgs e)
    {
        var game = _pendingRepositoryDeleteGame;

        if (game is null ||
            !game.GitHubLocalRepositoryManaged)
        {
            RepositoryDeletePopup.Visibility = Visibility.Collapsed;
            return;
        }

        RepositoryDeleteConfirmButton.IsEnabled = false;
        RepositoryDeleteCancelButton.IsEnabled = false;
        RepositoryDeleteWarning.Text =
            IsEnglish
                ? "Deleting repository…"
                : "Suppression du dépôt…";

        try
        {
            await Task.Run(
                () => _github.DeleteDownloadedSource(game));

            game.GitHubSourcePath = "";

            ApplyLocalRepositoryStatus(
                game,
                _github.DetectLocalRepository(game));

            _libraryService.Resolve(game);
            _runtimeInstaller.ApplyInstalledRuntime(game);
            _libraryService.Resolve(game);
            _libraryService.Save(_games);

            RefreshSelectedLocalState();

            await RefreshGameUpdateStatusAsync(game);
            UpdateLibraryUpdateSummary();

            RepositoryDeletePopup.Visibility =
                Visibility.Collapsed;

            ShowToast(
                IsEnglish
                    ? "Repository deleted"
                    : "Dépôt supprimé",
                IsEnglish
                    ? "The local GitHub source repository was deleted. Your ISO/RVZ was not touched."
                    : "Le dépôt GitHub local a été supprimé. Ton ISO/RVZ n'a pas été modifié.");
        }
        catch (Exception ex)
        {
            RepositoryDeleteWarning.Text =
                (IsEnglish
                    ? "Deletion failed: "
                    : "Échec de la suppression : ")
                + ex.Message;
        }
        finally
        {
            RepositoryDeleteConfirmButton.IsEnabled = true;
            RepositoryDeleteCancelButton.IsEnabled = true;
            _pendingRepositoryDeleteGame = null;
        }
    }
}
