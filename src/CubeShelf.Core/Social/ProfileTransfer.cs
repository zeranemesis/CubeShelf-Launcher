using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeShelf.Core.Social;

/// <summary>A friend as the phone receives it: enough to read their presence, nothing more.</summary>
public sealed record TransferredFriend(
    string PublicKey,
    string DisplayName,
    string PresenceUrl,
    long LastSequence,
    bool Paused);

/// <summary>What <see cref="ProfileTransfer"/> carries: the identity, the pseudo and the friends.</summary>
public sealed record ProfileTransferPayload(
    string DisplayName,
    byte[] PrivateKey,
    byte[] PublicKey,
    IReadOnlyList<TransferredFriend> Friends,
    DateTimeOffset ExportedAt);

/// <summary>
/// Hands this profile to PartyBoard on a phone, which cannot run CubeShelf.
///
/// The phone becomes this same person: it holds the same key, so it opens the documents friends
/// seal for us and sees their invitations. It does not publish -- the computer keeps doing that --
/// so two devices never race to write one presence document.
///
/// The file carries the private key, so it is sealed with a passphrase the user types on both
/// sides: PBKDF2-HMAC-SHA256 into AES-256-GCM. A copied file without the passphrase gives nothing
/// away but its rough size. Blocked people are left out: the phone cannot add friends, so it has
/// no tombstone to honour.
///
/// Layout, after the <c>CSP1-</c> prefix and base64url without padding:
/// <c>salt[16] nonce[12] ciphertext tag[16]</c>. The plaintext is the JSON of
/// <see cref="TransferDocument"/>. PartyBoard reads it in <c>src/port/online/cubeshelf_profile.cpp</c>;
/// both sides must change together.
/// </summary>
public static class ProfileTransfer
{
    public const string Prefix = "CSP1-";
    public const string FileExtension = ".cubeshelf-profile";
    public const int MinimumPassphraseLength = 8;

    // Enough that guessing a weak passphrase costs real time, cheap enough that a phone derives
    // the key in about a second.
    public const int Iterations = 310_000;

    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int ScalarLength = 32;
    private const int MaximumTextLength = 1024 * 1024;

    private static readonly byte[] AssociatedData = "cubeshelf-profile-v1"u8.ToArray();

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Export(
        PeerIdentity identity,
        string displayName,
        IEnumerable<Friend> friends,
        string passphrase,
        DateTimeOffset now) =>
        Export(identity, displayName, friends, passphrase, now,
            RandomNumberGenerator.GetBytes(SaltLength), RandomNumberGenerator.GetBytes(NonceLength));

