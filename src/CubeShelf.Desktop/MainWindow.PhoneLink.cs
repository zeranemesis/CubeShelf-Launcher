using System.Net;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// "Play on a phone", the QR code way: PartyBoard on Android scans it on the same Wi-Fi and gets
/// this profile and the Mario Party 4 memory cards, or sends its own cards back. The link lives
/// while the window is open, ten minutes at most; see <see cref="PhoneLinkServer"/>.
/// </summary>
public sealed partial class MainWindow
{
    private async void ShowPhoneQrCode(object? sender, RoutedEventArgs args)
    {
        if (_identity is null || _friends is null || !HasIdentity)
        {
            PhoneTransferStatusText.Text = P7("Crée d’abord ton identité.", "Create your identity first.");
            return;
        }

        var addresses = PhoneLinkServer.LocalAddresses();
        if (addresses.Count == 0)
        {
            PhoneTransferStatusText.Text = P7(
                "Ce PC n’est sur aucun réseau local : connecte-le au même Wi-Fi ou à la même box que le téléphone.",
                "This PC is on no local network: connect it to the same Wi-Fi or router as the phone.");
            return;
        }

        var identity = _identity;
        var friends = _friends;
        var name = _preferences.FriendsDisplayName;
        var savesDirectory = PhoneLinkServer.DefaultSavesDirectory();
        var french = !UiLocalization.IsEnglish(_preferences.Language);

        PhoneLinkServer? server = null;
        var dialog = CreatePhase7Dialog(P7("Jouer sur téléphone", "Play on a phone"), 560, 700);
        // Shown at its own size, module for module: a scaled QR code blurs into something a
        // phone camera reads less well.
        var image = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var codeBox = new TextBox { IsReadOnly = true, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        var picker = new ComboBox
        {
            ItemsSource = addresses.Select(address => address.ToString()).ToArray(),
            SelectedIndex = 0,
            IsVisible = addresses.Count > 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var copy = new Button { Content = P7("Copier le code", "Copy the code") };

        void Open(IPAddress address)
        {
            server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            server = new PhoneLinkServer(
                () => ProfileTransfer.Document(identity, name, friends.Load(), DateTimeOffset.UtcNow),
                savesDirectory,
                IsPartyBoardRunning,
                address,
                french);
            server.Happened += happened => Dispatcher.UIThread.Post(() =>
            {
                status.Text = happened.Message == "sent"
                    ? P7("Le téléphone a reçu ton compte, tes amis et tes sauvegardes. Il les met en place au prochain lancement de PartyBoard.",
                         "The phone got your account, your friends and your saves. It puts them in place the next time PartyBoard starts.")
                    : happened.Message;
                status.Foreground = happened.Succeeded ? Brushes.SeaGreen : Brushes.IndianRed;
            });
            server.Start();
            codeBox.Text = server.Code;
            image.Source = Render(QrCode.Encode(server.Code));
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) Open(addresses[picker.SelectedIndex]);
        };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(dialog)?.Clipboard is { } clipboard && server is not null)
                await clipboard.SetTextAsync(server.Code);
        };

        try
        {
            Open(addresses[0]);
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException)
        {
            PhoneTransferStatusText.Text = P7($"Le lien n’a pas pu s’ouvrir : {exception.Message}",
                                              $"The link could not open: {exception.Message}");
            return;
        }

        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = P7("Sur le téléphone, connecté au même Wi-Fi que ce PC : PartyBoard, onglet Amis, « Compte et sauvegardes (QR code) », puis Scanner.",
                                  "On the phone, on the same Wi-Fi as this PC: PartyBoard, Friends tab, “Account and saves (QR code)”, then Scan."),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Border { Background = Brushes.White, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Child = image },
                    new TextBlock
                    {
                        Text = P7("Le téléphone devient toi : il voit tes amis et leurs invitations, et récupère tes sauvegardes de Mario Party 4. C’est ce PC qui continue à publier ta présence. Le QR code vaut pour cette fenêtre et dix minutes au plus ; ne le montre à personne d’autre.",
                                  "The phone becomes you: it sees your friends and their invitations, and gets your Mario Party 4 saves. This PC keeps publishing your presence. The QR code lasts as long as this window, ten minutes at most; show it to nobody else."),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        Foreground = Brushes.Gray
                    },
                    new TextBlock
                    {
                        Text = P7("Le téléphone ne trouve pas le PC ? Essaie une autre adresse de ce PC :", "The phone cannot find the PC? Try another address of this PC:"),
                        IsVisible = addresses.Count > 1,
                        FontSize = 12
                    },
                    picker,
                    new TextBlock { Text = P7("Ou colle ce code dans PartyBoard :", "Or paste this code into PartyBoard:"), FontSize = 12 },
                    codeBox,
                    copy,
                    status
                }
            }
        };

        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (server is not null) await server.DisposeAsync();
        }
    }

    /// <summary>The code as a bitmap: eight pixels a module, four modules of white around it.</summary>
    private static WriteableBitmap Render(QrCode qr)
    {
        const int scale = 8;
        const int quiet = 4;
        var size = (qr.Size + 2 * quiet) * scale;
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        var row = new int[size];
        for (var y = 0; y < size; y++)
        {
            var my = y / scale - quiet;
            for (var x = 0; x < size; x++)
            {
                var mx = x / scale - quiet;
                var dark = mx >= 0 && my >= 0 && mx < qr.Size && my < qr.Size && qr[mx, my];
                row[x] = dark ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
            }
            Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, size);
        }
        return bitmap;
    }
}
