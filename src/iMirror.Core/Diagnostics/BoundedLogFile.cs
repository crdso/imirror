using System.Text;

namespace iMirror.Core.Diagnostics;

// Bounds both repeated sessions and a long-running session. Never touches unrelated logs.
public sealed class BoundedLogFile : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path, _family;
    private readonly long _limit;
    private readonly int _count;
    private StreamWriter _writer;
    private bool _disposed;
    public BoundedLogFile(string path, bool append = true, long maxBytes = 5 * 1024 * 1024,
        int maxFiles = 5, string? familyPattern = null)
    {
        if (maxBytes < 256 || maxFiles < 1) { throw new ArgumentOutOfRangeException(nameof(maxBytes)); }
        _path = Path.GetFullPath(path); _limit = maxBytes; _count = maxFiles;
        _family = familyPattern ?? Path.GetFileName(_path) + "*";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _writer = Open(append ? FileMode.Append : FileMode.CreateNew);
        Prune();
    }
    private StreamWriter Open(FileMode mode) => new(new FileStream(_path, mode, FileAccess.Write,
        FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    public void WriteLine(string text)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Limit a single pathological native message, including multibyte Unicode.
            if (Encoding.UTF8.GetByteCount(text) + 2 > _limit)
            { text = text[..Math.Min(text.Length, (int)(_limit / 4) - 16)] + " [truncated]"; }
            if (_writer.BaseStream.Length + Encoding.UTF8.GetByteCount(text) + 2 > _limit)
            {
                _writer.Dispose();
                if (File.Exists(_path + "." + (_count - 1))) { File.Delete(_path + "." + (_count - 1)); }
                for (int i = _count - 2; i >= 1; i--)
                { if (File.Exists(_path + "." + i)) { File.Move(_path + "." + i, _path + "." + (i + 1), true); } }
                if (_count > 1) { File.Move(_path, _path + ".1", true); } else { File.Delete(_path); }
                _writer = Open(FileMode.CreateNew);
                Prune();
            }
            _writer.WriteLine(text);
        }
    }
    private void Prune()
    {
        var files = new DirectoryInfo(Path.GetDirectoryName(_path)!).GetFiles(_family)
            .Where(file => file.FullName != _path && System.Text.RegularExpressions.Regex.IsMatch(file.Name, @"\.log(?:\.\d+)?$"))
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        foreach (var file in files.Skip(_count - 1))
        {
            try { file.Delete(); }
            catch (IOException) { /* Another live instance may still own this file. */ }
        }
    }
    public void Dispose()
    { lock (_gate) { if (_disposed) { return; } _disposed = true; _writer.Dispose(); } }
}
