using System.Runtime.InteropServices;

namespace CubeShelf.Desktop.Controls;

/// <summary>Optional SDL2 controller input for Linux/macOS; polled on Avalonia's UI thread.</summary>
internal sealed class SdlGamepad : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Quit(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IsController(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Open(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(nint controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Attached(nint controller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short Axis(nint controller, int axis);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Button(nint controller, int button);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Update();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EventState(int state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    private const uint ControllerSubsystem = 0x00002000;
    private readonly nint _library;
    private readonly Quit _quit;
    private readonly Count _count;
    private readonly IsController _isController;
    private readonly Open _open;
    private readonly Close _close;
    private readonly Attached _attached;
    private readonly Axis _axis;
    private readonly Button _button;
    private readonly Update _update;
    private nint _controller;
    private long _nextDiscovery;
    private bool _disposed;

    private SdlGamepad(nint library)
    {
        _library = library;
        T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        var init = Bind<Init>("SDL_InitSubSystem");
        _quit = Bind<Quit>("SDL_QuitSubSystem");
        _count = Bind<Count>("SDL_NumJoysticks");
        _isController = Bind<IsController>("SDL_IsGameController");
        _open = Bind<Open>("SDL_GameControllerOpen");
        _close = Bind<Close>("SDL_GameControllerClose");
        _attached = Bind<Attached>("SDL_GameControllerGetAttached");
        _axis = Bind<Axis>("SDL_GameControllerGetAxis");
        _button = Bind<Button>("SDL_GameControllerGetButton");
        _update = Bind<Update>("SDL_GameControllerUpdate");
        var hint = Bind<SetHint>("SDL_SetHint");
        var events = Bind<EventState>("SDL_GameControllerEventState");
        var joystickEvents = Bind<EventState>("SDL_JoystickEventState");
        // Avalonia owns the window, so SDL has no focused SDL_Window. The caller
        // only polls while CubeShelf or its modal dialog is active.
        hint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (init(ControllerSubsystem) != 0) throw new InvalidOperationException("SDL controller initialization failed.");
        // Consume state directly, without accumulating an unused SDL event queue.
        events(0);
        joystickEvents(0);
    }

    public static SdlGamepad? TryCreate()
    {
        var names = OperatingSystem.IsMacOS()
            ? new[] { "libSDL2.dylib", "/opt/homebrew/lib/libSDL2.dylib", "/usr/local/lib/libSDL2.dylib" }
            : new[] { "libSDL2-2.0.so.0", "libSDL2.so" };
        foreach (var name in names)
        {
            if (!NativeLibrary.TryLoad(name, out var library)) continue;
            try { return new SdlGamepad(library); }
            catch (Exception error) when (error is EntryPointNotFoundException or InvalidOperationException or ArgumentException)
            { NativeLibrary.Free(library); }
        }
        return null;
    }

    public int Read(out bool connected)
    {
        connected = false;
        if (_disposed) return 0;
        _update();
        if (_controller != 0 && _attached(_controller) == 0) { _close(_controller); _controller = 0; }
        if (_controller == 0)
        {
            var now = Environment.TickCount64;
            if (now < _nextDiscovery) return 0;
            _nextDiscovery = now + 1000;
            for (var index = 0; index < Math.Min(16, _count()); index++)
                if (_isController(index) != 0 && (_controller = _open(index)) != 0) break;
        }
        if (_controller == 0) return 0;
        connected = true;
        bool Pressed(int index) => _button(_controller, index) != 0;
        if (Pressed(1)) return 6;
        if (Pressed(0)) return 5;
        if (Pressed(2)) return 7;
        if (Pressed(3)) return 8;
        if (Pressed(9)) return 9;
        if (Pressed(10)) return 10;
        if (Pressed(13) || _axis(_controller, 0) < -16000) return 1;
        if (Pressed(14) || _axis(_controller, 0) > 16000) return 2;
        if (Pressed(11) || _axis(_controller, 1) < -16000) return 3;
        if (Pressed(12) || _axis(_controller, 1) > 16000) return 4;
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_controller != 0) _close(_controller);
        _quit(ControllerSubsystem);
        NativeLibrary.Free(_library);
    }
}
