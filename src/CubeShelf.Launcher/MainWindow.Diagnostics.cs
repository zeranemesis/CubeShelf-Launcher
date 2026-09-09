using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CubeShelf.Launcher.Services;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow(BuildDiagnosticReport)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private string BuildDiagnosticReport()
    {
        var lines = new List<string>();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        lines.Add("CubeShelf Diagnostic");
        lines.Add("====================");
        lines.Add($"Launcher       : v{version}");
        lines.Add($"OS             : {Environment.OSVersion}");
        lines.Add($"64 bits        : {Environment.Is64BitProcess}");
        lines.Add($"Dossier app    : {AppContext.BaseDirectory}");
        lines.Add($"Dossier données: {_preferencesService.DataDirectory}");
        lines.Add($"DolphinTool    : {(_runtimeInstaller.IsDolphinToolAvailable() ? "OK" : "Absent")}");

        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                lines.Add($"Espace libre   : {FormatBytes(drive.AvailableFreeSpace)}");
            }
        }
        catch
        {
            lines.Add("Espace libre   : indisponible");
        }

        lines.Add("");
        lines.Add("Jeu sélectionné");
        lines.Add("---------------");

        if (_selectedGame is null)
        {
            lines.Add("Aucun jeu sélectionné.");
            return string.Join(Environment.NewLine, lines);
        }

        var game = _selectedGame;
        var runtime = _runtimeInstaller.ReadState(game);
        var compatibility = DiscImageService.Inspect(game.DiscImageFullPath, english: false);

        lines.Add($"Titre          : {game.Title}");
        lines.Add($"ID             : {game.Id}");
        lines.Add($"Favori         : {(game.IsFavorite ? "Oui" : "Non")}");
        lines.Add($"Lancements     : {game.PlayCount}");
        lines.Add($"Temps de jeu   : {game.PlayTimeText}");
        lines.Add($"Dernier jeu    : {game.LastPlayedText}");
        lines.Add($"Runtime        : {(game.RuntimeInstalled ? "Installé" : "Non installé")}");
        lines.Add($"Données jeu    : {(game.GameDataReady ? "Prêtes" : "À préparer")}");
        lines.Add($"Executable     : {game.ExecutableFullPath}");
        lines.Add($"Game root      : {game.GameRootFullPath}");
        lines.Add($"Runtime version: {runtime?.Version ?? "—"}");
        lines.Add($"Runtime commit : {runtime?.Commit ?? "—"}");
        lines.Add($"GitHub local   : {ShortSha(game.GitHubCurrentCommit)}");
        lines.Add($"GitHub latest  : {ShortSha(game.GitHubLatestCommit)}");
        lines.Add($"Mise à jour    : {(game.GitHubUpdateAvailable ? "Disponible" : "Non")}");
        lines.Add($"Image disque   : {(game.HasDiscImage ? game.DiscImageFullPath : "Aucune")}");
        lines.Add($"Compatibilité  : {compatibility.Message}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

internal sealed class DiagnosticsWindow : Window
{
    private readonly Func<string> _reportFactory;
    private readonly TextBox _text;

    public DiagnosticsWindow(Func<string> reportFactory)
    {
        _reportFactory = reportFactory;

        Title = "CubeShelf • Diagnostic";
        Width = 760;
        Height = 620;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Background =
            Application.Current.TryFindResource("Bg") as Brush ?? Brushes.Black;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Diagnostic CubeShelf",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Application.Current.TryFindResource("Text") as Brush ?? Brushes.White,
            Margin = new Thickness(0, 0, 0, 14)
        };

        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Text = _reportFactory()
        };

        Grid.SetRow(_text, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var refresh = new Button { Content = "Actualiser" };
        refresh.Click += (_, _) => _text.Text = _reportFactory();

        var copy = new Button { Content = "Copier le diagnostic" };
        copy.Click += (_, _) => Clipboard.SetText(_text.Text);

        var close = new Button { Content = "Fermer" };
        close.Click += (_, _) => Close();

        buttons.Children.Add(refresh);
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);

        root.Children.Add(title);
        root.Children.Add(_text);
        root.Children.Add(buttons);
        Content = root;
    }
}
