using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CubeShelf.Core.Storage;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _maintenanceBusy;

    private void RefreshMaintenanceLabels()
    {
        MaintenanceTitle.Text = P7("Sauvegardes et récupération", "Backups and recovery");
        MaintenanceHelp.Text = P7(
            "Les points de restauration conservent les réglages, la bibliothèque et les profils de mods. Les sauvegardes des cartes mémoire sont gérées séparément.",
            "Restore points preserve settings, the library and mod profiles. Memory-card saves are managed separately.");
        BackupProfileButton.Content = P7("Créer un point de restauration", "Create restore point");
        RestoreProfileButton.Content = P7("Restaurer le profil", "Restore profile");
        RestoreCardsButton.Content = P7("Restaurer une carte mémoire", "Restore a memory card");
        PreviousLauncherButton.Content = P7("Version précédente de CubeShelf", "Previous CubeShelf version");
        MeasureLauncherButton.Content = P7("Mesurer la consommation", "Measure resource usage");
    }

    private async void MeasureLauncherPerformance(object? sender, RoutedEventArgs args)
    {
        MeasureLauncherButton.IsEnabled = false;
        try
        {
            MaintenanceStatus.Text = P7("Mesure de CubeShelf sur deux secondes…", "Measuring CubeShelf over two seconds…");
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(2));
            process.Refresh();
            var used = (process.TotalProcessorTime - cpu).TotalSeconds / timer.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
            MaintenanceStatus.Text = P7(
                $"CubeShelf : CPU {used:F1} % · mémoire {process.WorkingSet64 / 1048576d:F0} Mo · {process.Threads.Count} threads · jaquettes partagées ≈ {Controls.CoverBitmapCache.EstimatedBytes / 1048576d:F1} Mo. Mesure du launcher uniquement, sans FPS ni mémoire GPU.",
                $"CubeShelf: CPU {used:F1}% · memory {process.WorkingSet64 / 1048576d:F0} MiB · {process.Threads.Count} threads · shared covers ≈ {Controls.CoverBitmapCache.EstimatedBytes / 1048576d:F1} MiB. Launcher only; no game FPS or GPU memory measurement.");
        }
        catch (Exception error) { MaintenanceStatus.Text = error.Message; }
        finally { MeasureLauncherButton.IsEnabled = true; }
    }

    private bool CanMaintainProfile()
    {
        if (_maintenanceBusy || _sessions.RunningGameIds.Count > 0 || _playIntents.Count > 0 ||
            _portableQueue?.Items.Any(item => !item.IsTerminal) == true)
        {
            MaintenanceStatus.Text = P7("Ferme les jeux et termine ou annule les opérations en cours avant une restauration.",
                "Close games and finish or cancel pending operations before restoring.");
            return false;
        }
        return true;
    }

    private async void BackupLauncherProfile(object? sender, RoutedEventArgs args)
    {
        if (_maintenanceBusy) return;
        _maintenanceBusy = true;
        BackupProfileButton.IsEnabled = false;
        try
        {
            var backup = await Task.Run(() => new ProfileBackupService(_paths).Create(P7("Manuel", "Manual")));
            MaintenanceStatus.Text = P7($"Point de restauration créé : {backup.FileCount} fichiers, {backup.CreatedAt.ToLocalTime():g}.",
                $"Restore point created: {backup.FileCount} files, {backup.CreatedAt.ToLocalTime():g}.");
        }
        catch (Exception error) { MaintenanceStatus.Text = error.Message; }
        finally { _maintenanceBusy = false; BackupProfileButton.IsEnabled = true; }
    }

    private async void RestoreLauncherProfile(object? sender, RoutedEventArgs args)
    {
        if (!CanMaintainProfile()) return;
        try
        {
            var service = new ProfileBackupService(_paths);
            var backups = await Task.Run(service.List);
            if (backups.Count == 0) { MaintenanceStatus.Text = P7("Aucun point de restauration disponible.", "No restore point is available."); return; }
            var id = await ChooseBackup(P7("Restaurer le profil", "Restore profile"),
                P7("Choisis un point de restauration. Il sera appliqué au prochain démarrage de CubeShelf ; le profil actuel sera sauvegardé avant son remplacement.",
                   "Choose a restore point. It will be applied when CubeShelf next starts; the current profile will be saved before replacement."),
                backups.Select(item => (item.Id, $"{item.CreatedAt.ToLocalTime():g} · {item.Reason} · {item.FileCount}")).ToArray());
            if (id is null || !CanMaintainProfile()) return;
            _maintenanceBusy = true;
            await Task.Run(() => service.RequestRestore(id));
            MaintenanceStatus.Text = P7("Restauration programmée. Quitte complètement CubeShelf, puis relance-le pour l’appliquer.",
                "Restore scheduled. Fully quit CubeShelf, then reopen it to apply the restore point.");
        }
        catch (Exception error) { MaintenanceStatus.Text = error.Message; }
        finally { _maintenanceBusy = false; }
    }

    private async void RestoreMemoryCards(object? sender, RoutedEventArgs args)
    {
        if (!CanMaintainProfile()) return;
        try
        {
            var service = new SaveBackupService(PhoneLinkServer.DefaultSavesDirectory());
            var backups = await Task.Run(service.List);
            if (backups.Count == 0) { MaintenanceStatus.Text = P7("Aucune sauvegarde de carte mémoire disponible. Les copies créées par les transferts téléphone apparaîtront ici.",
                "No memory-card backups are available. Copies created by phone transfers will appear here."); return; }
            var id = await ChooseBackup(P7("Restaurer une carte mémoire", "Restore a memory card"),
                P7("Les fichiers de la copie choisie remplaceront les fichiers correspondants. Une copie des fichiers actuels sera créée avant le remplacement.",
                   "Files from the selected backup will replace the matching files. Current files will be backed up before replacement."),
                backups.Select(item => (item.Id, $"{item.CreatedAt.ToLocalTime():g} · {item.FileCount} · {item.Bytes / 1024:N0} Ko")).ToArray());
            if (id is null || !CanMaintainProfile()) return;
            _maintenanceBusy = true;
            var result = await Task.Run(() => service.Restore(id, () => _sessions.RunningGameIds.Count > 0));
            MaintenanceStatus.Text = P7($"{result.RestoredFiles} fichiers de sauvegarde restaurés.", $"Restored {result.RestoredFiles} save files.");
        }
        catch (Exception error) { MaintenanceStatus.Text = error.Message; }
        finally { _maintenanceBusy = false; }
    }

    private async Task<string?> ChooseBackup(string title, string help, (string Id, string Label)[] entries)
    {
        var dialog = CreatePhase7Dialog(title, 620, 480);
        var list = new ListBox { ItemsSource = entries.Select(entry => entry.Label).ToArray(), SelectedIndex = 0, MaxHeight = 180 };
        var apply = CreateDialogButton(P7("Restaurer", "Restore"), (_, _) =>
            dialog.Close(list.SelectedIndex >= 0 ? entries[list.SelectedIndex].Id : null));
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children = { new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap }, list, apply,
                CreateDialogButton(P7("Annuler", "Cancel"), (_, _) => dialog.Close(null)) }
        };
        return await dialog.ShowDialog<string?>(this);
    }

    private async void RestorePreviousLauncher(object? sender, RoutedEventArgs args)
    {
        if (!CanMaintainProfile()) return;
        try
        {
            var previous = Core.Releases.LauncherRollbackService.FindPrevious(_paths);
            if (previous is null)
            {
                MaintenanceStatus.Text = P7("Aucune version précédente conservée. Elle sera disponible après une mise à jour effectuée avec cette version de CubeShelf.",
                    "No previous version has been retained. It will be available after an update performed with this CubeShelf version.");
                return;
            }
            var choice = await ChooseBackup(P7("Version précédente", "Previous version"),
                P7("CubeShelf va préparer et vérifier la copie précédente, puis redémarrer. Ton profil sera sauvegardé avant cette opération.",
                   "CubeShelf will prepare and verify the previous copy, then restart. Your profile will be backed up first."),
                [(previous.Path, previous.Label)]);
            if (choice is null || !CanMaintainProfile()) return;
            _maintenanceBusy = true;
            var archive = await Task.Run(() =>
            {
                new ProfileBackupService(_paths).Create("Avant retour à la version précédente");
                return Core.Releases.LauncherRollbackService.Prepare(previous, _paths);
            });
            var result = _launcherUpdateInstaller!.Launch(archive, "rollback", Environment.ProcessId);
            MaintenanceStatus.Text = result.Message;
            if (result.ShutdownRequired) QuitCompletely();
        }
        catch (Exception error) { MaintenanceStatus.Text = error.Message; }
        finally { _maintenanceBusy = false; }
    }
}
