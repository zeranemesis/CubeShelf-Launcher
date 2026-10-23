using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _desktopModToolsReady;

    private void EnsureDesktopModTools()
    {
        if (_desktopModToolsReady || ModResultCount.Parent is not Grid parent) return;
        var summary = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        parent.Children.Remove(ModResultCount);
        summary.Children.Add(ModResultCount);
        var tools = new WrapPanel();
        var profiles = new Button { Content = P7("Profils et ordre des mods", "Mod profiles and load order"), Margin = new Thickness(0, 0, 8, 0) };
        profiles.Click += OpenDesktopModProfiles;
        var original = new Button { Content = P7("Profil original — aucun mod", "Original profile — no mods") };
        original.Click += (_, _) =>
        {
            if (_selectedGame is not { } game) return;
            try
            {
                EnsureModProfileChangeAllowed(game);
                new PortableModManager(_paths, game.ModLibraryId).SetActiveSelection([]);
                RefreshGameState(); RefreshModItems(); RefreshBigPictureStatus();
                ModsStatus.Text = P7("Profil original appliqué. Aucun mod actif ; les mods installés sont conservés.", "Original profile applied. No active mods; installed mods are preserved.");
            }
            catch (Exception error) { ModsStatus.Text = error.Message; }
        };
        tools.Children.Add(profiles); tools.Children.Add(original);
        summary.Children.Add(tools); Grid.SetRow(summary, 1); parent.Children.Add(summary);
        _desktopModToolsReady = true;
    }

    private void EnsureModProfileChangeAllowed(GameCatalogEntry game)
    {
        if (RunningGameFor(game) is not null || PlayIntentFor(game) is not null)
            throw new InvalidOperationException(P7("Ferme le jeu et attends la fin de sa préparation avant de changer ses mods.",
                "Close the game and wait for preparation to finish before changing its mods."));
    }

    private async void OpenDesktopModProfiles(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is not { } game) return;
        var dialog = CreatePhase7Dialog(P7("Profils de mods · ", "Mod profiles · ") + game.Title, 760, 720);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        try
        {
            EnsureModProfileChangeAllowed(game);
            var manager = new PortableModManager(_paths, game.ModLibraryId);
            var store = new ModLoadoutStore(_paths.ConfigurationDirectory, game.ModLibraryId);
            var installed = manager.GetInstalled().ToDictionary(mod => mod.Id);
            var disabled = manager.GetPlayerDisabled();
            var order = installed.Values.Where(mod => mod.Enabled && !disabled.Contains(mod.Id))
                .OrderByDescending(mod => mod.Priority).ThenBy(mod => mod.Id).Select(mod => mod.Id).ToList();
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var name = new TextBox { MaxLength = 80, Watermark = P7("Nom du profil", "Profile name") };
            var rows = new StackPanel { Spacing = 6 };
            var available = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 200 };
            var conflicts = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var apply = new Button { Content = P7("Appliquer cette sélection", "Apply this selection") };
            var save = new Button { Content = P7("Enregistrer le profil", "Save profile") };
            var delete = new Button { Content = P7("Supprimer le profil choisi", "Delete selected profile") };
            var add = new Button { Content = P7("Ajouter", "Add") };
            var close = new Button { Content = P7("Fermer", "Close") };
            close.Click += (_, _) => dialog.Close();
            var initializing = false;
            Dictionary<int, int> Priorities() => order.Select((id, index) => (id, priority: (order.Count - index) * 10)).ToDictionary(item => item.id, item => item.priority);
            void Profiles()
            {
                initializing = true;
                picker.Items.Clear();
                picker.Items.Add(new ComboBoxItem { Content = P7("Jeu original — aucun mod", "Original game — no mods"), Tag = new ModLoadout("", []) });
                foreach (var profile in store.Load()) picker.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile });
                picker.SelectedIndex = -1;
                initializing = false;
            }
            void Render()
            {
                rows.Children.Clear();
                for (var index = 0; index < order.Count; index++)
                {
                    var at = index; var id = order[index];
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
                    row.Children.Add(new TextBlock { Text = (index + 1) + ". " + (installed.TryGetValue(id, out var mod) ? mod.Name : P7("Mod absent #", "Missing mod #") + id), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                    var up = new Button { Content = P7("Priorité +", "Priority +"), IsEnabled = index > 0 };
                    var down = new Button { Content = P7("Priorité −", "Priority −"), IsEnabled = index < order.Count - 1 };
                    var remove = new Button { Content = P7("Retirer", "Remove") };
                    up.Click += (_, _) => { (order[at - 1], order[at]) = (order[at], order[at - 1]); Render(); };
                    down.Click += (_, _) => { (order[at + 1], order[at]) = (order[at], order[at + 1]); Render(); };
                    remove.Click += (_, _) => { order.Remove(id); Render(); };
                    Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(remove, 3);
                    row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove); rows.Children.Add(row);
                }
                var choices = installed.Values.Where(mod => !order.Contains(mod.Id)).OrderBy(mod => mod.Name)
                    .Select(mod => new ComboBoxItem { Content = mod.Name, Tag = mod.Id }).ToArray();
                available.ItemsSource = choices;
                available.SelectedIndex = choices.Length > 0 ? 0 : -1;
                add.IsEnabled = choices.Length > 0;
                var collisions = manager.AnalyzeConflicts(order);
                conflicts.Text = collisions.Count == 0 ? P7("Aucun fichier en conflit dans cette sélection.", "No file conflicts in this selection.")
                    : P7($"{collisions.Count} fichier(s) en conflit. Le mod le plus haut l’emporte :\n", $"{collisions.Count} file conflict(s). The highest mod wins:\n")
                        + string.Join("\n", collisions.Take(4).Select(conflict => conflict.RelativePath + " → " + installed[order.First(conflict.ModIds.Contains)].Name));
            }
            picker.SelectionChanged += (_, _) =>
            {
                if (initializing || picker.SelectedItem is not ComboBoxItem { Tag: ModLoadout profile }) return;
                order = profile.EnabledIds.OrderByDescending(id => profile.Priorities?.GetValueOrDefault(id) ?? installed.GetValueOrDefault(id)?.Priority ?? 0).ThenBy(id => id).ToList();
                name.Text = profile.Name; Render();
            };
            add.Click += (_, _) => { if (available.SelectedItem is ComboBoxItem { Tag: int id }) { order.Add(id); Render(); } };
            apply.Click += (_, _) =>
            {
                try
                {
                    EnsureModProfileChangeAllowed(game);
                    var variants = _catalogGames.Where(item => item.DiscPatch is not null && item.ModLibraryId == game.ModLibraryId).Select(item => item.DiscPatch!.GameBananaModId).ToHashSet();
                    if (manager.AnalyzeLayout(true).Any(item => order.Contains(item.Id) && !variants.Contains(item.Id)))
                        throw new InvalidOperationException(P7("Cette sélection contient un mod au format incompatible.", "This selection contains a mod with an incompatible layout."));
                    manager.SetActiveSelection(order, Priorities());
                    RefreshGameState(); RefreshModItems(); RefreshBigPictureStatus();
                    status.Text = P7("Sélection appliquée. Les mods installés sont conservés.", "Selection applied. Installed mods are preserved.");
                }
                catch (Exception error) { status.Text = error.Message; }
            };
            save.Click += (_, _) =>
            {
                try { store.Save(name.Text ?? "", order, Priorities()); Profiles(); status.Text = P7("Profil enregistré avec son ordre de chargement.", "Profile saved with its load order."); }
                catch (Exception error) { status.Text = error.Message; }
            };
            delete.Click += (_, _) =>
            {
                if (picker.SelectedItem is not ComboBoxItem { Tag: ModLoadout { Name.Length: > 0 } profile }) return;
                try { store.Delete(profile.Name); Profiles(); status.Text = P7("Profil supprimé. Les mods actifs sont conservés.", "Profile deleted. Active mods are preserved."); }
                catch (Exception error) { status.Text = error.Message; }
            };
            Profiles(); Render();
            var content = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
            content.Children.Add(new TextBlock { Text = P7("Choisis un profil ou modifie la sélection. Rien ne change avant « Appliquer ». Le haut de la liste a priorité.",
                "Choose a profile or edit the selection. Changes take effect only after Apply. Higher entries have priority."), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(picker); content.Children.Add(name);
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; addRow.Children.Add(available); addRow.Children.Add(add);
            content.Children.Add(addRow); content.Children.Add(rows); content.Children.Add(conflicts);
            var actions = new WrapPanel(); foreach (var button in new[] { apply, save, delete, close }) { button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button); }
            content.Children.Add(actions); content.Children.Add(status);
            dialog.Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            await dialog.ShowDialog(this);
        }
        catch (Exception error) { ShowToastParity(P7("Profils de mods", "Mod profiles"), error.Message); }
    }
}
