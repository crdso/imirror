using System.Runtime.InteropServices;

namespace iMirror.AirPlay;

public static class MirrorTcpInspection
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    // Inspect only state/port/PID. Addresses are never converted to text or logged.
    public static bool HasEstablishedConnection(int processId, int localPort)
    {
        if (!OperatingSystem.IsWindows()) { return false; }
        return Inspect(processId, localPort, 2, 24, 0, 8, 20) || Inspect(processId, localPort, 23, 56, 48, 20, 52);
    }
    private static bool Inspect(int processId, int port, int family, int rowSize, int stateOffset, int portOffset, int pidOffset)
    {
        var size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0) != 122 || size is < 4 or > 16777216) { return false; }
        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, family, 5, 0) != 0) { return false; }
            var count = Marshal.ReadInt32(table);
            if (count < 0 || count > (size - 4) / rowSize) { return false; }
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(table, 4 + i * rowSize);
                if (Marshal.ReadInt32(row, stateOffset) == 5 && Marshal.ReadInt32(row, pidOffset) == processId &&
                    ((Marshal.ReadByte(row, portOffset) << 8) | Marshal.ReadByte(row, portOffset + 1)) == port) { return true; }
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(table); }
    }
}
