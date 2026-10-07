using System.Runtime.InteropServices;
using System.Text;

namespace iMirror.Input;

public sealed record VideoWindow(nint Handle, int ProcessId)
{
    public static VideoWindow? Find(int ownProcessId)
    {
        if (ownProcessId <= 0) { return null; }
        VideoWindow? result = null;
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out uint pid);
            var name = new StringBuilder(256); Native.GetClassName(window, name, name.Capacity);
            if (pid == ownProcessId && Native.IsWindowVisible(window) && name.ToString().StartsWith("Gst", StringComparison.OrdinalIgnoreCase))
            { result = new(window, ownProcessId); return false; }
            return true;
        }, 0);
        return result;
    }
    public bool IsForeground => Native.GetAncestor(Native.GetForegroundWindow(), 2) == Handle && IsValid;
    public bool IsValid { get { Native.GetWindowThreadProcessId(Handle, out uint pid); return pid == ProcessId && Native.IsWindowVisible(Handle); } }
    public (VideoViewport Viewport, double X, double Y) Read(int screenX, int screenY, int streamWidth, int streamHeight)
    {
        Native.GetClientRect(Handle, out var rect);
        var point = new Native.Point { X = screenX, Y = screenY }; Native.ScreenToClient(Handle, ref point);
        return (VideoViewport.Fit(rect.Right - rect.Left, rect.Bottom - rect.Top, streamWidth, streamHeight), point.X, point.Y);
    }
}

internal static class Native
{
    internal delegate bool WindowCallback(nint window, nint param);
    internal delegate nint HookCallback(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Key { public uint Code, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, nint param);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
}
