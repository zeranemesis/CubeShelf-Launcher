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

            try
            {
                var process =
                    Process.GetProcessById(_pid);

                await process.WaitForExitAsync();
            }
            catch
            {
            }

            SetStatus("Préparation des fichiers…");
            SetProgress(0.08);

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

                File.Copy(
                    file,
                    dst,
                    true);

                SetProgress(
                    0.45 +
                    ((i + 1d) / files.Count) *
                    0.50);

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

            SetStatus(
                "Échec de la mise à jour.");

            MessageBox.Show(
                this,
                ex.Message,
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

    private void RestartButton_Click(
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
                "CubeShelf.exe est introuvable. " +
                "Relance le launcher manuellement.",
                "CubeShelf Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        Process.Start(
            new ProcessStartInfo(launcher)
            {
                WorkingDirectory =
                    _installDir,
                UseShellExecute = true
            });

        Close();
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
