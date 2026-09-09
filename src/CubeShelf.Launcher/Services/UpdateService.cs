using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CubeShelf.Launcher.Services;

public sealed record UpdateInfo(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string AssetUrl,
    string ChecksumUrl,
    string Message);

public sealed class UpdateService
{
    private readonly LauncherConfig _config;
    private readonly HttpClient _http = new();

    public UpdateService(LauncherConfig config)
    {
        _config = config;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.6.10");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo> CheckAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var api = $"https://api.github.com/repos/{_config.GitHubOwner}/{_config.GitHubRepo}/releases/latest";

        using var response = await _http.GetAsync(api);
        if (!response.IsSuccessStatusCode)
        {
            return new(
                false,
                current.ToString(),
                "",
                "",
                "",
                "",
                $"Aucune release disponible ou GitHub a répondu {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0";
        Version.TryParse(tag.TrimStart('v', 'V'), out var latest);
        latest ??= new Version(0, 0, 0);

        string assetUrl = "", checksumUrl = "";
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.Equals(_config.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                assetUrl = url;

            if (name.Equals(_config.ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                checksumUrl = url;
        }

        return new(
            latest > current,
            current.ToString(),
            latest.ToString(),
            doc.RootElement.GetProperty("html_url").GetString() ?? "",
            assetUrl,
            checksumUrl,
            latest > current ? "Nouvelle version disponible." : "CubeShelf est à jour.");
    }

    public async Task PrepareAndLaunchUpdateAsync(UpdateInfo info)
    {
        if (!info.UpdateAvailable || string.IsNullOrWhiteSpace(info.AssetUrl))
            throw new InvalidOperationException("Aucune mise à jour installable.");

        var progressWindow = new LauncherUpdateProgressWindow(info.LatestVersion);

        if (Application.Current?.MainWindow is Window owner &&
            !ReferenceEquals(owner, progressWindow))
        {
            progressWindow.Owner = owner;
        }

        progressWindow.Show();

        try
        {
            var safeVersion = string.Join(
                "_",
                info.LatestVersion.Split(
                    Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries));

            var tempDir = Path.Combine(
                Path.GetTempPath(),
                "CubeShelf",
                "Updates",
                safeVersion);

            Directory.CreateDirectory(tempDir);

            var zip = Path.Combine(tempDir, _config.ReleaseAssetName);

            progressWindow.SetStatus("Téléchargement de la mise à jour…");
            progressWindow.SetProgress(0.02);

            await DownloadFileAsync(
                info.AssetUrl,
                zip,
                p =>
                {
                    progressWindow.SetStatus(
                        $"Téléchargement… {(int)Math.Round(p * 100)} %");

                    progressWindow.SetProgress(0.02 + p * 0.78);
                });

            progressWindow.SetStatus("Vérification de l’intégrité SHA-256…");
            progressWindow.SetProgress(0.84);

            if (!string.IsNullOrWhiteSpace(info.ChecksumUrl))
            {
                var checksumText = await _http.GetStringAsync(info.ChecksumUrl);
                var expected = ParseChecksum(
                    checksumText,
                    _config.ReleaseAssetName);

                if (expected.Length == 0)
                {
                    throw new CryptographicException(
                        "Le fichier checksums.txt ne contient pas le hash attendu.");
                }

                await using var fs = File.OpenRead(zip);
                var actual = Convert
                    .ToHexString(await SHA256.HashDataAsync(fs))
                    .ToLowerInvariant();

                if (!actual.Equals(
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new CryptographicException(
                        "SHA-256 de la mise à jour invalide. " +
                        "Le fichier téléchargé a été refusé.");
                }
            }

            progressWindow.SetProgress(0.96);
            progressWindow.SetStatus("Mise à jour téléchargée et vérifiée.");

            var updaterSource = Path.Combine(
                AppContext.BaseDirectory,
                "CubeShelf.Updater.exe");

            if (!File.Exists(updaterSource))
            {
                throw new FileNotFoundException(
                    "CubeShelf.Updater.exe est absent.",
                    updaterSource);
            }

            var updaterTempDir = Path.Combine(
                Path.GetTempPath(),
                "CubeShelf",
                "Updater",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(updaterTempDir);

            var updaterTemp = Path.Combine(
                updaterTempDir,
                "CubeShelf.Updater.exe");

            File.Copy(updaterSource, updaterTemp, true);

            progressWindow.SetProgress(1.0);
            progressWindow.SetStatus(
                "Mise à jour prête. Redémarrage requis.");

            var answer = MessageBox.Show(
                progressWindow,
                $"CubeShelf {info.LatestVersion} a été téléchargé et vérifié.\n\n" +
                "Redémarrer CubeShelf maintenant pour installer la mise à jour ?",
                "Mise à jour CubeShelf prête",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                progressWindow.Close();
                throw new OperationCanceledException(
                    "Redémarrage reporté par l’utilisateur.");
            }

            progressWindow.SetStatus(
                "Fermeture de CubeShelf et lancement de l’installateur…");

            var process = Process.Start(
                new ProcessStartInfo(updaterTemp)
                {
                    WorkingDirectory = updaterTempDir,
                    UseShellExecute = false,
                    ArgumentList =
                    {
                        Environment.ProcessId.ToString(),
                        zip,
                        AppContext.BaseDirectory,
                        info.LatestVersion
                    }
                });

            if (process is null)
            {
                throw new InvalidOperationException(
                    "Impossible de démarrer CubeShelf.Updater.");
            }

            progressWindow.Close();
        }
        catch
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();

            throw;
        }
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        Action<double>? progress)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        await using var input =
            await response.Content.ReadAsStreamAsync();

        await using var output = File.Create(destination);

        var buffer = new byte[256 * 1024];
        long done = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer);

            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;

            if (total is > 0)
            {
                progress?.Invoke(
                    Math.Clamp(
                        (double)done / total.Value,
                        0,
                        1));
            }
        }

        progress?.Invoke(1.0);
    }

    private static string ParseChecksum(
        string text,
        string file)
    {
        foreach (var line in text.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2 &&
                parts[^1]
                    .TrimStart('*')
                    .Equals(
                        file,
                        StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].Trim();
            }
        }

        return "";
    }
}

internal sealed class LauncherUpdateProgressWindow : Window
{
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _percentText;
    private readonly TextBlock _statusText;

