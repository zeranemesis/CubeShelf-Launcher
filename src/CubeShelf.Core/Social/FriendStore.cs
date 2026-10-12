using System.Text.Json;

namespace CubeShelf.Core.Social;

public sealed class Friend
{
    /// <summary>Base64 of the peer's 65-byte identity. The primary key of a friend.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>What we call them locally. Their published name never overwrites this.</summary>
    public string DisplayName { get; set; } = "";

    public string PresenceUrl { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Highest sequence accepted from this peer; anything at or below it is a replay.</summary>
    public long LastSequence { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// A temporary quiet: they receive nothing and are not read, but nothing is forgotten and
    /// one click puts it back. Takes effect on the next publish, with no other co-ordination.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// Blocked. Like paused in what it stops, and different in what it means: the entry is kept
    /// deliberately as a tombstone so the same code cannot be re-added by accident later.
    ///
    /// It is local, and it has to be, because there is no server to enforce anything. It stops
    /// us addressing them and stops us reading them. It does not stop them watching our address
    /// change -- and so knowing when we play -- nor retract what they already downloaded. Only
    /// a new publishing address ends that.
    /// </summary>
    public bool Blocked { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    /// <summary>Cached ETag, so polling an unchanged document costs a 304 rather than a download.</summary>
    public string? LastETag { get; set; }

    /// <summary>
    /// How many polls in a row have failed, and when to try again.
    ///
    /// Persisted rather than kept in memory because the failure being defended against -- an
    /// address that is simply gone -- is permanent, while a launcher restarts several times a
    /// day. In-memory backoff would resume full-rate polling of a dead address every launch.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// Whether their last document was addressed to us: true once one opened, false when one was
    /// read and did not, null before anything was read. False is by far most often "they have not
    /// added us back yet" -- friendship here goes one way at a time -- and is what lets the list
    /// say so instead of showing someone who simply never appears.
    /// </summary>
    public bool? SharesWithUs { get; set; }

    /// <summary>
    /// How to reach their CubeShelf directly on the network, as their last document said
    /// (<see cref="Mesh.MeshPeerAddress"/>, base64). Null until one said.
    /// </summary>
    public string? MeshAddress { get; set; }
}

public enum FriendAddRefusal
{
    None,
    OwnCode,
    AlreadyFriend,
    Blocked
}

/// <summary>
/// The friends list, in the user's profile. Small enough to rewrite whole on every change, and
/// written through a temporary file so an interrupted save cannot leave a truncated list.
/// </summary>
public sealed class FriendStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private string? _lastGood;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FriendStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _file = Path.Combine(Path.GetFullPath(configurationDirectory), "friends.json");
    }

    public string FriendsFile => _file;

    public IReadOnlyList<Friend> Load()
    {
        lock (_gate)
            return LoadForReading();
    }

    /// <summary>
    /// Adds a peer from a decoded friend code. Returns false with a reason when the peer is
    /// already known or is this installation itself.
    /// </summary>
    public bool TryAdd(
        FriendCodePayload payload,
        string displayName,
        ReadOnlySpan<byte> ownPublicKey,
        out string error)
    {
        var added = TryAddWithReason(payload, displayName, ownPublicKey, out var refusal);
        error = refusal switch
        {
            FriendAddRefusal.OwnCode => "Ce code ami est le tien.",
            FriendAddRefusal.Blocked => "Tu as bloqué cette personne. Débloque-la d’abord si c’est voulu.",
            FriendAddRefusal.AlreadyFriend => "Cet ami est déjà dans ta liste.",
            _ => ""
        };
        return added;
    }

    /// <summary>As the other overload, with the reason as a value the screen can word in its own language.</summary>
    public bool TryAddWithReason(
        FriendCodePayload payload,
        string displayName,
        ReadOnlySpan<byte> ownPublicKey,
        out FriendAddRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(payload);
        refusal = FriendAddRefusal.None;

        if (payload.PublicKey.AsSpan().SequenceEqual(ownPublicKey))
        {
            refusal = FriendAddRefusal.OwnCode;
            return false;
        }

        var encoded = Convert.ToBase64String(payload.PublicKey);
        lock (_gate)
        {
            var friends = LoadUnsynchronized().ToList();
            var existing = friends.FirstOrDefault(friend => friend.PublicKey == encoded);

            // The whole point of keeping a blocked entry is that a code pasted again months
            // later does not quietly undo the decision.
            if (existing is { Blocked: true })
            {
                refusal = FriendAddRefusal.Blocked;
                return false;
            }

            if (existing is not null)
            {
                refusal = FriendAddRefusal.AlreadyFriend;
                return false;
            }

            friends.Add(new Friend
            {
                PublicKey = encoded,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Ami" : displayName.Trim(),
                PresenceUrl = payload.PresenceUrl,
                AddedAt = DateTimeOffset.UtcNow
            });
            SaveUnsynchronized(friends);
        }

        return true;
    }

    public bool Remove(string publicKeyBase64)
    {
        lock (_gate)
        {
            var friends = LoadUnsynchronized().ToList();
            var removed = friends.RemoveAll(friend =>
                string.Equals(friend.PublicKey, publicKeyBase64, StringComparison.Ordinal)) > 0;
            if (removed) SaveUnsynchronized(friends);
            return removed;
        }
    }

    /// <summary>Applies a change to one friend, if it is still there, and persists the result.</summary>
    public bool Update(string publicKeyBase64, Action<Friend> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            var friends = LoadUnsynchronized().ToList();
            var target = friends.FirstOrDefault(friend =>
                string.Equals(friend.PublicKey, publicKeyBase64, StringComparison.Ordinal));
            if (target is null) return false;

            change(target);
            SaveUnsynchronized(friends);
            return true;
        }
    }

