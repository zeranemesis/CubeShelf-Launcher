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
            PhoneTransferStatusText.Text = P7(
                "Profil exporté. Copie le fichier sur le téléphone, importe-le dans PartyBoard avec ce mot de passe, puis supprime-le.",
                "Profile exported. Copy the file to the phone, import it in PartyBoard with this passphrase, then delete it.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PhoneTransferStatusText.Text = P7($"Export impossible : {exception.Message}",
                                              $"Export failed: {exception.Message}");
        }
    }
}
