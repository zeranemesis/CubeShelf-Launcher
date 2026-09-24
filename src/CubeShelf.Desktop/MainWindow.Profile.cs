using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// The profile: what friends see beside our name, and the avatar that goes with it.
///
/// The avatar is rescaled to 96x96 and written once to the configuration directory rather than
/// kept as the file the user picked. The presence document is rewritten on every heartbeat, so
/// an unscaled photograph would not be a one-off cost -- it would be a steady stream through
/// whatever folder the user synchronises, for as long as the launcher is open.
/// </summary>
public sealed partial class MainWindow
{
    private const int AvatarSide = 96;

    private string AvatarFile => Path.Combine(_paths.ConfigurationDirectory, "avatar.png");

    /// <summary>
    /// Read once and reused for every row, because the friends list rebuilds on every poll and
    /// decoding a PNG per friend per refresh would be paid for nothing.
    /// </summary>
    private readonly Dictionary<string, Bitmap?> _friendAvatars = new(StringComparer.Ordinal);

    private void ApplyProfilePreferences()
    {
        if (ProfileStatusBox is null) return;

        ProfileStatusBox.Text = _preferences.ProfileStatus;
        ShareProfileBox.IsChecked = _preferences.ShareProfile;
        RefreshPinnedGameChoices();
        RefreshOwnAvatar();
    }

    /// <summary>
    /// The pinned game is chosen from the shelf, never typed: a free-text field would let the
    /// user publish a title that matches nothing a friend could open.
    /// </summary>
    private void RefreshPinnedGameChoices()
    {
        if (ProfilePinnedBox is null) return;

        var none = P7("Aucun", "None");
        var titles = new List<string> { none };
        titles.AddRange(_catalogGames.Select(game => game.Title));

        var wasLoading = _loadingSettings;
        _loadingSettings = true;
        ProfilePinnedBox.ItemsSource = titles;
        var pinned = _catalogGames.FirstOrDefault(game =>
            string.Equals(game.Id, _preferences.ProfilePinnedGameId, StringComparison.OrdinalIgnoreCase));
        ProfilePinnedBox.SelectedIndex = pinned is null ? 0 : titles.IndexOf(pinned.Title);
        _loadingSettings = wasLoading;
    }

    private void RefreshOwnAvatar()
    {
        if (ProfileAvatarImage is null) return;

        var bitmap = LoadAvatar(AvatarFile);
        ProfileAvatarImage.Source = bitmap;
        // The same face in the three places it shows: the profile card, the page header and
        // the sidebar, where it stands for you the way Discord's corner does.
        ProfileHeroAvatar.Source = bitmap;
        SidebarAvatar.Source = bitmap;
        ProfileAvatarImage.IsVisible = bitmap is not null;
        ProfileAvatarEmpty.IsVisible = bitmap is null;
        ClearProfileAvatarButton.IsEnabled = bitmap is not null;
    }

    private static Bitmap? LoadAvatar(string file)
    {
        try
        {
            return File.Exists(file) ? new Bitmap(file) : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void SaveProfileText(object? sender, RoutedEventArgs args)
    {
        if (_loadingSettings) return;

        var status = (ProfileStatusBox.Text ?? "").Trim();
        if (string.Equals(status, _preferences.ProfileStatus, StringComparison.Ordinal)) return;

        _preferences = _preferences with { ProfileStatus = status };
        _preferencesStore.Save(_preferences);
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    private void SavePinnedGame(object? sender, SelectionChangedEventArgs args)
    {
        if (_loadingSettings || ProfilePinnedBox is null) return;

        // Index 0 is "None"; every other index is the game at that position on the shelf.
        var index = ProfilePinnedBox.SelectedIndex - 1;
        var id = index >= 0 && index < _catalogGames.Count ? _catalogGames[index].Id : "";
        if (string.Equals(id, _preferences.ProfilePinnedGameId, StringComparison.Ordinal)) return;

        _preferences = _preferences with { ProfilePinnedGameId = id };
        _preferencesStore.Save(_preferences);
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    private async void ChooseProfileAvatar(object? sender, RoutedEventArgs args)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = P7("Choisir un avatar", "Choose an avatar"),
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } source) return;

        try
        {
            WriteScaledAvatar(source);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            ShowToastParity(P7("Profil", "Profile"),
                P7($"Cette image n’a pas pu être lue : {exception.Message}",
                   $"That image could not be read: {exception.Message}"));
            return;
        }

        RefreshOwnAvatar();
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    /// <summary>
    /// Decodes, squares off and rescales to <see cref="AvatarSide"/>, then writes the PNG the
    /// composer will read. If the result still exceeds the cap the composer drops it rather
    /// than publishing it, which is why the cap is checked there and not only here.
    /// </summary>
    private void WriteScaledAvatar(string source)
    {
        using var original = new Bitmap(source);

        // Crop to a square first, so a wide photograph is not squashed into a circle.
        var side = Math.Min(original.PixelSize.Width, original.PixelSize.Height);
        var offsetX = (original.PixelSize.Width - side) / 2;
        var offsetY = (original.PixelSize.Height - side) / 2;

        using var target = new RenderTargetBitmap(new Avalonia.PixelSize(AvatarSide, AvatarSide));
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(
                original,
                new Avalonia.Rect(offsetX, offsetY, side, side),
                new Avalonia.Rect(0, 0, AvatarSide, AvatarSide));
        }

        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        target.Save(AvatarFile);
    }

    private void ClearProfileAvatar(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (File.Exists(AvatarFile)) File.Delete(AvatarFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowToastParity(P7("Profil", "Profile"),
                P7($"L’avatar n’a pas pu être supprimé : {exception.Message}",
                   $"The avatar could not be removed: {exception.Message}"));
            return;
        }

        RefreshOwnAvatar();
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    /// <summary>
    /// A friend's avatar, decoded from the base64 PNG in their document and cached against the
    /// bytes themselves, so a friend who changes theirs is picked up and one who does not costs
    /// nothing on the next poll.
    /// </summary>
    private Bitmap? FriendAvatar(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        if (_friendAvatars.TryGetValue(base64, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length <= PeerProfile.MaximumAvatarBytes)
            {
                using var stream = new MemoryStream(bytes);
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or NotSupportedException or IOException)
        {
            // A friend's document is data we did not write. An avatar that will not decode is
            // a friend without an avatar, not an error worth showing anyone.
            bitmap = null;
        }

        // Bounded so a peer cycling avatars cannot grow this without limit.
        if (_friendAvatars.Count > 64) _friendAvatars.Clear();
        _friendAvatars[base64] = bitmap;
        return bitmap;
    }

    /// <summary>What we publish about ourselves, or nothing if the user turned the profile off.</summary>
    private ProfileInputs CurrentProfileInputs() => new(
        _preferences.ProfileStatus,
        _preferences.ProfilePinnedGameId,
        _preferences.ProfileFirstSeenAt);
}