    /// <summary>
    /// Records a snapshot only if it is newer than the last one accepted from that peer. The
    /// sequence check is what makes a replayed copy of an older published document inert.
    /// </summary>
    public bool TryAcceptSequence(string publicKeyBase64, long sequence, DateTimeOffset seenAt)
    {
        lock (_gate)
        {
            var friends = LoadUnsynchronized().ToList();
            var target = friends.FirstOrDefault(friend =>
                string.Equals(friend.PublicKey, publicKeyBase64, StringComparison.Ordinal));
            if (target is null || sequence <= target.LastSequence) return false;

            target.LastSequence = sequence;
            target.LastSeenAt = seenAt;
            SaveUnsynchronized(friends);
            return true;
        }
    }

    /// <summary>
    /// Blocks a peer, keeping the entry as a tombstone. Works on someone already in the list and
    /// on a code that was never added, which is the case that matters: blocking should not
    /// require befriending first.
    /// </summary>
    public void Block(FriendCodePayload payload, string displayName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var encoded = Convert.ToBase64String(payload.PublicKey);

        lock (_gate)
        {
            var friends = LoadUnsynchronized().ToList();
            var target = friends.FirstOrDefault(friend => friend.PublicKey == encoded);
            if (target is null)
            {
                target = new Friend
                {
                    PublicKey = encoded,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Bloqué" : displayName.Trim(),
                    PresenceUrl = payload.PresenceUrl,
                    AddedAt = DateTimeOffset.UtcNow
                };
                friends.Add(target);
            }

            target.Blocked = true;
            target.BlockedAt = DateTimeOffset.UtcNow;
            target.Paused = false;
            SaveUnsynchronized(friends);
        }
    }

    /// <summary>
    /// Takes the address a friend's own authenticated document states as theirs, when it is a
    /// usable https address and differs from the one we hold. This is how a friend met on the
    /// local network, with no address yet, becomes reachable from anywhere once they publish one,
    /// and how a friend who moved their document is followed without a new code. Only a document
    /// that opened under the key we share with them gets here, so only they can move themselves.
    /// </summary>
    public bool AdoptAddress(string publicKeyBase64, string? statedAddress)
    {
        var address = Lan.LanProtocol.SafeUrl(statedAddress);
        if (address.Length == 0) return false;

        var adopted = false;
        Update(publicKeyBase64, friend =>
        {
            if (string.Equals(friend.PresenceUrl, address, StringComparison.Ordinal)) return;
            friend.PresenceUrl = address;
            // Everything learnt about the old address says nothing about the new one.
            friend.LastETag = null;
            friend.ConsecutiveFailures = 0;
            friend.NextAttemptAt = null;
            adopted = true;
        });
        return adopted;
    }

