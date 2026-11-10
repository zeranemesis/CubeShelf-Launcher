using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CubeShelf.Assistance;
using Path = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

namespace CubeShelf.Android;

internal sealed class GameHelpPanel : LinearLayout
{
    private readonly LocalGameHelp _help;
    private readonly string _game;
    private readonly IGameHelpVoice? _voice;
    private readonly Action? _openVoice;
    private readonly Action<CompanionState>? _stateChanged;
    private readonly Func<CancellationToken, Task<GameFrame>>? _capture;
    private readonly Action _expand;
    private bool _expanded;
    private readonly CompanionView _companion;
    private readonly EditText _question;
    private HintLevel? _level;
    private readonly LinearLayout _details, _setup;
    private readonly TextView _answer, _status, _mood;
    private readonly Button _install, _hint, _solution, _cancel, _talk;
    private CancellationTokenSource? _operation;
    private readonly Handler _ui = new(Looper.MainLooper!);
    private bool _detached, _voiceMode;
    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1));

    internal GameHelpPanel(Context context, string game, Action close, Action? enableOverlay = null,
        IGameHelpVoice? voice = null, Action? openVoice = null, bool compact = false,
        Action<CompanionState>? stateChanged = null, Func<CancellationToken, Task<GameFrame>>? capture = null, Action? expanded = null)
        : base(new ContextThemeWrapper(context, global::Android.Resource.Style.ThemeMaterialNoActionBar))
    {
        context = Context!; _game = game; _voice = voice; _openVoice = openVoice; _stateChanged = stateChanged; _capture = capture; _expanded = !compact;
        _help = new LocalGameHelp(Path.Combine(context.FilesDir!.AbsolutePath, "Assistance"));
        Orientation = Orientation.Vertical; SetPadding(Dp(16), Dp(22), Dp(16), Dp(8));
        SetBackgroundColor(Color.Transparent); SetWillNotDraw(false);
        TextView Text(LinearLayout parent, string text, float size = 15)
        {
            var view = new TextView(context) { Text = text, TextSize = size };
            view.SetTextColor(Color.White); view.SetPadding(0, Dp(3), 0, Dp(3)); parent.AddView(view); return view;
        }
        Button ActionButton(LinearLayout parent, string text, Action action)
        {
            var button = new Button(context) { Text = text }; button.SetAllCaps(false); button.SetMinHeight(Dp(48));
            button.Click += (_, _) => action(); parent.AddView(button); return button;
        }
        var header = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        _companion = new CompanionView(context); header.AddView(_companion, new LayoutParams(Dp(72), Dp(96)));
        if (compact) _companion.Visibility = ViewStates.Gone;
        var intro = new LinearLayout(context) { Orientation = Orientation.Vertical };
        Text(intro, "Un coup de pouce ?", 21);
        Text(intro, "Aide locale · " + game, 12);
        _mood = Text(intro, "", 12); _mood.Visibility = ViewStates.Gone;
        header.AddView(intro, new LayoutParams(0, LayoutParams.WrapContent, 1)); AddView(header);
        var body = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(context) { FillViewport = true }; scroll.AddView(body);
        AddView(scroll, new LayoutParams(LayoutParams.MatchParent, 0, 1));
        _answer = Text(body, "Un petit coup de pouce ou la marche à suivre ?", 17); _answer.SetTextIsSelectable(true);
        var limitation = Text(body, capture is null ? "Lance un jeu pour analyser son écran." : "Une capture du jeu, analysée sur cet appareil.", 12);
        limitation.SetTextColor(Color.Rgb(181, 200, 214));
        _details = new LinearLayout(context) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        ActionButton(body, "Préciser ma situation", () => _details.Visibility = _details.Visibility == ViewStates.Gone ? ViewStates.Visible : ViewStates.Gone);
        body.AddView(_details);
        _setup = new LinearLayout(context) { Orientation = Orientation.Vertical };
        if (enableOverlay is not null) ActionButton(_setup, "Afficher Bip pendant les jeux", enableOverlay);
        _install = ActionButton(_setup, "Préparer Bip · 1,3 Go", () => _ = RunAsync(true));
        _details.AddView(_setup);
        _talk = ActionButton(_details, "Parler", () => { if (_openVoice is not null) _openVoice(); else _ = ListenAsync(); });
        var unavailable = _voice?.UnavailableReason ?? AndroidGameHelpVoice.Availability(context);
        if (unavailable is not null) Text(_details, unavailable, 12);
        _question = new EditText(context) { Hint = "Ou ajouter un détail par écrit…", InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine };
        _question.SetTextColor(Color.White); _question.SetHintTextColor(Color.Rgb(181, 200, 214)); _question.SetMaxLines(4); _question.SetMinLines(2);
        _question.SetFilters([new global::Android.Text.InputFilterLengthFilter(2000)]); _details.AddView(_question);
        _question.FocusChange += (_, args) => { if (args.HasFocus && _operation is null) _voiceMode = false; };
        Text(_details, "Puis choisis Indice ou Solution.", 12);
        Text(_details, "Questions traitées sur cet appareil. Le jeu peut continuer pendant l’aide.", 12);
        _status = Text(body, "", 12);
        body.RemoveView(_status); body.AddView(_status, 0);
        var choices = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _hint = ActionButton(choices, "Indice", () => _ = ChooseAsync(HintLevel.Hint));
        _solution = ActionButton(choices, "Solution", () => _ = ChooseAsync(HintLevel.Solution));
        _hint.LayoutParameters = new LayoutParams(0, Dp(58), 1);
        _solution.LayoutParameters = new LayoutParams(0, Dp(58), 1) { LeftMargin = Dp(8) };
        _hint.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(169, 234, 211));
        _hint.SetTextColor(Color.Rgb(16, 43, 37));
        _solution.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(40, 55, 70));
        _solution.SetTextColor(Color.White); AddView(choices);
        foreach (var (button, color) in new[] { (_hint, Color.Rgb(169, 234, 211)), (_solution, Color.Rgb(40, 55, 70)) })
        {
            var fill = new global::Android.Graphics.Drawables.GradientDrawable(); fill.SetColor(color); fill.SetCornerRadius(Dp(16));
            button.BackgroundTintList = null;
            button.Background = new global::Android.Graphics.Drawables.RippleDrawable(global::Android.Content.Res.ColorStateList.ValueOf(Color.Argb(40, 255, 255, 255)), fill, null);
        }
        _cancel = ActionButton(this, "Arrêter", Cancel);
        var reduce = ActionButton(this, compact ? "Réduire" : "Fermer l’aide", () => { Cancel(); close(); });
        reduce.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.Transparent);
        _expand = () => { _expanded = true; header.Visibility = scroll.Visibility = ViewStates.Visible; Invalidate(); expanded?.Invoke(); };
        if (compact) header.Visibility = scroll.Visibility = ViewStates.Gone;
        UpdateButtons();
    }

    internal void Cancel() { _operation?.Cancel(); _voice?.Stop(); }
    protected override void OnDetachedFromWindow() { _detached = true; Cancel(); _voice?.Dispose(); base.OnDetachedFromWindow(); }
    internal void VoiceFeedback(string message) { if (!_detached) { _status.Text = message; Mood(CompanionState.Idle); } }
    private void Mood(CompanionState state) { if (_detached) return; _companion.State = state; _stateChanged?.Invoke(state); _mood.Text = Companion.Message(state); _mood.Visibility = state == CompanionState.Idle ? ViewStates.Gone : ViewStates.Visible; }
    protected override void OnDraw(Canvas canvas)
    {
        using var paint = new Paint(PaintFlags.AntiAlias) { Color = Color.Rgb(23, 30, 43) };
        if (_expanded) canvas.DrawRoundRect(0, Dp(10), Width, Height, Dp(20), Dp(20), paint);
        else
        {
            void ChoiceTail(float x, Color color)
            {
                using var triangle = new global::Android.Graphics.Path();
                triangle.MoveTo(x - Dp(10), Dp(22)); triangle.LineTo(x, Dp(14)); triangle.LineTo(x + Dp(8), Dp(22)); triangle.Close();
                paint.Color = color; canvas.DrawPath(triangle, paint);
            }
            ChoiceTail(Width / 2f - Dp(30), Color.Rgb(169, 234, 211));
            ChoiceTail(Width - Dp(42), Color.Rgb(40, 55, 70));
            base.OnDraw(canvas); return;
        }
        using var tail = new global::Android.Graphics.Path();
        tail.MoveTo(Width - Dp(52), Dp(10)); tail.LineTo(Width - Dp(36), 0); tail.LineTo(Width - Dp(28), Dp(10)); tail.Close();
        canvas.DrawPath(tail, paint); base.OnDraw(canvas);
    }
    private async Task ChooseAsync(HintLevel level)
    {
        if (_operation is not null) return;
        _level = level;
        _expand();
        if (!Ready) { _details.Visibility = ViewStates.Visible; _status.Text = "Prépare d’abord le modèle local."; return; }
        if (_capture is null && string.IsNullOrWhiteSpace(_question.Text))
        {
            _details.Visibility = ViewStates.Visible;
            _status.Text = "Je ne vois pas encore le jeu. Précise ta situation à la voix ou à l’écrit.";
            return;
        }
        _details.Visibility = ViewStates.Gone; await RunAsync(false);
    }
    private void UpdateButtons()
    {
        _install.Visibility = Ready ? ViewStates.Gone : ViewStates.Visible;
        _install.Enabled = _operation is null;
        _question.Enabled = _hint.Enabled = _solution.Enabled = _operation is null;
        _hint.Alpha = _solution.Alpha = _operation is null ? 1 : .45f;
        _talk.Enabled = _operation is null && (_voice is not null || _openVoice is not null) && (_voice?.UnavailableReason ?? AndroidGameHelpVoice.Availability(Context!)) is null;
        _cancel.Visibility = _operation is null ? ViewStates.Gone : ViewStates.Visible;
    }
    private bool Ready => _capture is null ? _help.IsInstalled || _help.IsVisionInstalled : _help.IsVisionInstalled;
    internal async Task ListenAsync()
    {
        if (_operation is not null || _voice is null) return;
        using var operation = new CancellationTokenSource(); _operation = operation; _voiceMode = true; UpdateButtons(); Mood(CompanionState.Listening);
        _status.Text = "Micro actif pour cette question. Arrêter coupe l’écoute.";
        try { var text = await _voice.ListenAsync(operation.Token); if (!_detached) { _question.Text = text; _status.Text = "Vérifie ta question, puis demande un indice."; Mood(CompanionState.Idle); } }
        catch (OperationCanceledException) { if (!_detached) { _status.Text = "Écoute arrêtée."; Mood(CompanionState.Idle); } }
        catch (Exception error) { global::Android.Util.Log.Warn("DecompShelf.Help", error.ToString()); if (!_detached) { _status.Text = error.Message; Mood(CompanionState.Error); } }
        finally { _operation = null; if (!_detached) UpdateButtons(); }
    }
    private async Task RunAsync(bool install)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource(); _operation = operation; UpdateButtons(); Mood(CompanionState.Thinking);
        _status.Text = install ? "Téléchargement du modèle…" : "Préparation de ton indice sur cet appareil…";
        try
        {
            if (install)
            {
                var progress = new Progress<double>(value => PostUi(() => _status.Text = $"Téléchargement : {value:P0}"));
                await _help.InstallVisionAsync(progress, operation.Token);
                if (!_detached) _status.Text = "Prêt ! Tu peux demander de l’aide hors ligne.";
            }
            else
            {
                var progress = new Progress<string>(value => PostUi(() => _answer.Text = value));
                string response;
                if (_capture is not null)
                {
                    _status.Text = "Je regarde ton jeu…";
                    var frame = await _capture(operation.Token);
                    try { response = await _help.AskScreenAsync(_game, frame, _level ?? HintLevel.Hint, progress, operation.Token, _question.Text); }
                    finally { Array.Clear(frame.Rgb); }
                }
                else response = await _help.AskAsync(_game, _question.Text ?? "", _level ?? HintLevel.Hint, progress, operation.Token);
                if (!_detached)
                {
                    _answer.Text = response;
                    if (_voiceMode && _voice is not null) { Mood(CompanionState.Speaking); _status.Text = "Lecture de la réponse…"; await _voice.SpeakAsync(response, operation.Token); }
                    _status.Text = "Terminé · à toi de jouer. Je reste là si tu as besoin.";
                }
            }
            Mood(CompanionState.Ready);
        }
        catch (OperationCanceledException) { if (!_detached) { _status.Text = "Arrêté."; Mood(CompanionState.Idle); } }
        catch (Exception error) { global::Android.Util.Log.Warn("DecompShelf.Help", error.ToString()); if (!_detached) { _status.Text = error.Message; Mood(CompanionState.Error); } }
        finally { _operation = null; if (!_detached) UpdateButtons(); }
    }
    private void PostUi(Action action) => _ui.Post(() => { if (!_detached) action(); });
}
