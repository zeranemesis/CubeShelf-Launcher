using System.Runtime.InteropServices;

namespace CubeShelf.Desktop.Controls;

/// <summary>A private message loop registers a shortcut without intercepting game keyboard input.</summary>
internal sealed class WindowsHelpHotkey : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private readonly WinEventProc _foreground;
    public bool MonitorsFocus { get; private set; }
    public WindowsHelpHotkey(Action open, Action? foregroundChanged = null)
    {
        _foreground = (_, kind, _, objectId, child, _, _) => { if (kind == 3 || objectId == 0 && child == 0) foregroundChanged?.Invoke(); };
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var registered = RegisterHotKey(IntPtr.Zero, 0x4353, 0x4006, 0x48); // Ctrl+Shift+H, no repeat
            var hook = SetWinEventHook(3, 3, IntPtr.Zero, _foreground, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context
            var movement = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, _foreground, 0, 0, 0); // window location changes
            MonitorsFocus = hook != IntPtr.Zero && movement != IntPtr.Zero;
            _ready.Set();
            try { while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { if (message.Id == 0x0312) open(); else DispatchMessage(ref message); } }
            finally { if (registered) UnregisterHotKey(IntPtr.Zero, 0x4353); if (hook != IntPtr.Zero) UnhookWinEvent(hook); if (movement != IntPtr.Zero) UnhookWinEvent(movement); GC.KeepAlive(_foreground); }
        }) { IsBackground = true, Name = "DecompShelf help shortcut" };
        _thread.Start(); _ready.Wait();
    }
    public static IntPtr ForegroundWindow => GetForegroundWindow();
    public static IntPtr GameWindowForHelp(IntPtr foreground, IntPtr buddy, IntPtr panel, IntPtr game) =>
        foreground != IntPtr.Zero && (foreground == buddy || foreground == panel) ? game : foreground;
    public static void RestoreForeground(IntPtr window) { if (window != IntPtr.Zero && IsWindow(window)) SetForegroundWindow(window); }
    public void Dispose() { if (_thread.IsAlive) { PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero); _thread.Join(1000); } _ready.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
}
