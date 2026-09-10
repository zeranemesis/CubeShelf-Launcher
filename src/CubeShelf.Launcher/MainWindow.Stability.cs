using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CubeShelf.Launcher.Services;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private DispatcherTimer? _toastTimer;

    private void RefreshStorageSummary()
    {
        if (StorageSummaryText is null)
            return;

        var summary = StorageService.Measure(_games);

        StorageSummaryText.Text = IsEnglish
            ? $"PartyBoard: {FormatBytes(summary.RuntimeBytes)} • Game data: {FormatBytes(summary.GameDataBytes)} • Dolphin: {FormatBytes(summary.DolphinBytes)} • Cache: {FormatBytes(summary.CacheBytes)} • Update backups: {FormatBytes(summary.UpdaterBackupBytes)}"
            : $"PartyBoard : {FormatBytes(summary.RuntimeBytes)} • Données jeu : {FormatBytes(summary.GameDataBytes)} • Dolphin : {FormatBytes(summary.DolphinBytes)} • Cache : {FormatBytes(summary.CacheBytes)} • Sauvegardes MAJ : {FormatBytes(summary.UpdaterBackupBytes)}";
    }

    private void RefreshStorageButton_Click(object sender, RoutedEventArgs e)
        => RefreshStorageSummary();

    private void CleanCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var released = StorageService.CleanTransientCaches();
        RefreshStorageSummary();

        ShowToast(
            IsEnglish ? "Cache cleaned" : "Cache nettoyé",
            IsEnglish
                ? $"{FormatBytes(released)} freed. Game files, ISO/RVZ and Dolphin were not removed."
                : $"{FormatBytes(released)} libérés. Les fichiers du jeu, l'ISO/RVZ et Dolphin n'ont pas été supprimés.");
    }

    private async void RepairGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
            return;

        var game = _selectedGame;
        var health = _runtimeInstaller.InspectInstallation(game);

        if (health.Healthy)
        {
            ShowToast(
                IsEnglish ? "Installation healthy" : "Installation saine",
                IsEnglish
                    ? "PartyBoard, resources and prepared game data look correct."
                    : "PartyBoard, les ressources et les données préparées semblent corrects.");
            return;
        }

        if (!game.RuntimeReleaseAvailable)
        {
            try
            {
                game.RuntimeReleaseAvailable =
                    await _runtimeInstaller.IsPlayableReleaseAvailableAsync(game);
            }
            catch
            {
                game.RuntimeReleaseAvailable = false;
            }

            if (!game.RuntimeReleaseAvailable)
            {
                ShowToast(
                    IsEnglish ? "Repair unavailable" : "Réparation indisponible",
                    IsEnglish
                        ? "No compatible PartyBoard Windows release is currently available."
                        : "Aucune release Windows PartyBoard compatible n'est disponible actuellement.");
                return;
            }
        }

        _queue.RemoveTerminalByKey($"repair:{game.Id}");

        var added = _queue.Enqueue(
            $"repair:{game.Id}",
            game.Title,
            "REPAIR / GAME",
            string.Join(" • ", health.Issues),
            async (progress, cancellationToken) =>
            {
                var state = await _runtimeInstaller.RepairLatestAsync(
                    game,
                    progress,
                    cancellationToken);

                game.Executable = state.ExecutablePath;
                game.GameRoot = Path.Combine(
                    Path.GetDirectoryName(state.ExecutablePath)!,
                    game.Id);
            },
            async () =>
            {
                _runtimeInstaller.ApplyInstalledRuntime(game);
                _libraryService.Resolve(game);
                _libraryService.Save(_games);
                await RefreshGameUpdateStatusAsync(game);

                if (_selectedGame == game)
                {
                    SelectedInstallStatus.Text = game.RuntimeStatusText;
                    ExecutablePathText.Text = game.ExecutableFullPath;
                    UpdateSelectedGameGitHubPanel();
                    UpdateSelectedGamePlayUi();
                }

                RefreshStorageSummary();
                ShowToast(
                    IsEnglish ? "Repair complete" : "Réparation terminée",
                    IsEnglish
                        ? "PartyBoard was reinstalled without deleting your ISO/RVZ or prepared game data."
                        : "PartyBoard a été réinstallé sans supprimer ton ISO/RVZ ni tes données de jeu préparées.");
            });

        if (added)
            ShowDownloads();
    }

    private void UninstallGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
            return;

        var game = _selectedGame;

        if (_runningGames.TryGetValue(game.Id, out var running) &&
            IsProcessRunning(running.Process))
        {
            ShowToast(
                IsEnglish ? "Game is running" : "Jeu en cours",
                IsEnglish
                    ? "Stop the game before uninstalling it."
                    : "Arrête le jeu avant de le désinstaller.");
            return;
        }

        var dialog = new UninstallGameWindow(game.Title, IsEnglish)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Choice == UninstallChoice.None)
            return;

        if (dialog.Choice == UninstallChoice.RuntimeOnly)
            _runtimeInstaller.DeleteRuntime(game, keepPreparedGameData: true);
        else
            _runtimeInstaller.DeleteAllRuntimeFiles(game);

        _libraryService.Resolve(game);
        _libraryService.Save(_games);

        SelectedInstallStatus.Text = game.RuntimeStatusText;
        ExecutablePathText.Text = game.ExecutableFullPath;
        UpdateSelectedGameGitHubPanel();
        UpdateSelectedGamePlayUi();
        RefreshStorageSummary();

        ShowToast(
            IsEnglish ? "Game uninstalled" : "Jeu désinstallé",
            dialog.Choice == UninstallChoice.RuntimeOnly
                ? (IsEnglish
                    ? "PartyBoard was removed. Prepared game data and your ISO/RVZ were kept."
                    : "PartyBoard a été supprimé. Les données préparées et ton ISO/RVZ sont conservés.")
                : (IsEnglish
                    ? "PartyBoard and prepared game data were removed. Your original ISO/RVZ was not touched."
                    : "PartyBoard et les données préparées ont été supprimés. Ton ISO/RVZ original n'a pas été touché."));
    }

    private void ToastClose_Click(object sender, RoutedEventArgs e)
        => HideToast();

    private void ShowToast(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            ToastTitle.Text = title;
            ToastText.Text = message;

            ToastHost.BeginAnimation(OpacityProperty, null);
            ToastHost.Opacity = 0;
            ToastHost.Visibility = Visibility.Visible;

            ToastHost.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer?.Stop();
                HideToast();
            };
            _toastTimer.Start();
        });
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
    }
}

