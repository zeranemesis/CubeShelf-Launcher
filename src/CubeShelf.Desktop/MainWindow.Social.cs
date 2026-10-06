using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// What friends say to each other beyond "I am here": availability (away, do not disturb,
/// invisible), short messages, and answers to invitations. All of it rides in the presence
/// document -- see <see cref="PairwiseNotes"/> -- so it arrives at the pace presence does: seconds
/// on the local network, the sync service's pace otherwise.
/// </summary>
public sealed partial class MainWindow
{
    private MessageStore? _messages;
    private DispatcherTimer? _idleTimer;
    private bool _autoAway;

    /// <summary>Answers to our invitations, per friend: what and when, shown on their line for a while.</summary>
    private readonly Dictionary<string, (string Reply, DateTimeOffset At)> _inviteAnswers = new(StringComparer.Ordinal);

    /// <summary>Invitations we declined, so their Join button goes away at once.</summary>
    private readonly HashSet<string> _declinedInvites = new(StringComparer.Ordinal);

    private string? _openChatFriend;
    private Action? _refreshOpenChat;

    private void InitializeSocial()
    {
        if (_friends is null) return;
        _messages = new MessageStore(_paths.ConfigurationDirectory);
        _messages.Changed += friend => Dispatcher.UIThread.Post(() =>
        {
            if (friend == _openChatFriend) _refreshOpenChat?.Invoke();
            if (FriendsView.IsVisible) RefreshFriendsView();
        });

        // Away after ten minutes without touching the keyboard or the mouse, back on the first touch.
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();
        ApplyAvailabilityPicker();
    }

    private void DisposeSocial()
    {
        _idleTimer?.Stop();
        _idleTimer = null;
    }

    // ------------------------------------------------------------------ availability

    private PresenceAvailability ChosenAvailability => _preferences.Availability switch
    {
        "away" => PresenceAvailability.Away,
        "busy" => PresenceAvailability.Busy,
        "invisible" => PresenceAvailability.Invisible,
        _ => PresenceAvailability.Available
    };

    /// <summary>What is published: the user's choice, or away when they left the machine while available.</summary>
    private PresenceAvailability EffectiveAvailability =>
        ChosenAvailability == PresenceAvailability.Available && _autoAway ? PresenceAvailability.Away : ChosenAvailability;

    private bool DoNotDisturb => ChosenAvailability == PresenceAvailability.Busy;

    private void CheckIdle()
    {
        if (!_preferences.AutoAway || !OperatingSystem.IsWindows()) return;
        var idle = IdleTime();
        if (idle is null) return;

        var away = idle.Value >= TimeSpan.FromMinutes(10);
        if (away == _autoAway) return;
        _autoAway = away;
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static TimeSpan? IdleTime()
    {
        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return null;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static readonly string[] AvailabilityValues = { "", "away", "busy", "invisible" };

    private void ApplyAvailabilityPicker()
    {
        if (AvailabilityPicker is null) return;
        _loadingAvailability = true;
        AvailabilityPicker.ItemsSource = new[]
        {
            P7("● Disponible", "● Available"),
            P7("◐ Absent", "◐ Away"),
            P7("🔕 Ne pas déranger", "🔕 Do not disturb"),
            P7("○ Invisible", "○ Invisible")
        };
        AvailabilityPicker.SelectedIndex = Math.Max(0, Array.IndexOf(AvailabilityValues, _preferences.Availability));
        _loadingAvailability = false;
    }

    private bool _loadingAvailability;

    private void AvailabilityChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_loadingAvailability || AvailabilityPicker.SelectedIndex < 0) return;
        var chosen = AvailabilityValues[AvailabilityPicker.SelectedIndex];
        if (chosen == _preferences.Availability) return;

        _preferences = _preferences with { Availability = chosen };
        _preferencesStore.Save(_preferences);
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
        if (chosen == "invisible")
            ShowToastParity(P7("Invisible", "Invisible"),
                P7("Tes amis te voient hors ligne. Tu les vois toujours, et vos messages passent.",
                   "Your friends see you offline. You still see them, and messages still get through."));
    }

    /// <summary>A friend's availability and what the game says, as one short line.</summary>
    private string DescribeFriendStatus(PresenceSnapshot? known, PresenceStatus status)
    {
        var busy = known?.Availability == PresenceSnapshot.AvailabilityBusy;
        var away = known?.Availability == PresenceSnapshot.AvailabilityAway;
        return status switch
        {
            PresenceStatus.InGame => "▶ " + (known?.CurrentGameTitle ?? P7("En jeu", "In a game")) +
                                     (known?.Activity is { Length: > 0 } activity ? " — " + activity : "") +
                                     (busy ? " • 🔕" : ""),
            PresenceStatus.Online when busy => P7("🔕 Ne pas déranger", "🔕 Do not disturb"),
            PresenceStatus.Online when away => P7("◐ Absent", "◐ Away"),
            PresenceStatus.Online => P7("● En ligne", "● Online"),
            _ => P7("○ Hors ligne", "○ Offline")
        };
    }

    // ------------------------------------------------------------------ notes in and out

