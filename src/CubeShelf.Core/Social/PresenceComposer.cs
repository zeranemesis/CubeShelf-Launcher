using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Social;

/// <summary>
/// What the user agreed to publish. Every flag only ever removes data; none adds any, so the
/// safe reading of an unfamiliar combination is always "less is shared".
/// </summary>
public sealed record PresenceSharingOptions(
    bool ShareLibrary = true,
    bool SharePlayTime = true,
    bool ShareCurrentGame = true,
    bool ShareMods = true,
    bool ShareProfile = true);

/// <summary>What the user typed and chose for their profile, before any of it is published.</summary>
public sealed record ProfileInputs(
    string StatusLine = "",
    string PinnedGameId = "",
    DateTimeOffset? FirstSeenAt = null);

/// <summary>
/// A catalog entry copied off the UI thread.
///
/// <c>GameCatalogEntry</c> is mutable and lives on the UI thread, where play counts and
/// favourites are written as games start and stop. Handing one to a background composer would
/// read fields mid-change, so the caller projects into this immutable copy first.
/// </summary>
public sealed record PresenceGame(
    string Id,
    string Title,
    int PlayCount,
    long TotalPlaySeconds,
    bool IsFavorite,
    DateTimeOffset? LastPlayedAt);

/// <summary>
/// Builds the document that gets sealed and published.
///
/// Reads mod state from disk and touches no UI, so it is safe to call off the UI thread. Given
/// the same inputs it produces the same document apart from the timestamp and the sequence,
/// which is what lets a caller notice that nothing has changed and skip a publish.
/// </summary>
public sealed class PresenceComposer
{
    private readonly IPlatformPaths _paths;

    private static readonly JsonSerializerOptions Canonical = new() { WriteIndented = false };

    public PresenceComposer(IPlatformPaths paths) =>
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>Where the launcher stores the avatar it has already scaled down.</summary>
    public string AvatarFile => Path.Combine(Path.GetFullPath(_paths.ConfigurationDirectory), "avatar.png");

    public PresenceSnapshot Compose(
        string displayName,
        IReadOnlyList<PresenceGame> games,
        IReadOnlyCollection<string> runningGameIds,
        PresenceSharingOptions sharing,
        long sequence,
        DateTimeOffset now,
        ProfileInputs? profile = null,
        PresenceInvite? invite = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(runningGameIds);
        ArgumentNullException.ThrowIfNull(sharing);

        var running = runningGameIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        // A game we are playing but whose title we do not hold is still worth reporting as
        // "in a game": the status is the useful part, the title is a nicety.
        var current = sharing.ShareCurrentGame ? running.FirstOrDefault() : null;
        var currentTitle = current is null
            ? null
            : games.FirstOrDefault(game => string.Equals(game.Id, current, StringComparison.OrdinalIgnoreCase))?.Title;

        // Offline is never composed here. A reader decides that from staleness, because a peer
        // that stopped publishing cannot publish the fact that it stopped.
        var status = current is not null ? PresenceStatus.InGame : PresenceStatus.Online;

        return new PresenceSnapshot(
            PresenceSnapshot.CurrentVersion,
            (displayName ?? "").Trim(),
            now,
            sequence,
            status,
            current,
            currentTitle,
            sharing.ShareLibrary ? ComposeLibrary(games, sharing) : Array.Empty<SharedGame>(),
            sharing.ShareMods ? ComposeMods(games) : Array.Empty<SharedMod>(),
            sharing.ShareProfile ? ComposeProfile(profile, games, sharing) : null,
            // A lapsed or empty invitation is simply not published: a friend acting on a stale
            // one would be sent to a lobby that has already closed.
            invite is not null && invite.IsPublishable(now) ? invite : null);
    }

    private PeerProfile? ComposeProfile(
        ProfileInputs? profile,
        IReadOnlyList<PresenceGame> games,
        PresenceSharingOptions sharing)
    {
        if (profile is null) return null;

        var pinned = string.IsNullOrWhiteSpace(profile.PinnedGameId)
            ? null
            : games.FirstOrDefault(game =>
                string.Equals(game.Id, profile.PinnedGameId, StringComparison.OrdinalIgnoreCase));

        var status = (profile.StatusLine ?? "").Trim();
        if (status.Length > PeerProfile.MaximumStatusLength)
            status = status[..PeerProfile.MaximumStatusLength];

        return new PeerProfile(
            status,
            ReadAvatar(),
            pinned?.Id,
            pinned?.Title,
            // Aggregates follow the same switches as the detail: turning the library off should
            // not leave its size published in another field.
            sharing.ShareLibrary ? games.Count : 0,
            sharing.SharePlayTime ? games.Sum(game => game.TotalPlaySeconds) : 0,
            profile.FirstSeenAt);
    }

    /// <summary>
    /// The avatar the launcher has already scaled. Over the cap it is dropped rather than sent,
    /// because this file is rewritten on every heartbeat and a large one would turn a presence
    /// document into steady megabytes of sync traffic.
    /// </summary>
    private string? ReadAvatar()
    {
        try
        {
            var file = new FileInfo(AvatarFile);
            if (!file.Exists || file.Length == 0 || file.Length > PeerProfile.MaximumAvatarBytes)
                return null;
            return Convert.ToBase64String(File.ReadAllBytes(file.FullName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The farewell document: still us, doing nothing.</summary>
    public static PresenceSnapshot Offline(string displayName, long sequence, DateTimeOffset now) =>
        new(PresenceSnapshot.CurrentVersion,
            (displayName ?? "").Trim(),
            now,
            sequence,
            PresenceStatus.Offline,
            null,
            null,
            Array.Empty<SharedGame>(),
            Array.Empty<SharedMod>());

    /// <summary>
    /// Everything in the document except when it was written and which number it carries, so two
    /// snapshots of an unchanged shelf compare equal and the publish can be skipped.
    /// </summary>
    public static string ContentFingerprint(PresenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var comparable = snapshot with { PublishedAt = DateTimeOffset.UnixEpoch, Sequence = 0 };
        return Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(comparable, Canonical))).ToLowerInvariant();
    }

    private static IReadOnlyList<SharedGame> ComposeLibrary(
        IReadOnlyList<PresenceGame> games,
        PresenceSharingOptions sharing) =>
        games
            // Ordered before truncating, so the cut is stable too: the same shelf always yields
            // the same document rather than a different 250 each time.
            .OrderBy(game => game.Id, StringComparer.Ordinal)
            .Take(PresencePolicy.MaximumSharedGames)
            .Select(game => new SharedGame(
                game.Id,
                game.Title,
                sharing.SharePlayTime ? game.PlayCount : 0,
                sharing.SharePlayTime ? game.TotalPlaySeconds : 0,
                game.IsFavorite,
                sharing.SharePlayTime ? game.LastPlayedAt : null))
            .ToArray();

    private IReadOnlyList<SharedMod> ComposeMods(IReadOnlyList<PresenceGame> games) =>
        games
            .Select(game => game.Id)
            .Where(PortableModState.IsValidGameId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            // ReadEffective reads the two files the mod manager owns and writes nothing, which
            // matters because one of those games may be running right now.
            .SelectMany(id => PortableModState.ReadEffective(_paths, id)
                .Select(mod => new SharedMod(
                    id,
                    mod.Id.ToString(CultureInfo.InvariantCulture),
                    mod.Name,
                    mod.Enabled)))
            .Take(PresencePolicy.MaximumSharedMods)
            .ToArray();
}
