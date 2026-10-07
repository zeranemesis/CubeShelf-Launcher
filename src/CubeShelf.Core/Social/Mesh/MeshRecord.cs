using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

public enum MeshRecordKind : byte
{
    /// <summary>
    /// One writer: the record lives at the hash of its signing key, so only whoever holds that key
    /// can write there, and a higher sequence replaces a lower one.
    /// </summary>
    Signed = 1,

    /// <summary>
    /// Many writers, one entry each, at a place anyone who knows it may drop something: a friend
    /// request waiting for someone who is not yet a friend. Each entry carries a small proof of
    /// work in its sequence field, which is what keeps a mailbox from being flooded for free.
    /// </summary>
    Mailbox = 2
}

/// <summary>
/// A value stored in the network: who signed it, until when it lives, and opaque bytes.
///
/// Nodes that hold a record cannot read it -- everything a CubeShelf stores is encrypted for its
/// readers before it gets here -- and cannot change it: the signature covers every field. The worst
/// a holder can do is not answer, which is why each record lives on several nodes.
/// </summary>
public sealed record MeshRecord(
    NodeId Locator,
    MeshRecordKind Kind,
    byte[] SignerKey,
    ulong Sequence,
    long ExpiresAt,
    byte[] Payload,
    byte[] Signature)
{
    public const int MaximumPayload = 96 * 1024;
    public const int MaximumMailboxPayload = 2048;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(24);

    /// <summary>Leading zero bits a mailbox entry's work hash needs: a few milliseconds per entry.</summary>
    public const int MailboxDifficulty = 14;

    /// <summary>Where a signed record lives: a function of its key, which is what makes the place its owner's alone.</summary>
    public static NodeId LocatorOf(ReadOnlySpan<byte> signerKey) =>
        NodeId.FromHash(MeshCrypto.Hash("cubeshelf-locator-v1", signerKey.ToArray()));

    public static MeshRecord CreateSigned(ECDsa signer, ulong sequence, DateTimeOffset expiresAt, byte[] payload)
    {
        var key = MeshCrypto.PublicKeyOf(signer);
        var locator = LocatorOf(key);
        var expires = expiresAt.ToUnixTimeSeconds();
        var signature = MeshCrypto.Sign(signer, SignedBytes(locator, MeshRecordKind.Signed, key, sequence, expires, payload));
        return new MeshRecord(locator, MeshRecordKind.Signed, key, sequence, expires, payload, signature);
    }

    /// <summary>A mailbox entry, with its proof of work searched for here.</summary>
    public static MeshRecord CreateMailboxEntry(ECDsa signer, NodeId mailbox, DateTimeOffset expiresAt, byte[] payload, int difficulty = MailboxDifficulty)
    {
        var key = MeshCrypto.PublicKeyOf(signer);
        var expires = expiresAt.ToUnixTimeSeconds();
        var work = SolveMailboxWork(mailbox, key, payload, difficulty);
        var signature = MeshCrypto.Sign(signer, SignedBytes(mailbox, MeshRecordKind.Mailbox, key, work, expires, payload));
        return new MeshRecord(mailbox, MeshRecordKind.Mailbox, key, work, expires, payload, signature);
    }

    /// <summary>Every rule a holder enforces before keeping a record. Cheap checks first, the signature last.</summary>
    public bool IsValid(DateTimeOffset now, int mailboxDifficulty = MailboxDifficulty)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        if (ExpiresAt <= nowSeconds || ExpiresAt > nowSeconds + (long)MaximumLifetime.TotalSeconds + 300) return false;
        if (Signature.Length != MeshCrypto.SignatureLength || !MeshCrypto.HasPublicKeyShape(SignerKey)) return false;

        switch (Kind)
        {
            case MeshRecordKind.Signed:
                if (Payload.Length > MaximumPayload || Locator != LocatorOf(SignerKey)) return false;
                break;
            case MeshRecordKind.Mailbox:
                if (Payload.Length > MaximumMailboxPayload ||
                    NodeProof.LeadingZeroBits(MailboxWork(Locator, SignerKey, Payload, Sequence)) < mailboxDifficulty)
                    return false;
                break;
            default:
                return false;
        }

        return MeshCrypto.Verify(SignerKey, SignedBytes(Locator, Kind, SignerKey, Sequence, ExpiresAt, Payload), Signature);
    }

    public int Size => NodeId.Length + 1 + SignerKey.Length + 8 + 8 + Payload.Length + Signature.Length;

    public void WriteTo(MeshWriter writer) =>
        writer.Id(Locator).U8((byte)Kind).Fixed(SignerKey).U64(Sequence).U64((ulong)ExpiresAt).LargeBlob(Payload).Fixed(Signature);

    public static MeshRecord? Read(ref MeshReader reader)
    {
        var locator = reader.Id();
        var kind = (MeshRecordKind)reader.U8();
        var key = reader.Fixed(MeshCrypto.PublicKeyLength).ToArray();
        var sequence = reader.U64();
        var expires = (long)reader.U64();
        var payload = reader.LargeBlob(MaximumPayload).ToArray();
        var signature = reader.Fixed(MeshCrypto.SignatureLength).ToArray();
        if (!reader.Ok || kind is not (MeshRecordKind.Signed or MeshRecordKind.Mailbox) || expires < 0) return null;
        return new MeshRecord(locator, kind, key, sequence, expires, payload, signature);
    }

    private static byte[] SignedBytes(NodeId locator, MeshRecordKind kind, byte[] key, ulong sequence, long expires, byte[] payload) =>
        MeshCrypto.Hash("cubeshelf-record-v1", locator.ToBytes(), new[] { (byte)kind }, key,
            BigEndian(sequence), BigEndian((ulong)expires), payload);

    private static byte[] BigEndian(ulong value)
    {
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] MailboxWork(NodeId mailbox, byte[] key, byte[] payload, ulong nonce) =>
        MeshCrypto.Hash("cubeshelf-mailbox-work-v1", mailbox.ToBytes(), key, SHA256.HashData(payload), BigEndian(nonce));

    private static ulong SolveMailboxWork(NodeId mailbox, byte[] key, byte[] payload, int difficulty)
    {
        var digest = SHA256.HashData(payload);
        var mailboxBytes = mailbox.ToBytes();
        for (var nonce = (ulong)Random.Shared.NextInt64(); ; nonce++)
            if (NodeProof.LeadingZeroBits(MeshCrypto.Hash("cubeshelf-mailbox-work-v1", mailboxBytes, key, digest, BigEndian(nonce))) >= difficulty)
                return nonce;
    }
}

