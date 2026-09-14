using System.Text.Json.Serialization;

namespace CubeShelf.Core.Releases;

public sealed record ReleaseArtifact(
    string Os,
    string Architecture,
    string Type,
    string Url,
    long Size,
    string Sha256,
    string? MinimumOsVersion = null);

public sealed record ReleaseManifest(
    int SchemaVersion,
    string Version,
    IReadOnlyList<ReleaseArtifact> Artifacts,
    string? Signature = null)
{
    [JsonIgnore]
    public const int CurrentSchemaVersion = 2;

    public ReleaseArtifact Select(string os, string architecture, string type)
    {
        Validate();

        return Artifacts.SingleOrDefault(artifact =>
                   artifact.Os.Equals(os, StringComparison.OrdinalIgnoreCase) &&
                   artifact.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase) &&
                   artifact.Type.Equals(type, StringComparison.OrdinalIgnoreCase)) ??
               throw new PlatformNotSupportedException(
                   $"No {type} artifact exists for {os}-{architecture}.");
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported release manifest schema: {SchemaVersion}.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                Version,
                "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException("The release version is invalid.");
        if (Artifacts.Count == 0)
            throw new InvalidDataException("The release has no artifacts.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in Artifacts)
        {
            if (artifact.Size <= 0)
                throw new InvalidDataException("Artifact size must be positive.");
            if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Artifact URLs must use HTTPS.");
            if (artifact.Sha256.Length != 64 ||
                artifact.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Artifact SHA-256 is invalid.");

            var key = $"{artifact.Os}/{artifact.Architecture}/{artifact.Type}";
            if (!keys.Add(key))
                throw new InvalidDataException($"Duplicate artifact target: {key}.");
        }
    }
}
