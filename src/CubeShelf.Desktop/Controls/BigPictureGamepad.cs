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

    public BigPictureGamepad()
    {
        if (!OperatingSystem.IsWindows()) return;
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
        if (_getState is null) return 0;
        for (uint index = 0; index < 4; index++)
        {
            if (_getState(index, out var state) != 0) continue;
            connected = true;
            if ((state.Buttons & 0x2000) != 0) return 6;
            if ((state.Buttons & 0x1000) != 0) return 5;
            if ((state.Buttons & 0x0004) != 0 || state.LeftX < -16000) return 1;
            if ((state.Buttons & 0x0008) != 0 || state.LeftX > 16000) return 2;
            if ((state.Buttons & 0x0001) != 0 || state.LeftY > 16000) return 3;
            if ((state.Buttons & 0x0002) != 0 || state.LeftY < -16000) return 4;
        }
        return 0;
    }

    public void Dispose() { if (_library != 0) NativeLibrary.Free(_library); }
}