public enum StoreResult : byte
{
    Stored = 0,
    Unchanged = 1,
    Stale = 2,
    Invalid = 3,
    Full = 4,
    NotResponsible = 5
}

/// <summary>
/// The records this node holds for the network: what everyone contributes, and what makes the
/// network sturdier the larger it grows.
///
/// Bounded every way a stranger could push on it: a total count and size, a share per
/// neighbourhood of addresses (one household cannot fill it), a lifetime of at most a day, and a
/// cap on mailbox entries per mailbox.
/// </summary>
public sealed class RecordStore
{
    private readonly object _gate = new();
    private readonly Dictionary<NodeId, MeshRecord> _signed = new();
    private readonly Dictionary<NodeId, List<MeshRecord>> _mailboxes = new();
    private readonly Dictionary<(NodeId, string), (string Source, long ReceivedAt)> _origin = new();
    private long _bytes;

    public RecordStore(int maximumRecords = 4096, long maximumBytes = 32L * 1024 * 1024, int perSourceRecords = 256, long perSourceBytes = 4L * 1024 * 1024, int mailboxEntries = 32)
    {
        MaximumRecords = maximumRecords;
        MaximumBytes = maximumBytes;
        PerSourceRecords = perSourceRecords;
        PerSourceBytes = perSourceBytes;
        MailboxEntries = mailboxEntries;
    }

    public int MaximumRecords { get; }
    public long MaximumBytes { get; }
    public int PerSourceRecords { get; }
    public long PerSourceBytes { get; }
    public int MailboxEntries { get; }

    /// <summary>The mailbox work required of entries; lowered only by tests.</summary>
    public int MailboxDifficulty { get; init; } = MeshRecord.MailboxDifficulty;

    public int Count
    {
        get
        {
            lock (_gate) return _signed.Count + _mailboxes.Values.Sum(list => list.Count);
        }
    }

    public long Bytes
    {
        get
        {
            lock (_gate) return _bytes;
        }
    }

    /// <param name="source">Who handed it over, as a neighbourhood; quotas are counted per source.</param>
    public StoreResult Put(MeshRecord record, string source, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.IsValid(now, MailboxDifficulty)) return StoreResult.Invalid;

