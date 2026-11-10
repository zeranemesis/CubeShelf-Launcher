using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private void FocusModGrid()
    {
        if (!ModsView.IsVisible) return;
        if (ModsList.ItemCount == 0) { GameBananaModsTab.Focus(); return; }
        if (ModsList.SelectedIndex < 0) ModsList.SelectedIndex = 0;
        ModsList.Focus();
        SelectedModCard()?.BringIntoView();
    }

    private ListBoxItem? SelectedModCard() => ModsList.GetVisualDescendants().OfType<ListBoxItem>()
        .FirstOrDefault(card => card.IsSelected);

    private Control[] ModDetailActions() => ModDetailsPanel.GetVisualDescendants().OfType<Control>()
        .Where(control => control is Button or TextBox && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
            && control.Focusable).ToArray();

    private void FocusModDetails()
    {
        var primary = _selectedMod?.Installed == true ? ToggleModButton : InstallModButton;
        Control target = primary.IsEffectivelyEnabled ? primary : ModDetailsPanel;
        target.Focus();
        if (ReferenceEquals(target, ModDetailsPanel)) ModTitle.BringIntoView();
        else target.BringIntoView();
    }

    private void UpdateModControllerHelp()
    {
        ModsView.Classes.Set("controller-mods", _bigPictureActive);
        ModsControllerHint.IsVisible = _bigPictureActive;
        var action = _selectedMod?.Installed == true
            ? _selectedMod.Enabled ? P7("X Désactiver", "X Disable") : P7("X Activer", "X Enable")
            : P7("X Installer", "X Install");
        ModsControllerHelp.Text = P7("LB / RB Onglets   ·   Croix / stick Parcourir   ·   A Détails / Valider   ·   ",
            "LB / RB Tabs   ·   D-pad / stick Browse   ·   A Details / Select   ·   ") + action
            + P7("   ·   Y Rechercher   ·   B Retour", "   ·   Y Search   ·   B Back");
    }

    private bool HandleModControllerInput(int input)
    {
        if (!_bigPictureActive || !ModsView.IsVisible) return false;
        var focused = FocusManager?.GetFocusedElement() as Control;
        var inGrid = ModsList.IsKeyboardFocusWithin || ReferenceEquals(focused, ModsList);
        if (input is 9 or 10)
        {
            ModsTabs.SelectedIndex = input == 9 ? 0 : 1;
            FocusModGrid(); return true;
        }
        if (input == 8) { OpenBigPictureKeyboard(ModSearchBox); return true; }
        if (input == 6)
        {
            if (!inGrid && ModsList.ItemCount > 0) FocusModGrid();
            else ReturnToBigPicture(this, new RoutedEventArgs());
            return true;
        }
        if (input == 7)
        {
            if (_selectedMod is null) return true;
            var action = _selectedMod.Installed ? ToggleModButton : InstallModButton;
            if (action.IsEffectivelyEnabled) action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }
        if (input == 5)
        {
            if (inGrid) FocusModDetails();
            else if (focused is TabItem tab) { tab.IsSelected = true; FocusModGrid(); }
            else if (focused is TextBox text) OpenBigPictureKeyboard(text);
            else if (focused is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (focused is ComboBox combo && combo.ItemCount > 0) combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.ItemCount;
            else FocusModGrid();
            return true;
        }
        if (input is < 1 or > 4) return false;
        if (inGrid)
        {
            var card = SelectedModCard();
            var candidates = ModsList.GetVisualDescendants().OfType<ListBoxItem>().Cast<Control>().ToArray();
            if (FindInDirection(card, candidates, input, grid: true) is ListBoxItem next)
            {
                ModsList.SelectedItem = next.DataContext;
                next.BringIntoView();
            }
            else if (input == 2) FocusModDetails();
            else if (input == 3) { ModSearchBox.Focus(); ModSearchBox.BringIntoView(); }
            return true;
        }
        if (focused is ComboBox filter && input is 1 or 2 && filter.ItemCount > 0)
        {
            filter.SelectedIndex = (filter.SelectedIndex + (input == 1 ? -1 : 1) + filter.ItemCount) % filter.ItemCount;
            return true;
        }
        if (focused is TabItem && input is 1 or 2)
        {
            ModsTabs.SelectedIndex = input == 1 ? 0 : 1;
            (input == 1 ? InstalledModsTab : GameBananaModsTab).Focus(); return true;
        }
        if (input == 4 && focused is TabItem or TextBox && focused?.GetVisualAncestors().Contains(ModDetailsPanel) != true)
        { FocusModGrid(); return true; }
        var inDetails = ReferenceEquals(focused, ModDetailsPanel) || focused?.GetVisualAncestors().Contains(ModDetailsPanel) == true;
        var controls = inDetails ? ModDetailActions() : BigPicturePageControls();
        var target = FindInDirection(focused, controls, input);
        if (target is not null) { target.Focus(); target.BringIntoView(); }
        else if (inDetails && input == 1 || input == 4 && focused is TabItem or TextBox) FocusModGrid();
        return true;
    }

    private Control? FindInDirection(Control? current, IEnumerable<Control> controls, int direction, bool grid = false)
    {
        if (current?.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), ModsView) is not { } origin)
            return null;
        return controls.Where(control => !ReferenceEquals(control, current) && control.IsEffectivelyVisible)
            .Select(control => (Control: control, Center: control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ModsView)))
            .Where(item => item.Center is not null)
            .Select(item => (item.Control, Delta: item.Center!.Value - origin))
            .Select(item => (item.Control, Along: direction switch { 1 => -item.Delta.X, 2 => item.Delta.X, 3 => -item.Delta.Y, _ => item.Delta.Y },
                Across: Math.Abs(direction is 1 or 2 ? item.Delta.Y : item.Delta.X)))
            .Where(item => item.Along > 4 && item.Across <= (grid
                ? direction is 1 or 2 ? current.Bounds.Height / 2 : current.Bounds.Width / 2
                : item.Along * 1.2))
            .OrderBy(item => item.Along + item.Across * 3).Select(item => item.Control).FirstOrDefault();
    }
}
