using System.Net;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// A node another node can be told about: its key, the proof that makes its id, and where it
/// listens. The id is never sent -- it is recomputed from the key and the nonce, so a contact
/// cannot claim a position it did not pay for.
/// </summary>
public sealed record NodeContact(byte[] PublicKey, ulong Nonce, IPEndPoint Endpoint)
{
    public NodeId Id { get; } = NodeProof.IdOf(PublicKey, Nonce);

    public void WriteTo(MeshWriter writer) => writer.Fixed(PublicKey).U64(Nonce).Endpoint(Endpoint);

    /// <summary>Reads one contact; null (and the reader failed) for anything malformed or unproven.</summary>
    public static NodeContact? Read(ref MeshReader reader, int difficulty)
    {
        var key = reader.Fixed(MeshCrypto.PublicKeyLength).ToArray();
        var nonce = reader.U64();
        var endpoint = reader.Endpoint();
        if (!reader.Ok || endpoint is null) return null;
        if (!MeshCrypto.IsOnCurve(key) || !NodeProof.IsValid(key, nonce, difficulty)) return null;
        return new NodeContact(key, nonce, MeshAddresses.Normalize(endpoint));
    }

    public static NodeContact Of(MeshSession session, IPEndPoint endpoint) =>
        new(session.RemotePublicKey, session.RemoteNonce, MeshAddresses.Normalize(endpoint));
}

public enum RoutingResult
{
    Added,
    Updated,

    /// <summary>The bucket is full of nodes that answer; kept aside in case one of them stops.</summary>
    Cached,

    /// <summary>Not routable, ourselves, or over a diversity limit.</summary>
    Refused
}

/// <summary>
/// Kademlia's k-buckets, with the defences a public network needs.
///
/// Only nodes we have completed a handshake with at the address recorded get in -- a contact
/// someone merely told us about is a candidate to try, never an entry. Each bucket holds at most
/// two nodes of one neighbourhood (an IPv4 /24, an IPv6 /48) and the table at most two per
/// address, so whoever controls one address or one hosting range cannot fill a region of the
/// table, however many ids they pay for. And a full bucket keeps the nodes it already has as long
/// as they answer: long-lived nodes are the most reliable, and an attacker cannot evict them just
/// by showing up (the classic eclipse defence of the original paper).
/// </summary>
public sealed class RoutingTable
{
    public const int DefaultBucketSize = 16;
    private const int ReplacementCacheSize = 8;
    private const int PerNeighbourhoodPerBucket = 2;
    private const int PerAddress = 2;
    private const int FailuresBeforeEviction = 2;

    private readonly object _gate = new();
    private readonly Bucket[] _buckets = new Bucket[256];
    private readonly Func<IPAddress, bool> _isRoutable;
    private readonly int _bucketSize;

    public RoutingTable(NodeId local, Func<IPAddress, bool>? isRoutable = null, int bucketSize = DefaultBucketSize)
    {
        Local = local;
        _isRoutable = isRoutable ?? MeshAddresses.IsPublic;
        _bucketSize = bucketSize;
        for (var index = 0; index < _buckets.Length; index++) _buckets[index] = new Bucket();
    }

    public NodeId Local { get; }

    /// <summary>Whether an address is one this table would ever hold.</summary>
    public bool IsRoutable(IPAddress address) => _isRoutable(address);

    public int BucketSize => _bucketSize;

    public int Count
    {
        get
        {
            lock (_gate) return _buckets.Sum(bucket => bucket.Entries.Count);
        }
    }

    /// <summary>
    /// Records a node that just proved itself at <see cref="NodeContact.Endpoint"/> -- a handshake
    /// we completed there, or an answer to a request.
    /// </summary>
    public RoutingResult Observe(NodeContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        if (contact.Id == Local || !_isRoutable(contact.Endpoint.Address)) return RoutingResult.Refused;

        lock (_gate)
        {
            var bucket = _buckets[BucketOf(contact.Id)];
            bucket.TouchedAt = Environment.TickCount64;
            var existing = bucket.Entries.FindIndex(entry => entry.Contact.Id == contact.Id);
            if (existing >= 0)
            {
                var entry = bucket.Entries[existing];
                // Same id at a new address: the new address was just proven, so it wins -- unless
                // that would break the per-address limit.
                if (!entry.Contact.Endpoint.Equals(contact.Endpoint) && CountAtAddress(contact.Endpoint.Address, contact.Id) >= PerAddress)
                    return RoutingResult.Refused;
                bucket.Entries[existing] = entry with { Contact = contact, LastSeen = Environment.TickCount64, Failures = 0 };
                return RoutingResult.Updated;
            }

            if (CountAtAddress(contact.Endpoint.Address, contact.Id) >= PerAddress) return RoutingResult.Refused;
            var neighbourhood = MeshAddresses.Neighbourhood(contact.Endpoint.Address);
            if (bucket.Entries.Count(entry => MeshAddresses.Neighbourhood(entry.Contact.Endpoint.Address) == neighbourhood) >= PerNeighbourhoodPerBucket)
                return RoutingResult.Refused;

            var fresh = new Entry(contact, Environment.TickCount64, 0);
            if (bucket.Entries.Count < _bucketSize)
            {
                bucket.Entries.Add(fresh);
                bucket.Replacements.RemoveAll(entry => entry.Contact.Id == contact.Id);
                return RoutingResult.Added;
            }

            // A node that has been failing gives up its place; one that answers keeps it.
            var failing = bucket.Entries.FindIndex(entry => entry.Failures > 0);
            if (failing >= 0)
            {
                bucket.Entries[failing] = fresh;
                return RoutingResult.Added;
            }

            bucket.Replacements.RemoveAll(entry => entry.Contact.Id == contact.Id);
            bucket.Replacements.Insert(0, fresh);
            if (bucket.Replacements.Count > ReplacementCacheSize) bucket.Replacements.RemoveAt(bucket.Replacements.Count - 1);
            return RoutingResult.Cached;
        }
    }

