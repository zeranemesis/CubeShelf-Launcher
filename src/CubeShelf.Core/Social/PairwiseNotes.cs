using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeShelf.Core.Social;

/// <summary>One short message to one friend.</summary>
/// <param name="Id">Increasing per sender, so the receiver keeps each message once and acknowledges up to a number.</param>
public sealed record NoteMessage(long Id, string Text, DateTimeOffset At);

/// <summary>An answer to one invitation: "joined" or "declined".</summary>
/// <param name="Invite">The invitation's <see cref="PresenceInvite.Id"/>.</param>
public sealed record InviteReply(string Invite, string Reply, DateTimeOffset At)
{
    public const string Joined = "joined";
    public const string Declined = "declined";
}

/// <summary>
/// Everything one peer has to say to one friend in particular: messages not yet acknowledged,
/// how far it has read the friend's own messages, and answers to their invitations.
/// </summary>
public sealed record PairwiseNote(
    IReadOnlyList<NoteMessage> Messages,
    long Ack,
    IReadOnlyList<InviteReply> Replies)
{
    public static PairwiseNote Empty { get; } = new(Array.Empty<NoteMessage>(), 0, Array.Empty<InviteReply>());

    public bool IsEmpty => Messages.Count == 0 && Ack == 0 && Replies.Count == 0;
}

/// <summary>A <see cref="PairwiseNote"/> as published: readable by its one recipient only.</summary>
public sealed class SealedNote
{
    [JsonPropertyName("h")] public string Hint { get; set; } = "";
    [JsonPropertyName("n")] public string Nonce { get; set; } = "";
    [JsonPropertyName("c")] public string Ciphertext { get; set; } = "";
    [JsonPropertyName("t")] public string Tag { get; set; } = "";
}

