using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
namespace iMirror.App.Presentation;

internal static class WindowNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect
    { public int Left, Top, Right, Bottom; public readonly Rect Rect => new(Left, Top, Right - Left, Bottom - Top); }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    internal static Rect WorkArea(nint window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) { throw new System.ComponentModel.Win32Exception(); }
        return info.Work.Rect;
    }
    internal static Rect WorkAtCursor()
    {
        GetCursorPos(out var point); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(point, 2), ref info)) { throw new System.ComponentModel.Win32Exception(); }
        return info.Work.Rect;
    }
    internal static Rect? WorkForSaved(Rect? bounds)
    {
        if (bounds is not { } rect || !double.IsFinite(rect.X) || !double.IsFinite(rect.Y)) { return null; }
        var point = new Point { X = (int)(rect.X + rect.Width / 2), Y = (int)(rect.Y + rect.Height / 2) };
        var monitor = MonitorFromPoint(point, 0); if (monitor == 0) { return null; }
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? info.Work.Rect : null;
    }
    internal static bool IsOwnedGst(nint window, int ownPid)
    {
        GetWindowThreadProcessId(window, out var pid); var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return ownPid > 0 && pid == ownPid && IsWindowVisible(window) && name.ToString().StartsWith("Gst", StringComparison.OrdinalIgnoreCase);
    }
    internal static uint DpiForArea(Rect area)
    {
        var point = new Point { X = (int)(area.X + area.Width / 2), Y = (int)(area.Y + area.Height / 2) };
        return GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out var dpi, out _) == 0 ? dpi : 96;
    }
    internal static void Place(nint window, Rect bounds)
    { if (!SetWindowPos(window, 0, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x14)) { throw new System.ComponentModel.Win32Exception(); } }
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool AdjustWindowRectExForDpi(ref NativeRect rect, uint style, bool menu, uint extendedStyle, uint dpi);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool SetWindowText(nint window, string title);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
}
