using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CubeShelf.Launcher.Services;

namespace CubeShelf.Launcher;

internal sealed class FirstRunWizardWindow : Window
{
    private readonly UserPreferencesService _preferencesService;
    private readonly UserPreferences _preferences;

    private readonly Grid _pageHost = new();
    private readonly TextBlock _stepText = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();

    private ComboBox? _languageCombo;
    private ComboBox? _themeCombo;

    private int _page;

    public bool OpenGameAfterFinish { get; private set; }

    private bool English =>
        _preferences.Language.Equals(
            "en",
            StringComparison.OrdinalIgnoreCase);

    public FirstRunWizardWindow(
        UserPreferencesService preferencesService,
        UserPreferences preferences)
    {
        _preferencesService = preferencesService;
        _preferences = preferences;

        Title = "CubeShelf";
        Width = 760;
        Height = 580;
        MinWidth = 720;
        MinHeight = 540;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        RefreshWindowBrushes();
        BuildUi();
        ShowPage(0);
    }

    private void RefreshWindowBrushes()
    {
        Background =
            Application.Current.TryFindResource("Bg") as Brush ??
            Brushes.Black;
    }

    private Brush ResourceBrush(
        string key,
        Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ??
           fallback;

    private TextBlock TitleBlock(string text)
        => new()
        {
            Text = text,
            Foreground = ResourceBrush("Text", Brushes.White),
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };

    private TextBlock BodyBlock(string text)
        => new()
        {
            Text = text,
            Foreground = ResourceBrush(
                "Muted",
                Brushes.LightGray),
            FontSize = 14,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap
        };

    private Border Card(UIElement child)
        => new()
        {
            Background = ResourceBrush(
                "Panel2",
                Brushes.DimGray),
            BorderBrush = ResourceBrush(
                "Border",
                Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 14, 0, 0),
            Child = child
        };

    private void BuildUi()
    {
        var shell = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(30),
            CornerRadius = new CornerRadius(24),
            Background = ResourceBrush(
                "Panel",
                Brushes.DimGray),
            BorderBrush = ResourceBrush(
                "Border",
                Brushes.Gray),
            BorderThickness = new Thickness(1)
        };

        var root = new Grid();

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition());

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var header = new Grid();

        header.ColumnDefinitions.Add(
            new ColumnDefinition());

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var brand = new StackPanel();

        brand.Children.Add(
            new TextBlock
            {
                Text = "CUBESHELF",
                Foreground = ResourceBrush(
                    "Accent",
                    Brushes.MediumPurple),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });

        brand.Children.Add(
            new TextBlock
            {
                Text =
                    $"CubeShelf v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}",
                Foreground = ResourceBrush(
                    "Text",
                    Brushes.White),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });

        _stepText.Foreground =
            ResourceBrush(
                "Muted",
                Brushes.LightGray);

        _stepText.FontSize = 12;
        _stepText.VerticalAlignment =
            VerticalAlignment.Center;

        Grid.SetColumn(_stepText, 1);

        header.Children.Add(brand);
        header.Children.Add(_stepText);

        Grid.SetRow(_pageHost, 1);
        _pageHost.Margin =
            new Thickness(0, 28, 0, 24);

        var footer = new Grid();

        footer.ColumnDefinitions.Add(
            new ColumnDefinition());

        footer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var skip = new Button
        {
            HorizontalAlignment =
                HorizontalAlignment.Left
        };

        skip.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var nav = new StackPanel
        {
            Orientation =
                Orientation.Horizontal,
            HorizontalAlignment =
                HorizontalAlignment.Right
        };

        _backButton.Click += (_, _) =>
        {
            if (_page > 0)
                ShowPage(_page - 1);
        };

        _nextButton.Click += (_, _) =>
        {
            if (_page < 3)
            {
                ShowPage(_page + 1);
                return;
            }

            Finish();
        };

        nav.Children.Add(_backButton);
        nav.Children.Add(_nextButton);

        Grid.SetColumn(nav, 1);

        footer.Children.Add(skip);
        footer.Children.Add(nav);

        Grid.SetRow(footer, 2);

        root.Children.Add(header);
        root.Children.Add(_pageHost);
        root.Children.Add(footer);

        shell.Child = root;
        Content = shell;

        void RefreshSkipText()
            => skip.Content =
                English ? "Later" : "Plus tard";

        _pageHost.Tag =
            (Action)RefreshSkipText;

