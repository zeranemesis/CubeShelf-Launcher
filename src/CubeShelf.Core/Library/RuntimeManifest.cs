using System.Text.Json;

namespace CubeShelf.Core.Library;

/// <summary>
/// What a runtime package says it can do, in the <c>manifest.json</c> it ships beside its
/// executables: <c>{"capabilities": ["launcher-invites", "retroachievements-login", …]}</c>.
///
/// Read rather than probed, because an older build treats what it does not know as nothing: an
/// unknown flag is ignored, an unknown variable unread. An absent, unreadable or silent manifest
/// answers no, which is always the safe direction -- the worst outcome is not offering something
/// the game could have done.
/// </summary>
public static class RuntimeManifest
{
    private const long MaximumBytes = 1024 * 1024;

    public static bool HasCapability(string? executable, string capability)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(capability)) return false;

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            if (string.IsNullOrEmpty(directory)) return false;

            var manifest = Path.Combine(directory, "manifest.json");
            var file = new FileInfo(manifest);
            if (!file.Exists || file.Length == 0 || file.Length > MaximumBytes) return false;

            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("capabilities", out var capabilities) ||
                capabilities.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var entry in capabilities.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String &&
                    string.Equals(entry.GetString(), capability, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
