using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// Choosing where to publish, without knowing how any sync service works.
///
/// The folders the sync clients already watch are listed, one click prepares one, and the
/// steps to share the file are spelled out for that service. The link the service gives is
/// pasted as it is: <see cref="ShareLink"/> turns it into the file itself, and the self-test
/// tries its guesses and keeps the one that works. Afterwards the address is read back now and
/// then, because a share link can die quietly and the publisher is the last to notice.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>One sync client's folder, as offered on the profile page.</summary>
    public sealed record DetectedFolderRow(string Title, string Path, DetectedSyncFolder Folder);

    /// <summary>The guesses made from the last link pasted, tried in order by the self-test.</summary>
    private IReadOnlyList<string>? _pendingCandidates;

    private PresenceAddressReport? _lastAddressReport;

    private IReadOnlyList<DetectedSyncFolder> _detectedFolders = Array.Empty<DetectedSyncFolder>();

    private async void RefreshDetectedFolders()
    {
        if (DetectedFoldersList is null) return;

        // A few small files and environment variables, but on a network profile even that can
        // stall: never on the UI thread.
        var found = await Task.Run(() =>
        {
            try
            {
                return SyncedFolderDetector.Detect();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return (IReadOnlyList<DetectedSyncFolder>)Array.Empty<DetectedSyncFolder>();
            }
        });

        _detectedFolders = found;
        DetectedFoldersList.ItemsSource = found
            .Select(folder => new DetectedFolderRow(
                P7($"Utiliser {folder.Label}", $"Use {ProviderName(folder.Provider, english: true)}"),
                folder.Root,
                folder))
            .ToArray();
        DetectedFoldersEmptyText.IsVisible = found.Count == 0;
        RefreshShareSteps();
    }

    private void UseDetectedFolder(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DetectedFolderRow row }) return;

        string folder;
        try
        {
            folder = SyncedFolderDetector.PreparePublishingFolder(row.Folder.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PresenceStatusText.Text = exception.Message;
            return;
        }

        PresenceFolderBox.Text = folder;
        SavePresenceText(sender, args);

        // The file has to exist before the service can be asked to share it.
        if (WriteInitialPresenceDocument(out var error))
        {
            PresenceStatusText.Text = P7(
                $"Dossier prêt : {folder}. Partage maintenant le fichier {SyncedFolderTarget.DefaultFileName} comme indiqué ci-dessous.",
                $"Folder ready: {folder}. Now share the file {SyncedFolderTarget.DefaultFileName} as shown below.");
            RevealPresenceFile();
        }
        else
        {
            PresenceStatusText.Text = error;
        }

        RefreshShareSteps();
    }

    /// <summary>
    /// A first document, addressed to ourselves and our friends, so there is a file to share.
    /// It says "offline": nothing is published as present until publishing is switched on.
    /// </summary>
    private bool WriteInitialPresenceDocument(out string error)
    {
        error = "";
        if (_identity is null || _friends is null || string.IsNullOrWhiteSpace(_preferences.PresenceFolder))
        {
            error = P7("Choisis d’abord un dossier.", "Choose a folder first.");
            return false;
        }

        var target = Path.Combine(_preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName);
        if (File.Exists(target)) return true;

        var sequence = new PresenceSequence(_paths.ConfigurationDirectory);
        var snapshot = PresenceComposer.Offline(_preferences.FriendsDisplayName, sequence.Next(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        var json = SealedPresence.ToJson(SealedPresence.Seal(
            _identity, snapshot, PresenceRecipients.ForPublication(_identity, _friends)));
        return SyncedFolderPresencePublisher.TryWrite(
            _preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName, json, out error);
    }

    private void OpenPresenceFolder(object? sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_preferences.PresenceFolder) || !Directory.Exists(_preferences.PresenceFolder))
        {
            ShowToastParity(P7("Profil", "Profile"), P7("Choisis d’abord un dossier.", "Choose a folder first."));
            return;
        }

        if (!WriteInitialPresenceDocument(out var error))
        {
            ShowToastParity(P7("Profil", "Profile"), error);
            return;
        }

        RevealPresenceFile();
    }

    /// <summary>Opens the folder with the file selected, where the sync client's menu is a right-click away.</summary>
    private void RevealPresenceFile()
    {
        var file = Path.Combine(_preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName);
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { "-R", file });
            else
                Process.Start(new ProcessStartInfo(_preferences.PresenceFolder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>The service the publishing folder belongs to, read off the folders the clients declare.</summary>
    private ShareLinkProvider? FolderProvider()
    {
        var folder = _preferences.PresenceFolder;
        if (string.IsNullOrWhiteSpace(folder)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return _detectedFolders
            .Where(detected =>
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(detected.Root));
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       full.Equals(root, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(detected => detected.Root.Length)
            .Select(detected => (ShareLinkProvider?)detected.Provider)
            .FirstOrDefault();
    }

    /// <summary>How to get a public link to the file, in that service's own words.</summary>
    private void RefreshShareSteps()
    {
        if (ShareStepsPanel is null) return;

        var hasFolder = !string.IsNullOrWhiteSpace(_preferences.PresenceFolder);
        ShareStepsPanel.IsVisible = hasFolder && !IsAddressVerified();
        if (!ShareStepsPanel.IsVisible) return;

        var file = SyncedFolderTarget.DefaultFileName;
        ShareStepsText.Text = FolderProvider() switch
        {
            ShareLinkProvider.Dropbox => P7(
                $"Dropbox : dans le dossier qui vient de s’ouvrir, clic droit sur {file} → « Copier le lien Dropbox » (ou Partager → Copier le lien). Colle-le dans « Adresse publique » : CubeShelf s’occupe du reste.",
                $"Dropbox: in the folder that just opened, right-click {file} → “Copy Dropbox link” (or Share → Copy link). Paste it into “Public address”: CubeShelf does the rest."),
            ShareLinkProvider.Nextcloud => P7(
                $"Nextcloud : clic droit sur {file} → Nextcloud → « Copier le lien public » (ou Partager → Lien de partage → Copier). Colle-le dans « Adresse publique » : CubeShelf s’occupe du reste.",
                $"Nextcloud: right-click {file} → Nextcloud → “Copy public link” (or Share → Share link → Copy). Paste it into “Public address”: CubeShelf does the rest."),
            ShareLinkProvider.OneDrive => P7(
                $"OneDrive : clic droit sur {file} → Partager → « Toute personne disposant du lien » → Copier le lien. Colle-le dans « Adresse publique » : CubeShelf s’occupe du reste.",
                $"OneDrive: right-click {file} → Share → “Anyone with the link” → Copy link. Paste it into “Public address”: CubeShelf does the rest."),
            ShareLinkProvider.SharePoint => P7(
                $"OneDrive professionnel : clic droit sur {file} → Partager → « Toute personne disposant du lien » → Copier le lien. Si ce choix est grisé, ton organisation l’interdit : choisis un autre service ci-dessus.",
                $"OneDrive for work: right-click {file} → Share → “Anyone with the link” → Copy link. If that option is greyed out, your organisation forbids it: pick another service above."),
            ShareLinkProvider.GoogleDrive => P7(
                $"Google Drive : clic droit sur {file} → Partager → Accès général : « Tous les utilisateurs disposant du lien » → Copier le lien. Colle-le dans « Adresse publique » : CubeShelf s’occupe du reste.",
                $"Google Drive: right-click {file} → Share → General access: “Anyone with the link” → Copy link. Paste it into “Public address”: CubeShelf does the rest."),
            _ => P7(
                $"Partage le fichier {file} avec un lien public, accessible sans compte, et colle ce lien dans « Adresse publique » : CubeShelf le convertit et le teste.",
                $"Share the file {file} with a public link that needs no account, and paste that link into “Public address”: CubeShelf converts and tests it.")
        };
    }

    private static string ProviderName(ShareLinkProvider provider, bool english) => provider switch
    {
        ShareLinkProvider.Dropbox => "Dropbox",
        ShareLinkProvider.Nextcloud => "Nextcloud",
        ShareLinkProvider.OneDrive => "OneDrive",
        ShareLinkProvider.SharePoint => english ? "OneDrive for work" : "OneDrive professionnel",
        ShareLinkProvider.GoogleDrive => "Google Drive",
        _ => english ? "this link" : "ce lien"
    };

    /// <summary>
    /// Reads a pasted link, says what it was recognised as, and keeps the guesses for the test.
    /// Returns the address to store: the best guess, until a test proves one.
    /// </summary>
    private string InterpretPastedLink(string pasted)
    {
        PresenceUrlConversionText.IsVisible = false;
        _pendingCandidates = null;
        if (string.IsNullOrWhiteSpace(pasted)) return "";

        if (!ShareLink.TryConvert(pasted, SyncedFolderTarget.DefaultFileName, out var conversion, out var error))
        {
            PresenceUrlConversionText.Text = error;
            PresenceUrlConversionText.IsVisible = true;
            return pasted.Trim();
        }

        _pendingCandidates = conversion!.Candidates;
        if (conversion.Provider != ShareLinkProvider.Direct || conversion.Changed)
        {
            var name = ProviderName(conversion.Provider, UiLocalization.IsEnglish(_preferences.Language));
            PresenceUrlConversionText.Text = P7(
                $"Lien {name} reconnu : CubeShelf va essayer {conversion.Candidates.Count} façon(s) de lire le fichier et garder celle qui marche.",
                $"{name} link recognised: CubeShelf will try {conversion.Candidates.Count} way(s) of reading the file and keep the one that works.");
            PresenceUrlConversionText.IsVisible = true;
        }

        return conversion.Primary;
    }

    // ----------------------------------------------------------------- our own address, later on

    private void ApplyAddressReport(PresenceAddressReport report)
    {
        var previous = _lastAddressReport;
        _lastAddressReport = report;
        RefreshAddressHealthUi();

        // Said once when it goes wrong, not at every check while it stays wrong.
        if (report.NeedsAttention && previous?.Health != report.Health)
            Notify(P7("Ta présence", "Your presence"), DescribeAddressReport(report), showFriends: false);

        if (FriendsView.IsVisible) RefreshFriendsView();
    }

    private void RefreshAddressHealthUi()
    {
        if (AddressHealthBanner is null) return;
        var report = _lastAddressReport;
        AddressHealthBanner.IsVisible = report is { NeedsAttention: true };
        if (report is { NeedsAttention: true }) AddressHealthText.Text = DescribeAddressReport(report);
    }

    private string DescribeAddressReport(PresenceAddressReport report) => report.Health switch
    {
        PresenceAddressHealth.Healthy => P7("Ton adresse sert bien ton dernier document : tes amis te voient à jour.",
                                           "Your address serves your latest document: your friends see you up to date."),
        PresenceAddressHealth.Lagging => P7(
            $"Ton service met plus de {Math.Max(1, (int)(report.Lag ?? TimeSpan.Zero).TotalMinutes)} min à servir ce que CubeShelf écrit : tes amis te voient en retard, voire hors ligne. Vérifie que le client de synchronisation tourne.",
            $"Your service takes over {Math.Max(1, (int)(report.Lag ?? TimeSpan.Zero).TotalMinutes)} min to serve what CubeShelf writes: your friends see you late, or offline. Check that the sync client is running."),
        PresenceAddressHealth.NotOurs => P7(
            "Ton lien ne sert plus ton fichier (page de connexion ou d’aperçu à la place) : le partage a peut-être été retiré. Recrée le lien de partage, colle-le et teste-le.",
            "Your link no longer serves your file (a login or preview page instead): the share may have been removed. Create the share link again, paste it and test it."),
        PresenceAddressHealth.NotFound => P7(
            "Ton lien ne mène plus à rien : le fichier ou son partage a été supprimé. Tes amis te voient hors ligne. Recrée le lien de partage et teste-le.",
            "Your link leads nowhere any more: the file or its share was deleted. Your friends see you offline. Create the share link again and test it."),
        PresenceAddressHealth.SomeoneElsePublishes => P7(
            "Une autre installation de CubeShelf publie sous ton identité (un profil copié sur un autre PC ?). Tes amis voient les deux se mélanger : garde ce profil sur un seul PC.",
            "Another CubeShelf installation is publishing as you (a profile copied to another PC?). Your friends see the two mixed up: keep this profile on one PC only."),
        PresenceAddressHealth.Unreachable => P7(
            $"Ton adresse n’a pas pu être lue : {report.Detail}",
            $"Your address could not be read: {report.Detail}"),
        _ => ""
    };

    private async void CheckOwnAddressNow(object? sender, RoutedEventArgs args)
    {
        if (_presence is null)
        {
            ShowToastParity(P7("Ta présence", "Your presence"), DescribePublishingState());
            return;
        }

        if (sender is Button button) button.IsEnabled = false;
        try
        {
            var report = await _presence.CheckAddressNowAsync();
            ApplyAddressReport(report);
            if (!report.NeedsAttention) ShowToastParity(P7("Ta présence", "Your presence"), DescribeAddressReport(report));
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or OperationCanceledException)
        {
            ShowToastParity(P7("Ta présence", "Your presence"), exception.Message);
        }
        finally
        {
            if (sender is Button again) again.IsEnabled = true;
        }
    }

    private void OnAddressChecked(PresenceAddressReport report) =>
        Dispatcher.UIThread.Post(() => ApplyAddressReport(report));
}