    private static string Export(
        PeerIdentity identity,
        string displayName,
        IEnumerable<Friend> friends,
        string passphrase,
        DateTimeOffset now,
        byte[] salt,
        byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(friends);
        if (!PeerName.TryNormalize(displayName, out var name, out var nameError))
            throw new ArgumentException(nameError, nameof(displayName));
        if (!IsAcceptablePassphrase(passphrase, out var passphraseError))
            throw new ArgumentException(passphraseError, nameof(passphrase));
        if (salt.Length != SaltLength || nonce.Length != NonceLength)
            throw new ArgumentException("Sel ou nonce de mauvaise taille.");

        var scalar = identity.ExportPrivateScalar();
        byte[]? plaintext = null;
        var key = DeriveKey(passphrase, salt);
        try
        {
            var document = new TransferDocument
            {
                Name = name,
                PrivateKey = Convert.ToBase64String(scalar),
                PublicKey = Convert.ToBase64String(identity.PublicKey),
                ExportedAt = now,
                Friends = friends
                    .Where(friend => !friend.Blocked)
                    .Select(friend => new TransferDocumentFriend
                    {
                        PublicKey = friend.PublicKey,
                        Name = PeerName.Sanitize(friend.DisplayName),
                        PresenceUrl = friend.PresenceUrl,
                        LastSequence = friend.LastSequence,
                        Paused = friend.Paused
                    })
                    .ToList()
            };
            plaintext = JsonSerializer.SerializeToUtf8Bytes(document, Json);

            var framed = new byte[SaltLength + NonceLength + plaintext.Length + TagLength];
            salt.CopyTo(framed, 0);
            nonce.CopyTo(framed, SaltLength);
            using (var gcm = new AesGcm(key, TagLength))
                gcm.Encrypt(
                    nonce,
                    plaintext,
                    framed.AsSpan(SaltLength + NonceLength, plaintext.Length),
                    framed.AsSpan(SaltLength + NonceLength + plaintext.Length, TagLength),
                    AssociatedData);

            return Prefix + Convert.ToBase64String(framed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool TryImport(string? text, string passphrase, out ProfileTransferPayload? payload, out string error)
    {
        payload = null;
        error = "";

        var cleaned = new string((text ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (cleaned.Length > MaximumTextLength || !cleaned.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "Ce n’est pas un profil CubeShelf exporté.";
            return false;
        }

        byte[] framed;
        try
        {
            var body = cleaned[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            framed = Convert.FromBase64String(body.PadRight((body.Length + 3) / 4 * 4, '='));
        }
        catch (FormatException)
        {
            error = "Profil illisible : le fichier a été modifié ou tronqué.";
            return false;
        }

        if (framed.Length <= SaltLength + NonceLength + TagLength)
        {
            error = "Profil incomplet.";
            return false;
        }

        var salt = framed.AsSpan(0, SaltLength).ToArray();
        var nonce = framed.AsSpan(SaltLength, NonceLength);
        var cipherLength = framed.Length - SaltLength - NonceLength - TagLength;
        var plaintext = new byte[cipherLength];
        var key = DeriveKey(passphrase ?? "", salt);
        try
        {
            try
            {
                using var gcm = new AesGcm(key, TagLength);
                gcm.Decrypt(
                    nonce,
                    framed.AsSpan(SaltLength + NonceLength, cipherLength),
                    framed.AsSpan(SaltLength + NonceLength + cipherLength, TagLength),
                    plaintext,
                    AssociatedData);
            }
            catch (CryptographicException)
            {
                error = "Mot de passe incorrect, ou fichier modifié.";
                return false;
            }

            TransferDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<TransferDocument>(plaintext, Json);
            }
            catch (JsonException)
            {
                document = null;
            }

            if (document is null || document.Version != 1)
            {
                error = "Version de profil non prise en charge.";
                return false;
            }

            byte[] scalar, publicKey;
            try
            {
                scalar = Convert.FromBase64String(document.PrivateKey);
                publicKey = Convert.FromBase64String(document.PublicKey);
                PeerIdentity.ValidatePublicKey(publicKey);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                error = "Clé du profil illisible.";
                return false;
            }
            if (scalar.Length != ScalarLength)
            {
                error = "Clé du profil illisible.";
                return false;
            }

            payload = new ProfileTransferPayload(
                PeerName.Sanitize(document.Name),
                scalar,
                publicKey,
                (document.Friends ?? new List<TransferDocumentFriend>())
                    .Select(friend => new TransferredFriend(
                        friend.PublicKey, PeerName.Sanitize(friend.Name), friend.PresenceUrl,
                        friend.LastSequence, friend.Paused))
                    .ToList(),
                document.ExportedAt);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool IsAcceptablePassphrase(string? passphrase, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < MinimumPassphraseLength)
        {
            error = $"Le mot de passe doit faire au moins {MinimumPassphraseLength} caractères.";
            return false;
        }
        return true;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);

    internal sealed class TransferDocument
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 1;
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("d")] public string PrivateKey { get; set; } = "";
        [JsonPropertyName("q")] public string PublicKey { get; set; } = "";
        [JsonPropertyName("at")] public DateTimeOffset ExportedAt { get; set; }
        [JsonPropertyName("friends")] public List<TransferDocumentFriend>? Friends { get; set; }
    }

    internal sealed class TransferDocumentFriend
    {
        [JsonPropertyName("k")] public string PublicKey { get; set; } = "";
        [JsonPropertyName("n")] public string Name { get; set; } = "";
        [JsonPropertyName("u")] public string PresenceUrl { get; set; } = "";
        [JsonPropertyName("s")] public long LastSequence { get; set; }
        [JsonPropertyName("p")] public bool Paused { get; set; }
    }
}
