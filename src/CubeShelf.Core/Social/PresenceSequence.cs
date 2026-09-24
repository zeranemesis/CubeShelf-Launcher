using System.Text.Json;

namespace CubeShelf.Core.Social;

/// <summary>What a read-back of our own published document says about who else is publishing it.</summary>
public enum PresenceSequenceReconciliation
{
    /// <summary>What we published is at or below where we are. Normal.</summary>
    Consistent = 0,

    /// <summary>
    /// The document at our own URL carries a sequence we never issued. Another installation is
    /// publishing under this identity -- a copied profile, typically a second machine.
    /// </summary>
    RemoteAhead = 1
}

/// <summary>
/// The monotonic counter stamped on every published document.
///
/// This is the one number in the feature that must never go backwards. A friend that has seen
/// a higher sequence rejects everything at or below it (<see cref="FriendStore.TryAcceptSequence"/>),
/// and there is no reset: recovery would need that friend to remove and re-add us, on their
/// machine. So the counter is defended twice over.
///
/// It is reserved in batches and persisted <em>before</em> a number is handed out, so an abrupt
/// exit can skip numbers -- harmless, only ordering matters -- but can never reuse one.
/// And every value is floored at the current unix second, so even a profile restored from an
/// old backup lands above everything it published before, because time moved on meanwhile.
///
/// That second guarantee has a precondition worth naming: it holds while the counter is not
/// ahead of wall clock, and the counter only gets ahead by publishing more than once per second
/// sustained. <see cref="PresencePolicy.MinimumPublishGap"/> is what forbids that, so the two
/// pieces are load-bearing together rather than independently.
/// </summary>
public sealed class PresenceSequence
{
    /// <summary>
    /// How many numbers are reserved per disk write. Large enough that publishing costs no I/O
    /// in the common case, small enough that the gap after a crash stays unremarkable.
    /// </summary>
    private const int ReservationBatch = 32;

    private readonly string _file;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private long _issued;
    private long _reserved;

    public PresenceSequence(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _file = Path.Combine(Path.GetFullPath(configurationDirectory), "presence-state.json");

        var state = Read();
        // Start from the reservation, not the last number issued: anything below it may already
        // have gone out in a document we no longer remember writing.
        _issued = state.Reserved;
        _reserved = state.Reserved;
        InstanceId = state.InstanceId;
    }

    public string StateFile => _file;

    /// <summary>
    /// Identifies this installation, so a read-back can tell "my own last document" from
    /// "somebody else publishing to my URL".
    /// </summary>
    public string InstanceId { get; }

    public long Current
    {
        get { lock (_gate) return _issued; }
    }

    /// <summary>Issues the next sequence, persisting a new reservation when the batch runs out.</summary>
    public long Next(DateTimeOffset now)
    {
        lock (_gate)
        {
            // The clock is a floor, never a ceiling: it rescues a lost state file without ever
            // pulling the counter back if the clock itself is wrong in the other direction.
            var next = Math.Max(_issued + 1, now.ToUnixTimeSeconds());
            if (next > _reserved)
            {
                _reserved = next + ReservationBatch;
                Write(new SequenceState(_reserved, InstanceId));
            }

            _issued = next;
            return _issued;
        }
    }

    /// <summary>
    /// Compares a sequence read back from our own published document against what we have issued.
    /// A document ahead of us is not ours, however much it claims to be.
    /// </summary>
    public PresenceSequenceReconciliation Reconcile(long observedSequence)
    {
        lock (_gate)
        {
            if (observedSequence <= _issued) return PresenceSequenceReconciliation.Consistent;

            // Adopt it. Refusing would leave us publishing numbers the other installation has
            // already burned, which every friend would then reject -- the exact failure this
            // class exists to prevent. Interleaving is wrong but visible; silence is neither.
            _issued = observedSequence;
            if (_issued >= _reserved)
            {
                _reserved = _issued + ReservationBatch;
                Write(new SequenceState(_reserved, InstanceId));
            }

            return PresenceSequenceReconciliation.RemoteAhead;
        }
    }

    private SequenceState Read()
    {
        try
        {
            if (File.Exists(_file))
            {
                var state = JsonSerializer.Deserialize<SequenceState>(File.ReadAllText(_file), _json);
                if (state is not null && state.Reserved >= 0 && !string.IsNullOrWhiteSpace(state.InstanceId))
                    return state;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Falling through to a fresh state is safe precisely because of the clock floor.
        }

        return new SequenceState(0, Guid.NewGuid().ToString("N"));
    }

    private void Write(SequenceState state)
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
            // A reservation we could not persist is a reservation we must not rely on: fall back
            // to the clock floor by refusing to remember this batch.
            _reserved = _issued;
        }
    }

    private sealed record SequenceState(long Reserved, string InstanceId);
}
