using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace iMirror.Diagnostics
{
    // Diagnostic-only x64 ETW consumer. No ETL file, packet dump, peer addresses,
    // authentication material, SMP keys, ATT contents or HID reports are persisted.
    // Layouts: evntrace.h EVENT_TRACE_LOGFILEW and evntcons.h EVENT_RECORD.
    public sealed class BluetoothLinkTrace : IDisposable
    {
        [StructLayout(LayoutKind.Explicit, Size = 448)]
        private struct TraceFile
        {
            [FieldOffset(8)] public IntPtr LoggerName;
            [FieldOffset(28)] public uint ProcessTraceMode;
            [FieldOffset(424)] public IntPtr EventRecordCallback;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void RecordCallback(IntPtr record);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")]
        private static extern uint StartTrace(out ulong handle, string name, IntPtr properties);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
        private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint control);
        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong any, ulong all, uint timeout, IntPtr parameters);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW", SetLastError = true)]
        private static extern ulong OpenTrace(ref TraceFile file);
        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong handle);
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyDescriptor
        {
            public ulong PropertyName;
            public uint ArrayIndex;
            public uint Reserved;
        }
        [DllImport("tdh.dll")]
        private static extern uint TdhGetProperty(IntPtr record, uint contextCount, IntPtr context,
            uint propertyCount, ref PropertyDescriptor property, uint size, byte[] value);
        private static readonly Guid Provider = new Guid("8A1F9517-3A8C-4A9E-A018-4F17A200F277");
        private readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();
        private readonly Dictionary<ushort, string> _links = new Dictionary<ushort, string>();
        private readonly RecordCallback _callback;
        private readonly string _name = "iMirror.Bluetooth.Status." + Guid.NewGuid().ToString("N");
        private IntPtr _properties, _logger;
        private ulong _session, _consumer = ulong.MaxValue;
        private Task _task;
        private int _disposed, _number, _events, _dropped, _unsupported, _errors, _decoded, _schemaErrors;
        private readonly long[] _types = new long[8];
        public string SessionName { get { return _name; } }
        public bool StopConfirmed { get; private set; }
        public BluetoothLinkTrace() { _callback = OnRecord; }
        private void Line(string text)
        {
            if (_lines.Count >= 500) { Interlocked.Increment(ref _dropped); return; }
            _lines.Enqueue(DateTimeOffset.Now.ToString("o") + " " + text);
        }
        public string[] Drain()
        {
            var result = new List<string>(); string line;
            while (_lines.TryDequeue(out line)) { result.Add(line); }
            return result.ToArray();
        }
        public void Heartbeat()
        { Line("HEARTBEAT controllerEvents=" + _events + "; decodedStatusEvents=" + _decoded + "; schemaErrors=" + _schemaErrors + "; parseErrors=" + _errors + "; consumerCompleted=" + (_task != null && _task.IsCompleted)); }
        private static void Check(uint result, string operation)
        { if (result != 0) { throw new Win32Exception((int)result, operation + " failed, Win32=" + result); } }
        public void Start()
        {
            if (IntPtr.Size != 8) { throw new PlatformNotSupportedException("Use PowerShell x64."); }
            if (_properties != IntPtr.Zero || _disposed != 0) { throw new InvalidOperationException("One trace per instance."); }
            // EVENT_TRACE_PROPERTIES x64 (120 bytes) + session name; no filename.
            int size = 120 + (_name.Length + 1) * 2;
            _properties = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, _properties, size);
            Marshal.WriteInt32(_properties, 0, size);
            Marshal.WriteInt32(_properties, 40, 1); // QPC; ProcessTrace converts timestamp.
            Marshal.WriteInt32(_properties, 44, 0x20000); // WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(_properties, 48, 64); // buffer KiB
            Marshal.WriteInt32(_properties, 52, 4);
            Marshal.WriteInt32(_properties, 56, 8);
            Marshal.WriteInt32(_properties, 64, 0x100); // REAL_TIME only, no disk logger
            Marshal.WriteInt32(_properties, 68, 1);
            Marshal.WriteInt32(_properties, 116, 120);
            Marshal.Copy((_name + "\0").ToCharArray(), 0, IntPtr.Add(_properties, 120), _name.Length + 1);
            try
            {
                Check(StartTrace(out _session, _name, _properties), "StartTrace");
                _logger = Marshal.StringToHGlobalUni(_name);
                var file = new TraceFile { LoggerName = _logger, ProcessTraceMode = 0x10000100,
                    EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback) };
                _consumer = OpenTrace(ref file);
                if (_consumer == ulong.MaxValue) { throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenTrace failed"); }
                _task = Task.Run(() => { uint result = ProcessTrace(new[] { _consumer }, 1, IntPtr.Zero, IntPtr.Zero);
                    if (result != 0 && result != 1223) { Line("ProcessTrace Win32=" + result); } });
                Guid provider = Provider;
                // HCIRAW is consumed only in memory; OnRecord allows numeric
                // status/error and advertisement metadata, never packet dumps.
                Check(EnableTraceEx2(_session, ref provider, 1, 4, 0x8000000000000000UL, 0, 0, IntPtr.Zero), "EnableTraceEx2");
                Line("TRACE STARTED; session=" + _name + "; realtime only; no ETL; controller status allowlist; all radio links, not proof of iPhone identity");
            }
            catch { Dispose(); throw; }
        }
        private void OnRecord(IntPtr record)
        {
            try
            {
                Guid provider = (Guid)Marshal.PtrToStructure(IntPtr.Add(record, 24), typeof(Guid));
                if (provider != Provider || (ushort)Marshal.ReadInt16(record, 40) != 402) { return; }
                Interlocked.Increment(ref _events);
                if (Marshal.ReadByte(record, 42) != 0) { Interlocked.Increment(ref _unsupported); return; }
                int size = (ushort)Marshal.ReadInt16(record, 86);
                IntPtr data = Marshal.ReadIntPtr(record, 96);
                if (size < 8 || data == IntPtr.Zero) { Interlocked.Increment(ref _errors); return; }
                int type = Marshal.ReadByte(data, 3);
                if (type < _types.Length) { Interlocked.Increment(ref _types[type]); }
                uint length = unchecked((uint)Marshal.ReadInt32(data, 4));
                // Independently verify packed offsets against the installed
                // manifest. Read only scalar metadata with TDH, never BIP_Data.
                uint namedType, namedLength;
                if (!Scalar(record, "BIP_Type", 1, out namedType) || !Scalar(record, "BIP_DataLen", 4, out namedLength)
                    || namedType != type || namedLength != length)
                { Interlocked.Increment(ref _schemaErrors); return; }
                string decoded = DecodeBip(data, size);
                if (decoded != null)
                {
                    Interlocked.Increment(ref _decoded);
                    long timestamp = Marshal.ReadInt64(record, 16);
                    Line("controller=" + DateTimeOffset.FromFileTime(timestamp).ToString("o") + " " + decoded);
                }
            }
            catch { Interlocked.Increment(ref _errors); }
        }
        private static bool Scalar(IntPtr record, string name, uint size, out uint value)
        {
            IntPtr propertyName = Marshal.StringToHGlobalUni(name);
            try
            {
                var descriptor = new PropertyDescriptor { PropertyName = unchecked((ulong)propertyName.ToInt64()), ArrayIndex = uint.MaxValue };
                byte[] bytes = new byte[size];
                value = 0;
                if (TdhGetProperty(record, 0, IntPtr.Zero, 1, ref descriptor, size, bytes) != 0) { return false; }
                value = size == 1 ? bytes[0] : BitConverter.ToUInt32(bytes, 0);
                return true;
            }
            finally { Marshal.FreeHGlobal(propertyName); }
        }
        // Also exercised by fixtures with the entire Event 402 BIP envelope.
        // BIP kinds are not H4 packet types. On this Windows provider, kind 2
        // carries HCI events (verified with TDH metadata and native advertising
        // CommandComplete responses); kind 1 permits safe advertising metadata,
        // kind 3 permits complete error responses only, never arbitrary contents.
        public string DecodeBip(IntPtr data, int size)
        {
                if (data == IntPtr.Zero || size < 8) { return null; }
                int type = Marshal.ReadByte(data, 3);
                uint length = unchecked((uint)Marshal.ReadInt32(data, 4));
                if (length != size - 8 || length < 2) { return null; }
                IntPtr packet = IntPtr.Add(data, 8);
                if (type == 1) { return AdvertisingMetadata(packet, (int)length); }
                if (type == 3) { return AclStatus(packet, (int)length); }
                if (type != 2) { return null; }
                int code = Marshal.ReadByte(packet), payload = Marshal.ReadByte(packet, 1);
                if (payload + 2 != length) { return null; }
                if (code == 0x0E && payload >= 4)
                {
                    int opcode = (ushort)Marshal.ReadInt16(packet, 3);
                    // Only commands whose first return parameter is Status.
                    // Never format arbitrary read responses or secret bytes.
                    if (!StatusCommand(opcode)) { return null; }
                    return "BIP kind=" + type + " CommandComplete opcode=0x" + opcode.ToString("X4") + " status=0x" + Marshal.ReadByte(packet, 5).ToString("X2");
                }
                if (code == 0x0F && payload == 4)
                {
                    int opcode = (ushort)Marshal.ReadInt16(packet, 4);
                    if (!StatusCommand(opcode)) { return null; }
                    return "BIP kind=" + type + " CommandStatus opcode=0x" + opcode.ToString("X4") + " status=0x" + Marshal.ReadByte(packet, 2).ToString("X2");
                }
                int need = Needed(code, packet, payload);
                if (need == 0 || payload < need) { return null; }
                // Copy only the allowlisted event prefix. Peer address fields are
                // present in some controller events but never read or formatted.
                byte[] safeEvent = new byte[need + 2];
                try
                {
                    Marshal.Copy(packet, safeEvent, 0, safeEvent.Length);
                    string decoded = Decode(safeEvent);
                    return decoded == null ? null : "BIP kind=" + type + " " + decoded;
                }
                finally { Array.Clear(safeEvent, 0, safeEvent.Length); }
        }
        private static bool StatusCommand(int opcode)
        {
            switch (opcode)
            {
                case 0x0406: // Disconnect
                case 0x2006: case 0x2008: case 0x2009: case 0x200A: // LE advertising parameters/data/scan response/enable
                case 0x2036: case 0x2037: case 0x2038: case 0x2039: // extended LE advertising equivalents
                    return true;
                default: return false;
            }
        }
        private static ushort Read16(IntPtr data, int offset) { return (ushort)Marshal.ReadInt16(data, offset); }
        private static string AdvertisingMetadata(IntPtr packet, int size)
        {
            if (size < 3 || Marshal.ReadByte(packet, 2) + 3 != size) { return null; }
            int opcode = Read16(packet, 0);
            if (opcode == 0x200A && size == 4)
            { return "LE AdvertisingEnable enabled=" + Marshal.ReadByte(packet, 3); }
            if (opcode == 0x2006 && size == 18)
            {
                int type = Marshal.ReadByte(packet, 7);
                return "LE AdvertisingParameters type=0x" + type.ToString("X2") + " connectable=" + (type == 0 || type == 1 || type == 4) +
                    " ownAddressType=" + Marshal.ReadByte(packet, 8) + " channels=0x" + Marshal.ReadByte(packet, 16).ToString("X2") +
                    " filterPolicy=0x" + Marshal.ReadByte(packet, 17).ToString("X2") + " intervalMinUnits=" + Read16(packet, 3) + " intervalMaxUnits=" + Read16(packet, 5);
            }
            if ((opcode == 0x2008 || opcode == 0x2009) && size == 35)
            {
                int used = Marshal.ReadByte(packet, 3);
                if (used > 31) { return null; }
                bool hid = false, battery = false; int flags = -1;
                for (int offset = 4; offset < 4 + used; )
                {
                    int count = Marshal.ReadByte(packet, offset);
                    if (count == 0) { break; }
                    if (count < 1 || offset + count + 1 > 4 + used) { return null; }
                    int kind = Marshal.ReadByte(packet, offset + 1);
                    if (kind == 1 && count == 2) { flags = Marshal.ReadByte(packet, offset + 2); }
                    if (kind == 2 || kind == 3)
                    {
                        if ((count - 1) % 2 != 0) { return null; }
                        for (int position = offset + 2; position < offset + count; position += 2)
                        { int uuid = Read16(packet, position); hid |= uuid == 0x1812; battery |= uuid == 0x180F; }
                    }
                    // Names/manufacturer data/service data/addresses are skipped.
                    offset += count + 1;
                }
                return "LE " + (opcode == 0x2008 ? "AdvertisingData" : "ScanResponseData") + " HID1812=" + hid + " BAS180F=" + battery +
                    " flags=" + (flags < 0 ? "absent" : "0x" + flags.ToString("X2"));
            }
            return null;
        }
        private string AclStatus(IntPtr packet, int size)
        {
            if (size < 9 || Read16(packet, 2) + 4 != size) { return null; }
            int flags = Read16(packet, 0), boundary = (flags >> 12) & 3;
            if ((boundary != 0 && boundary != 2) || Read16(packet, 4) + 8 != size) { return null; }
            int cid = Read16(packet, 6), opcode = Marshal.ReadByte(packet, 8);
            // No reassembly: never store fragments which might contain keys/input.
            string details = null;
            if (cid == 6 && opcode == 5 && size == 10)
            { details = "SMP PairingFailed reason=0x" + Marshal.ReadByte(packet, 9).ToString("X2"); }
            if (cid == 4 && opcode == 1 && size == 13)
            { details = "ATT ErrorResponse request=0x" + Marshal.ReadByte(packet, 9).ToString("X2") + " error=0x" + Marshal.ReadByte(packet, 12).ToString("X2"); }
            if (cid == 1 && opcode == 3 && size == 20 && Read16(packet, 10) == 8)
            { details = "L2CAP ConnectionResponse result=0x" + Read16(packet, 16).ToString("X4") + " status=0x" + Read16(packet, 18).ToString("X4"); }
            return details == null ? null : details + " " + Alias((ushort)(flags & 0x0FFF), false);
        }
        private static int Needed(int code, IntPtr packet, int payload)
        {
            switch (code)
            {
                case 0x03: return 11; // Classic Connection Complete
                case 0x05: case 0x08: return 4;
                case 0x06: case 0x30: return 3;
                case 0x36: return 1; // pairing status only; exclude address
                case 0x3E:
                    if (payload < 1) { return 0; }
                    int sub = Marshal.ReadByte(packet, 2);
                    return sub == 1 ? 19 : sub == 0x0A ? 31 : 0;
                default: return 0;
            }
        }
        private string Alias(ushort handle, bool fresh)
        {
            string alias;
            if (fresh || !_links.TryGetValue(handle, out alias)) { alias = "L" + (++_number); _links[handle] = alias; }
            return _links[handle];
        }
        private static ushort U16(byte[] data, int offset) { return (ushort)(data[offset] | (data[offset + 1] << 8)); }
        private static string Status(byte value) { return "0x" + value.ToString("X2"); }
        public string Decode(byte[] data)
        {
            if (data == null || data.Length < 2 || data[1] + 2 < data.Length) { return null; }
            int code = data[0];
            if (code == 3 && data.Length >= 13) { return "Classic ConnectionComplete " + Alias(U16(data, 3), data[2] == 0) + " status=" + Status(data[2]) + " linkType=" + data[11] + " encryption=" + data[12]; }
            if (code == 5 && data.Length >= 6) { return "DisconnectionComplete " + Alias(U16(data, 3), false) + " status=" + Status(data[2]) + " reason=" + Status(data[5]) + " (" + Reason(data[5]) + ")"; }
            if (code == 8 && data.Length >= 6) { return "EncryptionChange " + Alias(U16(data, 3), false) + " status=" + Status(data[2]) + " enabled=" + data[5]; }
            if ((code == 6 || code == 0x30) && data.Length >= 5) { return (code == 6 ? "AuthenticationComplete " : "EncryptionKeyRefresh ") + Alias(U16(data, 3), false) + " status=" + Status(data[2]); }
            if (code == 0x36 && data.Length >= 3) { return "SimplePairingComplete status=" + Status(data[2]); }
            if (code == 0x3E && data.Length >= 3 && (data[2] == 1 || data[2] == 0x0A))
            {
                int min = data[2] == 1 ? 21 : 33;
                if (data.Length < min) { return null; }
                int offset = data[2] == 1 ? 14 : 26;
                return "LE ConnectionComplete " + Alias(U16(data, 4), data[3] == 0) + " status=" + Status(data[3]) + " role=" + (data[6] == 1 ? "Peripheral" : data[6] == 0 ? "Central" : "Unknown") +
                    " interval=" + (U16(data, offset) * 1.25).ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms latency=" + U16(data, offset + 2) + " supervision=" + (U16(data, offset + 4) * 10) + "ms";
            }
            return null;
        }
        private static string Reason(byte reason)
        {
            switch (reason) { case 0x05: return "Authentication Failure"; case 0x06: return "PIN or Key Missing";
                case 0x08: return "Connection Timeout"; case 0x13: return "Remote User Terminated Connection";
                case 0x16: return "Connection Terminated By Local Host"; case 0x22: return "LMP Response Timeout";
                case 0x3E: return "Connection Failed to be Established"; default: return "controller reason; consult Bluetooth specification"; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            if (_session != 0)
            {
                uint result = ControlTrace(_session, _name, _properties, 1);
                StopConfirmed = result == 0 || result == 4201;
                Line("TRACE STOP Win32=" + result + "; eventsLost=" + Marshal.ReadInt32(_properties, 88) + "; realtimeBuffersLost=" + Marshal.ReadInt32(_properties, 100));
            }
            if (_consumer != ulong.MaxValue) { CloseTrace(_consumer); }
            if (_task != null && !_task.Wait(5000)) { Line("Consumer shutdown pending; native callback retained until process exit"); return; }
            Line("SUMMARY controllerEvents=" + _events + "; BIPkind1=" + _types[1] + "; BIPkind2=" + _types[2] + "; BIPkind3=" + _types[3] + "; BIPkind4=" + _types[4] + "; decodedStatusEvents=" + _decoded + "; unsupportedVersion=" + _unsupported + "; schemaErrors=" + _schemaErrors + "; parseErrors=" + _errors + "; outputDropped=" + _dropped);
            if (_logger != IntPtr.Zero) { Marshal.FreeHGlobal(_logger); }
            if (_properties != IntPtr.Zero) { Marshal.FreeHGlobal(_properties); }
            GC.KeepAlive(_callback);
        }
    }
}
