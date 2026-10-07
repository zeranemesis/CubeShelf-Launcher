namespace CubeShelf.Core.Social;

/// <summary>
/// Moving a published document to a new address without losing anyone.
///
/// The old file gets one last document: offline, stating the new address
/// (<see cref="PresenceSnapshot.Address"/>), sealed for the current friends only. Every friend
/// still reading the old address opens it, adopts the new one (<see cref="FriendStore.AdoptAddress"/>)
/// and carries on -- no new code to send anyone. A friend removed meanwhile is not among the
/// recipients: they cannot read where we went, and the old file never changes again, so they stop
/// learning when we play. That is the remedy <see cref="Friend.Blocked"/> could only describe.
///
/// The old file has to stay where it is for a while: a friend away for a month only finds the new
/// address when they next read the old one.
/// </summary>
public static class PresenceAddressMove
{
    public static bool WriteMovedDocument(
        PeerIdentity identity,
        FriendStore friends,
        PresenceSequence sequence,
        string displayName,
        string oldFolder,
        string newAddress,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(friends);
        ArgumentNullException.ThrowIfNull(sequence);

        var address = Lan.LanProtocol.SafeUrl(newAddress);
        if (address.Length == 0)
        {
            error = "La nouvelle adresse n’est pas une adresse https.";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var moved = PresenceComposer.Offline(displayName, sequence.Next(now), now, address);
        var json = SealedPresence.ToJson(SealedPresence.Seal(identity, moved, PresenceRecipients.ForPublication(identity, friends)));
        return SyncedFolderPresencePublisher.TryWrite(oldFolder, SyncedFolderTarget.DefaultFileName, json, out error);
    }
}
