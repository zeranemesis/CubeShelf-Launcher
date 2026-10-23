using System.Runtime.InteropServices;

namespace CubeShelf.Desktop.Controls;

// XInput is optional: keyboard and mouse remain available on every platform.
internal sealed class BigPictureGamepad : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetState(uint index, out State state);
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    private readonly nint _library;
    private readonly GetState? _getState;
    private uint? _controllerIndex;
    private long _nextDiscovery;
    private bool _disposed;
    private readonly SdlGamepad? _sdl;

    public BigPictureGamepad()
    {
        if (!OperatingSystem.IsWindows()) { _sdl = SdlGamepad.TryCreate(); return; }
        foreach (var name in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (!NativeLibrary.TryLoad(name, typeof(BigPictureGamepad).Assembly, DllImportSearchPath.System32, out var library)) continue;
            if (NativeLibrary.TryGetExport(library, "XInputGetState", out var address))
            {
                _library = library;
                _getState = Marshal.GetDelegateForFunctionPointer<GetState>(address);
                break;
            }
            NativeLibrary.Free(library);
        }
    }

    public int Read(out bool connected)
    {
        connected = false;
        if (!_disposed && _sdl is not null) return _sdl.Read(out connected);
        if (_disposed || _getState is null) return 0;
        State state;
        if (_controllerIndex is { } current && _getState(current, out state) == 0)
        {
            connected = true;
            return Input(state);
        }
        _controllerIndex = null;
        var now = Environment.TickCount64;
        if (now < _nextDiscovery) return 0;
        // Disconnected XInput slots can be more expensive than reading a known
        // controller. Discover at most once per second, not four slots per tick.
        _nextDiscovery = now + 1000;
        for (uint index = 0; index < 4; index++)
        {
            if (_getState(index, out state) != 0) continue;
            _controllerIndex = index;
            connected = true;
            return Input(state);
        }
        return 0;
    }

    private static int Input(State state)
    {
        if ((state.Buttons & 0x2000) != 0) return 6;
        if ((state.Buttons & 0x1000) != 0) return 5;
        if ((state.Buttons & 0x4000) != 0) return 7;
        if ((state.Buttons & 0x8000) != 0) return 8;
        if ((state.Buttons & 0x0004) != 0 || state.LeftX < -16000) return 1;
        if ((state.Buttons & 0x0008) != 0 || state.LeftX > 16000) return 2;
        if ((state.Buttons & 0x0001) != 0 || state.LeftY > 16000) return 3;
        if ((state.Buttons & 0x0002) != 0 || state.LeftY < -16000) return 4;
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sdl?.Dispose();
        if (_library != 0) NativeLibrary.Free(_library);
    }
}
