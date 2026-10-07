namespace CubeShelf.Core.Social;

/// <summary>Who a presence document is sealed for.</summary>
public static class PresenceRecipients
{
    /// <summary>
    /// The active friends, plus ourselves.
    ///
    /// Addressing our own document to ourselves is what makes it possible to read it back and
    /// prove the whole chain works -- that the file reached the folder, that the sync client
    /// served it, and that the URL about to go into a friend code actually returns a document
    /// this identity can open. ECDH with one's own key is a perfectly ordinary agreement, and
    /// the extra lockbox is absorbed by the padding for any realistic number of friends.
    ///
    /// It also gives the sequence something to reconcile against: a document at our own address
    /// carrying a number we never issued means a second installation is publishing as us.
    /// </summary>
    public static IReadOnlyList<byte[]> ForPublication(PeerIdentity identity, FriendStore friends)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(friends);

        var recipients = new List<byte[]>(friends.ActiveRecipients()) { identity.PublicKey };
        return recipients;
    }

    /// <summary>
    /// Who the document is for, as a short digest: part of what decides whether a publish is
    /// needed. A friend just added, paused or unblocked changes who can open the document even
    /// when nothing in it changed -- and without this, they waited for the next heartbeat, five
    /// minutes, to see someone they had just added.
    /// </summary>
    public static string Digest(IEnumerable<byte[]> recipients)
    {
        var sorted = recipients.Select(Convert.ToBase64String).OrderBy(key => key, StringComparer.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(string.Join("|", sorted))), 0, 8);
    }
}
