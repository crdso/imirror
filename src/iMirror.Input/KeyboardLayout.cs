using System.Runtime.InteropServices;
using iMirror.Bluetooth;

namespace iMirror.Input;

public enum KeyboardLayoutMode { Auto, PortugueseBrazilAbnt2, UnitedStates }
public readonly record struct PhysicalKey(byte Usage, byte Modifiers = 0);

public static class KeyboardLayout
{
    public static KeyboardLayoutMode Resolve(KeyboardLayoutMode mode, nint hkl) => mode != KeyboardLayoutMode.Auto ? mode :
        ((long)hkl & 0xFFFF) == 0x0416 ? KeyboardLayoutMode.PortugueseBrazilAbnt2 : KeyboardLayoutMode.UnitedStates;
    public static KeyboardLayoutMode ForWindow(KeyboardLayoutMode mode, VideoWindow window)
    {
        uint thread = Native.GetWindowThreadProcessId(window.Handle, out _);
        return Resolve(mode, GetKeyboardLayout(thread));
    }
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);

    // Set-1 positions -> USB HID positions. OEM virtual keys depend on HKL;
    // e.g. ABNT2 apostrophe is scan 29 (usage 35), not US usage 34.
    public static byte Usage(int vk, uint scan, bool extended, KeyboardLayoutMode mode)
    {
        if (vk == 0x1B || VirtualKeyMap.Modifier(vk) != 0) { return 0; }
        if (scan == 0x73 || vk == 0xC1) { return 0; } // International1 is outside iOS Stable.
        if (extended) { return vk == 0x0D ? (byte)0x58 : vk == 0x6F ? (byte)0x54 : VirtualKeyMap.Usage(vk); }
        byte usage = scan switch
        {
            0x0C => 0x2D, 0x0D => 0x2E, 0x1A => 0x2F, 0x1B => 0x30,
            0x2B => 0x31, 0x27 => 0x33, 0x28 => 0x34, 0x29 => 0x35,
            0x33 => 0x36, 0x34 => 0x37, 0x35 => 0x38, 0x56 => 0x64, 0x73 => 0,
            _ => 0
        };
        if (usage != 0) { return usage; }
        if (mode == KeyboardLayoutMode.PortugueseBrazilAbnt2)
        {
            return vk switch { 0xC0 => 0x35, 0xDE => 0x2F, 0xDB => 0x30, 0xDD => 0x31,
                0xBA => 0x33, 0xDC => 0x34, 0xBF => 0x38, 0xE2 => 0x64, 0xC1 => 0, _ => VirtualKeyMap.Usage(vk) };
        }
        return vk == 0xE2 ? (byte)0x64 : VirtualKeyMap.Usage(vk);
    }

    // Diagnostic text composition: standard physical presses, each followed by
    // a neutral report. Production hooks forward physical dead-key press/release
    // directly; the iPhone's selected hardware layout performs composition.
    public static PhysicalKey[] Compose(char value, KeyboardLayoutMode mode)
    {
        if (mode == KeyboardLayoutMode.Auto) { throw new ArgumentException("Resolve Auto antes de compor texto."); }
        if (mode == KeyboardLayoutMode.UnitedStates)
        {
            var report = HidSchema.MapAscii(value); return [new(report[2], report[0])];
        }
        const string accented = "áàâãéêíóôõú";
        const string bases = "aaaaeeiooou";
        const string accents = "´`^~´^´´^~´";
        int index = accented.IndexOf(value);
        if (index >= 0) { return [Dead(accents[index]), Letter(bases[index])]; }
        if (value is '´' or '`' or '^' or '~') { return [Dead(value), new(0x2C)]; }
        if (value == 'ç') { return [new(0x33)]; }
        const string plain = "'[];,./\\-= ";
        const string shifted = "\"{}:<>?|_+ ";
        byte[] usages = [0x35,0x30,0x31,0x38,0x36,0x37,0x87,0x64,0x2D,0x2E,0x2C];
        index = plain.IndexOf(value);
        if (index >= 0) { return StableKey(usages[index]); }
        index = shifted.IndexOf(value);
        if (index >= 0) { return StableKey(usages[index], 2); }
        if (value == '@') { return [new(0x1F,2)]; }
        var ascii = HidSchema.MapAscii(value); return [new(ascii[2],ascii[0])];
    }
    private static PhysicalKey Letter(char value) => new((byte)(4 + value - 'a'));
    private static PhysicalKey[] StableKey(byte usage, byte modifiers = 0) => usage <= HidSchema.MaximumKeyboardUsage
        ? [new(usage, modifiers)] : throw new InputBlockedException("iOS Stable: a tecla ABNT2 International1 (/ e ?) exige usage 0x87, fora do descriptor validado. Nenhum texto foi enviado.");
    private static PhysicalKey Dead(char value) => value switch { '´' => new(0x2F), '`' => new(0x2F,2), '~' => new(0x34), '^' => new(0x34,2), _ => throw new ArgumentException("Dead key inválida.") };
    public static byte[][] PrepareText(string text, KeyboardLayoutMode mode)
    {
        if (text.Length is 0 or > 120) { throw new ArgumentException("Use 1..120 caracteres."); }
        return text.SelectMany(value => Compose(value, mode)).SelectMany(key => new[] { HidSchema.Keyboard(key.Usage,key.Modifiers), HidSchema.Neutral(HidSchema.KeyboardId) }).ToArray();
    }
}

public sealed class RelativeMotion
{
    private double _x, _y;
    public (int X, int Y) Add(double x, double y, double speed)
    {
        speed = double.IsFinite(speed) ? Math.Clamp(speed,0.25,3) : 1;
        _x += x * speed; _y += y * speed;
        int dx = (int)Math.Clamp(Math.Truncate(_x),-32767,32767), dy = (int)Math.Clamp(Math.Truncate(_y),-32767,32767);
        // Discard overflow rather than retaining a burst for a later event.
        _x = Math.Abs(_x) > 32767 ? 0 : _x - dx; _y = Math.Abs(_y) > 32767 ? 0 : _y - dy;
        return (dx,dy);
    }
    public void Reset() => _x = _y = 0;
}
