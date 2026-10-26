using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Library;

public sealed record ShelfProduct(string Id, string Name, string Console, string Accent)
{
    public PlatformPaths CreatePaths() => new(Name, new Dictionary<string, string?>
    {
        ["XDG_DATA_HOME"] = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        ["XDG_CACHE_HOME"] = Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
        ["XDG_CONFIG_HOME"] = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        [PlatformPaths.DataDirectoryVariable] = Environment.GetEnvironmentVariable(Name.ToUpperInvariant() + "_DATA_DIR")
    });
    public static ShelfProduct Resolve(string? id) => id?.ToLowerInvariant() switch
    {
        "wii" or "wiishelf" => new("wii", "WiiShelf", "Wii", "#45C5E8"),
        "switch" or "switchshelf" => new("switch", "SwitchShelf", "Switch", "#FA5365"),
        null or "gamecube" or "cubeshelf" => new("gamecube", "CubeShelf", "GameCube", "#A88BFA"),
        _ => throw new ArgumentException("Console inconnue.", nameof(id))
    };
}

public sealed class NativePort
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Method { get; set; } = "";
    public string Status { get; set; } = "Expérimental";
    public string Notes { get; set; } = "";
    public string VerifiedAt { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string ReadmeUrl { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public List<string> Platforms { get; set; } = [];
    public List<NativePortAsset> Assets { get; set; } = [];
    public string Homepage => "https://github.com/" + Repository;
    public override string ToString() => Title + " · " + Project;
}

public sealed class NativePortAsset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public List<string> RuntimeIdentifiers { get; set; } = [];
    public bool MatchesCurrentPlatform => RuntimeIdentifiers.Contains(CurrentRuntimeIdentifier);
    public static string CurrentRuntimeIdentifier => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")
        + "-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
    public override string ToString() => $"{Name} · {Size / 1048576d:0.#} Mo";
}

public static class NativePortCatalog
{
    public static string GameKey(GameCatalogEntry game) => game.ShelfConsole + ":" +
        string.Concat(game.Title.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static IEnumerable<GameCatalogEntry> VersionsOf(IEnumerable<GameCatalogEntry> games, GameCatalogEntry game) =>
        games.Where(candidate => !GameVariants.IsVariant(candidate) && GameKey(candidate) == GameKey(game));

    public static IEnumerable<GameCatalogEntry> ForConsole(IEnumerable<GameCatalogEntry> games, string console,
        IReadOnlyDictionary<string, string>? selectedVersions = null)
    {
        var id = ShelfProduct.Resolve(console).Id;
        return games.Where(game => game.ShelfConsole == id && !GameVariants.IsVariant(game))
            .GroupBy(GameKey).Select(group => selectedVersions is not null && selectedVersions.TryGetValue(group.Key, out var selected)
                ? group.FirstOrDefault(game => game.Id == selected) ?? group.First() : group.First());
    }
    public static IReadOnlyList<NativePort> Load(string file)
    {
        var ports = JsonSerializer.Deserialize<List<NativePort>>(File.ReadAllText(file)) ?? [];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in ports)
        {
            if (!IsSafeId(port.Id) || !ids.Add(port.Id) || string.IsNullOrWhiteSpace(port.Title)
                || port.Repository.Split('/') is not [var owner, var repo] || !IsSafeId(owner) || !IsSafeId(repo))
                throw new InvalidDataException("Entrée de catalogue invalide ou dupliquée.");
            foreach (var asset in port.Assets) ValidateAsset(port, asset);
        }
        return ports;
    }

    public static bool IsSafeId(string id) => id.Length is > 0 and <= 160 && id is not "." and not ".."
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    public static void ValidateAsset(NativePort port, NativePortAsset asset)
    {
        var prefix = port.Homepage + "/releases/download/";
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !asset.Url.StartsWith(prefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(asset.Name)
            || Path.GetFileName(asset.Name) != asset.Name || asset.Name.Contains('\\')
            || asset.Size <= 0 || asset.Size > 2L * 1024 * 1024 * 1024
            || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Paquet sans origine ou empreinte vérifiable.");
    }
}

public sealed class NativePortLibrary
{
    private readonly IPlatformPaths _paths;
    private readonly string _file;
    private Dictionary<string, string> _executables = new(StringComparer.OrdinalIgnoreCase);
    public NativePortLibrary(IPlatformPaths paths)
    {
        _paths = paths;
        _file = Path.Combine(paths.ConfigurationDirectory, "native-ports.json");
        if (File.Exists(_file)) _executables = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file)) ?? [];
    }
    public string? Executable(NativePort port) => _executables.GetValueOrDefault(port.Id);
    public bool IsConfigured(NativePort port) => Executable(port) is { } path && (File.Exists(path) || OperatingSystem.IsMacOS() && Directory.Exists(path) && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase));
    public void Configure(NativePort port, string executable)
    {
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path) && !(OperatingSystem.IsMacOS() && Directory.Exists(path) && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException("Choisis le programme natif du port.", path);
        _executables[port.Id] = path;
        Directory.CreateDirectory(_paths.ConfigurationDirectory);
        var temporary = _file + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_executables));
        File.Move(temporary, _file, true);
    }
    public void Launch(NativePort port)
    {
        if (!IsConfigured(port)) throw new InvalidOperationException("Associe d'abord le programme du port.");
        var executable = Executable(port)!;
        ProcessStartInfo info;
        if (OperatingSystem.IsMacOS() && executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            info = new("open") { UseShellExecute = false }; info.ArgumentList.Add(executable);
        }
        else
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
            info = new(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
        }
        _ = Process.Start(info) ?? throw new InvalidOperationException("Le programme n'a pas démarré.");
    }

    public async Task<string> InstallAsync(NativePort port, NativePortAsset asset, IProgress<double>? progress,
        CancellationToken cancellationToken, HttpClient? http = null)
    {
        NativePortCatalog.ValidateAsset(port, asset);
        if (!asset.MatchesCurrentPlatform) throw new InvalidOperationException("Ce paquet n'est pas déclaré compatible avec cette plateforme.");
        // A new directory for every attempt: never overwrite an installed runtime or its saves.
        var directory = Path.Combine(_paths.DataDirectory, "NativePorts", port.Id, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, asset.Name);
        using var client = http is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(30) } : null;
        var network = http ?? client!;
        using var response = await network.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Redirection non HTTPS refusée.");
        if (response.Content.Headers.ContentLength is long size && size != asset.Size) throw new InvalidDataException("Taille différente de la release vérifiée.");
        var temporary = file + ".part";
        try
        {
            long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                int read;
                var last = Environment.TickCount64;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > asset.Size) throw new InvalidDataException("Téléchargement trop volumineux.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (Environment.TickCount64 - last >= 150) { progress?.Report((double)total / asset.Size); last = Environment.TickCount64; }
                }
            }
            if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Le paquet ne correspond pas au SHA-256 publié.");
            File.Move(temporary, file);
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var extracted = Path.Combine(directory, "runtime");
                await Task.Run(() => SecureArchiveExtractor.Extract(file, extracted,
                    new(100000, 8L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024)), cancellationToken).ConfigureAwait(false);
                progress?.Report(1); return extracted;
            }
            progress?.Report(1); return directory;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
