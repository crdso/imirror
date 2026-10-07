namespace iMirror.Core.Diagnostics;

public enum LogLevel { Information, Warning, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Source, string Message)
{
    public string DisplayText => $"{Timestamp:HH:mm:ss.fff zzz} [{Level}] [{Source}] {Message}";
}
