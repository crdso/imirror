namespace iMirror.Input;

public readonly record struct VideoViewport(double Left, double Top, double Width, double Height, double Scale)
{
    public static VideoViewport Fit(double clientWidth, double clientHeight, int streamWidth, int streamHeight)
    {
        if (clientWidth <= 0 || clientHeight <= 0 || streamWidth <= 0 || streamHeight <= 0) { return default; }
        double scale = Math.Min(clientWidth / streamWidth, clientHeight / streamHeight);
        double width = streamWidth * scale, height = streamHeight * scale;
        return new((clientWidth - width) / 2, (clientHeight - height) / 2, width, height, scale);
    }
    public bool Contains(double x, double y) => Scale > 0 && x >= Left && y >= Top && x < Left + Width && y < Top + Height;
    public (double X, double Y)? ToStream(double x, double y) => Contains(x, y) ? ((x - Left) / Scale, (y - Top) / Scale) : null;
    public static double Physical(double dips, double dpi) => dips * dpi / 96;
}

public static class VirtualKeyMap
{
    public static byte Usage(int key) => key switch
    {
        >= 0x41 and <= 0x5A => (byte)(key - 0x41 + 4),
        >= 0x31 and <= 0x39 => (byte)(key - 0x31 + 0x1E), 0x30 => 0x27,
        >= 0x70 and <= 0x7B => (byte)(key - 0x70 + 0x3A),
        0x0D => 0x28, 0x08 => 0x2A, 0x09 => 0x2B, 0x20 => 0x2C, 0x14 => 0x39,
        0x27 => 0x4F, 0x25 => 0x50, 0x28 => 0x51, 0x26 => 0x52,
        0x2D => 0x49, 0x24 => 0x4A, 0x21 => 0x4B, 0x2E => 0x4C, 0x23 => 0x4D, 0x22 => 0x4E,
        0xBD => 0x2D, 0xBB => 0x2E, 0xDB => 0x2F, 0xDD => 0x30, 0xDC => 0x31,
        0xBA => 0x33, 0xDE => 0x34, 0xC0 => 0x35, 0xBC => 0x36, 0xBE => 0x37, 0xBF => 0x38,
        _ => 0 // ESC is exclusively local; US fallback. Capture maps OEM scan codes through KeyboardLayout.
    };
    public static byte Modifier(int key) => key switch
    { 0xA2 => 1, 0xA0 => 2, 0xA4 => 4, 0x5B => 8, 0xA3 => 16, 0xA1 => 32, 0xA5 => 64, 0x5C => 128, _ => 0 };
}

public sealed class KeyboardState
{
    private readonly Dictionary<int, byte> _pressed = [];
    public bool Update(int key, bool down) => Update(key, down, VirtualKeyMap.Usage(key));
    public bool Update(int key, bool down, byte usage) => down ? _pressed.TryAdd(key, usage) : _pressed.Remove(key);
    public byte Modifiers => (byte)_pressed.Keys.Aggregate(0, (bits, key) => bits | VirtualKeyMap.Modifier(key));
    public byte[] Usages => _pressed.Values.Where(usage => usage != 0).Distinct().ToArray();
    public void Reset() => _pressed.Clear();
}

public sealed record InputReport(bool Keyboard, byte Buttons, int X, int Y, int Wheel, byte Modifiers, byte[] Keys)
{
    public static InputReport Mouse(byte buttons, int x = 0, int y = 0, int wheel = 0) => new(false, buttons, x, y, wheel, 0, []);
    public static InputReport Key(byte modifiers, byte[] keys) => new(true, 0, 0, 0, 0, modifiers, (byte[])keys.Clone());
}

// Coalesce only consecutive motion, never across a click, wheel or key transition.
// An overloaded link fails open rather than dropping a release or retaining stale input.
public sealed class InputReportBuffer(int capacity = 128)
{
    private readonly object _gate = new();
    private readonly LinkedList<InputReport> _queue = [];
    private bool _lastWasMotion;
    private bool _releaseRequested;
    private int _generation;
    public int Generation { get { lock (_gate) { return _generation; } } }
    public bool TakeReleaseRequest() { lock (_gate) { bool value = _releaseRequested; _releaseRequested = false; return value; } }
    public void ClearAndRelease() { lock (_gate) { _queue.Clear(); _lastWasMotion = false; _releaseRequested = true; _generation++; } }
    public int Count { get { lock (_gate) { return _queue.Count; } } }
    public bool Enqueue(InputReport report, bool motion = false)
    {
        lock (_gate)
        {
            if (motion && _lastWasMotion && _queue.Last?.Value is { Keyboard: false, Wheel: 0 } last && last.Buttons == report.Buttons)
            {
                _queue.Last.Value = report with { X = Clamp((long)last.X + report.X), Y = Clamp((long)last.Y + report.Y) }; return true;
            }
            if (_queue.Count >= capacity) { return false; }
            _queue.AddLast(report); _lastWasMotion = motion; return true;
        }
    }
    private static int Clamp(long value) => (int)Math.Clamp(value, -32767L, 32767L);
    public InputReport? Take() { lock (_gate) { var value = _queue.First?.Value; if (value is not null) { _queue.RemoveFirst(); } return value; } }
    public void Clear() { lock (_gate) { _queue.Clear(); _lastWasMotion = false; } }
}

public sealed class WheelAccumulator
{
    private int _remainder;
    public int Add(int delta, int intensity) { _remainder += delta; int steps = _remainder / 120; _remainder %= 120; return Math.Clamp(steps * Math.Clamp(intensity, 1, 5), -127, 127); }
    public void Reset() => _remainder = 0;
}

public static class CapturePolicy
{
    public static bool CanSend(bool active, bool ownForeground, bool insideViewport, bool subscribed) => active && ownForeground && insideViewport && subscribed;
    public static bool Emergency(int key, bool ctrl, bool alt) => key == 0x1B || key == 0x51 && ctrl && alt;
}