/// <summary>
/// Notes from one peer to one friend, inside the presence document.
///
/// The document is sealed once for all friends, so anything in it is read by all of them. A
/// message to one friend, or an answer to their invitation, is sealed a second time, under a key
/// only that pair can derive, before it goes in. Other friends see that notes exist; the count is
/// padded with decoys, like the lockboxes, so it does not say how many friends one is talking to.
/// The direction is bound into the associated data, so a note from A to B can never be passed off
/// as one from B to A.
/// </summary>
public static class PairwiseNotes
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HintLength = 8;
    private const int Padding = 4;

    /// <summary>Notes are small: a few messages of a few hundred characters.</summary>
    public const int MaximumNoteBytes = 64 * 1024;
    public const int MaximumNotes = 512;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Seals one note per friend that has something to say, plus decoys, shuffled. Friends whose
    /// note is empty get nothing at all.
    /// </summary>
    public static IReadOnlyList<SealedNote> SealAll(PeerIdentity author, IReadOnlyDictionary<string, PairwiseNote> notes)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(notes);

        var sealedNotes = new List<SealedNote>();
        foreach (var (recipient, note) in notes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (note.IsEmpty) continue;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(recipient);
                PeerIdentity.ValidatePublicKey(key);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                continue;
            }
            sealedNotes.Add(Seal(author, key, note));
        }

        if (sealedNotes.Count == 0) return Array.Empty<SealedNote>();

        while (sealedNotes.Count % Padding != 0) sealedNotes.Add(Decoy());
        for (var index = sealedNotes.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (sealedNotes[index], sealedNotes[swap]) = (sealedNotes[swap], sealedNotes[index]);
        }
        return sealedNotes;
    }

    public static SealedNote Seal(PeerIdentity author, byte[] recipientPublicKey, PairwiseNote note)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(note);

        var pairwise = author.DeriveSharedKey(recipientPublicKey);
        var key = Derive(pairwise, "cubeshelf-note-key-v1");
        try
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(note, Json);
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagLength];
            using (var gcm = new AesGcm(key, TagLength))
                gcm.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(author.PublicKey, recipientPublicKey));

            return new SealedNote
            {
                Hint = Convert.ToBase64String(Hint(pairwise, author.PublicKey)),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairwise);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Finds and opens the note <paramref name="author"/> addressed to us, if there is one.</summary>
    public static bool TryOpen(
        PeerIdentity reader,
        byte[] authorPublicKey,
        IReadOnlyList<SealedNote>? notes,
        out PairwiseNote? note)
    {
        ArgumentNullException.ThrowIfNull(reader);
        note = null;
        if (notes is null || notes.Count is 0 or > MaximumNotes) return false;

        byte[] pairwise;
        try
        {
            pairwise = reader.DeriveSharedKey(authorPublicKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }

        var key = Derive(pairwise, "cubeshelf-note-key-v1");
        try
        {
            var hint = Hint(pairwise, authorPublicKey);
            foreach (var candidate in notes)
            {
                if (!TryBase64(candidate.Hint, out var candidateHint) || !CryptographicOperations.FixedTimeEquals(hint, candidateHint))
                    continue;
                if (!TryBase64(candidate.Nonce, out var nonce) || nonce.Length != NonceLength ||
                    !TryBase64(candidate.Tag, out var tag) || tag.Length != TagLength ||
                    !TryBase64(candidate.Ciphertext, out var ciphertext) || ciphertext.Length > MaximumNoteBytes)
                    continue;

                var plaintext = new byte[ciphertext.Length];
                try
                {
                    using (var gcm = new AesGcm(key, TagLength))
                        gcm.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(authorPublicKey, reader.PublicKey));
                    note = Sanitize(JsonSerializer.Deserialize<PairwiseNote>(plaintext, Json));
                    return note is not null;
                }
                catch (Exception exception) when (exception is CryptographicException or JsonException)
                {
                    return false;
                }
            }
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairwise);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// A note opened under the pair key is authentic, not necessarily well-formed: whatever the
    /// friend's software wrote, only plain, bounded text comes out.
    /// </summary>
    private static PairwiseNote? Sanitize(PairwiseNote? note)
    {
        if (note is null) return null;
        var messages = (note.Messages ?? Array.Empty<NoteMessage>())
            .Where(message => message is not null && message.Id > 0)
            .Take(100)
            .Select(message => message with { Text = ChatText.Clean(message.Text) })
            .Where(message => message.Text.Length > 0)
            .ToArray();
        var replies = (note.Replies ?? Array.Empty<InviteReply>())
            .Where(reply => reply is not null &&
                            reply.Reply is InviteReply.Joined or InviteReply.Declined &&
                            !string.IsNullOrEmpty(reply.Invite) && reply.Invite.Length <= 64)
            .Take(20)
            .ToArray();
        return new PairwiseNote(messages, Math.Max(0, note.Ack), replies);
    }

    private static SealedNote Decoy() => new()
    {
        Hint = Convert.ToBase64String(RandomNumberGenerator.GetBytes(HintLength)),
        Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceLength)),
        Ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64 + RandomNumberGenerator.GetInt32(192))),
        Tag = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TagLength))
    };

    private static byte[] Derive(byte[] pairwise, string context) =>
        HMACSHA256.HashData(pairwise, Encoding.ASCII.GetBytes(context));

    /// <summary>Per pair and per direction: the hint of A's note to B differs from B's to A.</summary>
    private static byte[] Hint(byte[] pairwise, ReadOnlySpan<byte> authorPublicKey)
    {
        var context = Encoding.ASCII.GetBytes("cubeshelf-note-hint-v1|");
        var input = new byte[context.Length + authorPublicKey.Length];
        context.CopyTo(input, 0);
        authorPublicKey.CopyTo(input.AsSpan(context.Length));
        return HMACSHA256.HashData(pairwise, input).AsSpan(0, HintLength).ToArray();
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> author, ReadOnlySpan<byte> recipient)
    {
        var context = Encoding.ASCII.GetBytes("cubeshelf-note-v1");
        var aad = new byte[context.Length + author.Length + recipient.Length];
        context.CopyTo(aad, 0);
        author.CopyTo(aad.AsSpan(context.Length));
        recipient.CopyTo(aad.AsSpan(context.Length + author.Length));
        return aad;
    }

    private static bool TryBase64(string? value, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>A digest of what would be said, so an unchanged conversation does not force a publish.</summary>
    public static string Digest(IReadOnlyDictionary<string, PairwiseNote>? notes)
    {
        if (notes is null || notes.Count == 0) return "";
        var canonical = notes
            .Where(pair => !pair.Value.IsEmpty)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + JsonSerializer.Serialize(pair.Value, Json));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical))));
    }
}

/// <summary>What a short message may contain: text, on one or a few lines, and not much of it.</summary>
public static class ChatText
{
    public const int MaximumLength = 500;

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, MaximumLength));
        foreach (var rune in text.EnumerateRunes())
        {
            if (builder.Length >= MaximumLength) break;
            // Newlines stay, every other control character goes: no terminal escapes, no
            // invisible tricks in a friend's name for a message.
            if (rune.Value == '\n') builder.Append('\n');
            else if (!Rune.IsControl(rune) && rune.Value is not (0x202E or 0x202D or 0x2066 or 0x2067 or 0x2068 or 0x2069))
                builder.Append(rune.ToString());
        }
        return builder.ToString().Trim();
    }
}
