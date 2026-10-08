using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace iMirror.Input;

// Read motion only: buttons/wheel remain in the existing ordered hook path.
// No device identity, hardware data or injected cursor positions are persisted.
public static class RawMouseMotion
{
    public static (int X, int Y)? Decode(ReadOnlySpan<byte> packet, int headerSize)
    {
        if (headerSize is not (16 or 24) || packet.Length < headerSize + 24 ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet) != 0) { return null; }
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        if (declared < headerSize + 24 || declared > packet.Length) { return null; }
        var mouse = packet[headerSize..];
        if ((BinaryPrimitives.ReadUInt16LittleEndian(mouse) & 1) != 0) { return null; } // absolute tablet/touch is not relative movement.
        return (BinaryPrimitives.ReadInt32LittleEndian(mouse[12..]), BinaryPrimitives.ReadInt32LittleEndian(mouse[16..]));
    }
    internal static (int X, int Y)? Read(nint input)
    {
        uint size = 0, header = (uint)(8 + 2 * IntPtr.Size);
        if (GetRawInputData(input, 0x10000003, null, ref size, header) == uint.MaxValue) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (size < header + 24 || size > 4096) { return null; }
        var packet = new byte[size];
        uint read = GetRawInputData(input, 0x10000003, packet, ref size, header);
        if (read == uint.MaxValue) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        return Decode(packet.AsSpan(0, (int)read), (int)header);
    }
    internal static void Register(nint window, bool remove = false)
    {
        var device = new Device { Page = 1, Usage = 2, Flags = remove ? 1u : 0x100u, Window = remove ? 0 : window };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<Device>())) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Window; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, [Out] byte[]? data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
}