internal enum UninstallChoice
{
    None,
    RuntimeOnly,
    RuntimeAndData
}

internal sealed class UninstallGameWindow : Window
{
    public UninstallChoice Choice { get; private set; } = UninstallChoice.None;

    public UninstallGameWindow(string gameTitle, bool english)
    {
        Title = "CubeShelf";
        Width = 590;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Application.Current.TryFindResource("Bg") as Brush ?? Brushes.Black;

        var card = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(26),
            CornerRadius = new CornerRadius(18),
            Background = Application.Current.TryFindResource("Panel") as Brush ?? Brushes.DimGray,
            BorderBrush = Application.Current.TryFindResource("Border") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = english ? $"Uninstall {gameTitle}" : $"Désinstaller {gameTitle}",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Application.Current.TryFindResource("Text") as Brush ?? Brushes.White
        });
        stack.Children.Add(new TextBlock
        {
            Text = english
                ? "Your original ISO/RVZ is never deleted by CubeShelf."
                : "CubeShelf ne supprime jamais ton ISO/RVZ original.",
            Margin = new Thickness(0, 7, 0, 18),
            Foreground = Application.Current.TryFindResource("Muted") as Brush ?? Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap
        });

        var runtimeOnly = new Button
        {
            Content = english
                ? "Remove PartyBoard only — keep prepared game data"
                : "Supprimer PartyBoard uniquement — conserver les données préparées",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        runtimeOnly.Click += (_, _) =>
        {
            Choice = UninstallChoice.RuntimeOnly;
            DialogResult = true;
        };

        var everything = new Button
        {
            Content = english
                ? "Remove PartyBoard + prepared game data"
                : "Supprimer PartyBoard + les données de jeu préparées",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        everything.Click += (_, _) =>
        {
            Choice = UninstallChoice.RuntimeAndData;
            DialogResult = true;
        };

        var cancel = new Button
        {
            Content = english ? "Cancel" : "Annuler",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        cancel.Click += (_, _) => Close();

        stack.Children.Add(runtimeOnly);
        stack.Children.Add(everything);
        stack.Children.Add(cancel);
        card.Child = stack;
        Content = card;
    }
}
