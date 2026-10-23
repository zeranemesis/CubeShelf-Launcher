using Avalonia.Media.Imaging;

namespace CubeShelf.Desktop.Controls;

/// <summary>Reference-counted artwork shared by shelf and detail views, with a bounded idle cache.</summary>
internal static class CoverBitmapCache
{
    private sealed class Entry(string key, Bitmap bitmap)
    {
        public string Key { get; } = key;
        public Bitmap Bitmap { get; } = bitmap;
        public long Bytes { get; } = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        public int References;
        public long LastUse;
    }

    private const long IdleLimit = 24L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly Dictionary<Bitmap, Entry> Bitmaps = new(ReferenceEqualityComparer.Instance);
    private static long _clock;

    public static long EstimatedBytes { get { lock (Gate) return Entries.Values.Sum(entry => entry.Bytes); } }

    public static Bitmap Acquire(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        var key = file.FullName + "\0" + file.LastWriteTimeUtc.Ticks + "\0" + file.Length;
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var entry))
            {
                using var stream = File.OpenRead(file.FullName);
                entry = new Entry(key, Bitmap.DecodeToHeight(stream, 1024, BitmapInterpolationMode.HighQuality));
                Entries.Add(key, entry);
                Bitmaps.Add(entry.Bitmap, entry);
            }
            entry.References++;
            entry.LastUse = ++_clock;
            Trim();
            return entry.Bitmap;
        }
    }

    public static void Release(Bitmap? bitmap)
    {
        if (bitmap is null) return;
        lock (Gate)
        {
            if (!Bitmaps.TryGetValue(bitmap, out var entry)) return;
            if (entry.References <= 0) return;
            entry.References--;
            entry.LastUse = ++_clock;
            Trim();
        }
    }

    public static void ReleaseUnused()
    {
        lock (Gate)
            foreach (var entry in Entries.Values.Where(entry => entry.References == 0).ToArray())
            {
                Entries.Remove(entry.Key);
                Bitmaps.Remove(entry.Bitmap);
                entry.Bitmap.Dispose();
            }
    }

    private static void Trim()
    {
        var idle = Entries.Values.Where(entry => entry.References == 0).OrderBy(entry => entry.LastUse).ToArray();
        var bytes = idle.Sum(entry => entry.Bytes);
        foreach (var entry in idle)
        {
            if (bytes <= IdleLimit) break;
            bytes -= entry.Bytes;
            Entries.Remove(entry.Key);
            Bitmaps.Remove(entry.Bitmap);
            entry.Bitmap.Dispose();
        }
    }
}
