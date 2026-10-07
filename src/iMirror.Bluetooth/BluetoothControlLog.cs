using System.Text;
using System.Text.RegularExpressions;
using iMirror.Core.Diagnostics;

namespace iMirror.Bluetooth;

public sealed class BluetoothControlLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly bool _echo;
    private bool _closed;
    public event Action<string, string>? Written;

    public BluetoothControlLog(string path, bool echo = false)
    {
        _echo = echo;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Write(string category, string message)
    {
        lock (_gate)
        {
            if (_closed) { return; }
            string line = $"{DateTimeOffset.Now:O} [{category}] {message}";
            _writer.WriteLine(line);
            if (_echo) { Console.WriteLine(line); }
            Written?.Invoke(category, message);
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
