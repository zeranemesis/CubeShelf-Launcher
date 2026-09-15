using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CubeShelf.Core.Platform;

namespace CubeShelf.Desktop;

public sealed class FirstRunWizardWindow : Window
{
    private readonly IPlatformPaths _paths;
    private readonly UserPreferencesStore _store;
    private readonly Grid _pageHost = new();
    private readonly TextBlock _step = new();
    private readonly Button _back = new() { Content = "Retour" };
    private readonly Button _next = new() { Content = "Continuer" };
    private readonly Button _later = new() { Content = "Plus tard", HorizontalAlignment = HorizontalAlignment.Left };
    private int _page;

    public FirstRunWizardWindow(IPlatformPaths paths, UserPreferencesStore store, UserPreferences preferences)
    {
        _paths = paths;
        _store = store;
        Preferences = preferences;
        this.Title = English ? "CubeShelf • First setup" : "CubeShelf • Premier démarrage";
        Width = 760;
        Height = 590;
        MinWidth = 720;
        MinHeight = 540;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BuildUi();
        KeyDown += OnWizardKeyDown;
        AutomationProperties.SetName(this, Title ?? "CubeShelf");
        ShowPage(0);
    }

    public UserPreferences Preferences { get; private set; }
    public bool Completed { get; private set; }
    public bool OpenGameAfterFinish { get; private set; }
    private bool English => Preferences.Language.Equals("en", StringComparison.OrdinalIgnoreCase);

    private void BuildUi()
    {
        var root = new Grid { Margin = new Avalonia.Thickness(28) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var brand = new StackPanel { Spacing = 4 };
        brand.Children.Add(new TextBlock { Text = "CUBESHELF", FontSize = 11, FontWeight = FontWeight.Bold });
        brand.Children.Add(new TextBlock { Text = English ? "GameCube Library" : "Bibliothèque GameCube", FontSize = 20, FontWeight = FontWeight.SemiBold });
        Grid.SetColumn(_step, 1);
        _step.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(brand);
        header.Children.Add(_step);
        root.Children.Add(header);

        Grid.SetRow(_pageHost, 1);
        _pageHost.Margin = new Avalonia.Thickness(0, 28, 0, 24);
        root.Children.Add(_pageHost);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _later.Click += (_, _) => Close();
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _back.Click += (_, _) => { if (_page > 0) ShowPage(_page - 1); };
        _next.Click += (_, _) => { if (_page < 3) ShowPage(_page + 1); else Finish(); };
        nav.Children.Add(_back);
        nav.Children.Add(_next);
        Grid.SetColumn(nav, 1);
        footer.Children.Add(_later);
        footer.Children.Add(nav);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, 3);
        _pageHost.Children.Clear();
        _pageHost.Children.Add(_page switch
        {
            0 => WelcomePage(),
            1 => PreferencesPage(),
            2 => StoragePage(),
            _ => GamePage()
        });
        _step.Text = English ? $"Step {_page + 1} of 4" : $"Étape {_page + 1} sur 4";
        _back.Content = English ? "Back" : "Retour";
        _later.Content = English ? "Later" : "Plus tard";
        _back.IsEnabled = _page > 0;
        _next.Content = _page == 3
            ? (English ? "Finish and configure Mario Party 4" : "Terminer et configurer Mario Party 4")
            : (English ? "Continue" : "Continuer");
    }

    private Control WelcomePage()
    {
        var stack = PageStack();
        stack.Children.Add(Heading(English ? "Welcome to CubeShelf" : "Bienvenue dans CubeShelf"));
        stack.Children.Add(Body(English
            ? "CubeShelf manages PartyBoard, updates, mods and your local GameCube setup. Nintendo game data is never bundled: you select your own legally obtained ISO/RVZ."
            : "CubeShelf gère PartyBoard, les mises à jour, les mods et ton installation GameCube locale. Les données Nintendo ne sont jamais fournies : tu sélectionnes ton propre ISO/RVZ obtenu légalement."));
        return stack;
    }

    private Control PreferencesPage()
    {
        var stack = PageStack();
        stack.Children.Add(Heading(English ? "Language and appearance" : "Langue et apparence"));
        var language = new ComboBox
        {
            ItemsSource = new[] { "Français", "English" },
            SelectedIndex = English ? 1 : 0
        };
        language.SelectionChanged += (_, _) =>
        {
            Preferences = Preferences with { Language = language.SelectedIndex == 1 ? "en" : "fr" };
            _store.Save(Preferences);
            UiLocalization.Apply(Preferences.Language);
            ShowPage(_page);
        };
        var theme = new ComboBox
        {
            ItemsSource = new[] { "Sombre / Dark", "Clair / Light" },
            SelectedIndex = Preferences.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? 1 : 0
        };
        theme.SelectionChanged += (_, _) =>
        {
            Preferences = Preferences with { Theme = theme.SelectedIndex == 1 ? "light" : "dark" };
            _store.Save(Preferences);
            if (Application.Current is { } application)
                application.RequestedThemeVariant = Preferences.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        };
        stack.Children.Add(Card("Langue / Language", language));
        stack.Children.Add(Card("Thème / Theme", theme));
        return stack;
    }

    private Control StoragePage()
    {
        var stack = PageStack();
        stack.Children.Add(Heading(English ? "Storage" : "Stockage"));
        stack.Children.Add(Body(English
            ? "Managed files are stored in the platform-native CubeShelf data folder. Your original ISO/RVZ remains at the location you chose."
            : "Les fichiers gérés sont stockés dans le dossier CubeShelf natif de ta plateforme. Ton ISO/RVZ original reste à l'emplacement que tu as choisi."));
        var path = new TextBlock { Text = _paths.DataDirectory, TextWrapping = TextWrapping.Wrap };
        var open = new Button { Content = English ? "Open data folder" : "Ouvrir le dossier", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) =>
        {
            try { new ProcessLauncher().OpenDirectory(_paths.DataDirectory); } catch { }
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(path);
        panel.Children.Add(open);
        stack.Children.Add(Card(English ? "CubeShelf data" : "Données CubeShelf", panel));
        return stack;
    }

    private Control GamePage()
    {
        var stack = PageStack();
        stack.Children.Add(Heading("Mario Party 4"));
        stack.Children.Add(Body(English
            ? "CubeShelf will guide you from the game page: install PartyBoard, select ISO/RVZ, prepare data, then play."
            : "CubeShelf va te guider depuis la page du jeu : installer PartyBoard, sélectionner ISO/RVZ, préparer les données puis jouer."));
        stack.Children.Add(Body(English
            ? "1  PartyBoard\n2  ISO / RVZ\n3  Local preparation\n4  Play"
            : "1  PartyBoard\n2  ISO / RVZ\n3  Préparation locale\n4  Jouer"));
        return stack;
    }

    private void OnWizardKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            Close();
            args.Handled = true;
            return;
        }

        if (args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.None)
        {
            if (_page < 3) ShowPage(_page + 1);
            else Finish();
            args.Handled = true;
        }
    }

    private void Finish()
    {
        Preferences = Preferences with { FirstRunCompleted = true };
        _store.Save(Preferences);
        Completed = true;
        OpenGameAfterFinish = true;
        Close();
    }

    private static StackPanel PageStack() => new() { Spacing = 18 };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 30, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Body(string text) => new() { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private static Border Card(string title, Control child)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold });
        panel.Children.Add(child);
        return new Border { Padding = new Avalonia.Thickness(16), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(14), Child = panel };
    }
}