    /// <summary>A node did not answer. Twice in a row and it is replaced by the freshest candidate waiting.</summary>
    public void Failed(NodeId id)
    {
        lock (_gate)
        {
            var bucket = _buckets[BucketOf(id)];
            var index = bucket.Entries.FindIndex(entry => entry.Contact.Id == id);
            if (index < 0)
            {
                bucket.Replacements.RemoveAll(entry => entry.Contact.Id == id);
                return;
            }

            var entry = bucket.Entries[index];
            if (entry.Failures + 1 < FailuresBeforeEviction)
            {
                bucket.Entries[index] = entry with { Failures = entry.Failures + 1 };
                return;
            }

            bucket.Entries.RemoveAt(index);
            foreach (var candidate in bucket.Replacements.ToArray())
            {
                bucket.Replacements.Remove(candidate);
                if (CountAtAddress(candidate.Contact.Endpoint.Address, candidate.Contact.Id) >= PerAddress) continue;
                bucket.Entries.Add(candidate with { Failures = 0 });
                break;
            }
        }
    }

    public void Remove(NodeId id)
    {
        lock (_gate)
        {
            var bucket = _buckets[BucketOf(id)];
            bucket.Entries.RemoveAll(entry => entry.Contact.Id == id);
            bucket.Replacements.RemoveAll(entry => entry.Contact.Id == id);
        }
    }

    public NodeContact? Get(NodeId id)
    {
        if (id == Local) return null;
        lock (_gate) return _buckets[BucketOf(id)].Entries.FirstOrDefault(entry => entry.Contact.Id == id)?.Contact;
    }

    /// <summary>The <paramref name="count"/> known nodes closest to <paramref name="target"/>, closest first.</summary>
    public IReadOnlyList<NodeContact> Closest(NodeId target, int count)
    {
        lock (_gate)
        {
            return _buckets
                .SelectMany(bucket => bucket.Entries)
                .Select(entry => entry.Contact)
                .OrderBy(contact => contact.Id.Xor(target))
                .Take(count)
                .ToArray();
        }
    }

    public IReadOnlyList<NodeContact> All()
    {
        lock (_gate) return _buckets.SelectMany(bucket => bucket.Entries).Select(entry => entry.Contact).ToArray();
    }

    /// <summary>The least recently heard node of each full bucket: the ones worth a ping.</summary>
    public IReadOnlyList<NodeContact> Stalest(TimeSpan olderThan)
    {
        var limit = Environment.TickCount64 - (long)olderThan.TotalMilliseconds;
        lock (_gate)
        {
            return _buckets
                .Where(bucket => bucket.Entries.Count > 0)
                .Select(bucket => bucket.Entries.MinBy(entry => entry.LastSeen)!)
                .Where(entry => entry.LastSeen < limit)
                .Select(entry => entry.Contact)
                .ToArray();
        }
    }

    /// <summary>
    /// Buckets nobody has looked up into for a while, up to one past the deepest non-empty one:
    /// below that, every bucket is empty because the network is small, not because it is stale.
    /// </summary>
    public IReadOnlyList<int> BucketsToRefresh(TimeSpan olderThan)
    {
        var limit = Environment.TickCount64 - (long)olderThan.TotalMilliseconds;
        lock (_gate)
        {
            var deepest = -1;
            for (var index = 0; index < _buckets.Length; index++)
                if (_buckets[index].Entries.Count > 0) deepest = index;
            var result = new List<int>();
            for (var index = 0; index <= Math.Min(deepest + 1, 255); index++)
                if (_buckets[index].TouchedAt < limit) result.Add(index);
            return result;
        }
    }

    public void MarkRefreshed(int bucket)
    {
        lock (_gate) _buckets[bucket].TouchedAt = Environment.TickCount64;
    }

    /// <summary>The bucket a node falls into: the length of the prefix it shares with us.</summary>
    public int BucketOf(NodeId id) => Math.Min(255, Local.Xor(id).LeadingZeroBits());

    private int CountAtAddress(IPAddress address, NodeId except) =>
        _buckets.Sum(bucket => bucket.Entries.Count(entry => entry.Contact.Id != except && entry.Contact.Endpoint.Address.Equals(address)));

    private sealed record Entry(NodeContact Contact, long LastSeen, int Failures);

    private sealed class Bucket
    {
        public List<Entry> Entries { get; } = new();
        public List<Entry> Replacements { get; } = new();
        public long TouchedAt { get; set; } = Environment.TickCount64;
    }
}
