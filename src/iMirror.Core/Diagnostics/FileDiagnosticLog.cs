using System.Text;

namespace iMirror.Core.Diagnostics;

// Writes synchronously under a lock; appropriate for low-volume phase-1 diagnostics.
// Future video/input must not log every frame or packet through this sink.
public sealed class FileDiagnosticLog : IDiagnosticLog, IDisposable
{
    public const int HistoryCapacity = 500;
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _history = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileDiagnosticLog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"iMirror-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string FilePath { get; }
    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) { return _history.ToArray(); }
    }

    public void Write(LogLevel level, string source, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);
        var entry = new LogEntry(DateTimeOffset.Now, level, source, message);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Persistence errors propagate to the caller; never report a successful write on failure.
            _writer.WriteLine(entry.DisplayText);
            _history.Enqueue(entry);
            while (_history.Count > HistoryCapacity) { _history.Dequeue(); }
        }
        EntryAdded?.Invoke(entry);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _writer.Dispose();
        }
    }
}