        lock (_gate)
        {
            PruneUnsynchronized(now);
            var key = (record.Locator, Convert.ToBase64String(record.SignerKey));

            if (record.Kind == MeshRecordKind.Signed)
            {
                if (_signed.TryGetValue(record.Locator, out var existing))
                {
                    if (record.Sequence < existing.Sequence) return StoreResult.Stale;
                    if (record.Sequence == existing.Sequence)
                    {
                        // The same record again: a republish. It refreshes nothing but our note of when.
                        if (existing.Payload.AsSpan().SequenceEqual(record.Payload)) return Touch(key, source, now, StoreResult.Unchanged);
                        return StoreResult.Stale;
                    }
                    if (!Admit(source, record.Size - existing.Size)) return StoreResult.Full;
                    _bytes += record.Size - existing.Size;
                    _signed[record.Locator] = record;
                    return Touch(key, source, now, StoreResult.Stored);
                }

                if (!Admit(source, record.Size)) return StoreResult.Full;
                _signed[record.Locator] = record;
                _bytes += record.Size;
                return Touch(key, source, now, StoreResult.Stored);
            }

            if (!_mailboxes.TryGetValue(record.Locator, out var entries))
            {
                entries = new List<MeshRecord>();
                _mailboxes[record.Locator] = entries;
            }

            var same = entries.FindIndex(entry => entry.SignerKey.AsSpan().SequenceEqual(record.SignerKey));
            if (same >= 0)
            {
                if (entries[same].Payload.AsSpan().SequenceEqual(record.Payload) && entries[same].ExpiresAt >= record.ExpiresAt)
                    return Touch(key, source, now, StoreResult.Unchanged);
                _bytes += record.Size - entries[same].Size;
                entries[same] = record;
                return Touch(key, source, now, StoreResult.Stored);
            }

            if (!Admit(source, record.Size)) return StoreResult.Full;
            if (entries.Count >= MailboxEntries)
            {
                // A full mailbox drops the entry closest to expiring, not the newcomer: a flood
                // has to keep paying to keep the box full, and it still only fills one box.
                var victim = entries.MinBy(entry => entry.ExpiresAt)!;
                entries.Remove(victim);
                _bytes -= victim.Size;
                _origin.Remove((victim.Locator, Convert.ToBase64String(victim.SignerKey)));
            }
            entries.Add(record);
            _bytes += record.Size;
            return Touch(key, source, now, StoreResult.Stored);
        }
    }

    public IReadOnlyList<MeshRecord> Get(NodeId locator, DateTimeOffset now)
    {
        var seconds = now.ToUnixTimeSeconds();
        lock (_gate)
        {
            if (_signed.TryGetValue(locator, out var record))
                return record.ExpiresAt > seconds ? new[] { record } : Array.Empty<MeshRecord>();
            return _mailboxes.TryGetValue(locator, out var entries)
                ? entries.Where(entry => entry.ExpiresAt > seconds).ToArray()
                : Array.Empty<MeshRecord>();
        }
    }

    /// <summary>Records whose last copy arrived more than <paramref name="quietFor"/> ago: those whose owner may be gone.</summary>
    public IReadOnlyList<MeshRecord> NotRefreshedSince(TimeSpan quietFor, DateTimeOffset now)
    {
        var limit = now - quietFor;
        lock (_gate)
        {
            PruneUnsynchronized(now);
            return _signed.Values.Concat(_mailboxes.Values.SelectMany(list => list))
                .Where(record => _origin.TryGetValue((record.Locator, Convert.ToBase64String(record.SignerKey)), out var origin) &&
                                 DateTimeOffset.FromUnixTimeMilliseconds(origin.ReceivedAt) < limit)
                .ToArray();
        }
    }

    public void Prune(DateTimeOffset now)
    {
        lock (_gate) PruneUnsynchronized(now);
    }

    private StoreResult Touch((NodeId, string) key, string source, DateTimeOffset now, StoreResult result)
    {
        _origin[key] = (source, now.ToUnixTimeMilliseconds());
        return result;
    }

    private bool Admit(string source, long addedBytes)
    {
        if (addedBytes <= 0) return true;
        var total = _signed.Count + _mailboxes.Values.Sum(list => list.Count);
        if (total >= MaximumRecords || _bytes + addedBytes > MaximumBytes) return false;

        var fromSource = _origin.Where(entry => entry.Value.Source == source).Select(entry => entry.Key).ToArray();
        if (fromSource.Length >= PerSourceRecords) return false;
        long sourceBytes = 0;
        foreach (var (locator, signer) in fromSource)
        {
            if (_signed.TryGetValue(locator, out var record)) sourceBytes += record.Size;
            else if (_mailboxes.TryGetValue(locator, out var entries))
                sourceBytes += entries.Where(entry => Convert.ToBase64String(entry.SignerKey) == signer).Sum(entry => (long)entry.Size);
        }
        return sourceBytes + addedBytes <= PerSourceBytes;
    }

    private void PruneUnsynchronized(DateTimeOffset now)
    {
        var seconds = now.ToUnixTimeSeconds();
        foreach (var (locator, record) in _signed.Where(entry => entry.Value.ExpiresAt <= seconds).ToArray())
        {
            _signed.Remove(locator);
            _bytes -= record.Size;
            _origin.Remove((locator, Convert.ToBase64String(record.SignerKey)));
        }
        foreach (var (locator, entries) in _mailboxes.ToArray())
        {
            foreach (var expired in entries.Where(entry => entry.ExpiresAt <= seconds).ToArray())
            {
                entries.Remove(expired);
                _bytes -= expired.Size;
                _origin.Remove((locator, Convert.ToBase64String(expired.SignerKey)));
            }
            if (entries.Count == 0) _mailboxes.Remove(locator);
        }
    }
}
