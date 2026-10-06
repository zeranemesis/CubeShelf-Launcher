using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Mesh;

namespace CubeShelf.Desktop;

/// <summary>
/// The profile page: who you are to your friends, and how they get your code.
///
/// A pseudo is required for anything friends-related -- adding someone, publishing, handing out
/// a code -- and for nothing else. Playing alone never asks for one.
///
/// The number after the pseudo is read off the key (<see cref="PeerName.Tag"/>). It lets two
/// Zeras be told apart; it does not let anyone be found, because there is no directory to look a
/// tag up in. The friend code is still what travels, so this page's job is to make handing it
/// over painless: one button copies a message ready to paste in any chat, and the other side's
/// Friends page finds the code in the clipboard on its own.
/// </summary>
public sealed partial class MainWindow
{
    private FriendCodePayload? _clipboardCandidate;

    /// <summary>Codes the user already dismissed from the banner, so it does not keep coming back.</summary>
    private readonly HashSet<string> _ignoredClipboardKeys = new(StringComparer.Ordinal);

    private bool HasIdentity => PeerName.TryNormalize(_preferences.FriendsDisplayName, out _, out _);

    /// <summary><c>Zera#4821</c>. Without a key there is no tag to show, only the pseudo.</summary>
    private string OwnHandle() =>
        _identity is null
            ? PeerName.Sanitize(_preferences.FriendsDisplayName)
            : PeerName.Handle(_preferences.FriendsDisplayName, _identity.PublicKey);

    private void ParityShowProfile(object? sender, RoutedEventArgs args)
    {
        ShowParityView(ProfileView);
        RefreshIdentityUi();
        RefreshDetectedFolders();
        RefreshAddressHealthUi();
        if (!HasIdentity) IdentityNameBox.Focus();
    }

    private void RefreshIdentityUi()
    {
        if (ProfileHandleText is null) return;

        var has = HasIdentity;
        ProfileHandleText.Text = has ? OwnHandle() : P7("Pas encore d’identité", "No identity yet");
        SidebarHandleText.Text = has ? OwnHandle() : P7("Créer mon identité", "Create my identity");
        IdentityGate.IsVisible = !has;
        IdentityBody.IsVisible = has;

        if (_identity is null)
        {
            // A corrupt identity.key: say so here rather than offer a form that cannot work.
            IdentityPreviewText.Text = _friendsFailure.Length > 0
                ? P7($"Amis indisponibles : {_friendsFailure}", $"Friends unavailable: {_friendsFailure}")
                : P7("Amis indisponibles.", "Friends unavailable.");
            CreateIdentityButton.IsEnabled = false;
        }

        RefreshOwnAvatar();
        RefreshOwnFriendCode();
        RefreshBackupHint();
        RefreshMeshUi();
    }

    private void PreviewIdentity(object? sender, TextChangedEventArgs args)
    {
        if (_identity is null) return;

        if (PeerName.TryNormalize(IdentityNameBox.Text, out var name, out var error))
        {
            IdentityPreviewText.Text = P7($"Tes amis te verront ainsi : {PeerName.Handle(name, _identity.PublicKey)}",
                                          $"Your friends will see you as: {PeerName.Handle(name, _identity.PublicKey)}");
            CreateIdentityButton.IsEnabled = true;
        }
        else
        {
            IdentityPreviewText.Text = string.IsNullOrWhiteSpace(IdentityNameBox.Text) ? "" : error;
            CreateIdentityButton.IsEnabled = false;
        }
    }

    private void CreateIdentity(object? sender, RoutedEventArgs args)
    {
        if (!PeerName.TryNormalize(IdentityNameBox.Text, out var name, out var error))
        {
            IdentityPreviewText.Text = error;
            return;
        }

        _preferences = _preferences with { FriendsDisplayName = name };
        _preferencesStore.Save(_preferences);
        PresenceNameBox.Text = name;

        RefreshIdentityUi();
        StartPresenceService();
        RefreshFriendsView();
        ShowToastParity(P7("Profil", "Profile"), _preferences.MeshEnabled
            ? P7($"Bienvenue, {OwnHandle()}. Ton code ami est prêt sur cette page : envoie-le à tes amis.",
                 $"Welcome, {OwnHandle()}. Your friend code is ready on this page: send it to your friends.")
            : P7($"Bienvenue, {OwnHandle()}. Active le réseau CubeShelf sur cette page pour obtenir ton code ami.",
                 $"Welcome, {OwnHandle()}. Turn the CubeShelf network on, on this page, to get your friend code."));
    }

