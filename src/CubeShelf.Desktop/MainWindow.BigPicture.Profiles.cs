using Avalonia.Controls;
using Avalonia.Interactivity;
using CubeShelf.Core.Mods;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private void RefreshBigPictureProfiles()
    {
        if (_bigPictureModsGame is not { } game) return;
        var previous = (BigPictureProfilePicker.SelectedItem as ComboBoxItem)?.Tag as ModLoadout;
        BigPictureProfilePicker.Items.Clear();
        BigPictureProfilePicker.Items.Add(new ComboBoxItem { Content = P7("Jeu original — aucun mod", "Original game — no mods"), Tag = new ModLoadout("", []) });
        try
        {
            foreach (var profile in new ModLoadoutStore(_paths.ConfigurationDirectory, game.ModLibraryId).Load())
                BigPictureProfilePicker.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile });
        }
        catch (Exception exception) { BigPictureProfileStatus.Text = exception.Message; }
        BigPictureProfilePicker.SelectedIndex = 0;
        if (previous is not null)
            BigPictureProfilePicker.SelectedItem = BigPictureProfilePicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals((item.Tag as ModLoadout)?.Name, previous.Name, StringComparison.OrdinalIgnoreCase)) ?? BigPictureProfilePicker.Items[0];
        BigPictureApplyProfileButton.Content = P7("Appliquer", "Apply");
        BigPictureSaveProfileButton.Content = P7("Enregistrer la sélection", "Save selection");
        BigPictureDeleteProfileButton.Content = P7("Supprimer le profil", "Delete profile");
        BigPictureProfileNameBox.Watermark = P7("Mes plateaux / Entre amis…", "My boards / With friends…");
    }

    private void ApplyBigPictureProfile(object? sender, RoutedEventArgs args)
    {
        if (_bigPictureModsGame is not { } game || BigPictureProfilePicker.SelectedItem is not ComboBoxItem { Tag: ModLoadout profile }) return;
        try
        {
            if (RunningGameFor(game) is not null || PlayIntentFor(game) is not null)
            {
                BigPictureProfileStatus.Text = P7("Ferme le jeu et attends la fin de sa préparation avant de changer de profil.",
                    "Close the game and wait for preparation to finish before changing profiles.");
                return;
            }
            var manager = new PortableModManager(_paths, game.ModLibraryId);
            var incompatible = manager.AnalyzeLayout(includeDisabled: true).Select(item => item.Id).Intersect(profile.EnabledIds).ToArray();
            // A known game variant's disc patch is intentional, unlike an arbitrary unsupported pack.
            var variants = _catalogGames.Where(item => item.DiscPatch is not null).Select(item => item.DiscPatch!.GameBananaModId).ToHashSet();
            if (incompatible.Any(id => !variants.Contains(id))) throw new InvalidOperationException(P7("Ce profil contient un mod incompatible avec ce moteur.", "This profile contains a mod incompatible with this runtime."));
            manager.SetActiveSelection(profile.EnabledIds, profile.Priorities);
            RefreshBigPictureModChoices();
            RefreshBigPictureStatus();
            BigPictureProfileStatus.Text = P7("Profil appliqué : ", "Profile applied: ") + (profile.Name.Length == 0 ? P7("Jeu original", "Original game") : profile.Name);
        }
        catch (Exception exception) { BigPictureProfileStatus.Text = exception.Message; }
    }

    private void SaveBigPictureProfile(object? sender, RoutedEventArgs args)
    {
        if (_bigPictureModsGame is not { } game) return;
        try
        {
            var manager = new PortableModManager(_paths, game.ModLibraryId);
            var disabled = manager.GetPlayerDisabled();
            var name = BigPictureProfileNameBox.Text ?? "";
            new ModLoadoutStore(_paths.ConfigurationDirectory, game.ModLibraryId).SaveSelection(name, manager.GetInstalled(), disabled);
            RefreshBigPictureProfiles();
            BigPictureProfilePicker.SelectedItem = BigPictureProfilePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (item.Tag as ModLoadout)?.Name == name.Trim());
            BigPictureProfileStatus.Text = P7("Profil enregistré avec l’ordre de chargement.", "Profile saved with its load order.");
        }
        catch (Exception exception) { BigPictureProfileStatus.Text = exception.Message; }
    }

    private void DeleteBigPictureProfile(object? sender, RoutedEventArgs args)
    {
        if (_bigPictureModsGame is not { } game || BigPictureProfilePicker.SelectedItem is not ComboBoxItem { Tag: ModLoadout { Name.Length: > 0 } profile }) return;
        try
        {
            new ModLoadoutStore(_paths.ConfigurationDirectory, game.ModLibraryId).Delete(profile.Name);
            RefreshBigPictureProfiles();
            BigPictureProfileStatus.Text = P7("Profil supprimé. Les mods actifs sont conservés.", "Profile deleted. Active mods are preserved.");
        }
        catch (Exception exception) { BigPictureProfileStatus.Text = exception.Message; }
    }
}
