using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CubeShelf.Core.Social;

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
        ShowToastParity(P7("Profil", "Profile"),
            P7($"Bienvenue, {OwnHandle()}. Il reste à choisir où publier pour obtenir ton code ami.",
               $"Welcome, {OwnHandle()}. One step left: choose where to publish to get your friend code."));
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

        if (_identity is not null && HasIdentity && verified)
        {
            try
            {
                _ownFriendCode = FriendCode.Encode(
                    _identity.PublicKey, _preferences.PresenceUrl, _preferences.FriendsDisplayName);
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

        var host = Uri.TryCreate(found.PresenceUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
        ClipboardInviteText.Text = P7(
            $"Code ami trouvé dans ton presse-papiers : {found.Handle}, qui publie sur {host}. L’ajouter ?",
            $"Friend code found in your clipboard: {found.Handle}, publishing on {host}. Add them?");
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
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);

        // Friendship here goes one way at a time: adding them lets you read them, not them you.
        ShowToastParity(P7("Amis", "Friends"),
            P7($"{payload.Handle} ajouté. Pour qu’il te voie aussi, envoie-lui ton code en retour.",
               $"{payload.Handle} added. For them to see you too, send them your code back."));
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
