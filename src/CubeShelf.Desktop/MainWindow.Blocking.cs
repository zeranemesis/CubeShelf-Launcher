using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// Blocking, which is a stronger thing than removing and has to be described as such.
///
/// Removing a friend stops the relationship; pasting their code again restores it. Blocking
/// leaves a tombstone in friends.json precisely so that the code cannot quietly undo the
/// decision later -- that is the whole difference between the two.
///
/// What it cannot do is the part the dialog states rather than lets the user discover: a blocked
/// peer keeps the address they were given, so they can still see when the file changes, and they
/// keep the pairwise key forever, because it derives from the two identities. Only rotating the
/// address ends that, and rotating it invalidates every friend code already handed out.
/// </summary>
public sealed partial class MainWindow
{
    private async void BlockFriend(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == row.PublicKey);
        if (friend is null) return;

        var dialog = CreatePhase7Dialog(P7("Bloquer", "Block"), 580, 300);
        var confirmed = false;
        var confirm = new Button { Content = P7("Bloquer", "Block"), Classes = { "danger" } };
        confirm.Click += (_, _) => { confirmed = true; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = P7($"Bloquer {row.Name} ? Il cesse de recevoir ta présence, tu cesses de lire la sienne, " +
                              "et son code ami ne pourra plus être rajouté tant qu’il est bloqué.",
                              $"Block {row.Name}? They stop receiving your presence, you stop reading theirs, " +
                              "and their friend code cannot be added again while the block stands."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = P7("Ce que ça ne fait pas : il garde ton adresse et verra encore quand ton fichier " +
                              "change, donc quand tu joues. Seul un changement d’adresse y met fin, et il " +
                              "invalide tous les codes amis déjà distribués.",
                              "What it does not do: they keep your address and can still see when your file " +
                              "changes, and so when you play. Only changing the address ends that, and doing " +
                              "so invalidates every friend code already handed out."),
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

        _friends.Block(new FriendCodePayload(
            Convert.FromBase64String(friend.PublicKey), friend.PresenceUrl), friend.DisplayName);

        // Their last known presence goes with them: a blocked peer should not keep showing as
        // "in a game" from a document we will never refresh again.
        _friendPresence.TryRemove(row.PublicKey, out _);
        RefreshFriendsView();
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
    }

    private void UnblockFriend(object? sender, RoutedEventArgs args)
    {
        if (_friends is null || sender is not Button { Tag: FriendRow row }) return;

        if (!_friends.Unblock(row.PublicKey)) return;

        RefreshFriendsView();
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
        ShowToastParity(P7("Amis", "Friends"),
            P7($"{row.Name} est débloqué et redevient un ami.",
               $"{row.Name} is unblocked and is a friend again."));
    }
}
