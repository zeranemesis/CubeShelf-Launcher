using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private async void AddFriend(object? sender, RoutedEventArgs args)
    {
        if (_identity is null || _friends is null)
        {
            ShowToastParity(P7("Amis", "Friends"), P7("Amis indisponibles.", "Friends unavailable."));
            return;
        }

        var dialog = CreatePhase7Dialog(P7("Ajouter un ami", "Add a friend"), 620, 340);
        var codeBox = new TextBox
        {
            Watermark = "CSF1-…",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 92
        };
        var nameBox = new TextBox { Watermark = P7("Son nom", "Their name") };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var confirm = new Button
        {
            Content = P7("Ajouter", "Add"),
            IsEnabled = false,
            Classes = { "primary" }
        };

        FriendCodePayload? decoded = null;
        codeBox.TextChanged += (_, _) =>
        {
            if (FriendCode.TryDecode(codeBox.Text, out var payload, out var error))
            {
                decoded = payload;
                confirm.IsEnabled = true;
                // Naming the host is the one thing worth showing: the user is about to start
                // polling that address every couple of minutes.
                status.Text = P7($"Code valide. Adresse : {new Uri(payload!.PresenceUrl).Host}",
                                 $"Valid code. Address: {new Uri(payload!.PresenceUrl).Host}");
            }
            else
            {
                decoded = null;
                confirm.IsEnabled = false;
                status.Text = codeBox.Text is { Length: > 0 } ? error : "";
            }
        };

        confirm.Click += (_, _) =>
        {
            if (decoded is null) return;
            var chosen = string.IsNullOrWhiteSpace(nameBox.Text) ? P7("Ami", "Friend") : nameBox.Text!.Trim();
            if (!_friends.TryAdd(decoded, chosen, _identity.PublicKey, out var error))
            {
                status.Text = error;
                return;
            }
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 9,
            Children =
            {
                new TextBlock
                {
                    Text = P7("Colle le code ami qu’on t’a donné.", "Paste the friend code you were given."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                codeBox,
                nameBox,
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { confirm }
                }
            }
        };

        await dialog.ShowDialog(this);

        RefreshFriendsView();
        // The recipient list changed, so the next document has to include them.
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
    }

    private void ToggleFriendPause(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        _friends.Update(row.PublicKey, friend => friend.Paused = !friend.Paused);
        RefreshFriendsView();
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
    }

    private async void RemoveFriend(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        var dialog = CreatePhase7Dialog(P7("Retirer un ami", "Remove a friend"), 560, 260);
        var confirmed = false;
        var confirm = new Button { Content = P7("Retirer", "Remove"), Classes = { "danger" } };
        confirm.Click += (_, _) => { confirmed = true; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = P7($"Retirer {row.Name} ? Il cessera de recevoir ta présence.",
                              $"Remove {row.Name}? They will stop receiving your presence."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                // Worth saying plainly rather than letting it be discovered: removal is visible
                // to them, and it does not stop them watching the address change.
                new TextBlock
                {
                    Text = P7("Il peut le remarquer, et il garde ton adresse : il verra encore quand " +
                              "ton fichier change, donc quand tu joues. Seul un changement d’adresse y met fin.",
                              "They may notice, and they keep your address: they can still see when your " +
                              "file changes, and so when you play. Only changing the address stops that."),
                    Foreground = Avalonia.Media.Brushes.Gray,
                    FontSize = 12,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { confirm }
                }
            }
        };

        await dialog.ShowDialog(this);
        if (!confirmed) return;

        _friends.Remove(row.PublicKey);
        _friendPresence.TryRemove(row.PublicKey, out _);
        RefreshFriendsView();
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
    }

    private async void CopyFriendCode(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == row.PublicKey);
        if (friend is null) return;

        try
        {
            // Rebuilding their code is how two of your friends get introduced to each other.
            var code = FriendCode.Encode(Convert.FromBase64String(friend.PublicKey), friend.PresenceUrl);
            await CopyToClipboardAsync(code, P7($"Code de {friend.DisplayName} copié.",
                                                $"{friend.DisplayName}’s code copied."));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            ShowToastParity(P7("Amis", "Friends"),
                P7("Ce contact n’a pas d’adresse valide.", "This contact has no valid address."));
        }
    }

    private async void CopyOwnFriendCode(object? sender, RoutedEventArgs args)
    {
        if (_ownFriendCode.Length == 0) return;
        await CopyToClipboardAsync(_ownFriendCode,
            P7("Ton code ami est copié.", "Your friend code is copied."));
    }

    private async Task CopyToClipboardAsync(string text, string confirmation)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(text);
        ShowToastParity(P7("Amis", "Friends"), confirmation);
    }

    private void SavePresenceText(object? sender, RoutedEventArgs args)
    {
        if (_loadingSettings) return;

        var url = (PresenceUrlBox.Text ?? "").Trim();
        var addressChanged = !string.Equals(url, _preferences.PresenceUrl, StringComparison.Ordinal);

        _preferences = _preferences with
        {
            FriendsDisplayName = (PresenceNameBox.Text ?? "").Trim(),
            PresenceFolder = (PresenceFolderBox.Text ?? "").Trim(),
            PresenceUrl = url
        };
        _preferencesStore.Save(_preferences);

        if (addressChanged)
        {
            // The address is sealed inside every friend code already handed out. Changing it
            // silently orphans every existing friend, who keeps polling the old one forever.
            _ownFriendCode = "";
            CopyOwnCodeButton.IsEnabled = false;
            OwnFriendCodeBox.Text = "";
            PresenceStatusText.Text = P7(
                "Adresse modifiée : teste-la, puis redistribue ton code ami. Les codes déjà donnés ne fonctionnent plus.",
                "Address changed: test it, then hand out your friend code again. Codes already given no longer work.");
        }

        StartPresenceService();
        RefreshFriendsView();
    }

    private async void BrowsePresenceFolder(object? sender, RoutedEventArgs args)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = P7("Dossier synchronisé", "Synchronised folder"),
            AllowMultiple = false
        });

        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        PresenceFolderBox.Text = path;
        SavePresenceText(sender, args);
    }

    private async void RunPresenceSelfTest(object? sender, RoutedEventArgs args)
    {
        if (_identity is null || _friends is null) return;

        PresenceSelfTestButton.IsEnabled = false;
        PresenceStatusText.Text = P7("Test en cours…", "Testing…");
        try
        {
            var publisher = new SyncedFolderPresencePublisher(new SyncedFolderTarget(
                _preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName, _preferences.PresenceUrl));
            using var selfTest = new PresenceSelfTest(_identity);

            var sequence = new PresenceSequence(_paths.ConfigurationDirectory);
            var snapshot = PresenceComposer.Offline(
                _preferences.FriendsDisplayName, sequence.Next(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);

            var result = await selfTest.RunAsync(publisher, snapshot,
                PresenceRecipients.ForPublication(_identity, _friends), TimeSpan.FromMinutes(2));

            if (result.Succeeded)
            {
                _ownFriendCode = result.FriendCode;
                OwnFriendCodeBox.Text = result.FriendCode;
                CopyOwnCodeButton.IsEnabled = true;
                PresenceStatusText.Text = P7(
                    "Adresse vérifiée : ton document a été relu et déchiffré. Tu peux distribuer ton code.",
                    "Address verified: your document was read back and decrypted. You can hand out your code.");
                StartPresenceService();
            }
            else
            {
                _ownFriendCode = "";
                OwnFriendCodeBox.Text = "";
                CopyOwnCodeButton.IsEnabled = false;
                PresenceStatusText.Text = result.Error ?? P7("Le test a échoué.", "The test failed.");
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            PresenceStatusText.Text = exception.Message;
        }
        finally
        {
            PresenceSelfTestButton.IsEnabled = true;
            RefreshFriendsView();
        }
    }

    /// <summary>Loads the presence card. Called from ApplyPreferences, inside its loading guard.</summary>
    private void ApplyPresencePreferences()
    {
        PresenceNameBox.Text = _preferences.FriendsDisplayName;
        PresenceFolderBox.Text = _preferences.PresenceFolder;
        PresenceUrlBox.Text = _preferences.PresenceUrl;
        PresencePublishBox.IsChecked = _preferences.PresencePublishEnabled;
        ShareLibraryBox.IsChecked = _preferences.ShareLibrary;
        SharePlayTimeBox.IsChecked = _preferences.SharePlayTime;
        ShareCurrentGameBox.IsChecked = _preferences.ShareCurrentGame;
        ShareModsBox.IsChecked = _preferences.ShareMods;
        ApplyProfilePreferences();

        // The code stays hidden until a self-test proves the address serves a readable document.
        // Offering it earlier would be handing out a promise we have not checked.
        OwnFriendCodeBox.Text = _ownFriendCode;
        CopyOwnCodeButton.IsEnabled = _ownFriendCode.Length > 0;
    }

    /// <summary>Reads the presence card back. Called from SaveSettings.</summary>
    private void SavePresencePreferences()
    {
        _preferences = _preferences with
        {
            PresencePublishEnabled = PresencePublishBox.IsChecked == true,
            ShareLibrary = ShareLibraryBox.IsChecked == true,
            SharePlayTime = SharePlayTimeBox.IsChecked == true,
            ShareCurrentGame = ShareCurrentGameBox.IsChecked == true,
            ShareMods = ShareModsBox.IsChecked == true,
            ShareProfile = ShareProfileBox.IsChecked == true
        };
    }
}
