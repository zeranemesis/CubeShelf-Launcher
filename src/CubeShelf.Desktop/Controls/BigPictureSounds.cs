using System.Runtime.InteropServices;

namespace CubeShelf.Desktop.Controls;

internal sealed class BigPictureSounds
{
    private readonly Action<string?> _play;
    private readonly Func<long> _clock;
    private long _nextClick = long.MinValue;

    internal BigPictureSounds(Action<string?>? play = null, Func<long>? clock = null)
    {
        _play = play ?? PlayWindowsSound;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    internal void Click()
    {
        var now = _clock();
        if (now < _nextClick) return;
        _nextClick = now + 80;
        _play("click.wav");
    }

    internal void Launch()
    {
        // The launch chime takes priority over rapid navigation and routed click events.
        _nextClick = _clock() + 500;
        _play("launch.wav");
    }

    internal void Stop()
    {
        _nextClick = long.MinValue;
        _play(null);
    }

    private static void PlayWindowsSound(string? file)
        =>PlayFile(file is null?null:Path.Combine(AppContext.BaseDirectory,"Assets","Sounds",file));

    internal static void PlayFile(string? path)
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint asyncFilenameNoDefault = 0x0001 | 0x00020000 | 0x0002;
        _ = PlaySoundW(path,0,path is null ? 0 : asyncFilenameNoDefault);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(string? sound, nint module, uint flags);
}
