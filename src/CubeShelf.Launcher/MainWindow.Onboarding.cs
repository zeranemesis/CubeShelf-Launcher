using System.Windows;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private void RunFirstStartAssistantIfNeeded()
    {
        if (_preferences.FirstRunCompleted)
            return;

        ShowFirstStartAssistant();
    }

    private void RunSetupAssistantButton_Click(
        object sender,
        RoutedEventArgs e)
        => ShowFirstStartAssistant();

    private void ShowFirstStartAssistant()
    {
        var wizard =
            new FirstRunWizardWindow(
                _preferencesService,
                _preferences)
            {
                Owner = this
            };

        var result =
            wizard.ShowDialog();

        LoadSettingsControls();
        UpdateThemeIndicator(false);
        RefreshLibraryItems();

        if (result == true &&
            wizard.OpenGameAfterFinish)
        {
            var game =
                _games.FirstOrDefault();

            if (game is not null)
                SelectGame(game);
        }
    }

    private void UpdateSetupProgress(
        GameDefinition game)
    {
        if (SetupProgressText is null)
            return;

        var runtimeReady =
            game.RuntimeInstalled &&
            File.Exists(
                game.ExecutableFullPath);

        var discReady =
            game.HasDiscImage;

        var dataReady =
            game.GameDataReady;

        string Mark(bool ready)
            => ready ? "✓" : "○";

        SetupProgressText.Text =
            IsEnglish
                ? $"{Mark(runtimeReady)} PartyBoard   {Mark(discReady)} ISO/RVZ   {Mark(dataReady)} Game data"
                : $"{Mark(runtimeReady)} PartyBoard   {Mark(discReady)} ISO/RVZ   {Mark(dataReady)} Données jeu";
    }
}
