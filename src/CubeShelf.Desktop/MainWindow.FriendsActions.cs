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

        if (!HasIdentity)
        {
            ParityShowProfile(sender, args);
            return;
        }

        var dialog = CreatePhase7Dialog(P7("Ajouter un ami", "Add a friend"), 620, 360);
        var codeBox = new TextBox
        {
            Watermark = P7("Colle ici le message ou le code de ton ami", "Paste your friend’s message or code here"),
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
            // A whole chat message is fine: the code is found inside it. Only when nothing is
            // found is the text judged as a bare code, so the error says what is wrong with it.
            FriendCodePayload? payload = null;
            var error = "";
            var ok = FriendCode.TryFind(codeBox.Text, out payload) ||
                     FriendCode.TryDecode(codeBox.Text, out payload, out error);
            if (ok && payload is not null)
            {
                decoded = payload;
                confirm.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(nameBox.Text) && payload.DisplayName.Length > 0)
                    nameBox.Text = payload.DisplayName;
                // Who, and where: the user is about to read them every couple of minutes.
                status.Text = P7($"Tu vas ajouter {payload.Handle}. {DescribeCodeSource(payload, french: true)}",
                                 $"You are adding {payload.Handle}. {DescribeCodeSource(payload, french: false)}");
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

            // Same window, next step: friendship goes one way at a time, and the moment they were
            // added is the moment to send our own code back -- the network carries a request on
            // its own, the message is for when it cannot.
            AfterAddingFromCode(decoded);
            dialog.Content = ReplyStep(decoded.Handle, () => dialog.Close());
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 9,
            Children =
            {
                new TextBlock
                {
                    Text = P7("Colle le message ou le code que ton ami t’a envoyé.",
                              "Paste the message or code your friend sent you."),
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

        // Opened with a code already in the clipboard, the dialog starts filled in.
        if (_clipboardCandidate is { } candidate)
            codeBox.Text = string.IsNullOrEmpty(candidate.PresenceUrl)
                ? FriendCode.EncodeForNetwork(candidate.PublicKey, candidate.DisplayName, candidate.Seeds)
                : FriendCode.Encode(candidate.PublicKey, candidate.PresenceUrl, candidate.DisplayName);

        await dialog.ShowDialog(this);

        _ = CheckClipboardForFriendCodeAsync();
        RefreshFriendsView();
        // The recipient list changed, so the next document has to include them.
        OnFriendsListChanged();
        // And read them now rather than in two minutes: whether they already added us back is
        // the first thing the list should say -- and keep reading actively for a while, since
        // that is usually the moment they do.
        _ = RefreshFriendsSilentlyAsync();
        _presence?.PollEagerly();
    }

    private void ToggleFriendPause(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        _friends.Update(row.PublicKey, friend => friend.Paused = !friend.Paused);
        RefreshFriendsView();
        OnFriendsListChanged();
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
                    Text = string.IsNullOrWhiteSpace(_preferences.PresenceUrl)
                        ? P7("Il peut le remarquer. Jusqu’à minuit (UTC), il pourra encore voir que ta présence change, sans pouvoir la lire ; " +
                             "ensuite il perd ta trace sur le réseau CubeShelf.",
                             "They may notice. Until midnight (UTC) they can still see your presence change, without being able to read it; " +
                             "after that they lose track of you on the CubeShelf network.")
                        : P7("Il peut le remarquer, et il garde ton adresse : il verra encore quand " +
                             "ton fichier change, donc quand tu joues. Pour y mettre fin, change d’adresse (Mon profil) : " +
                             "tes autres amis te suivront tout seuls, lui perdra ta trace.",
                             "They may notice, and they keep your address: they can still see when your " +
                             "file changes, and so when you play. To end that, change your address (My profile): " +
                             "your other friends follow on their own, they lose track of you."),
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
        _messages?.Forget(row.PublicKey);
        RefreshFriendsView();
        OnFriendsListChanged();
    }

    /// <summary>Where a pasted code says its author publishes, in words: an https host, or the network.</summary>
    private static string DescribeCodeSource(FriendCodePayload payload, bool french)
    {
        if (Uri.TryCreate(payload.PresenceUrl, UriKind.Absolute, out var uri))
            return french ? $"Adresse : {uri.Host}" : $"Address: {uri.Host}";
        return french ? "Par le réseau CubeShelf." : "Through the CubeShelf network.";
    }

    private async void CopyFriendCode(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == row.PublicKey);
        if (friend is null) return;

        try
        {
            // Rebuilding their code is how two of your friends get introduced to each other.
            var code = FriendCodeOf(friend);
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

        // A message rather than the bare code: it reads as something in a chat window, and it
        // tells the friend what to do with it. The code inside is found on their side.
        await CopyToClipboardAsync(ShareMessage(),
            P7("Message copié : colle-le dans ta conversation avec ton ami.",
               "Message copied: paste it into your chat with your friend."));
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

        // What is in the box is what the service gave, not necessarily what serves the file: it
        // is read, recognised and turned into guesses, unless it is the address already proven.
        var pasted = (PresenceUrlBox.Text ?? "").Trim();
        var url = string.Equals(pasted, _preferences.PresenceUrl, StringComparison.Ordinal)
            ? pasted
            : InterpretPastedLink(pasted);
        var folder = (PresenceFolderBox.Text ?? "").Trim();
        var addressChanged = !string.Equals(url, _preferences.PresenceUrl, StringComparison.Ordinal);
        var folderChanged = !string.Equals(folder, _preferences.PresenceFolder, StringComparison.Ordinal);

        // Leaving a pair that works: remember it, so that once the new one is proven the old file
        // can tell friends where we went. Only the first pair left behind counts -- an address
        // tried and abandoned on the way never reached anyone.
        var leavingVerified = (addressChanged || folderChanged) && IsAddressVerified() &&
                              _preferences.PreviousPresenceUrl.Length == 0;

        _preferences = _preferences with
        {
            PresenceFolder = folder,
            PresenceUrl = url,
            // The proof was of this folder served at this address. Either one moving voids it.
            PresenceVerifiedUrl = addressChanged || folderChanged ? "" : _preferences.PresenceVerifiedUrl,
            PreviousPresenceFolder = leavingVerified ? _preferences.PresenceFolder : _preferences.PreviousPresenceFolder,
            PreviousPresenceUrl = leavingVerified ? _preferences.PresenceUrl : _preferences.PreviousPresenceUrl
        };
        _preferencesStore.Save(_preferences);
        RefreshOwnFriendCode();

        if (addressChanged)
        {
            // Friends follow on their own once the new address is proven: the old file is left a
            // last document pointing at it. Codes handed to people not yet friends still name the old one.
            PresenceStatusText.Text = P7(
                "Adresse modifiée : dès qu’elle sera vérifiée, l’ancien fichier indiquera la nouvelle à tes amis, qui suivront tout seuls. Laisse l’ancien fichier en place quelques semaines. Les codes donnés à des gens pas encore amis pointent encore vers l’ancienne.",
                "Address changed: once it is verified, the old file points your friends at the new one and they follow on their own. Leave the old file in place for a few weeks. Codes given to people not yet friends still point at the old one.");
        }

        StartPresenceService();
        RefreshFriendsView();
        RefreshShareSteps();

        // A new link is tested straight away: the user just did the one thing they had to do,
        // and the answer -- does it work -- should not wait for a second click.
        if (addressChanged && url.Length > 0 && HasIdentity && Directory.Exists(folder))
            RunPresenceSelfTest(sender, args);
    }

    /// <summary>
    /// With a new address proven, the old file -- if it is somewhere else -- gets its last
    /// document: offline, pointing at the new address, for the current friends only.
    /// </summary>
    private void LeaveForwardingAddress(string newAddress)
    {
        var oldFolder = _preferences.PreviousPresenceFolder;
        var oldUrl = _preferences.PreviousPresenceUrl;
        _preferences = _preferences with { PreviousPresenceFolder = "", PreviousPresenceUrl = "" };
        _preferencesStore.Save(_preferences);

        if (oldUrl.Length == 0 || string.Equals(oldUrl, newAddress, StringComparison.Ordinal)) return;
        if (_identity is null || _friends is null) return;

        // Same folder, new link: every document there already states the new address.
        if (string.Equals(Path.GetFullPath(oldFolder), Path.GetFullPath(_preferences.PresenceFolder), StringComparison.OrdinalIgnoreCase))
            return;

        if (!Directory.Exists(oldFolder))
        {
            ShowToastParity(P7("Ta présence", "Your presence"),
                P7("L’ancien dossier n’existe plus : tes amis ne peuvent pas être prévenus de ta nouvelle adresse. Envoie-leur ton nouveau code.",
                   "The old folder is gone: your friends cannot be told about your new address. Send them your new code."));
            return;
        }

        if (PresenceAddressMove.WriteMovedDocument(_identity, _friends, new PresenceSequence(_paths.ConfigurationDirectory),
                _preferences.FriendsDisplayName, oldFolder, newAddress, out var error))
            ShowToastParity(P7("Ta présence", "Your presence"),
                P7("Nouvelle adresse en place. L’ancien fichier l’indique à tes amis actuels, qui suivront tout seuls : garde-le quelques semaines.",
                   "New address in place. The old file tells your current friends, who follow on their own: keep it a few weeks."));
        else
            ShowToastParity(P7("Ta présence", "Your presence"), error);
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
        if (!HasIdentity)
        {
            ParityShowProfile(sender, args);
            return;
        }

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

            // The guesses made from the pasted link, or from the stored address when the test is
            // run again by hand: a link stored before conversion existed gets converted too.
            var candidates = _pendingCandidates ??
                (ShareLink.TryConvert(_preferences.PresenceUrl, SyncedFolderTarget.DefaultFileName, out var stored, out _)
                    ? stored!.Candidates
                    : null);

            var result = await selfTest.RunAsync(publisher, snapshot,
                PresenceRecipients.ForPublication(_identity, _friends), TimeSpan.FromMinutes(2), candidates);

            if (result.Succeeded)
            {
                // Remembered, so the code is still there after a restart. The address kept is the
                // guess that worked, which may not be the one tried first.
                _preferences = _preferences with
                {
                    PresenceUrl = result.PresenceUrl,
                    PresenceVerifiedUrl = result.PresenceUrl
                };
                _preferencesStore.Save(_preferences);
                _pendingCandidates = null;
                PresenceUrlBox.Text = result.PresenceUrl;
                PresenceUrlConversionText.IsVisible = false;
                LeaveForwardingAddress(result.PresenceUrl);
                RefreshShareSteps();
                RefreshOwnFriendCode();
                PresenceStatusText.Text = P7(
                    "Adresse vérifiée : ton document a été relu et déchiffré. Tu peux distribuer ton code.",
                    "Address verified: your document was read back and decrypted. You can hand out your code.");
                StartPresenceService();
            }
            else
            {
                _preferences = _preferences with { PresenceVerifiedUrl = "" };
                _preferencesStore.Save(_preferences);
                RefreshOwnFriendCode();
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
        LanVisibleBox.IsChecked = _preferences.LanVisible;
        MeshEnabledBox.IsChecked = _preferences.MeshEnabled;
        MeshMapPortBox.IsChecked = _preferences.MeshMapPort;
        // The folder card is for whoever still has one set up; nobody else is shown a cloud.
        LegacyPresenceCard.IsVisible = _preferences.PresenceFolder.Length > 0 || _preferences.PresenceUrl.Length > 0;
        CloseToTrayBox.IsChecked = _preferences.CloseToTray;
        NotifyOnlineBox.IsChecked = _preferences.NotifyFriendsOnline;
        AutoAwayBox.IsChecked = _preferences.AutoAway;
        ApplyProfilePreferences();

        // The code stays hidden until a self-test proves the address serves a readable document.
        // Offering it earlier would be handing out a promise we have not checked.
        RefreshOwnFriendCode();
    }

    /// <summary>Reads the presence card back. Called from SaveSettings.</summary>
    private void SavePresencePreferences()
    {
        var before = _preferences;
        _preferences = _preferences with
        {
            PresencePublishEnabled = PresencePublishBox.IsChecked == true,
            ShareLibrary = ShareLibraryBox.IsChecked == true,
            SharePlayTime = SharePlayTimeBox.IsChecked == true,
            ShareCurrentGame = ShareCurrentGameBox.IsChecked == true,
            ShareMods = ShareModsBox.IsChecked == true,
            ShareProfile = ShareProfileBox.IsChecked == true,
            LanVisible = LanVisibleBox.IsChecked == true,
            MeshEnabled = MeshEnabledBox.IsChecked == true,
            MeshMapPort = MeshMapPortBox.IsChecked == true,
            CloseToTray = CloseToTrayBox.IsChecked == true,
            NotifyFriendsOnline = NotifyOnlineBox.IsChecked == true,
            AutoAway = AutoAwayBox.IsChecked == true
        };

        // Ticking "publish" has to start publishing now. In 0.9.0 nothing restarted the service,
        // so the box did nothing until the next launch -- a friend could add you and see nobody.
        // The local network decides whether there is anything to publish at all without a folder.
        if (before.MeshEnabled != _preferences.MeshEnabled || before.MeshMapPort != _preferences.MeshMapPort)
            RestartMesh();
        else if (before.PresencePublishEnabled != _preferences.PresencePublishEnabled ||
            before.LanVisible != _preferences.LanVisible)
            StartPresenceService();
        else if (before != _preferences)
            _presence?.RequestPublish(PresencePublishReason.ProfileChanged);

        RefreshOwnFriendCode();
        if (FriendsView.IsVisible) RefreshFriendsView();
    }
}
