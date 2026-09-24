using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CubeShelf.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            MessageBox.Show(
                "Usage : CubeShelf.Updater <pid> <zip> <installDir> [version]",
                "CubeShelf Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return 2;
        }

        if (!int.TryParse(args[0], out var pid))
            return 2;

        var zip = Path.GetFullPath(args[1]);
        var installDir = Path.GetFullPath(args[2]);
        var version = args.Length >= 4 ? args[3] : "";

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        var window =
            new UpdaterWindow(
                pid,
                zip,
                installDir,
                version);

        app.Run(window);
        return window.ExitCode;
    }
}

internal sealed class UpdaterWindow : Window
{
    private readonly int _pid;
    private readonly string _zip;
    private readonly string _installDir;
    private readonly string _version;

    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;
    private readonly TextBlock _percentText;
    private readonly Button _restartButton;
    private string? _backupDirectory;

    public int ExitCode { get; private set; } = 1;

    public UpdaterWindow(
        int pid,
        string zip,
        string installDir,
        string version)
    {
        _pid = pid;
        _zip = zip;
        _installDir = installDir;
        _version = version;

        Title = "CubeShelf Updater";
        Width = 540;
        Height = 300;
        MinWidth = 500;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation =
            WindowStartupLocation.CenterScreen;

        Background =
            new SolidColorBrush(
                Color.FromRgb(7, 9, 18));

        var card = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(28),
            CornerRadius = new CornerRadius(16),
            Background =
                new SolidColorBrush(
                    Color.FromRgb(20, 27, 43)),
            BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(58, 73, 104)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();

        stack.Children.Add(
            new TextBlock
            {
                Text = "INSTALLATION DE LA MISE À JOUR",
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(152, 132, 255)),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });

