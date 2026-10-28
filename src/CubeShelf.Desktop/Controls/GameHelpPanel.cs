using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CubeShelf.Assistance;

namespace CubeShelf.Desktop.Controls;

internal sealed class GameHelpPanel : UserControl
{
    private readonly LocalGameHelp _help;
    private readonly string _game;
    private readonly IGameHelpVoice? _voice;
    private readonly Action<CompanionState>? _stateChanged;
    private readonly Func<CancellationToken, Task<GameFrame>>? _capture;
    private readonly Action _expand;
    private readonly CompanionControl _companion = new() { Width = 84, Height = 112 };
    private readonly TextBlock _mood = new() { Text = Companion.Message(CompanionState.Idle), FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _question = new() { Watermark = "La zone, l’objectif, ce que tu as essayé…", AcceptsReturn = true, MaxLength = 2000, MinHeight = 76, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _answer = new() { TextWrapping = TextWrapping.Wrap, FontSize = 17 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, IsVisible = false };
    private readonly SelectableTextBlock _sources = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Expander _sourceDetails = new() { Header = "Pages consultées", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private HintLevel? _level;
    private readonly Expander _details = new() { Header = "Options", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _talk = new() { Content = "Parler", MinHeight = 44 };
    private readonly Button _install = new() { Content = "Télécharger LFM3 · 1,1 Go", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
    private readonly Button _hint = new() { Content = "Indice", MinHeight = 48, FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _solution = new() { Content = "Solution", MinHeight = 48, FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _cancel = new() { Content = "Arrêter", IsVisible = false, MinHeight = 44 };
    private CancellationTokenSource? _operation;
    private bool _voiceMode, _closed;
    private bool _minimal;
    private readonly bool _compact;
    private readonly Action? _resize;
    private StackPanel? _actions;
    private readonly TextBox _refinement = new() { Watermark = "Ajoute le lieu, l’objectif ou ce qui manque…", AcceptsReturn = true, MaxLength = 1000, MinHeight = 72, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _editor = new() { Spacing = 8, IsVisible = false };

    public GameHelpPanel(string game, string directory, Action close, IGameHelpVoice? voice = null, bool reduceMotion = false,
        bool compact = false, Action<CompanionState>? stateChanged = null,
        Func<CancellationToken, Task<GameFrame>>? capture = null, Action? expanded = null, Action? resized = null)
    {
        _game = game; _voice = voice; _help = new LocalGameHelp(directory); _stateChanged = stateChanged; _capture = capture;
        _compact = compact; _resize = resized;
        _answer.IsVisible = false;
        _answer.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) _answer.IsVisible = !string.IsNullOrWhiteSpace(_answer.Text); };
        _install.Content = "Préparer Bip · 1,3 Go";
        _companion.ReduceMotion = reduceMotion;
        Background = Brushes.Transparent; Foreground = Brushes.White;
        var surface = new SolidColorBrush(Color.Parse("#171E2B"));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        var header = new DockPanel { Name = "HelpHeader", Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_companion, Dock.Left); _companion.Margin = new Thickness(0, 0, 10, 0);
        header.Children.Add(_companion);
        var intro = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        intro.Children.Add(new TextBlock { Text = "Un coup de pouce ?", FontSize = 21, FontWeight = FontWeight.SemiBold });
        intro.Children.Add(new TextBlock { Text = game, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#B5C8D6")), TextTrimming = TextTrimming.CharacterEllipsis });
        _mood.FontSize = 12; _mood.IsVisible = false; intro.Children.Add(_mood);
        header.Children.Add(intro); root.Children.Add(header);
        if (compact)
        {
            _companion.IsVisible = false;
            _question.MinHeight = 52;
        }
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(_answer);
        _editor.Children.Add(_refinement);
        var refineActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var refine = new Button { Content = "Préciser", MinHeight = 40 };
        var dismiss = new Button { Content = "Annuler", MinHeight = 40 };
        refine.Click += async (_, _) =>
        {
            if (_operation is not null || string.IsNullOrWhiteSpace(_refinement.Text)) return;
            var context = _refinement.Text.Trim() + "\n" + (_question.Text ?? "");
            _question.Text = context[..Math.Min(1000, context.Length)];
            _refinement.Clear(); _editor.IsVisible = false;
            await ChooseAsync(_level ?? HintLevel.Hint);
        };
        dismiss.Click += (_, _) => { _editor.IsVisible = false; _resize?.Invoke(); };
        refineActions.Children.Add(refine); refineActions.Children.Add(dismiss); _editor.Children.Add(refineActions); body.Children.Add(_editor);
        _answer.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        ToolTip.SetTip(_answer, "Cliquer pour ajouter une précision");
        _answer.PointerReleased += (_, e) =>
        {
            if (_operation is not null || string.IsNullOrWhiteSpace(_answer.Text) || e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return;
            _editor.IsVisible = true; _resize?.Invoke(); _refinement.Focus(); e.Handled = true;
        };
        _sourceDetails.Content = _sources;
        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(_sourceDetails);
        details.Children.Add(_install); details.Children.Add(_talk); details.Children.Add(_question);
        details.Children.Add(new TextBlock { Text = "Puis choisis Indice ou Solution.", FontSize = 12 });
        var unavailable = _voice is null ? "La voix locale n’est pas disponible dans cette édition. Tu peux écrire." : _voice.UnavailableReason;
        if (unavailable is not null) details.Children.Add(new TextBlock { Text = unavailable, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        _details.Content = details; body.Children.Add(_details);
        details.Children.Add(new TextBlock { Text = "Images traitées sur cet appareil. La recherche de guides utilise Internet.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_status);
        var scroll = new ScrollViewer { Name = "HelpBody", Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        _actions = actions;
        var prompt = new TextBlock { Text = "Comment je t’aide ?", FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#B5C8D6")), Margin = new Thickness(0, 0, 0, 2), IsVisible = compact };
        actions.Children.Add(prompt);
        var choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        _hint.Background = new SolidColorBrush(Color.Parse("#A9EAD3")); _hint.Foreground = new SolidColorBrush(Color.Parse("#102B25"));
        _solution.Background = new SolidColorBrush(Color.Parse("#283746")); _solution.Foreground = Brushes.White;
        _hint.CornerRadius = _solution.CornerRadius = new CornerRadius(10);
        _hint.Margin = _solution.Margin = new Thickness(0);
        _hint.Padding = _solution.Padding = new Thickness(8);
        _hint.BorderThickness = _solution.BorderThickness = new Thickness(0);
        choices.Children.Add(_hint); Grid.SetColumn(_solution, 1); choices.Children.Add(_solution); actions.Children.Add(choices);
        actions.Children.Add(_cancel);
        var back = new Button { Content = compact ? "Réduire" : "Fermer l’aide", HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 28, FontSize = 12, Padding = new Thickness(8, 3), Foreground = new SolidColorBrush(Color.Parse("#B5C8D6")) };
        back.Click += (_, _) => { Cancel(); close(); };
        back.Background = Brushes.Transparent; back.BorderThickness = new Thickness(0);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var options = new Button { Content = "Options", MinHeight = 28, FontSize = 12, Padding = new Thickness(8, 3),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = back.Foreground };
        options.Click += (_, _) => { _details.IsVisible = !_details.IsVisible; _details.IsExpanded = _details.IsVisible; };
        var more = new MenuItem { Header = "Options et sources" };
        more.Click += (_, _) => { _details.IsVisible = _details.IsExpanded = true; _resize?.Invoke(); };
        var again = new MenuItem { Header = "Nouvel indice ou solution" };
        again.Click += (_, _) => { Cancel(); _minimal = false; actions.IsVisible = true; _editor.IsVisible = false; _resize?.Invoke(); };
        var reduce = new MenuItem { Header = "Réduire" }; reduce.Click += (_, _) => { Cancel(); close(); };
        ContextMenu = new ContextMenu { ItemsSource = new[] { more, again, reduce } };
        footer.Children.Add(options); Grid.SetColumn(back, 2); footer.Children.Add(back);
        actions.Children.Add(footer); Grid.SetRow(actions, 2); root.Children.Add(actions);
        var bubble = new Border { Background = surface, BorderBrush = new SolidColorBrush(Color.Parse("#35495A")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = root };
        Content = bubble;
        _expand = () =>
        {
            header.IsVisible = !compact; scroll.IsVisible = true; prompt.IsVisible = false;
            root.RowDefinitions = new RowDefinitions(compact ? "0,*,Auto" : "Auto,*,Auto"); root.VerticalAlignment = VerticalAlignment.Stretch;
            root.Margin = new Thickness(18); expanded?.Invoke();
        };
        if (compact)
        {
            header.IsVisible = scroll.IsVisible = _details.IsVisible = options.IsVisible = false;
            root.RowDefinitions = new RowDefinitions("0,0,Auto"); root.VerticalAlignment = VerticalAlignment.Top;
            root.Margin = new Thickness(14, 12); actions.Margin = new Thickness(0);
        }
        _hint.Click += (_, _) => options.IsVisible = true;
        _solution.Click += (_, _) => options.IsVisible = true;
        _install.Click += async (_, _) => await RunAsync(true);
        _hint.Click += async (_, _) => await ChooseAsync(HintLevel.Hint);
        _solution.Click += async (_, _) => await ChooseAsync(HintLevel.Solution);
        _talk.Click += async (_, _) => await ListenAsync();
        _question.GotFocus += (_, _) => { if (_operation is null) _voiceMode = false; };
        _cancel.Click += (_, _) => Cancel();
        DetachedFromVisualTree += (_, _) => { _closed = true; Cancel(); _voice?.Dispose(); _companion.Dispose(); };
        UpdateButtons();
    }

    public void Cancel() { _operation?.Cancel(); _voice?.Stop(); }
    public double HeightForWidth(double width)
    {
        if (!_compact || !_minimal) return _details.IsVisible ? 360 : 142;
        var contentWidth = Math.Max(80, width - 38);
        _answer.Measure(new Size(contentWidth, double.PositiveInfinity));
        _status.Measure(new Size(contentWidth, double.PositiveInfinity));
        var height = 38 + (_answer.IsVisible ? _answer.DesiredSize.Height : 0) +
            (_status.IsVisible ? _status.DesiredSize.Height + (_answer.IsVisible ? 12 : 0) : 0);
        if (_editor.IsVisible) height += 156;
        if (_details.IsVisible) height += 280;
        return Math.Clamp(height, 56, 440);
    }
    private async Task ChooseAsync(HintLevel level)
    {
        if (_operation is not null) return;
        _level = level;
        _expand();
        if (!Ready) { _details.IsVisible = _details.IsExpanded = true; _status.IsVisible = true; _status.Text = "Prépare d’abord le modèle local."; return; }
        if (_capture is null && string.IsNullOrWhiteSpace(_question.Text))
        {
            _details.IsVisible = _details.IsExpanded = true;
            _status.IsVisible = true;
            _status.Text = "Je ne vois pas encore le jeu. Précise ta situation à la voix ou à l’écrit.";
            return;
        }
        _details.IsVisible = _details.IsExpanded = false; await RunAsync(false);
    }
    private void Mood(CompanionState state) { if (_closed) return; _companion.State = state; _stateChanged?.Invoke(state); _mood.Text = Companion.Message(state); _mood.IsVisible = state != CompanionState.Idle; }
    private void UpdateButtons()
    {
        _install.IsVisible = !Ready; _install.IsEnabled = _operation is null;
        _question.IsEnabled = _hint.IsEnabled = _solution.IsEnabled = _operation is null;
        _talk.IsEnabled = _operation is null && _voice is not null && _voice.UnavailableReason is null;
        _cancel.IsVisible = _operation is not null;
        _talk.FontWeight = _voiceMode ? FontWeight.Bold : FontWeight.Normal;
    }
    private bool Ready => _capture is null ? _help.IsInstalled || _help.IsVisionInstalled : _help.IsVisionInstalled;
    private async Task ListenAsync()
    {
        if (_operation is not null || _voice is null) return;
        using var operation = new CancellationTokenSource(); _operation = operation; _voiceMode = true; UpdateButtons(); Mood(CompanionState.Listening);
        _status.IsVisible = true; _status.Text = "Je t’écoute…";
        try { _question.Text = await _voice.ListenAsync(operation.Token); _status.Text = "Vérifie ta question, puis demande un indice."; Mood(CompanionState.Idle); }
        catch (OperationCanceledException) { _status.Text = "Écoute arrêtée."; Mood(CompanionState.Idle); }
        catch (Exception error) { _status.Text = error.Message; Mood(CompanionState.Error); }
        finally { _operation = null; if (!_closed) UpdateButtons(); }
    }
    private async Task RunAsync(bool install)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource(); _operation = operation; UpdateButtons(); Mood(CompanionState.Thinking);
        _status.IsVisible = true;
        _status.Text = install ? "Téléchargement du modèle…" : "Je réfléchis…";
        if (!install) _answer.Text = "";
        if (_compact && !install) { _minimal = true; _actions!.IsVisible = false; }
        _resize?.Invoke();
        _sourceDetails.IsVisible = false;
        try
        {
            if (install)
            {
                var progress = new Progress<double>(value => { if (!_closed) _status.Text = $"Téléchargement : {value:P0}"; });
                await _help.InstallVisionAsync(progress, operation.Token);
                _status.Text = "Prêt ! Tu peux maintenant demander de l’aide hors ligne.";
            }
            else
            {
                if (_capture is not null)
                {
                    var frame = await _capture(operation.Token);
                    try
                    {
                        var advice = await _help.AskWebScreenAsync(_game, frame, _level ?? HintLevel.Hint, null,
                            null, operation.Token, _question.Text);
                        _answer.Text = advice.Answer;
                        _sources.Text = advice.WebStatus + "\n\n" + string.Join("\n\n", advice.Sources.Select((source, index) => $"[{index + 1}] {source.Title}\n{source.Url}"));
                        _sourceDetails.IsVisible = true;
                    }
                    finally { Array.Clear(frame.Rgb); }
                }
                else _answer.Text = await _help.AskAsync(_game, _question.Text ?? "", _level ?? HintLevel.Hint, null, operation.Token);
                if (_voiceMode && _voice is not null) { Mood(CompanionState.Speaking); _status.Text = "Lecture de la réponse…"; await _voice.SpeakAsync(_answer.Text ?? "", operation.Token); }
                _status.Text = ""; _status.IsVisible = false;
            }
            Mood(CompanionState.Ready);
        }
        catch (OperationCanceledException) { _status.Text = "Arrêté."; Mood(CompanionState.Idle); }
        catch (Exception error) { _status.Text = error.Message; Mood(CompanionState.Error); }
        finally { _operation = null; if (!_closed) { UpdateButtons(); _resize?.Invoke(); } }
    }
}
