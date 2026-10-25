using Avalonia.Media.Imaging;
using SkiaSharp;

namespace CubeShelf.Desktop.Controls;

/// <summary>Reference-counted artwork shared by shelf and detail views, with a bounded idle cache.</summary>
internal static class CoverBitmapCache
{
    private sealed class Entry(string key, Bitmap bitmap, string path)
    {
        public string Key { get; } = key;
        public Bitmap Bitmap { get; } = bitmap;
        public string Path { get; } = path;
        public SKImage? GpuImage;
        public SKShader? GpuShader;
        public int Width { get; } = bitmap.PixelSize.Width;
        public int Height { get; } = bitmap.PixelSize.Height;
        public bool Retired;
        public bool Preparing;
        public CancellationTokenSource? PreparationCancellation;
        public long Bytes => (long)Bitmap.PixelSize.Width * Bitmap.PixelSize.Height * 4 * (GpuImage is null ? 1 : 2);
        public int References;
        public long LastUse;
    }

    private const long IdleLimit = 24L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly Dictionary<Bitmap, Entry> Bitmaps = new(ReferenceEqualityComparer.Instance);
    private static long _clock;
    private static readonly SemaphoreSlim PreparationSlot = new(1, 1);
    private static readonly List<Entry> RemovalBuffer = new();

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
                entry = new Entry(key, Bitmap.DecodeToHeight(stream, 1024, BitmapInterpolationMode.HighQuality), file.FullName);
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

    // Paints retain their native shader independently of the cache. Pending
    // scene operations remain valid when their case or cached image is released.
    public static SKPaint? CreateTexturePaint(Bitmap bitmap)
    {
        Entry entry;
        lock (Gate)
        {
            if (!Bitmaps.TryGetValue(bitmap, out entry!)) return null;
            if (entry.GpuImage is not null)
                return MakeTexturePaint(entry);
        }
        // Never hold the global cache lock during file IO or image decoding.
        var decoded = DecodeTexture(entry);
        lock (Gate)
        {
            if (entry.Retired) { decoded?.Dispose(); return null; }
            if (entry.GpuImage is null) { entry.GpuImage = decoded; decoded = null; }
            decoded?.Dispose();
            if (entry.GpuImage is null) return null;
            return MakeTexturePaint(entry);
        }
    }

    private static SKPaint MakeTexturePaint(Entry entry)
    {
        entry.GpuShader ??= entry.GpuImage!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        return new SKPaint { Shader = entry.GpuShader, FilterQuality = SKFilterQuality.Low, IsAntialias = true };
    }

    public static void PrepareTexture(Bitmap? bitmap)
    {
        if (bitmap is null) return;
        Entry entry;
        CancellationToken cancellation;
        lock (Gate)
        {
            if (!Bitmaps.TryGetValue(bitmap, out entry!) || entry.GpuImage is not null || entry.Preparing) return;
            entry.Preparing = true;
            entry.PreparationCancellation ??= new CancellationTokenSource();
            cancellation = entry.PreparationCancellation.Token;
        }
        _ = Task.Run(async () =>
        {
            var acquired = false;
            SKImage? prepared = null;
            try
            {
                await PreparationSlot.WaitAsync(cancellation).ConfigureAwait(false);
                acquired = true;
                lock (Gate)
                    if (entry.Retired || entry.References == 0 || entry.GpuImage is not null) return;
                prepared = DecodeTexture(entry);
                lock (Gate)
                {
                    if (cancellation.IsCancellationRequested || entry.Retired || entry.References == 0 || entry.GpuImage is not null) return;
                    entry.GpuImage = prepared;
                    prepared = null;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                prepared?.Dispose();
                lock (Gate) entry.Preparing = false;
                if (acquired) PreparationSlot.Release();
            }
        });
    }

    private static SKImage? DecodeTexture(Entry entry)
    {
        try
        {
            using var source = SKBitmap.Decode(entry.Path);
            if (source is null) return null;
            using var scaled = source.Resize(new SKImageInfo(entry.Width, entry.Height), SKFilterQuality.High);
            return scaled is null ? null : SKImage.FromBitmap(scaled);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void Retire(Entry entry)
    {
        entry.Retired = true;
        entry.PreparationCancellation?.Cancel();
        entry.PreparationCancellation?.Dispose();
        entry.Bitmap.Dispose();
        entry.GpuShader?.Dispose();
        entry.GpuImage?.Dispose();
    }

    public static void ReleaseUnused()
    {
        lock (Gate)
        {
            RemovalBuffer.Clear();
            foreach (var entry in Entries.Values)
                if (entry.References == 0) RemovalBuffer.Add(entry);
            foreach (var entry in RemovalBuffer)
            {
                Entries.Remove(entry.Key);
                Bitmaps.Remove(entry.Bitmap);
                Retire(entry);
            }
            RemovalBuffer.Clear();
        }
    }

    private static void Trim()
    {
        long bytes = 0;
        foreach (var entry in Entries.Values)
            if (entry.References == 0) bytes += entry.Bytes;
        while (bytes > IdleLimit)
        {
            Entry? oldest = null;
            foreach (var entry in Entries.Values)
                if (entry.References == 0 && (oldest is null || entry.LastUse < oldest.LastUse)) oldest = entry;
            if (oldest is null) break;
            bytes -= oldest.Bytes;
            Entries.Remove(oldest.Key);
            Bitmaps.Remove(oldest.Bitmap);
            Retire(oldest);
        }
    }
}