    public LauncherUpdateProgressWindow(string version)
    {
        Title = "Mise à jour CubeShelf";
        Width = 520;
        Height = 245;
        MinWidth = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Background =
            Application.Current?.TryFindResource("Bg") as Brush ??
            new SolidColorBrush(Color.FromRgb(7, 9, 18));

        var foreground =
            Application.Current?.TryFindResource("Text") as Brush ??
            Brushes.White;

        var muted =
            Application.Current?.TryFindResource("Muted") as Brush ??
            new SolidColorBrush(Color.FromRgb(170, 183, 210));

        var panel =
            Application.Current?.TryFindResource("Panel") as Brush ??
            new SolidColorBrush(Color.FromRgb(20, 27, 43));

        var accent =
            Application.Current?.TryFindResource("Accent") as Brush ??
            new SolidColorBrush(Color.FromRgb(115, 87, 255));

        var root = new Border
        {
            Padding = new Thickness(28),
            Background = panel
        };

        var stack = new StackPanel();

        stack.Children.Add(
            new TextBlock
            {
                Text = "MISE À JOUR DU LAUNCHER",
                Foreground = accent,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });

        stack.Children.Add(
            new TextBlock
            {
                Text = $"CubeShelf {version}",
                Foreground = foreground,
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 18)
            });

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 13,
            Value = 0
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
            Text = "Préparation…",
            Foreground = muted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        _percentText = new TextBlock
        {
            Text = "0 %",
            Foreground = foreground,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(14, 0, 0, 0)
        };

        Grid.SetColumn(_percentText, 1);

        statusGrid.Children.Add(_statusText);
        statusGrid.Children.Add(_percentText);

        stack.Children.Add(statusGrid);

        root.Child = stack;
        Content = root;
    }

    public void SetProgress(double progress)
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

    public void SetStatus(string status)
        => Dispatcher.Invoke(
            () => _statusText.Text = status);
}
