using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social;

/// <summary>
/// Keeps the identity key encrypted at rest with Windows' own data protection (DPAPI), tied to
/// the Windows account that wrote it.
///
/// Two things change. A copy of the profile folder -- a backup drive, a synced profile, a second
/// PC -- no longer carries a usable key: it cannot publish as us, which is how two installations
/// would end up fighting over one presence document. And another account on the same PC cannot
/// read it either. The price is that the key no longer survives a reinstall of Windows on its own,
/// which is why the profile page keeps asking for a passphrase-sealed backup (ProfileTransfer)
/// until one exists.
///
/// Called through crypt32 directly rather than the ProtectedData package: CubeShelf ships no
/// third-party code, and this is two functions. Elsewhere than Windows the key stays as it was,
/// protected by the file's permissions.
/// </summary>
internal static class IdentityProtection
{
    public const string Marker = "dpapi1:";

    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("CubeShelf identity v1");

    public static bool Available => OperatingSystem.IsWindows();

    public static string Protect(byte[] secret) => Marker + Convert.ToBase64String(Transform(secret, protect: true));

    /// <summary>Throws <see cref="CryptographicException"/> when this account cannot open it.</summary>
    public static byte[] Unprotect(string text)
    {
        if (!text.StartsWith(Marker, StringComparison.Ordinal))
            throw new CryptographicException("Not a protected identity.");
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(text[Marker.Length..]);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Protected identity unreadable.", exception);
        }
        return Transform(blob, protect: false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    private const int UiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Transform(byte[] input, bool protect)
    {
        if (!Available) throw new PlatformNotSupportedException("La protection de l’identité n’existe que sous Windows.");

        var inputBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(Math.Max(1, input.Length)) };
        var entropyBlob = new DataBlob { Size = Entropy.Length, Data = Marshal.AllocHGlobal(Entropy.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            Marshal.Copy(Entropy, 0, entropyBlob.Data, Entropy.Length);

            var ok = protect
                ? CryptProtectData(ref inputBlob, "CubeShelf identity", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok)
                throw new CryptographicException(Marshal.GetLastWin32Error());

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            // The secret went through unmanaged memory in both directions: wiped before freed.
            ZeroUnmanaged(inputBlob.Data, input.Length);
            Marshal.FreeHGlobal(inputBlob.Data);
            Marshal.FreeHGlobal(entropyBlob.Data);
            if (output.Data != IntPtr.Zero)
            {
                ZeroUnmanaged(output.Data, output.Size);
                LocalFree(output.Data);
            }
        }
    }

    private static void ZeroUnmanaged(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero || length <= 0) return;
        Marshal.Copy(new byte[length], 0, pointer, length);
    }
}
