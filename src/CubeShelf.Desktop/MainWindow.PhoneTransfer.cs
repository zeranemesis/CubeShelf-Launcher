using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// "Play on a phone": hands this profile to PartyBoard on Android, which cannot run CubeShelf.
/// See <see cref="ProfileTransfer"/> for what the file holds and why the phone never publishes.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// What the page says about the backup: the failure, when the key on disk does not open here;
    /// a reminder, while no backup was ever made; nothing once one exists.
    /// </summary>
    private void RefreshBackupHint()
    {
        if (IdentityBackupHint is null) return;
        if (_identity is null && _friendsFailure.Length > 0)
        {
            IdentityBackupHint.Text = _friendsFailure;
            IdentityBackupHint.IsVisible = true;
            return;
        }
        IdentityBackupHint.IsVisible = HasIdentity && _preferences.IdentityBackedUpAt is null;
        IdentityBackupHint.Text = PeerIdentity.ProtectedAtRest
            ? P7("Ton identité n’est sauvegardée nulle part. Elle est chiffrée pour ce compte Windows : si tu réinstalles Windows ou changes de PC, tes amis devront t’ajouter de nouveau. Exporte-la ci-dessous avec un mot de passe et garde le fichier ailleurs.",
                 "Your identity is backed up nowhere. It is encrypted for this Windows account: reinstall Windows or change PC and your friends have to add you again. Export it below with a passphrase and keep the file elsewhere.")
            : P7("Ton identité n’est sauvegardée nulle part : si ce PC lâche, tes amis devront t’ajouter de nouveau. Exporte-la ci-dessous avec un mot de passe et garde le fichier ailleurs.",
                 "Your identity is backed up nowhere: if this PC fails, your friends have to add you again. Export it below with a passphrase and keep the file elsewhere.");
    }

    /// <summary>
    /// Brings an identity back from an exported file: on a new PC, after reinstalling Windows, or
    /// when the key on disk was protected by another account. Friends already here are kept; those
    /// in the file are added. The key it replaces is kept beside it, renamed, never deleted.
    /// </summary>
    private async void RestoreIdentityBackup(object? sender, RoutedEventArgs args)
    {
        PhoneTransferStatusText.Text = "";
        var passphrase = PhoneTransferPassphraseBox.Text ?? "";
        if (passphrase.Length == 0)
        {
            PhoneTransferStatusText.Text = P7("Tape d’abord le mot de passe de la sauvegarde ci-dessus.",
                                              "Type the backup’s passphrase above first.");
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = P7("Restaurer une sauvegarde d’identité", "Restore an identity backup"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(P7("Profil CubeShelf", "CubeShelf profile")) { Patterns = new[] { "*" + ProfileTransfer.FileExtension, "*.txt" } },
                FilePickerFileTypes.All
            }
        });
        if (picked.FirstOrDefault() is not { } file) return;

        string text;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PhoneTransferStatusText.Text = exception.Message;
            return;
        }

        if (!ProfileTransfer.TryImport(text, passphrase, out var payload, out var error) || payload is null)
        {
            PhoneTransferStatusText.Text = error;
            return;
        }

        var incoming = PeerName.Handle(payload.DisplayName, payload.PublicKey);
        if (_identity is not null && !_identity.PublicKey.AsSpan().SequenceEqual(payload.PublicKey))
        {
            var dialog = CreatePhase7Dialog(P7("Restaurer une sauvegarde", "Restore a backup"), 560, 260);
            var confirmed = false;
            var replace = new Button { Content = P7("Remplacer", "Replace"), Classes = { "danger" } };
            replace.Click += (_, _) => { confirmed = true; dialog.Close(); };
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = P7($"Tu es {OwnHandle()} sur ce PC. Devenir {incoming} ? Tes amis actuels restent dans la liste, ceux de la sauvegarde s’y ajoutent. L’identité actuelle est gardée à côté, renommée.",
                                  $"You are {OwnHandle()} on this PC. Become {incoming}? Your current friends stay listed, those in the backup are added. The current identity is kept beside it, renamed."),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { replace } }
                }
            };
            await dialog.ShowDialog(this);
            if (!confirmed) return;
        }

        try
        {
            var keyPath = Path.Combine(_paths.ConfigurationDirectory, "identity.key");
            if (File.Exists(keyPath))
                File.Copy(keyPath, keyPath + ".replaced-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), true);
            using (var restored = PeerIdentity.FromPrivateScalar(payload.PrivateKey, payload.PublicKey))
                restored.Save(keyPath);

            var store = _friends ?? new FriendStore(_paths.ConfigurationDirectory);
            foreach (var friend in payload.Friends)
            {
                byte[] key;
                try
                {
                    key = Convert.FromBase64String(friend.PublicKey);
                }
                catch (FormatException)
                {
                    continue;
                }
                store.TryAdd(new FriendCodePayload(key, friend.PresenceUrl, friend.DisplayName), friend.DisplayName, payload.PublicKey, out _);
                store.Update(friend.PublicKey, entry =>
                {
                    entry.LastSequence = Math.Max(entry.LastSequence, friend.LastSequence);
                    entry.Paused = entry.Paused || friend.Paused;
                });
            }

            _preferences = _preferences with
            {
                FriendsDisplayName = payload.DisplayName,
                // A restored identity is one that was backed up.
                IdentityBackedUpAt = DateTimeOffset.UtcNow
            };
            _preferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            PhoneTransferStatusText.Text = P7($"Restauration impossible : {exception.Message}", $"Restore failed: {exception.Message}");
            return;
        }

        ReloadFriends();
        PhoneTransferPassphraseBox.Text = "";
        PhoneTransferConfirmBox.Text = "";
        PresenceNameBox.Text = _preferences.FriendsDisplayName;
        PhoneTransferStatusText.Text = P7($"Identité restaurée : tu es de nouveau {OwnHandle()}.", $"Identity restored: you are {OwnHandle()} again.");
    }

    /// <summary>Everything the friends feature built on the identity, built again on the one now on disk.</summary>
    private void ReloadFriends()
    {
        _sessions.Started -= OnFriendsSessionChanged;
        _sessions.Ended -= OnFriendsSessionEnded;
        StopLan();
        StopPresenceServiceBounded();
        _friendsLifetime?.Cancel();
        _friendsLifetime?.Dispose();
        _friendsLifetime = null;
        _friendPresence.Clear();
        _friendsFailure = "";

        InitializeFriends();
        if (_messages is null) InitializeSocial();
        RefreshIdentityUi();
        RefreshBackupHint();
        RefreshFriendsView();
    }

    private async void ExportProfileForPhone(object? sender, RoutedEventArgs args)
    {
        PhoneTransferStatusText.Text = "";

        if (_identity is null || _friends is null || !HasIdentity)
        {
            PhoneTransferStatusText.Text = P7("Crée d’abord ton identité.", "Create your identity first.");
            return;
        }

        var passphrase = PhoneTransferPassphraseBox.Text ?? "";
        if (!ProfileTransfer.IsAcceptablePassphrase(passphrase, out _))
        {
            PhoneTransferStatusText.Text = P7(
                $"Le mot de passe doit faire au moins {ProfileTransfer.MinimumPassphraseLength} caractères.",
                $"The passphrase needs at least {ProfileTransfer.MinimumPassphraseLength} characters.");
            return;
        }
        if (!string.Equals(passphrase, PhoneTransferConfirmBox.Text, StringComparison.Ordinal))
        {
            PhoneTransferStatusText.Text = P7("Les deux mots de passe ne sont pas identiques.",
                                              "The two passphrases do not match.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = P7("Exporter le profil pour le téléphone", "Export the profile for the phone"),
            SuggestedFileName = "profil" + ProfileTransfer.FileExtension,
            DefaultExtension = ProfileTransfer.FileExtension.TrimStart('.'),
            ShowOverwritePrompt = true
        });
        if (file is null) return;

        try
        {
            var exported = ProfileTransfer.Export(
                _identity, _preferences.FriendsDisplayName, _friends.Load(), passphrase, DateTimeOffset.UtcNow);
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(exported);

            PhoneTransferPassphraseBox.Text = "";
            PhoneTransferConfirmBox.Text = "";
            // The same file is the backup of the identity: once one exists, the page stops asking.
            _preferences = _preferences with { IdentityBackedUpAt = DateTimeOffset.UtcNow };
            _preferencesStore.Save(_preferences);
            RefreshBackupHint();
            PhoneTransferStatusText.Text = P7(
                "Profil exporté. C’est aussi la sauvegarde de ton identité : garde ce fichier et son mot de passe à l’abri, hors de ce PC. Pour le téléphone, importe-le dans PartyBoard avec ce mot de passe.",
                "Profile exported. It is also the backup of your identity: keep the file and its passphrase safe, off this PC. For the phone, import it in PartyBoard with this passphrase.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PhoneTransferStatusText.Text = P7($"Export impossible : {exception.Message}",
                                              $"Export failed: {exception.Message}");
        }
    }
}
