using System.Runtime.InteropServices;
using CubeShelf.Assistance;

namespace CubeShelf.Desktop.Controls;

internal static class WindowsGameCapture
{
    public static uint ProcessId(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid; }
    public static (int X, int Y, int Width, int Height)? ClientBounds(IntPtr window)
    {
        if (!GetClientRect(window, out var rect) || rect.Right < 16 || rect.Bottom < 16 || IsIconic(window)) return null;
        var point = new Point();
        return ClientToScreen(window, ref point) ? (point.X, point.Y, rect.Right, rect.Bottom) : null;
    }

    public static GameFrame Capture(IntPtr window, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var processId = ProcessId(window);
        if (processId == 0 || processId == Environment.ProcessId) throw new InvalidOperationException("Aucune fenêtre de jeu à analyser.");
        if (window == IntPtr.Zero || WindowsHelpHotkey.ForegroundWindow != window || IsIconic(window) ||
            !GetClientRect(window, out var rect) || rect.Right < 16 || rect.Bottom < 16)
            throw new InvalidOperationException("Reviens dans le jeu, puis demande à nouveau de l’aide.");
        var origin = new Point();
        if (!ClientToScreen(window, ref origin)) throw new InvalidOperationException("Fenêtre du jeu inaccessible.");
        var scale = Math.Min(1d, 960d / Math.Max(rect.Right, rect.Bottom));
        var width = Math.Max(1, (int)(rect.Right * scale)); var height = Math.Max(1, (int)(rect.Bottom * scale));
        var screen = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height); var previous = SelectObject(memory, bitmap);
        byte[]? pixels = null;
        try
        {
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("Capture Windows indisponible.");
            DwmFlush(); SetStretchBltMode(memory, 4);
            if (!StretchBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, rect.Right, rect.Bottom, 0x40CC0020))
                throw new InvalidOperationException("Le jeu refuse la capture. Essaie le mode fenêtré sans bordure.");
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            pixels = new byte[width * height * 4];
            SelectObject(memory, previous); previous = IntPtr.Zero;
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height)
                throw new InvalidOperationException("Lecture de l’image du jeu impossible.");
            cancellation.ThrowIfCancellationRequested();
            if (WindowsHelpHotkey.ForegroundWindow != window || ProcessId(window) != processId) throw new InvalidOperationException("Le jeu a perdu le premier plan. Relance la demande depuis le jeu.");
            var rgb = new byte[width * height * 3];
            for (int p = 0, q = 0; p < pixels.Length; p += 4, q += 3) { rgb[q] = pixels[p + 2]; rgb[q + 1] = pixels[p + 1]; rgb[q + 2] = pixels[p]; }
            if (rgb.All(value => value < 4)) throw new InvalidOperationException("Capture noire : attends la fin du chargement ou passe en mode sans bordure.");
            return new GameFrame(width, height, rgb);
        }
        finally
        {
            if (pixels is not null) Array.Clear(pixels);
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
