using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly Button _appUpdatesButton = MakeButton("↻");
    private readonly Button _checkAppUpdate = MakeButton("Vérifier");
    private readonly Button _downloadAppUpdate = MakeButton("Télécharger la mise à jour");
    private readonly Button _installAppUpdate = MakeButton("Installer la mise à jour");
    private readonly Button _cancelAppUpdate = MakeButton("Annuler le téléchargement");
    private readonly TextBlock _appUpdateText = DownloadText(14);
    private readonly ProgressBar _appUpdateProgress = new() { Minimum = 0, Maximum = 100, Height = 7, IsVisible = false };
    private CancellationTokenSource? _appUpdateLifetime;

    private Control BuildAppUpdateCard()
    {
        _appUpdatesButton.MinWidth = 48; _appUpdatesButton.MinHeight = 48;
        _appUpdatesButton.Padding = new Thickness(10, 6); _appUpdatesButton.Margin = new Thickness(6, 0, 0, 0);
        _appUpdatesButton.IsVisible = _games.AppUpdates is not null;
        AutomationProperties.SetName(_appUpdatesButton, "Mises à jour de l'application DecompShelf");
        _appUpdatesButton.Click += async (_, _) =>
        {
            ShowDownloads(); _checkAppUpdate.Focus(); _checkAppUpdate.BringIntoView();
            if (_games.AppUpdates is { } updates) await updates.CheckAsync();
        };
        var card = new StackPanel { Spacing = 12 };
        card.Children.Add(new TextBlock { Text = "Application DecompShelf", FontSize = 19, FontWeight = FontWeight.SemiBold });
        card.Children.Add(_appUpdateText); card.Children.Add(_appUpdateProgress);
        card.Children.Add(new TextBlock { Text = "L'installation sera confirmée dans l'écran Android. Tes données sont conservées lors d'une mise à jour avec la même signature.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _checkAppUpdate, _downloadAppUpdate, _installAppUpdate, _cancelAppUpdate })
        { button.FontSize = 13; button.Margin = new Thickness(0, 0, 8, 6); actions.Children.Add(button); }
        _checkAppUpdate.Click += async (_, _) => { if (_games.AppUpdates is { } updates) await updates.CheckAsync(); };
        _downloadAppUpdate.Click += async (_, _) => { if (_games.AppUpdates is { } updates) await updates.DownloadAsync(); };
        _cancelAppUpdate.Click += (_, _) => _games.AppUpdates?.CancelDownload();
        _installAppUpdate.Click += (_, _) => { if (_games.AppUpdates is { } updates) _appUpdateText.Text = updates.Install(); };
        card.Children.Add(actions);
        var border = DownloadCard(card);
        border.IsVisible = _games.AppUpdates is not null;
        if (_games.AppUpdates is { } service) ApplyAppUpdate(service.Snapshot);
        return border;
    }

    private void SetAppUpdatesActive(bool active)
    {
        if (_games.AppUpdates is not { } updates) return;
        if (!active)
        {
            _appUpdateLifetime?.Cancel(); _appUpdateLifetime?.Dispose(); _appUpdateLifetime = null;
            updates.Changed -= AppUpdateChanged;
            return;
        }
        if (_appUpdateLifetime is not null) return;
        updates.Changed += AppUpdateChanged;
        ApplyAppUpdate(updates.Snapshot);
        _appUpdateLifetime = new CancellationTokenSource();
        _ = AppUpdateLoopAsync(updates, _appUpdateLifetime.Token);
    }

    private static async Task AppUpdateLoopAsync(LauncherAppUpdates updates, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await updates.CheckAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void AppUpdateChanged(AppUpdateSnapshot snapshot) => Dispatcher.UIThread.Post(() => ApplyAppUpdate(snapshot));
    private void ApplyAppUpdate(AppUpdateSnapshot snapshot)
    {
        var busy = snapshot.State is AppUpdateState.Checking or AppUpdateState.Downloading;
        var available = snapshot.Package is not null && snapshot.State != AppUpdateState.Current;
        _checkAppUpdate.IsEnabled = !busy;
        _downloadAppUpdate.IsVisible = available && !busy && snapshot.State != AppUpdateState.Ready;
        _installAppUpdate.IsVisible = snapshot.State == AppUpdateState.Ready;
        _cancelAppUpdate.IsVisible = snapshot.State == AppUpdateState.Downloading;
        _appUpdateProgress.IsVisible = snapshot.State == AppUpdateState.Downloading;
        _appUpdateProgress.Value = snapshot.Package is { Size: > 0 } package ? 100d * snapshot.Received / package.Size : 0;
        _appUpdatesButton.Content = available ? "↻ •" : "↻";
        _appUpdateText.Text = snapshot.State switch
        {
            AppUpdateState.Checking => "Recherche de la dernière version sur GitHub…",
            AppUpdateState.Current => $"DecompShelf {snapshot.CurrentVersion} est à jour.",
            AppUpdateState.Available => $"DecompShelf {snapshot.Package?.Version} est disponible (version installée : {snapshot.CurrentVersion}).",
            AppUpdateState.Downloading => $"DecompShelf {snapshot.Package?.Version} · {DataSize(snapshot.Received)} / {DataSize(snapshot.Package?.Size ?? 0)}",
            AppUpdateState.Ready => $"DecompShelf {snapshot.Package?.Version} est téléchargé et vérifié. Tu peux l'installer.",
            AppUpdateState.Failed => "Mise à jour indisponible : " + snapshot.Error + " Tu peux réessayer.",
            _ => $"Version installée : {snapshot.CurrentVersion}. Vérification automatique au démarrage et toutes les cinq minutes."
        };
        AutomationProperties.SetName(_appUpdatesButton, available ? $"DecompShelf {snapshot.Package?.Version} disponible" : "Mises à jour de l'application DecompShelf");
    }
}