    /// <summary>Opens what a friend's document says to us alone, and acts on it.</summary>
    private void ReceiveNotes(string friendKey, PresenceSnapshot snapshot)
    {
        if (_identity is null || _messages is null || snapshot.Notes is not { Count: > 0 }) return;

        byte[] author;
        try
        {
            author = Convert.FromBase64String(friendKey);
        }
        catch (FormatException)
        {
            return;
        }

        if (!PairwiseNotes.TryOpen(_identity, author, snapshot.Notes, out var note) || note is null) return;

        var friend = _friends?.Load().FirstOrDefault(entry => entry.PublicKey == friendKey);
        if (friend is null || friend.Blocked) return;
        var handle = PeerName.Handle(friend.DisplayName, friend.PublicKey);

        var arrived = _messages.Receive(friendKey, note, DateTimeOffset.UtcNow);
        if (arrived.Count > 0)
        {
            // Acknowledged in our next document, so their side marks them delivered.
            _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
            if (friendKey == _openChatFriend && IsActive) _messages.MarkRead(friendKey);
            else
            {
                var last = arrived[^1].Text;
                Notify(P7($"Message de {handle}", $"Message from {handle}"), last.Length > 140 ? last[..140] + "…" : last);
            }
        }

        // Answers to our invitation: said once each.
        var current = _outgoingInvite;
        foreach (var reply in note.Replies)
        {
            if (current is null || reply.Invite != current.Id) continue;
            if (_inviteAnswers.TryGetValue(friendKey, out var known) && known.Reply == reply.Reply) continue;
            _inviteAnswers[friendKey] = (reply.Reply, DateTimeOffset.UtcNow);
            Notify(P7("Invitation", "Invitation"), reply.Reply == InviteReply.Joined
                ? P7($"{handle} rejoint ton salon.", $"{handle} is joining your lobby.")
                : P7($"{handle} a décliné ton invitation.", $"{handle} declined your invitation."));
        }
    }

    /// <summary>Records our answer to a friend's invitation; it rides along with our next document.</summary>
    private void AnswerInvite(string friendKey, PresenceInvite invite, string reply)
    {
        _messages?.Reply(friendKey, invite.Id, reply, DateTimeOffset.UtcNow);
        if (reply == InviteReply.Declined) _declinedInvites.Add(friendKey + "|" + invite.Id);
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    private bool IsDeclined(string friendKey, PresenceInvite invite) => _declinedInvites.Contains(friendKey + "|" + invite.Id);

    /// <summary>The Friends page's "Decline" beside "Join".</summary>
    private void DeclineFriendInvite(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row }) return;
        if (!_friendPresence.TryGetValue(row.PublicKey, out var known) || known.Invite is not { } invite) return;
        AnswerInvite(row.PublicKey, invite, InviteReply.Declined);
        RefreshFriendsView();
        WriteInGameState();
    }

    /// <summary>A line about how a friend answered our invitation, while it is recent.</summary>
    private string InviteAnswerLine(string friendKey)
    {
        if (!_inviteAnswers.TryGetValue(friendKey, out var answer) || DateTimeOffset.UtcNow - answer.At > TimeSpan.FromMinutes(15))
            return "";
        return answer.Reply == InviteReply.Joined
            ? P7("Rejoint ton salon", "Joining your lobby")
            : P7("A décliné ton invitation", "Declined your invitation");
    }

    // ------------------------------------------------------------------ the conversation window

    private async void OpenChat(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || _messages is null) return;
        var store = _messages;
        var friendKey = row.PublicKey;

        var dialog = CreatePhase7Dialog(row.Name, 560, 560);
        var history = new StackPanel { Spacing = 6 };
        var scroller = new ScrollViewer { Content = history, Height = 360 };
        var input = new TextBox
        {
            Watermark = P7("Écris un message…", "Write a message…"),
            MaxLength = ChatText.MaximumLength,
            AcceptsReturn = false
        };
        var send = new Button { Content = P7("Envoyer", "Send"), Classes = { "primary" } };

        void Render()
        {
            history.Children.Clear();
            foreach (var message in store.Conversation(friendKey))
            {
                var stamp = message.At.ToLocalTime().ToString("dd/MM HH:mm");
                var state = message.Mine
                    ? (message.Delivered ? P7(" • remis", " • delivered") : P7(" • envoyé", " • sent"))
                    : "";
                history.Children.Add(new Border
                {
                    HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MaxWidth = 420,
                    Padding = new Avalonia.Thickness(10, 6),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    Background = message.Mine
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 124, 92, 255))
                        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 255, 255, 255)),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = message.Text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                            new TextBlock { Text = stamp + state, FontSize = 10, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right }
                        }
                    }
                });
            }
            if (history.Children.Count == 0)
                history.Children.Add(new TextBlock
                {
                    Text = P7("Aucun message pour l’instant.", "No messages yet."),
                    Foreground = Avalonia.Media.Brushes.Gray
                });
            store.MarkRead(friendKey);
            Dispatcher.UIThread.Post(() => scroller.ScrollToEnd(), DispatcherPriority.Background);
        }

        void Send()
        {
            var text = input.Text ?? "";
            if (store.Send(friendKey, text, DateTimeOffset.UtcNow) is null) return;
            input.Text = "";
            _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
            Render();
        }

        send.Click += (_, _) => Send();
        input.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
            {
                key.Handled = true;
                Send();
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                scroller,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8,
                    Children = { input, Column(send, 1) }
                },
                new TextBlock
                {
                    Text = P7("Les messages passent avec ta présence, chiffrés pour lui seul : quelques secondes sur le même réseau, une minute ou deux sinon. Ils attendent jusqu’à une semaine qu’il les lise.",
                              "Messages travel with your presence, encrypted for them alone: seconds on the same network, a minute or two otherwise. They wait up to a week to be read."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 11,
                    Foreground = Avalonia.Media.Brushes.Gray
                }
            }
        };

        _openChatFriend = friendKey;
        _refreshOpenChat = Render;
        Render();
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _openChatFriend = null;
            _refreshOpenChat = null;
            RefreshFriendsView();
        }
    }

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