    /// <summary>
    /// Takes the network address a friend's own authenticated document states, as <see cref="AdoptAddress"/>
    /// does for the https one. Only something that decodes is kept.
    /// </summary>
    public bool AdoptMeshAddress(string publicKeyBase64, string? statedAddress)
    {
        if (Mesh.MeshPeerAddress.FromBase64(statedAddress) is not { IsEmpty: false }) return false;
        var adopted = false;
        Update(publicKeyBase64, friend =>
        {
            if (string.Equals(friend.MeshAddress, statedAddress, StringComparison.Ordinal)) return;
            friend.MeshAddress = statedAddress;
            adopted = true;
        });
        return adopted;
    }

    public bool Unblock(string publicKeyBase64) =>
        Update(publicKeyBase64, friend =>
        {
            friend.Blocked = false;
            friend.BlockedAt = null;
        });

    /// <summary>The peers a document should currently be addressed to, for counting on screen.</summary>
    public IReadOnlyList<byte[]> ActiveRecipients() => RecipientsOf(Load());

    /// <summary>
    /// The peers a document is about to be sealed for. Unlike <see cref="ActiveRecipients"/>, a
    /// list that cannot be read is reported rather than taken as empty: a document sealed for
    /// nobody tells every friend, wrongly, that they were dropped.
    /// </summary>
    public IReadOnlyList<byte[]> ActiveRecipientsForPublication()
    {
        lock (_gate)
            return RecipientsOf(LoadUnsynchronized());
    }

    private static IReadOnlyList<byte[]> RecipientsOf(IEnumerable<Friend> friends) =>
        friends
            .Where(friend => !friend.Paused && !friend.Blocked)
            .Select(friend => TryDecodeKey(friend.PublicKey))
            .OfType<byte[]>()
            .ToArray();

    private static byte[]? TryDecodeKey(string encoded)
    {
        try
        {
            var key = Convert.FromBase64String(encoded);
            PeerIdentity.ValidatePublicKey(key);
            return key;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The list as it is on disk. When the file is held by another program for longer than a
    /// short retry, the last list read or written stands in for it -- the friends have not gone
    /// anywhere just because an antivirus is scanning the file. Only with nothing to stand in
    /// does this report <see cref="ProfileFileUnavailableException"/>, so that no change is ever
    /// saved on top of a list that was never read.
    /// </summary>
    private List<Friend> LoadUnsynchronized()
    {
        string? text;
        try
        {
            text = ProfileFiles.ReadText(_file);
        }
        catch (ProfileFileUnavailableException)
        {
            if (_lastGood is null) throw;
            text = _lastGood;
        }

        if (text is null) return new();
        try
        {
            var friends = JsonSerializer.Deserialize<List<Friend>>(text, _json) ?? new();
            _lastGood = text;
            // A hand-edited or partially written file should cost the unreadable entries, not the list.
            return friends.Where(friend => TryDecodeKey(friend.PublicKey) is not null).ToList();
        }
        catch (JsonException)
        {
            // Not a list at all. Kept under another name, for whoever wants it back, and the list
            // goes on from the last one this session knew -- or from empty, which is the truth.
            ProfileFiles.SetAside(_file);
            if (_lastGood is not null && !ReferenceEquals(text, _lastGood))
                return JsonSerializer.Deserialize<List<Friend>>(_lastGood, _json) ?? new();
            _lastGood = null;
            return new();
        }
    }

    private void SaveUnsynchronized(List<Friend> friends)
    {
        var text = JsonSerializer.Serialize(friends, _json);
        ProfileFiles.WriteText(_file, text);
        _lastGood = text;
    }

    /// <summary>The list for the screen and for loops that only read: never throws, empty at worst.</summary>
    private List<Friend> LoadForReading()
    {
        try
        {
            return LoadUnsynchronized();
        }
        catch (ProfileFileUnavailableException)
        {
            return new();
        }
    }
}