        RefreshSkipText();
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, 3);

        _pageHost.Children.Clear();

        _pageHost.Children.Add(
            _page switch
            {
                0 => BuildWelcomePage(),
                1 => BuildPreferencesPage(),
                2 => BuildStoragePage(),
                _ => BuildGamePage()
            });

        _stepText.Text =
            English
                ? $"Step {_page + 1} of 4"
                : $"Étape {_page + 1} sur 4";

        _backButton.Content =
            English ? "Back" : "Retour";

        _backButton.IsEnabled =
            _page > 0;

        _nextButton.Content =
            _page == 3
                ? (English
                    ? "Finish and configure Mario Party 4"
                    : "Terminer et configurer Mario Party 4")
                : (English
                    ? "Continue"
                    : "Continuer");

        if (_pageHost.Tag is Action refreshSkip)
            refreshSkip();
    }

    private UIElement BuildWelcomePage()
    {
        var stack = new StackPanel();

        stack.Children.Add(
            TitleBlock(
                English
                    ? "Welcome to CubeShelf"
                    : "Bienvenue dans CubeShelf"));

        stack.Children.Add(
            new TextBlock
            {
                Text = English
                    ? "Your native GameCube library for Windows."
                    : "Ta bibliothèque GameCube native pour Windows.",
                Foreground = ResourceBrush(
                    "Accent",
                    Brushes.MediumPurple),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 18)
            });

        stack.Children.Add(
            BodyBlock(
                English
                    ? "CubeShelf manages PartyBoard, updates, downloads and your local game setup. Nintendo game data is never bundled with CubeShelf: you provide your own legally obtained ISO/RVZ when a game needs it."
                    : "CubeShelf gère PartyBoard, les mises à jour, les téléchargements et ton installation locale. Les données Nintendo ne sont jamais fournies avec CubeShelf : tu sélectionnes toi-même ton ISO/RVZ obtenu légalement lorsque le jeu en a besoin."));

        stack.Children.Add(
            Card(
                BodyBlock(
                    English
                        ? "The assistant only takes a few seconds. You can reopen it later from Settings."
                        : "L'assistant ne prend que quelques secondes. Tu pourras le relancer plus tard depuis Paramètres.")));

        return stack;
    }

    private UIElement BuildPreferencesPage()
    {
        var stack = new StackPanel();

        stack.Children.Add(
            TitleBlock(
                English
                    ? "Language and appearance"
                    : "Langue et apparence"));

        stack.Children.Add(
            BodyBlock(
                English
                    ? "These settings are applied immediately."
                    : "Ces réglages sont appliqués immédiatement."));

        _languageCombo = new ComboBox
        {
            Margin = new Thickness(0, 10, 0, 0)
        };

        _languageCombo.Items.Add(
            new ComboBoxItem
            {
                Content = "Français",
                Tag = "fr"
            });

        _languageCombo.Items.Add(
            new ComboBoxItem
            {
                Content = "English",
                Tag = "en"
            });

        _languageCombo.SelectedIndex =
            English ? 1 : 0;

        _languageCombo.SelectionChanged +=
            LanguageCombo_SelectionChanged;

        _themeCombo = new ComboBox
        {
            Margin = new Thickness(0, 10, 0, 0)
        };

        _themeCombo.Items.Add(
            new ComboBoxItem
            {
                Content = "Sombre / Dark",
                Tag = "dark"
            });

        _themeCombo.Items.Add(
            new ComboBoxItem
            {
                Content = "Clair / Light",
                Tag = "light"
            });

        _themeCombo.SelectedIndex =
            _preferences.Theme.Equals(
                "light",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;

        _themeCombo.SelectionChanged +=
            ThemeCombo_SelectionChanged;

        var languagePanel =
            new StackPanel();

        languagePanel.Children.Add(
            new TextBlock
            {
                Text =
                    English ? "Language" : "Langue",
                Foreground =
                    ResourceBrush(
                        "Text",
                        Brushes.White),
                FontWeight =
                    FontWeights.Bold
            });

        languagePanel.Children.Add(
            _languageCombo);

        var themePanel =
            new StackPanel();

        themePanel.Children.Add(
            new TextBlock
            {
                Text =
                    English ? "Theme" : "Thème",
                Foreground =
                    ResourceBrush(
                        "Text",
                        Brushes.White),
                FontWeight =
                    FontWeights.Bold
            });

        themePanel.Children.Add(
            _themeCombo);

        stack.Children.Add(
            Card(languagePanel));

        stack.Children.Add(
            Card(themePanel));

        return stack;
    }

    private UIElement BuildStoragePage()
    {
        var stack = new StackPanel();

        stack.Children.Add(
            TitleBlock(
                English
                    ? "Storage"
                    : "Stockage"));

        stack.Children.Add(
            BodyBlock(
                English
                    ? "CubeShelf keeps its managed files in your Windows local application data folder. Your ISO/RVZ remains wherever you selected it."
                    : "CubeShelf conserve ses fichiers gérés dans les données locales Windows. Ton ISO/RVZ reste à l'emplacement que tu as choisi."));

        var dataDir =
            _preferencesService.DataDirectory;

        long free = 0;

        try
        {
            var root =
                Path.GetPathRoot(dataDir);

            if (!string.IsNullOrWhiteSpace(root))
            {
                free =
                    new DriveInfo(root)
                        .AvailableFreeSpace;
            }
        }
        catch
        {
        }

        var panel =
            new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text = dataDir,
                Foreground =
                    ResourceBrush(
                        "Text",
                        Brushes.White),
                TextWrapping =
                    TextWrapping.Wrap,
                FontWeight =
                    FontWeights.SemiBold
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    English
                        ? $"Free space: {FormatBytes(free)}"
                        : $"Espace libre : {FormatBytes(free)}",
                Foreground =
                    ResourceBrush(
                        "Muted",
                        Brushes.LightGray),
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        0)
            });

        var open = new Button
        {
            Content =
                English
                    ? "Open data folder"
                    : "Ouvrir le dossier",
            HorizontalAlignment =
                HorizontalAlignment.Left,
            Margin =
                new Thickness(
                    0,
                    12,
                    0,
                    0)
        };

        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(
                    dataDir);

                Process.Start(
                    new ProcessStartInfo(
                        dataDir)
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        };

        panel.Children.Add(open);
        stack.Children.Add(Card(panel));

        return stack;
    }

    private UIElement BuildGamePage()
    {
        var stack =
            new StackPanel();

        stack.Children.Add(
            TitleBlock("Mario Party 4"));

        stack.Children.Add(
            BodyBlock(
                English
                    ? "CubeShelf will guide you through the playable setup directly from the game page."
                    : "CubeShelf va maintenant te guider directement depuis la page du jeu."));

        var steps =
            new StackPanel();

        steps.Children.Add(
            StepRow(
                "1",
                English
                    ? "Download PartyBoard"
                    : "Télécharger PartyBoard",
                English
                    ? "CubeShelf retrieves the compiled Windows runtime from GitHub and verifies its SHA-256."
                    : "CubeShelf récupère le runtime Windows compilé depuis GitHub et vérifie son SHA-256."));

        steps.Children.Add(
            StepRow(
                "2",
                English
                    ? "Select ISO / RVZ"
                    : "Sélectionner ISO / RVZ",
                English
                    ? "Your original disc image stays local and is never uploaded."
                    : "Ton image disque originale reste locale et n'est jamais envoyée."));

        steps.Children.Add(
            StepRow(
                "3",
                English
                    ? "Play"
                    : "Jouer",
                English
                    ? "CubeShelf prepares the required game data, then launches PartyBoard."
                    : "CubeShelf prépare les données nécessaires puis lance PartyBoard."));

        stack.Children.Add(Card(steps));

        return stack;
    }

    private UIElement StepRow(
        string number,
        string title,
        string text)
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        10)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(42)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition());

        var badge =
            new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius =
                    new CornerRadius(10),
                Background =
                    ResourceBrush(
                        "Accent",
                        Brushes.MediumPurple),
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child =
                    new TextBlock
                    {
                        Text = number,
                        Foreground =
                            Brushes.White,
                        FontWeight =
                            FontWeights.Bold,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    }
            };

        var copy =
            new StackPanel();

        copy.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground =
                    ResourceBrush(
                        "Text",
                        Brushes.White),
                FontWeight =
                    FontWeights.Bold
            });

        copy.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground =
                    ResourceBrush(
                        "Muted",
                        Brushes.LightGray),
                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0),
                TextWrapping =
                    TextWrapping.Wrap
            });

        Grid.SetColumn(copy, 1);

        grid.Children.Add(badge);
        grid.Children.Add(copy);

        return grid;
    }

    private void LanguageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_languageCombo?.SelectedItem is
            not ComboBoxItem
            {
                Tag: string language
            })
        {
            return;
        }

        if (_preferences.Language.Equals(
                language,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _preferences.Language =
            language;

        _preferencesService.Save(
            _preferences);

        _preferencesService.ApplyLanguage(
            language);

        ShowPage(_page);
    }

    private void ThemeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_themeCombo?.SelectedItem is
            not ComboBoxItem
            {
                Tag: string theme
            })
        {
            return;
        }

        if (_preferences.Theme.Equals(
                theme,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _preferences.Theme =
            theme;

        _preferencesService.Save(
            _preferences);

        _preferencesService.ApplyTheme(
            theme);

        RefreshWindowBrushes();
        ShowPage(_page);
    }

    private void Finish()
    {
        _preferences.FirstRunCompleted = true;

        _preferencesService.Save(
            _preferences);

        OpenGameAfterFinish = true;
        DialogResult = true;
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        var units =
            new[]
            {
                "B",
                "KB",
                "MB",
                "GB",
                "TB"
            };

        var value =
            (double)bytes;

        var unit = 0;

        while (value >= 1024 &&
               unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
