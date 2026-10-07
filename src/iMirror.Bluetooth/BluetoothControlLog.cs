using System.Text;
using System.Text.RegularExpressions;
using iMirror.Core.Diagnostics;

namespace iMirror.Bluetooth;

public sealed class BluetoothControlLog : IDisposable
{
    private readonly object _gate = new();
    private readonly BoundedLogFile _writer;
    private readonly bool _echo;
    private bool _closed;
    private readonly string _session = Guid.NewGuid().ToString("N")[..8];
    private int _providerGeneration;
    private readonly string _stagePath;
    private int _stageGeneration = -1;
    private bool _gattObserved, _keyboardObserved, _mouseObserved;
    public void SetProviderGeneration(int generation) => Volatile.Write(ref _providerGeneration, generation);
    public event Action<string, string>? Written;

    public BluetoothControlLog(string path, bool echo = false)
    {
        _echo = echo;
        _stagePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "bluetooth-hid-stage.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new BoundedLogFile(path);
    }

    public void Write(string category, string message)
    {
        lock (_gate)
        {
            if (_closed) { return; }
            string line = $"{DateTimeOffset.Now:O} [{category}] session={_session}; generation={Volatile.Read(ref _providerGeneration)}; {message}";
            _writer.WriteLine(line);
            if (_echo) { Console.WriteLine(line); }
            Written?.Invoke(category, message);
        }
    }
    public void WriteStageSnapshot(BluetoothStatus status)
    {
        lock (_gate)
        {
            if (_closed) { return; }
            var host = status.DiagnosticHost;
            int generation = Volatile.Read(ref _providerGeneration);
            if (_stageGeneration != generation) { _stageGeneration = generation; _gattObserved = _keyboardObserved = _mouseObserved = false; }
            _gattObserved |= host?.GattActive == true;
            _keyboardObserved |= status.KeyboardConnected; _mouseObserved |= status.MouseConnected;
            bool? Known(Func<BluetoothHost, bool> value) => host is not null ? value(host) : status.Hosts.Count == 0 ? false : null;
            var snapshot = new { AtUtc = DateTimeOffset.UtcNow, ProviderGeneration = generation,
                GattSession = _gattObserved, GattSessionActive = Known(h => h.GattActive), HidInformation = Known(h => h.HidInformationRead),
                ReportMap = Known(h => h.ReportMapRead), ProtocolMode = Known(h => h.ProtocolModeWritten),
                KeyboardCCCD = _keyboardObserved, MouseCCCD = _mouseObserved,
                KeyboardLive = status.KeyboardConnected, MouseLive = status.MouseConnected };
            try
            {
                File.WriteAllText(_stagePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(snapshot));
                File.Move(_stagePath + ".tmp", _stagePath, overwrite:true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Write("stage-snapshot", $"Snapshot unavailable; HRESULT=0x{error.HResult:X8}"); }
        }
    }

    // Do not log OS device IDs, MAC addresses, report payloads, typed text or pairing PINs.
    public void Error(string category, Exception error)
    {
        // Preserve type, message, HRESULT, inner exceptions and stack, while suppressing device identities.
        string details = Regex.Replace(error.ToString(), @"(?i)\b(?:BluetoothLE|BTHLE|BTHENUM)[#\\][^\s""']*", "[Bluetooth device]");
        details = Regex.Replace(details, @"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b|\b[0-9a-f]{12}\b", "[address]");
        Write(category, $"HRESULT=0x{error.HResult:X8}; {details}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) { return; }
            _closed = true;
            _writer.Dispose();
        }
    }
}
