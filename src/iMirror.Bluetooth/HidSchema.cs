// Composite descriptor adapted from abhishek-raj/windows-ble-hid (MIT).
// Copyright (c) 2026 Abhishek Raj. See THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;

namespace iMirror.Bluetooth;

public static class HidSchema
{
    public static Guid Uuid(ushort value) => new($"0000{value:x4}-0000-1000-8000-00805f9b34fb");
    public const byte KeyboardId = 1;
    public const byte MouseId = 2;
    public const int KeyboardLength = 8;
    public const int MouseLength = 6;

    // Report mode descriptor. Separate standard Boot Input characteristics handle Protocol Mode 0.
    public static readonly byte[] ReportMap =
    [
        // Keyboard: modifiers, reserved byte, six usages.
        0x05,0x01, 0x09,0x06, 0xA1,0x01, 0x85,KeyboardId,
        0x05,0x07, 0x19,0xE0, 0x29,0xE7, 0x15,0x00, 0x25,0x01,
        0x75,0x01, 0x95,0x08, 0x81,0x02,
        0x95,0x01, 0x75,0x08, 0x81,0x01,
        // ABNT2 International1 (0x87): positive 16-bit logical maximum.
        // IDs, lengths and all mouse fields remain identical to the validated map.
        0x95,0x06, 0x75,0x08, 0x15,0x00, 0x26,0x87,0x00,
        0x05,0x07, 0x19,0x00, 0x29,0x87, 0x81,0x00, 0xC0,
        // Mouse: three buttons, padding, signed 16-bit relative X/Y, signed 8-bit wheel.
        0x05,0x01, 0x09,0x02, 0xA1,0x01, 0x85,MouseId,
        0x09,0x01, 0xA1,0x00, 0x05,0x09, 0x19,0x01, 0x29,0x03,
        0x15,0x00, 0x25,0x01, 0x95,0x03, 0x75,0x01, 0x81,0x02,
        0x95,0x01, 0x75,0x05, 0x81,0x01,
        0x05,0x01, 0x09,0x30, 0x09,0x31, 0x16,0x01,0x80,
        0x26,0xFF,0x7F, 0x75,0x10, 0x95,0x02, 0x81,0x06,
        0x09,0x38, 0x15,0x81, 0x25,0x7F, 0x75,0x08, 0x95,0x01,
        0x81,0x06, 0xC0,0xC0
    ];

    public static byte[] Neutral(byte id) => new byte[id switch
    {
        KeyboardId => KeyboardLength,
        MouseId => MouseLength,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    }];

    public static byte[] Keyboard(byte usage, byte modifiers = 0)
    {
        if (usage is >= 0xE0 and <= 0xE7)
        {
            modifiers |= (byte)(1 << (usage - 0xE0));
            usage = 0;
        }
        else if (usage is < 0x04 or > 0x87) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        byte[] payload = Neutral(KeyboardId);
        payload[0] = modifiers;
        payload[2] = usage;
        return payload;
    }

    public static byte[] KeyboardState(byte modifiers, IEnumerable<byte> usages)
    {
        byte[] report = Neutral(KeyboardId); report[0] = modifiers;
        byte[] keys = usages.Where(value => value is >= 4 and <= 0x87).Distinct().Order().ToArray();
        if (keys.Length > 6) { Array.Fill(report, (byte)1, 2, 6); return report; } // HID ErrorRollOver.
        keys.CopyTo(report, 2); return report;
    }

    public static byte[] ClampedMouse(byte buttons, int dx, int dy, int wheel) =>
        Mouse(Math.Clamp(dx, -32767, 32767), Math.Clamp(dy, -32767, 32767), Math.Clamp(wheel, -127, 127), (byte)(buttons & 7));

    public static byte[] Mouse(int dx, int dy, int wheel = 0, byte buttons = 0)
    {
        if (dx is < -32767 or > 32767 || dy is < -32767 or > 32767 || wheel is < -127 or > 127 || buttons > 7)
        { throw new ArgumentOutOfRangeException(nameof(dx), "Mouse: dx/dy -32767..32767, wheel -127..127."); }
        byte[] payload = Neutral(MouseId);
        payload[0] = buttons;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1, 2), (short)dx);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3, 2), (short)dy);
        payload[5] = unchecked((byte)(sbyte)wheel);
        return payload;
    }

    public static byte[] MapAscii(char value)
    {
        if (value is >= 'a' and <= 'z') { return Keyboard((byte)(4 + value - 'a')); }
        if (value is >= 'A' and <= 'Z') { return Keyboard((byte)(4 + value - 'A'), 2); }
        if (value is >= '1' and <= '9') { return Keyboard((byte)(0x1E + value - '1')); }
        if (value == '0') { return Keyboard(0x27); }
        if (value == ' ') { return Keyboard(0x2C); }
        const string plain = "-=[]\\;'`,./";
        const string shifted = "_+{}|:\"~<>?";
        byte[] usages = [0x2D,0x2E,0x2F,0x30,0x31,0x33,0x34,0x35,0x36,0x37,0x38];
        int index = plain.IndexOf(value);
        if (index >= 0) { return Keyboard(usages[index]); }
        index = shifted.IndexOf(value);
        if (index >= 0) { return Keyboard(usages[index], 2); }
        index = "!@#$%^&*()".IndexOf(value);
        if (index >= 0) { return Keyboard(index == 9 ? (byte)0x27 : (byte)(0x1E + index), 2); }
        throw new ArgumentException("type aceita somente ASCII imprimível com layout US; nenhum caractere foi enviado.");
    }

    public static byte[][] PrepareText(string text)
    {
        if (text.Length is 0 or > 120) { throw new ArgumentException("type: use 1..120 caracteres ASCII."); }
        // Validate the entire string before emitting any report.
        return text.Select(MapAscii).ToArray();
    }
}
