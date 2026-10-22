namespace CubeShelf.Core.Online;

/// <summary>A public-directory lookup hint, never an alias for a private bearer invite.</summary>
public static class PublicRoomCode
{
    public static string Display(string roomId)
    {
        if (roomId.Length != 32 || !roomId.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid room id.", nameof(roomId));
        return (roomId[..4] + "-" + roomId.Substring(4, 4)).ToUpperInvariant();
    }

    public static PublicRoom Resolve(string code, IReadOnlyList<PublicRoom> compatibleRooms)
    {
        var normalized = code.Trim().Replace("-", "").Replace(" ", "");
        if (normalized.Length is not (8 or 32) || !normalized.All(Uri.IsHexDigit))
            throw new FormatException("Utilise un code de 8 caractères, comme ABCD-1234.");
        var matches = compatibleRooms.Where(room => room.IsJoinable && room.Room.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new RoomGoneException("Salon introuvable : fermé, privé, ou disque/version différents."),
            _ => throw new InvalidOperationException("Ce code correspond à plusieurs salons. Choisis ton salon dans la liste.")
        };
    }
}
