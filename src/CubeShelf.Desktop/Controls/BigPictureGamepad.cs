using System.Runtime.InteropServices;
using CubeShelf.Core.Platform;

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
    private StickDirection _stickDirection;

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
        if (!_disposed && _sdl is not null) return _sdl.Read(out connected, ref _stickDirection);
        if (_disposed || _getState is null) return 0;
        State state;
        if (_controllerIndex is { } current && _getState(current, out state) == 0)
        {
            connected = true;
            return Input(state);
        }
        _controllerIndex = null;
        _stickDirection = StickDirection.Neutral;
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

    private int Input(State state)
    {
        if ((state.Buttons & 0x2000) != 0) return 6;
        if ((state.Buttons & 0x1000) != 0) return 5;
        if ((state.Buttons & 0x4000) != 0) return 7;
        if ((state.Buttons & 0x8000) != 0) return 8;
        if ((state.Buttons & 0x0100) != 0) return 9;
        if ((state.Buttons & 0x0200) != 0) return 10;
        if ((state.Buttons & 0x0020) != 0) return 16; // View / Select opens the personal library.
        if ((state.Buttons & 0x0010) != 0) return 15;
        var dpad = (state.Buttons & 4) != 0 ? 1 : (state.Buttons & 8) != 0 ? 2
            : (state.Buttons & 1) != 0 ? 3 : (state.Buttons & 2) != 0 ? 4 : 0;
        return DirectionInput(dpad,state.LeftX,state.LeftY,ref _stickDirection);
    }

    internal static int DirectionInput(int dpad,int x,int y,ref StickDirection held)
    {
        held=StickNavigation.Resolve(x/32768f,-y/32768f,held);
        return dpad!=0?dpad:held==StickDirection.Neutral?0:(int)held+10;
    }

    internal static int MapNavigation(int input,bool swap) => !swap ? input : input switch
    { >=1 and <=4 => input+10, >=11 and <=14 => input-10, _=>input };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sdl?.Dispose();
        if (_library != 0) NativeLibrary.Free(_library);
    }
}
