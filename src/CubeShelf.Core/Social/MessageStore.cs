using System.Text.Json;

namespace CubeShelf.Core.Social;

/// <param name="Delivered">For our own messages: the friend's document acknowledged it.</param>
public sealed record ChatMessage(long Id, bool Mine, string Text, DateTimeOffset At, bool Delivered = false);

/// <summary>
/// Short messages between friends, kept on this machine.
///
/// There is no server to hold a message until it is read, so each side holds its own: a message
/// stays in our published document, sealed for its one recipient, until their document says they
/// have it (<see cref="PairwiseNote.Ack"/>). Delivery is therefore at the pace of presence -- a
/// few seconds on the local network, the sync service's pace otherwise. A mailbox, not a chat.
///
/// Bounded everywhere: a week of history per friend is plenty for what this is, and a message
/// that went undelivered for a week is dropped from the document rather than carried forever.
/// </summary>
public sealed class MessageStore
{
    public const int MaximumHistory = 200;
    public static readonly TimeSpan UndeliveredLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ReplyLifetime = TimeSpan.FromMinutes(30);

    private readonly string _file;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private State? _state;

    public MessageStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _file = Path.Combine(Path.GetFullPath(configurationDirectory), "messages.json");
    }

    /// <summary>Raised after messages arrive or are delivered, with the friend concerned. Any thread.</summary>
    public event Action<string>? Changed;

    public IReadOnlyList<ChatMessage> Conversation(string friendKey)
    {
        lock (_gate)
            return Load().Conversations.TryGetValue(friendKey, out var conversation)
                ? conversation.Messages.ToArray()
                : Array.Empty<ChatMessage>();
    }

    public int Unread(string friendKey)
    {
        lock (_gate)
            return Load().Conversations.TryGetValue(friendKey, out var conversation) ? conversation.Unread : 0;
    }

    public void MarkRead(string friendKey)
    {
        lock (_gate)
        {
            var state = Load();
            if (!state.Conversations.TryGetValue(friendKey, out var conversation) || conversation.Unread == 0) return;
            conversation.Unread = 0;
            Save(state);
        }
    }

    public ChatMessage? Send(string friendKey, string text, DateTimeOffset now)
    {
        var clean = ChatText.Clean(text);
        if (clean.Length == 0) return null;

        ChatMessage message;
        lock (_gate)
        {
            var state = Load();
            var conversation = ConversationOf(state, friendKey);
            // Floored at the clock, like the presence sequence: a lost messages.json must not
            // restart numbering below what the friend already has, or they would drop every new
            // message as already seen.
            state.LastSentId = Math.Max(state.LastSentId + 1, now.ToUnixTimeMilliseconds());
            message = new ChatMessage(state.LastSentId, true, clean, now);
            conversation.Messages.Add(message);
            Trim(conversation);
            Save(state);
        }
        Changed?.Invoke(friendKey);
        return message;
    }

    /// <summary>Records our answer to a friend's invitation, carried to them with the next document.</summary>
    public void Reply(string friendKey, string inviteId, string reply, DateTimeOffset now)
    {
        if (reply is not (InviteReply.Joined or InviteReply.Declined) || string.IsNullOrEmpty(inviteId)) return;
        lock (_gate)
        {
            var state = Load();
            var conversation = ConversationOf(state, friendKey);
            conversation.Replies.RemoveAll(existing => existing.Invite == inviteId);
            conversation.Replies.Add(new InviteReply(inviteId, reply, now));
            Save(state);
        }
    }

    /// <summary>What to say to each friend in our next document.</summary>
    public IReadOnlyDictionary<string, PairwiseNote> Outgoing(DateTimeOffset now)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, PairwiseNote>(StringComparer.Ordinal);
            foreach (var (friend, conversation) in Load().Conversations)
            {
                var pending = conversation.Messages
                    .Where(message => message.Mine && !message.Delivered && now - message.At <= UndeliveredLifetime)
                    .TakeLast(50)
                    .Select(message => new NoteMessage(message.Id, message.Text, message.At))
                    .ToArray();
                var replies = conversation.Replies.Where(reply => now - reply.At <= ReplyLifetime).ToArray();
                var note = new PairwiseNote(pending, conversation.LastReceivedId, replies);
                if (!note.IsEmpty) result[friend] = note;
            }
            return result;
        }
    }

    /// <summary>
    /// Takes what a friend's document said to us: new messages are kept once, ours they
    /// acknowledged are marked delivered. Returns the messages that are new.
    /// </summary>
    public IReadOnlyList<ChatMessage> Receive(string friendKey, PairwiseNote note, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(note);
        var arrived = new List<ChatMessage>();
        var delivered = false;

        lock (_gate)
        {
            var state = Load();
            var conversation = ConversationOf(state, friendKey);

            foreach (var message in note.Messages.OrderBy(message => message.Id))
            {
                if (message.Id <= conversation.LastReceivedId) continue;
                conversation.LastReceivedId = message.Id;
                // Their clock is theirs: a message is placed at when it arrived if it claims the future.
                var at = message.At > now ? now : message.At;
                var received = new ChatMessage(message.Id, false, ChatText.Clean(message.Text), at);
                if (received.Text.Length == 0) continue;
                conversation.Messages.Add(received);
                conversation.Unread++;
                arrived.Add(received);
            }

            for (var index = 0; index < conversation.Messages.Count; index++)
            {
                var message = conversation.Messages[index];
                if (message.Mine && !message.Delivered && message.Id <= note.Ack)
                {
                    conversation.Messages[index] = message with { Delivered = true };
                    delivered = true;
                }
            }

            if (arrived.Count > 0 || delivered)
            {
                Trim(conversation);
                Save(state);
            }
        }

        if (arrived.Count > 0 || delivered) Changed?.Invoke(friendKey);
        return arrived;
    }

    public void Forget(string friendKey)
    {
        lock (_gate)
        {
            var state = Load();
            if (state.Conversations.Remove(friendKey)) Save(state);
        }
    }

    private static ConversationState ConversationOf(State state, string friendKey)
    {
        if (!state.Conversations.TryGetValue(friendKey, out var conversation))
        {
            conversation = new ConversationState();
            state.Conversations[friendKey] = conversation;
        }
        return conversation;
    }

    private static void Trim(ConversationState conversation)
    {
        if (conversation.Messages.Count > MaximumHistory)
            conversation.Messages.RemoveRange(0, conversation.Messages.Count - MaximumHistory);
    }

    private State Load()
    {
        if (_state is not null) return _state;
        try
        {
            if (File.Exists(_file))
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(_file), _json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        _state ??= new State();
        return _state;
    }

    private void Save(State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, _json));
            File.Move(temporary, _file, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for this session; the next successful save writes it.
        }
    }

    private sealed class State
    {
        public long LastSentId { get; set; }
        public Dictionary<string, ConversationState> Conversations { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ConversationState
    {
        public List<ChatMessage> Messages { get; set; } = new();
        public List<InviteReply> Replies { get; set; } = new();
        public long LastReceivedId { get; set; }
        public int Unread { get; set; }
    }
}