        stack.Children.Add(
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(_version)
                        ? "CubeShelf"
                        : $"CubeShelf {_version}",
                Foreground = Brushes.White,
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 20)
            });

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 13
        };

        stack.Children.Add(_progressBar);

        var statusGrid = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0)
        };

        statusGrid.ColumnDefinitions.Add(
            new ColumnDefinition());

        statusGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        _statusText = new TextBlock
        {
            Text =
                "Attente de la fermeture de CubeShelf…",
            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(190, 201, 225)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        _percentText = new TextBlock
        {
            Text = "0 %",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(14, 0, 0, 0)
        };

        Grid.SetColumn(_percentText, 1);

        statusGrid.Children.Add(_statusText);
        statusGrid.Children.Add(_percentText);

        stack.Children.Add(statusGrid);

        _restartButton = new Button
        {
            Content = "Relancer CubeShelf",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment =
                HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
            Padding = new Thickness(18, 9, 18, 9),
            FontWeight = FontWeights.SemiBold
        };

        _restartButton.Click += RestartButton_Click;

        stack.Children.Add(_restartButton);

        card.Child = stack;
        Content = card;

        ContentRendered +=
            async (_, _) => await RunUpdateAsync();
    }

    private async Task RunUpdateAsync()
    {
        var staging = Path.Combine(
            Path.GetTempPath(),
            "CubeShelfUpdate-" +
            Guid.NewGuid().ToString("N"));

        try
        {
            SetStatus(
                "Attente de la fermeture de CubeShelf…");

            SetProgress(0.03);

            await StopAllCubeShelfProcessesAsync();

            SetStatus("Préparation des fichiers…");
            SetProgress(0.08);

            // The previous update left its own displaced image behind -- it could not
            // delete it while running from it. Nothing maps it now, so it goes here.
            RemoveDisplacedImages();

            if (!File.Exists(_zip))
            {
                throw new FileNotFoundException(
                    "Archive de mise à jour introuvable.",
                    _zip);
            }

            Directory.CreateDirectory(staging);

            await ExtractSafelyAsync(
                _zip,
                staging,
                p =>
                {
                    SetStatus(
                        "Extraction de la mise à jour…");

                    SetProgress(
                        0.10 + p * 0.35);
                });

            var files =
                Directory
                    .EnumerateFiles(
                        staging,
                        "*",
                        SearchOption.AllDirectories)
                    .ToList();

            if (files.Count == 0)
            {
                throw new InvalidDataException(
                    "L’archive de mise à jour ne contient aucun fichier.");
            }

            if (!File.Exists(Path.Combine(staging, "CubeShelf.exe")) ||
                !File.Exists(Path.Combine(staging, "CubeShelf.Updater.exe")))
            {
                throw new InvalidDataException(
                    "Le package de mise à jour ne contient pas les exécutables CubeShelf attendus.");
            }

            // Before the backup and before any write: if a file cannot be overwritten, say so
            // now, while the installation is still exactly what the user had.
            SetStatus("Vérification des fichiers…");
            await EnsureDestinationsAreFreeAsync(staging, files);

            SetStatus("Sauvegarde de la version actuelle…");
            SetProgress(0.46);
            _backupDirectory = await CreateBackupAsync();

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];

                var rel =
                    Path.GetRelativePath(
                        staging,
                        file);

                var dst =
                    Path.Combine(
                        _installDir,
                        rel);

                SetStatus(
                    $"Installation… {rel}");

                Directory.CreateDirectory(
                    Path.GetDirectoryName(dst)!);

                // The updater ships inside its own update. Its running image cannot be
                // overwritten, so move it aside and write the new build in its place.
                DisplaceIfRunningImage(dst);

                await CopyWithRetryAsync(file, dst);

                SetProgress(
                    0.52 +
                    ((i + 1d) / files.Count) *
                    0.43);

                await Task.Yield();
            }

            SetProgress(1.0);

            SetStatus(
                "Mise à jour installée. " +
                "Tu peux maintenant relancer CubeShelf.");

            _restartButton.Visibility =
                Visibility.Visible;

            ExitCode = 0;

            try
            {
                File.Delete(_zip);
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            ExitCode = 1;

            var rollbackMessage = "";
            if (!string.IsNullOrWhiteSpace(_backupDirectory) &&
                Directory.Exists(_backupDirectory))
            {
                try
                {
                    SetStatus("Échec • restauration de la version précédente…");
                    await RestoreBackupAsync();
                    rollbackMessage =
                        "\n\nLa version précédente de CubeShelf a été restaurée automatiquement.";
                }
                catch (Exception rollbackEx)
                {
                    rollbackMessage =
                        "\n\nLa restauration automatique a aussi échoué : " +
                        rollbackEx.Message;
                }
            }

            SetStatus("Échec de la mise à jour.");

            MessageBox.Show(
                this,
                ex.Message + rollbackMessage,
                "CubeShelf Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch
            {
            }
        }
    }

    private async Task ExtractSafelyAsync(
        string zipPath,
        string destination,
        Action<double> progress)
    {
        var destinationRoot =
            Path.GetFullPath(destination)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive =
            ZipFile.OpenRead(zipPath);

        var entries =
            archive.Entries
                .Where(
                    x => !string.IsNullOrEmpty(x.Name))
                .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                "Archive de mise à jour vide.");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            var target =
                Path.GetFullPath(
                    Path.Combine(
                        destination,
                        entry.FullName));

            if (!target.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Chemin invalide détecté dans l’archive.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);

            entry.ExtractToFile(
                target,
                true);

            progress(
                (i + 1d) / entries.Count);

            await Task.Yield();
        }
    }

    private async Task<string> CreateBackupAsync()
    {
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf",
            "UpdaterBackups");

        Directory.CreateDirectory(backupRoot);

        // A new update starts only after the previous one has finished, so stale
        // backups can be removed before creating the next transactional backup.
        foreach (var old in Directory
                     .EnumerateDirectories(backupRoot)
                     .ToList())
        {
            try { Directory.Delete(old, true); } catch { }
        }

        var backup = Path.Combine(
            backupRoot,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(backup);
        await CopyDirectoryAsync(_installDir, backup);
        return backup;
    }

    // An antivirus looks at a file the moment something opens it -- and the backup has just
    // opened every one of them. A copy that lands on such a scan is retried for a few seconds
    // before it counts as a failure, so a scanner's glance does not turn into a rollback. The
    // check before the backup cannot see these: they start after it, because of it.
    private static async Task CopyWithRetryAsync(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException) && attempt < 20)
            {
                await Task.Delay(250);
            }
        }
    }

    // Puts the backed-up files back over the installation without emptying it first.
    //
    // The restore used to delete everything and then copy. When one file resisted, the copy
    // stopped there and every file after it in the listing was simply gone -- so an update that
    // failed on its very first file could leave no application behind at all. Now each file is
    // put back on its own, and a file that cannot be is set aside rather than ending the
    // restore. Such a file is almost always one the failed update could not overwrite either,
    // so it still holds the previous version; the message says where the full copy lives anyway.
    private async Task RestoreBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(_backupDirectory) ||
            !Directory.Exists(_backupDirectory))
            return;

        Directory.CreateDirectory(_installDir);

        var backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unrestored = new List<string>();

        foreach (var file in Directory.EnumerateFiles(_backupDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_backupDirectory, file);
            backedUp.Add(relative);

            var target = Path.Combine(_installDir, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                DisplaceIfRunningImage(target);
                await CopyWithRetryAsync(file, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unrestored.Add(relative);
            }

            await Task.Yield();
        }

        // What the failed update added and the previous version never had. Displaced images
        // are left for the next run, which deletes them once nothing maps them.
        foreach (var file in Directory.EnumerateFiles(_installDir, "*", SearchOption.AllDirectories).ToList())
        {
            var relative = Path.GetRelativePath(_installDir, file);
            if (backedUp.Contains(relative) ||
                relative.EndsWith(DisplacedSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (!DisplaceIfRunningImage(file))
                    File.Delete(file);
            }
            catch
            {
            }
        }

        if (unrestored.Count > 0)
        {
            throw new IOException(
                $"{unrestored.Count} fichier(s) n’ont pas pu être remis en place, dont {unrestored[0]}. " +
                $"Une copie complète de la version précédente se trouve dans {_backupDirectory}. " +
                "Le plus simple est de réinstaller CubeShelf avec CubeShelf-Setup-x64.exe : " +
                "tes amis, tes jeux et tes mods sont ailleurs et ne seront pas touchés.");
        }
    }

    // Windows refuses to overwrite or delete the image of a running process, and the
    // updater is itself part of what it installs. That is what broke an update in the
    // field on 2026-09-18: the install loop reached CubeShelf.Updater.exe, Windows said
    // "being used by another process", and the rollback -- which empties the install
    // directory before restoring -- died on the same file after it had already deleted
    // CubeShelf.exe. The user was left with no application at all, which is worse than
    // the failure it was trying to undo.
    //
    // Windows does allow *renaming* a running image. Moving the live file aside frees its
    // name, the new build is written there, and the displaced copy is deleted by the next
    // run, when nothing maps it any more. This is the only way a program can replace its
    // own executable, and it is why the two helpers below exist.
    private const string DisplacedSuffix = ".old";

    private static readonly string SelfImagePath =
        string.IsNullOrEmpty(Environment.ProcessPath)
            ? ""
            : Path.GetFullPath(Environment.ProcessPath);

    private static bool IsRunningImage(string path)
    {
        if (SelfImagePath.Length == 0)
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(path),
                SelfImagePath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Frees a path that cannot be written because it is this process's own image.
    // Returns true when the caller may now treat the path as absent.
    private static bool DisplaceIfRunningImage(string path)
    {
        if (!IsRunningImage(path) || !File.Exists(path))
            return false;

        var displaced = path + DisplacedSuffix;

        // A leftover from a previous update is not locked any more, so it can go. If it
        // somehow still is, a unique name keeps this update moving rather than failing.
        try
        {
            if (File.Exists(displaced))
                File.Delete(displaced);
        }
        catch
        {
            displaced = path + "." + Guid.NewGuid().ToString("N")[..8] + DisplacedSuffix;
        }

        File.Move(path, displaced);
        return true;
    }

    // Removes images displaced by an earlier run. It is deliberately narrow -- only
    // CubeShelf's own executables, only the exact suffix this file writes -- so it can
    // never reach a file that belongs to the application.
    private void RemoveDisplacedImages()
    {
        if (!Directory.Exists(_installDir))
            return;

        foreach (var stale in Directory.EnumerateFiles(_installDir, "CubeShelf*.exe*" + DisplacedSuffix))
        {
            try { File.Delete(stale); } catch { }
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using var input = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await using var output = new FileStream(
                target, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await input.CopyToAsync(output);
        }
    }

    private void DeleteBackup()
    {
        if (string.IsNullOrWhiteSpace(_backupDirectory))
            return;

        try
        {
            if (Directory.Exists(_backupDirectory))
                Directory.Delete(_backupDirectory, true);
        }
        catch { }

        _backupDirectory = null;
    }

    // How long CubeShelf gets to close by itself before it is killed. It saves its preferences
    // on the way out and, from 0.9, writes a farewell presence document bounded at three
    // seconds. Two seconds was already too short for some machines before that.
    private static readonly TimeSpan GracefulExit = TimeSpan.FromSeconds(10);

    private async Task StopAllCubeShelfProcessesAsync()
    {
        SetStatus("Fermeture complète de CubeShelf…");
        SetProgress(0.02);

        // CubeShelf starts this updater, so this updater is its child -- and
        // Kill(entireProcessTree: true) refuses any tree that contains the calling process. It
        // throws InvalidOperationException, which the catch here used to swallow, so this phase
        // never killed anything at all: an update only worked when CubeShelf happened to close by
        // itself within about two seconds, and when it did not, the copy started with the old
        // CubeShelf still mapping its own DLLs. Each process is now killed on its own. On Windows
        // killing a parent does not take its children with it, and a game CubeShelf launched maps
        // nothing in the install directory, so there is no tree worth taking down anyway.
        try
        {
            using var parent = Process.GetProcessById(_pid);

            var graceful = parent.WaitForExitAsync();
            if (await Task.WhenAny(graceful, Task.Delay(GracefulExit)) != graceful &&
                !parent.HasExited)
            {
                SetStatus("CubeShelf ne se ferme pas, arrêt forcé…");
                parent.Kill();
                await parent.WaitForExitAsync();
            }
        }
        catch
        {
            // Already gone, or not ours to kill. Either way the file check before the copy is
            // what decides whether the update may proceed, not this.
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var remaining = RunningFromInstallDirectory();
            if (remaining.Count == 0)
                break;

            foreach (var process in remaining)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            await Task.Delay(150);
        }

        await Task.Delay(350);
        SetProgress(0.06);
    }

    // CubeShelf processes whose image lives in the directory being updated. Another copy
    // installed elsewhere is left alone: it maps none of the files this update writes.
    private List<Process> RunningFromInstallDirectory()
    {
        var installRoot =
            Path.GetFullPath(_installDir)
                .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        var found = new List<Process>();

        foreach (var name in new[] { "CubeShelf", "CubeShelf.Updater" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                var keep = false;
                try
                {
                    if (process.Id != Environment.ProcessId && !process.HasExited)
                    {
                        var path = process.MainModule?.FileName;
                        keep = !string.IsNullOrWhiteSpace(path) &&
                               Path.GetFullPath(path).StartsWith(installRoot, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // A process whose modules we may not read is one we cannot place. The file
                    // check before the copy catches it if it matters.
                }

                if (keep)
                    found.Add(process);
                else
                    process.Dispose();
            }
        }

        return found;
    }

    // The stop phase finds processes by name and path, and can miss one: an elevated copy
    // whose modules we may not read, an antivirus holding a freshly written DLL open, a second
    // copy started under another name. What matters is whether the files can be written, so
    // that is what is checked -- before a single one is touched. Failing here costs a retry.
    // Failing half way through the copy is what left an installation broken in the field on
    // 2026-09-24: Avalonia.Base.dll was still mapped by the CubeShelf this updater had failed
    // to close, and the rollback died on the same file.
    private async Task EnsureDestinationsAreFreeAsync(string staging, IReadOnlyList<string> files)
    {
        // Long enough for an antivirus to finish looking at a file, short enough that someone
        // with CubeShelf genuinely still open is told so rather than left watching a bar.
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (true)
        {
            var locked = FirstLockedDestination(staging, files);
            if (locked is null)
                return;

            if (DateTime.UtcNow >= deadline)
            {
                throw new IOException(
                    $"{Path.GetFileName(locked)} est encore utilisé par un autre programme, " +
                    "sans doute CubeShelf lui-même.\n\n" +
                    "Rien n’a été modifié : ta version actuelle est intacte.\n\n" +
                    "Ferme CubeShelf depuis le gestionnaire des tâches, puis relance la mise à jour " +
                    "depuis les Paramètres.");
            }

            SetStatus($"En attente de la libération de {Path.GetFileName(locked)}…");
            await Task.Delay(500);
        }
    }

    private string? FirstLockedDestination(string staging, IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            var destination = Path.Combine(_installDir, Path.GetRelativePath(staging, file));

            // Our own image is locked by definition, and is moved aside rather than written.
            if (!File.Exists(destination) || IsRunningImage(destination))
                continue;

            try
            {
                // Exclusive, and for writing: exactly what the copy is about to need. A DLL
                // mapped by another process refuses this, and so does any open handle at all.
                using var probe = new FileStream(
                    destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return destination;
            }
        }

        return null;
    }

    private async void RestartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var launcher =
            Path.Combine(
                _installDir,
                "CubeShelf.exe");

        if (!File.Exists(launcher))
        {
            MessageBox.Show(
                this,
                "CubeShelf.exe est introuvable. La version précédente va être restaurée.",
                "CubeShelf Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            await RestoreBackupAsync();
            return;
        }

        _restartButton.IsEnabled = false;
        SetStatus("Vérification du nouveau CubeShelf…");

        Process? process = null;
        try
        {
            process = Process.Start(
                new ProcessStartInfo(launcher)
                {
                    WorkingDirectory = _installDir,
                    UseShellExecute = true
                });

            if (process is null)
                throw new InvalidOperationException("Impossible de relancer CubeShelf.");

            await Task.Delay(TimeSpan.FromSeconds(3));

            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                SetStatus("Le nouveau CubeShelf s'est fermé trop tôt • rollback…");
                await RestoreBackupAsync();

                MessageBox.Show(
                    this,
                    $"La nouvelle version n'est pas restée ouverte (code {exitCode}). " +
                    "CubeShelf a restauré automatiquement la version précédente.",
                    "Rollback CubeShelf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                var restored = Path.Combine(_installDir, "CubeShelf.exe");
                if (File.Exists(restored))
                {
                    Process.Start(new ProcessStartInfo(restored)
                    {
                        WorkingDirectory = _installDir,
                        UseShellExecute = true
                    });
                }

                Close();
                return;
            }

            DeleteBackup();
            SetStatus("Nouvelle version démarrée correctement.");
            Close();
        }
        catch (Exception ex)
        {
            try { await RestoreBackupAsync(); } catch { }
            _restartButton.IsEnabled = true;
            SetStatus("Échec du redémarrage • version précédente restaurée.");

            MessageBox.Show(
                this,
                ex.Message,
                "CubeShelf Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private void SetProgress(double progress)
    {
        var value =
            Math.Clamp(progress, 0, 1) * 100;

        Dispatcher.Invoke(
            () =>
            {
                _progressBar.Value = value;

                _percentText.Text =
                    $"{(int)Math.Round(value)} %";
            });
    }

    private void SetStatus(string status)
        => Dispatcher.Invoke(
            () => _statusText.Text = status);
}
