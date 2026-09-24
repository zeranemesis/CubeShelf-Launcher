using System.Net;
using System.Net.Sockets;

namespace CubeShelf.Core.Social;

/// <summary>
/// A standing offer to play, carried inside the presence document.
///
/// Not a notification. The transport is pull-based, so a friend sees this at their next poll --
/// up to a couple of minutes later, plus whatever the sync client adds. It is an invitation left
/// on the table for a quarter of an hour, not something that interrupts anyone.
///
/// It also only reaches a friend who can open a direct connection back. Ring Out's netplay is a
/// direct connection over LAN, a VPN, or a forwarded port; nothing here performs NAT traversal,
/// deliberately, because the whole transport was chosen to avoid needing any.
/// </summary>
public sealed record PresenceInvite(
    string GameId,
    string GameTitle,
    string Address,
    int Port,
    DateTimeOffset ExpiresAt,

    /// <summary>Base64 public key of the one friend it is meant for, or empty for all of them.</summary>
    string ForFriend = "")
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public bool IsLive(DateTimeOffset now) => now < ExpiresAt;

    public bool IsFor(string friendPublicKeyBase64) =>
        ForFriend.Length == 0 ||
        string.Equals(ForFriend, friendPublicKeyBase64, StringComparison.Ordinal);
}

/// <param name="Reachable">Whether the port could be bound here. Says nothing about the outside.</param>
/// <param name="IsPrivateAddress">A LAN or VPN address: friends elsewhere will not connect.</param>
public sealed record InviteReadiness(
    bool Reachable,
    bool IsPrivateAddress,
    string Address,
    string Message);

/// <summary>
/// The honest half of "test before you invite".
///
/// Without a server somewhere else on the internet, nothing here can prove a port is reachable
/// from outside -- that is exactly the kind of helper this design set out not to require. What
/// it can do is catch the failures that are knowable locally: a port already taken, and an
/// address that is private and therefore only good on a LAN or a VPN. The rest is stated rather
/// than silently assumed.
/// </summary>
public static class InviteHost
{
    public const int DefaultPort = 27070;

    public static InviteReadiness Check(string address, int port)
    {
        if (port is < 1024 or > 65535)
            return new(false, false, address, "Le port doit être compris entre 1024 et 65535.");

        if (!IPAddress.TryParse(address, out var parsed))
            return new(false, false, address,
                "L’adresse doit être une adresse IP joignable par ton ami.");

        if (!TryBind(port, out var bindError))
            return new(false, false, address, bindError);

        var privateAddress = IsPrivate(parsed);
        return new(true, privateAddress, address, privateAddress
            ? "Le port est libre. Cette adresse est privée : ton ami doit être sur le même réseau " +
              "local ou le même VPN. Depuis Internet, il faudra une redirection de port."
            : "Le port est libre. Vérifie qu’il est bien redirigé vers cette machine : personne " +
              "ne peut le confirmer d’ici.");
    }

    /// <summary>Addresses of this machine a friend could plausibly be given.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Where(address => !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryBind(int port, out string error)
    {
        error = "";
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            error = $"Le port {port} est déjà utilisé sur cette machine.";
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return false;
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            169 => bytes[1] == 254,
            _ => false
        };
    }
}
