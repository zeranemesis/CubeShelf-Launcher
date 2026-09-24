namespace CubeShelf.Core.Social;

/// <param name="Directory">A folder something else already synchronises and serves.</param>
/// <param name="FileName">The document's name inside that folder.</param>
/// <param name="ReadUrl">The public address that folder is served from, pasted by the user.</param>
public sealed record SyncedFolderTarget(string Directory, string FileName, string ReadUrl)
{
    public const string DefaultFileName = "cubeshelf-presence.json";
}

/// <summary>
/// Publishes by writing a file into a folder the user already has synchronised -- Nextcloud,
/// Dropbox, anything that turns a local folder into a public https address.
///
/// This is the transport that needs no account of its own and stores no secret: there is no
/// token to keep, nothing to revoke, and no third party accumulating a timestamped log of when
/// the user plays. The price is that propagation speed belongs to the sync client, which is why
/// the freshness window has to be generous.
/// </summary>
public sealed class SyncedFolderPresencePublisher : IPresencePublisher
{
    private readonly SyncedFolderTarget _target;

    public SyncedFolderPresencePublisher(SyncedFolderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_target.Directory) &&
        !string.IsNullOrWhiteSpace(_target.FileName) &&
        IsUsableReadUrl(_target.ReadUrl);

    public string PresenceUrl => IsUsableReadUrl(_target.ReadUrl) ? _target.ReadUrl.Trim() : "";

    public Task<PresencePublishResult> PublishAsync(
        string envelopeJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeJson);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
            return Task.FromResult(new PresencePublishResult(
                false, "", "La publication de présence n’est pas configurée."));

        var directory = Path.GetFullPath(_target.Directory);

        // Deliberately not created. A folder that does not exist is far more likely to be a typo
        // than an intention, and creating it would publish into a path nothing synchronises --
        // which looks like it worked and reaches nobody.
        if (!Directory.Exists(directory))
            return Task.FromResult(new PresencePublishResult(
                false, "", $"Le dossier de publication est introuvable : {directory}"));

        var destination = Path.Combine(directory, Path.GetFileName(_target.FileName));
        var temporary = destination + ".tmp";
        try
        {
            // The temporary file has to sit beside its target: across volumes File.Move degrades
            // to a copy, and a sync client would then upload a half-written document.
            File.WriteAllText(temporary, envelopeJson);
            File.Move(temporary, destination, true);
            return Task.FromResult(new PresencePublishResult(true, PresenceUrl));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
            return Task.FromResult(new PresencePublishResult(
                false, "", $"Écriture impossible dans le dossier de publication : {exception.Message}"));
        }
    }

    /// <summary>
    /// Only https, matching what a friend code will accept. Rejecting it here means the user
    /// finds out while configuring rather than when a friend cannot read them.
    /// </summary>
    private static bool IsUsableReadUrl(string? readUrl) =>
        !string.IsNullOrWhiteSpace(readUrl) &&
        Uri.TryCreate(readUrl.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary is untidy, not harmful; the next publish overwrites it.
        }
    }

    public void Dispose()
    {
        // Nothing to release: no handle, no socket, no secret.
    }
}
