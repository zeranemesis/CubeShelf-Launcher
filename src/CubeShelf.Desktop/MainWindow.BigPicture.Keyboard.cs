using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private Window? _bigPictureKeyboardWindow;
    private readonly List<Button> _bigPictureKeyboardKeys = new();
    private int _bigPictureKeyboardIndex;
    private TextBox? _bigPictureLastTextBox;

    private void InitializeBigPictureTextFocus()
    {
        AddHandler(GotFocusEvent, (_, args) =>
        {
            if (_bigPictureActive && args.Source is Control focused) _bigPictureLastFocusedControl = focused;
            if (args.Source is TextBox text && !text.IsReadOnly) _bigPictureLastTextBox = text;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OpenFocusedBigPictureKeyboard(object? sender, RoutedEventArgs args)
    {
        var fields = BigPicturePageControls().OfType<TextBox>().Where(text => !text.IsReadOnly).ToArray();
        var target = fields.FirstOrDefault(text => text.IsKeyboardFocusWithin)
            ?? fields.FirstOrDefault(text => ReferenceEquals(text, _bigPictureLastTextBox))
            ?? fields.FirstOrDefault();
        if (target is not null) OpenBigPictureKeyboard(target);
    }

    private Window BigPictureInputWindow()
    {
        Window current = this;
        while (current.OwnedWindows.OfType<Window>().LastOrDefault(window => window.IsVisible) is { } owned) current = owned;
        return current;
    }

    private Control[] BigPicturePageControls()
    {
        var window = BigPictureInputWindow();
        var root = window == this ? ContentViews.FirstOrDefault(view => view.IsVisible) : window;
        if (root is null) return [];
        var controls = root.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Focusable
                && control is Button or TextBox or ComboBox or ListBox or Slider)
            .Where(control => control is not TextBox { IsReadOnly: true }
                && !control.GetVisualAncestors().Any(ancestor => ancestor is ComboBox or Slider)).ToList();
        if (window == this && BigPictureReturnButton.IsEffectivelyVisible)
        {
            if (BigPictureKeyboardButton.IsEffectivelyEnabled) controls.Add(BigPictureKeyboardButton);
            if (BigPictureReturnButton.IsEffectivelyEnabled) controls.Add(BigPictureReturnButton);
        }
        return controls.ToArray();
    }

    private async void OpenBigPictureKeyboard(TextBox target)
    {
        if (!_bigPictureActive || _bigPictureKeyboardWindow is not null || target.IsReadOnly
            || !target.IsEffectivelyEnabled || !target.IsEffectivelyVisible) return;
        var owner = TopLevel.GetTopLevel(target) as Window ?? this;
        var dialog = new Window
        {
            Title = P7("Clavier à l’écran", "On-screen keyboard"), Width = 1000, Height = 570,
            MinWidth = 760, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _bigPictureKeyboardWindow = dialog;
        var preview = new TextBox
        {
            Text = target.Text ?? "", FontSize = 24, IsReadOnly = true, Focusable = false,
            PasswordChar = target.PasswordChar, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 90
        };
        var mode = 0;
        var letters = new UniformGrid { Columns = 10, Rows = 5 };
        var keyLetters = new List<(Button Button, string Value)>();
        _bigPictureKeyboardKeys.Clear();
        void Append(string text)
        {
            var limit = target.MaxLength > 0 ? target.MaxLength : 4096;
            if ((preview.Text?.Length ?? 0) + text.Length <= limit) { preview.Text += text; preview.CaretIndex = preview.Text?.Length ?? 0; }
        }
        foreach (var character in "AZERTYUIOPQSDFGHJKLMWXCVBN0123456789@._-/:!?éèàçù")
        {
            var value = character.ToString();
            var key = new Button
            {
                Content = value.ToUpperInvariant(), FontSize = 23, Margin = new Thickness(4), MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            key.Click += (_, _) => Append(key.Content?.ToString() ?? "");
            letters.Children.Add(key); _bigPictureKeyboardKeys.Add(key); keyLetters.Add((key, value));
        }
        // Complete the last row so vertical navigation remains a regular ten-column grid.
        while (_bigPictureKeyboardKeys.Count < 50)
        {
            var key = new Button { Content = ",", FontSize = 23, Margin = new Thickness(4), MinHeight = 48 };
            key.Click += (_, _) => Append(","); letters.Children.Add(key); _bigPictureKeyboardKeys.Add(key);
        }
        var actions = new UniformGrid { Columns = 5 };
        void Action(string label, Action action)
        {
            var key = new Button { Content = label, FontSize = 16, Margin = new Thickness(4), MinHeight = 52,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            key.Click += (_, _) => action(); actions.Children.Add(key); _bigPictureKeyboardKeys.Add(key);
        }
        Action("ABC / abc / #+=", () =>
        {
            mode = (mode + 1) % 3;
            const string symbols = "0123456789!@#$%^&*()_+-=[]{}\\|;:'\",.<>/?`~éèàçù€£";
            for (var index = 0; index < keyLetters.Count; index++)
            {
                var pair = keyLetters[index];
                pair.Button.Content = mode == 2 ? symbols[index % symbols.Length].ToString()
                    : mode == 0 ? pair.Value.ToUpperInvariant() : pair.Value.ToLowerInvariant();
            }
        });
        Action(P7("Espace", "Space"), () => Append(" "));
        Action("⌫", () =>
        {
            if (preview.Text is not { Length: > 0 } text) return;
            // Remove a complete Unicode character when existing text ends in a surrogate pair.
            var count = text.Length >= 2 && char.IsSurrogatePair(text, text.Length - 2) ? 2 : 1;
            preview.Text = text[..^count];
            preview.CaretIndex = preview.Text.Length;
        });
        Action(P7("Annuler", "Cancel"), () => dialog.Close());
        Action(P7("Valider", "Done"), () =>
        {
            target.Text = preview.Text;
            if (ReferenceEquals(target, ModSearchBox)) ApplyModFilters();
            dialog.Close();
        });
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(preview); panel.Children.Add(letters); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = P7("Croix / stick : choisir · A : saisir · B : annuler", "D-pad / stick: choose · A: type · B: cancel"), FontSize = 16 });
        dialog.Content = new ScrollViewer { Content = panel };
        dialog.KeyDown += (_, args) =>
        {
            var input = args.Key switch { Key.Left => 1, Key.Right => 2, Key.Up => 3, Key.Down => 4, Key.Enter => 5, Key.Escape => 6, _ => 0 };
            if (input != 0) { HandleBigPictureKeyboardInput(input); args.Handled = true; }
        };
        dialog.Opened += (_, _) => { _bigPictureKeyboardIndex = 0; _bigPictureKeyboardKeys[0].Focus(); };
        try { await dialog.ShowDialog(owner); }
        finally
        {
            _bigPictureKeyboardWindow = null; _bigPictureKeyboardKeys.Clear();
            if (target.IsEffectivelyVisible && target.IsEffectivelyEnabled) target.Focus();
            _bigPictureLastInput = -1;
        }
    }

    private void HandleBigPictureKeyboardInput(int input)
    {
        if (_bigPictureKeyboardWindow is not { } window || _bigPictureKeyboardKeys.Count == 0) return;
        if (input == 6) { window.Close(); return; }
        var focused = _bigPictureKeyboardKeys.FindIndex(button => button.IsFocused);
        if (focused >= 0) _bigPictureKeyboardIndex = focused;
        if (input == 5)
        {
            _bigPictureKeyboardKeys[_bigPictureKeyboardIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        var index = _bigPictureKeyboardIndex;
        if (input == 1) index--;
        if (input == 2) index++;
        if (input == 3) index = index >= 50 ? 40 + (index - 50) * 2
            : index < 10 ? 50 + index / 2 : index - 10;
        if (input == 4) index = index >= 50 ? (index - 50) * 2
            : index >= 40 ? 50 + (index - 40) / 2 : index + 10;
        _bigPictureKeyboardIndex = (index % _bigPictureKeyboardKeys.Count + _bigPictureKeyboardKeys.Count) % _bigPictureKeyboardKeys.Count;
        _bigPictureKeyboardKeys[_bigPictureKeyboardIndex].Focus();
        _bigPictureKeyboardKeys[_bigPictureKeyboardIndex].BringIntoView();
    }
}