    /// <summary>Renaming later goes through the same rules as creating.</summary>
    private void SavePseudo(object? sender, RoutedEventArgs args)
    {
        if (_loadingSettings) return;

        if (!PeerName.TryNormalize(PresenceNameBox.Text, out var name, out var error))
        {
            PresenceNameBox.Text = _preferences.FriendsDisplayName;
            ShowToastParity(P7("Profil", "Profile"), error);
            return;
        }

        PresenceNameBox.Text = name;
        if (string.Equals(name, _preferences.FriendsDisplayName, StringComparison.Ordinal)) return;

        _preferences = _preferences with { FriendsDisplayName = name };
        _preferencesStore.Save(_preferences);

        // The pseudo is inside the code, so the code changes -- but codes already handed out keep
        // working: the key and the address are what count, the name is a courtesy.
        RefreshIdentityUi();
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    /// <summary>
    /// The code is rebuilt from what it is made of rather than stored: the key never changes, the
    /// pseudo may, and the address counts only once a round trip has proven it. Storing the result
    /// of the last test instead is what made the code vanish at every restart in 0.9.0.
    /// </summary>
    private void RefreshOwnFriendCode()
    {
        _ownFriendCode = "";
        var verified = IsAddressVerified();

        if (_identity is not null && HasIdentity)
        {
            try
            {
                // On the network a code needs nothing proven: the identity, the pseudo, and ways
                // in. The old way still needs an address a round trip has proven.
                if (_preferences.MeshEnabled) _ownFriendCode = BuildNetworkFriendCode();
                else if (verified)
                    _ownFriendCode = FriendCode.Encode(_identity.PublicKey, _preferences.PresenceUrl, _preferences.FriendsDisplayName);
            }
            catch (ArgumentException)
            {
            }
        }

        if (OwnFriendCodeBox is null) return;
        OwnFriendCodeBox.Text = _ownFriendCode;
        CopyOwnCodeButton.IsEnabled = _ownFriendCode.Length > 0;
        ShareStatusText.Text = DescribeShareSteps(verified);
    }

    private bool IsAddressVerified() =>
        _preferences.PresenceVerifiedUrl.Length > 0 &&
        string.Equals(_preferences.PresenceVerifiedUrl, _preferences.PresenceUrl, StringComparison.Ordinal);

    /// <summary>The four things a code needs, each ticked or not, and what is missing in words.</summary>
    private string DescribeShareSteps(bool verified)
    {
        static string Mark(bool done) => done ? "✓" : "○";

        if (_preferences.MeshEnabled)
        {
            var node = _meshNode;
            var joined = node is not null && (node.Table.Count > 0 || node.Reachability == MeshReachability.Public);
            var reachable = node?.Reachability is MeshReachability.Public or MeshReachability.Relayed;
            var how = node?.Reachability == MeshReachability.Public ? P7(" directement", " directly") : P7(" par relais", " through relays");
            var networkSteps = P7(
                $"{Mark(HasIdentity)} Identité : {OwnHandle()}\n" +
                $"{Mark(joined)} Réseau CubeShelf rejoint\n" +
                $"{Mark(reachable)} Joignable{(reachable ? how : "")}",
                $"{Mark(HasIdentity)} Identity: {OwnHandle()}\n" +
                $"{Mark(joined)} CubeShelf network joined\n" +
                $"{Mark(reachable)} Reachable{(reachable ? how : "")}");
            var networkVerdict = joined
                ? P7("Ton code est prêt. « Copier mon code » copie un message à envoyer à ton ami : il le copie à son tour, ouvre sa page Amis, et CubeShelf le trouve tout seul. Une demande d’ami te revient ensuite par le réseau.",
                     "Your code is ready. “Copy my code” copies a message to send your friend: they copy it in turn, open their Friends page, and CubeShelf finds it by itself. A friend request then comes back to you through the network.")
                : P7("Ton code est prêt. Ton CubeShelf ne connaît encore aucun autre nœud : le code de ton ami lui servira de porte d’entrée, et le tien à lui.",
                     "Your code is ready. Your CubeShelf knows no other node yet: your friend’s code will be its way in, and yours theirs.");
            return networkSteps + "\n\n" + networkVerdict;
        }

        var folder = !string.IsNullOrWhiteSpace(_preferences.PresenceFolder) &&
                     Directory.Exists(_preferences.PresenceFolder);
        var publishing = _preferences.PresencePublishEnabled;

        var steps = P7(
            $"{Mark(HasIdentity)} Identité : {OwnHandle()}\n" +
            $"{Mark(folder)} Dossier synchronisé choisi\n" +
            $"{Mark(verified)} Adresse publique vérifiée\n" +
            $"{Mark(publishing)} Publication activée",
            $"{Mark(HasIdentity)} Identity: {OwnHandle()}\n" +
            $"{Mark(folder)} Synchronised folder chosen\n" +
            $"{Mark(verified)} Public address verified\n" +
            $"{Mark(publishing)} Publishing on");

        var verdict = !verified
            ? P7("Ton code apparaîtra dès que l’adresse aura été vérifiée plus bas : un code qui pointe vers une adresse cassée serait inutilisable, et personne ne saurait pourquoi.",
                 "Your code appears as soon as the address below is verified: a code pointing at a broken address would be useless, and nobody could tell why.")
            : !publishing
                ? P7("Ton code est prêt, mais coche « Publier ma présence » : sinon ton ami t’ajoutera et ne te verra jamais.",
                     "Your code is ready, but tick “Publish my presence”: otherwise your friend will add you and never see you.")
                : P7("Ton code est prêt. « Copier mon code » copie un message à envoyer à ton ami : il le copie à son tour, ouvre sa page Amis, et CubeShelf le trouve tout seul.",
                     "Your code is ready. “Copy my code” copies a message to send your friend: they copy it in turn, open their Friends page, and CubeShelf finds it by itself.");

        return steps + "\n\n" + verdict;
    }

    /// <summary>
    /// What "Copy my code" puts in the clipboard: a message that reads fine in a chat window and
    /// still carries the code whole, which <see cref="FriendCode.TryFind"/> picks out on the other side.
    /// </summary>
    private string ShareMessage() => P7(
        $"Ajoute-moi sur CubeShelf : {OwnHandle()}\n\n{_ownFriendCode}\n\n" +
        "Copie tout ce message, puis ouvre la page Amis de CubeShelf : il le trouvera tout seul.",
        $"Add me on CubeShelf: {OwnHandle()}\n\n{_ownFriendCode}\n\n" +
        "Copy this whole message, then open CubeShelf’s Friends page: it will find it by itself.");

    /// <summary>
    /// What to send someone we just added: our own code, worded as an answer to theirs. Found in
    /// a chat message on their side exactly like the first one was on ours.
    /// </summary>
    private string ReplyMessage(string theirHandle) => P7(
        $"Je t’ai ajouté sur CubeShelf, {theirHandle} ! Ajoute-moi en retour : {OwnHandle()}\n\n{_ownFriendCode}\n\n" +
        "Copie tout ce message, puis ouvre la page Amis de CubeShelf : il le trouvera tout seul.",
        $"I added you on CubeShelf, {theirHandle}! Add me back: {OwnHandle()}\n\n{_ownFriendCode}\n\n" +
        "Copy this whole message, then open CubeShelf’s Friends page: it will find it by itself.");

    /// <summary>
    /// The step after adding someone: send our code back, now, while the conversation is open.
    /// Without a code of our own yet, it says what is missing instead of offering a dead button.
    /// </summary>
    private Control ReplyStep(string theirHandle, Action close)
    {
        var copy = new Button { Content = P7("Copier mon message pour lui", "Copy my message for them"), Classes = { "primary" } };
        var later = new Button { Content = P7("Plus tard", "Later") };
        later.Click += (_, _) => close();

        string text;
        if (_ownFriendCode.Length > 0)
        {
            text = _meshFriends is not null
                ? P7($"{theirHandle} est ajouté. Une demande d’ami lui part par le réseau CubeShelf : il la verra sur sa page Amis. Si elle tarde, envoie-lui aussi ton code : copie ce message et colle-le dans votre conversation.",
                     $"{theirHandle} is added. A friend request goes to them through the CubeShelf network: they see it on their Friends page. If it is slow to arrive, send them your code too: copy this message and paste it into your conversation.")
                : P7($"{theirHandle} est ajouté. Pour qu’il te voie aussi, envoie-lui ton code : copie ce message et colle-le dans votre conversation.",
                     $"{theirHandle} is added. For them to see you too, send them your code: copy this message and paste it into your conversation.");
            copy.Click += async (_, _) =>
            {
                await CopyToClipboardAsync(ReplyMessage(theirHandle),
                    P7("Message copié : colle-le dans ta conversation avec lui.", "Message copied: paste it into your conversation with them."));
                close();
            };
        }
        else
        {
            text = P7($"{theirHandle} est ajouté. Pour qu’il te voie aussi, il lui faudra ton code, qui n’est pas encore prêt : termine la page Mon profil, puis « Envoyer mon code » sur sa ligne.",
                      $"{theirHandle} is added. For them to see you too they need your code, which is not ready yet: finish the My profile page, then “Send my code” on their line.");
            copy.Content = P7("Ouvrir Mon profil", "Open My profile");
            copy.Click += (_, _) =>
            {
                close();
                ParityShowProfile(null, new RoutedEventArgs());
            };
        }

        return new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { later, copy }
                }
            }
        };
    }

    /// <summary>The Friends page's "Send my code" on a pending friend.</summary>
    private async void SendOwnCodeToFriend(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || _ownFriendCode.Length == 0) return;
        await CopyToClipboardAsync(ReplyMessage(row.Name),
            P7($"Message copié : colle-le dans ta conversation avec {row.Name}.",
               $"Message copied: paste it into your conversation with {row.Name}."));
    }

    // ----------------------------------------------------------------- clipboard

    /// <summary>
    /// Looks for a friend code in the clipboard, and only while the Friends page is on screen: the
    /// user has just copied a message somewhere and come back to add someone. Nothing is kept or
    /// sent -- anything that is not a friend code is dropped on the spot.
    /// </summary>
    private async Task CheckClipboardForFriendCodeAsync()
    {
        if (ClipboardInviteBanner is null) return;

        FriendCodePayload? found = null;
        try
        {
            if (HasIdentity && _identity is not null && _friends is not null &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var text = await clipboard.TryGetTextAsync();
                if (FriendCode.TryFind(text, out var payload) && payload is not null)
                {
                    var key = Convert.ToBase64String(payload.PublicKey);
                    // Someone already listed -- friend, paused or blocked -- is not offered again,
                    // and neither is our own code, which is what we would find right after copying it.
                    var known = _friends.Load().Any(friend => friend.PublicKey == key);
                    var self = payload.PublicKey.AsSpan().SequenceEqual(_identity.PublicKey);
                    if (!known && !self && !_ignoredClipboardKeys.Contains(key))
                        found = payload;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Another program holding the clipboard is not our problem to report.
        }

        _clipboardCandidate = found;
        ClipboardInviteBanner.IsVisible = found is not null;
        if (found is null) return;

        ClipboardInviteText.Text = P7(
            $"Code ami trouvé dans ton presse-papiers : {found.Handle}. {DescribeCodeSource(found, french: true)} L’ajouter ?",
            $"Friend code found in your clipboard: {found.Handle}. {DescribeCodeSource(found, french: false)} Add them?");
    }

    private void AddFriendFromClipboard(object? sender, RoutedEventArgs args)
    {
        if (_clipboardCandidate is not { } payload || _identity is null || _friends is null) return;

        var name = payload.DisplayName.Length > 0 ? payload.DisplayName : P7("Ami", "Friend");
        if (!_friends.TryAdd(payload, name, _identity.PublicKey, out var error))
        {
            ShowToastParity(P7("Amis", "Friends"), error);
            return;
        }

        _clipboardCandidate = null;
        ClipboardInviteBanner.IsVisible = false;
        RefreshFriendsView();
        AfterAddingFromCode(payload);
        _ = RefreshFriendsSilentlyAsync();
        _presence?.PollEagerly();

        // Friendship here goes one way at a time: adding them lets you read them, not them you.
        var dialog = CreatePhase7Dialog(P7("Ami ajouté", "Friend added"), 560, 280);
        dialog.Content = ReplyStep(payload.Handle, () => dialog.Close());
        _ = dialog.ShowDialog(this);
    }

    private void IgnoreClipboardCode(object? sender, RoutedEventArgs args)
    {
        if (_clipboardCandidate is { } payload)
            _ignoredClipboardKeys.Add(Convert.ToBase64String(payload.PublicKey));
        _clipboardCandidate = null;
        ClipboardInviteBanner.IsVisible = false;
    }

    /// <summary>The footer used to say 0.8 whatever was running; it reads the assembly now.</summary>
    private void ShowRunningVersion()
    {
        var version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0];
        VersionText.Text = $"CubeShelf {version ?? "?"} • Avalonia";
    }
}
