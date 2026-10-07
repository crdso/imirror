using System.Runtime.InteropServices;
using System.Text;

namespace iMirror.AirPlay;

public sealed record VideoWindowSnapshot(int VideoWindows, int VisibleWindows, string[] Classes);

// Read-only inspection restricted to the receiver PID; never show/hide/move any window.
public static class VideoWindowInspection
{
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int size);

    public static VideoWindowSnapshot Read(int ownedProcessId)
    {
        if (!OperatingSystem.IsWindows() || ownedProcessId <= 0) { return new(0, 0, []); }
        var classes = new HashSet<string>(); var total = 0; var visible = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != ownedProcessId) { return true; }
            var text = new StringBuilder(256); GetClassName(window, text, text.Capacity);
            var name = text.ToString();
            if (!name.StartsWith("Gst", StringComparison.OrdinalIgnoreCase)) { return true; }
            total++; if (IsWindowVisible(window)) { visible++; } classes.Add(name);
            return true;
        }, IntPtr.Zero);
        return new(total, visible, classes.Order(StringComparer.Ordinal).ToArray());
    }
}
