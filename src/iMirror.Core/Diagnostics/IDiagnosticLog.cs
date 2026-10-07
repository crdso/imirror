namespace iMirror.Core.Diagnostics;

public interface IDiagnosticLog
{
    event Action<LogEntry>? EntryAdded;
    IReadOnlyList<LogEntry> Snapshot();
    void Write(LogLevel level, string source, string message);
}
