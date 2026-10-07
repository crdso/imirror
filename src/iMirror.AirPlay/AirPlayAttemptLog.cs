using System.Text;
using System.Text.Json;
using iMirror.Core.Diagnostics;

namespace iMirror.AirPlay;

internal sealed class AirPlayAttemptLog : IDisposable
{
    private readonly BoundedLogFile _writer;
    private readonly object _gate = new();
    public AirPlayAttemptLog(AirPlayOptions options)
    {
        var path = Path.GetFullPath(options.AttemptLogPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new BoundedLogFile(path);
        Write("SESSION START: UxPlay-iOS27; physical outcome not yet known; sanitized negotiation allowlist only");
        Write($"Expected UxPlay={AirPlayOptions.SupportedUxPlayVersion}; h265={options.EnableH265}; sink={options.VideoSink}; decoder={options.VideoDecoder}; receiver={options.ReceiverName}; ports={options.BasePort}-{options.BasePort + 2}");
    }
    public void Arguments(ReceiverProcessRequest request)
    {
        Write("Executable=" + request.Executable);
        Write("Arguments JSON=" + JsonSerializer.Serialize(request.Arguments));
    }
    public void Write(string safeMessage)
    {
        lock (_gate) { _writer.WriteLine($"{DateTimeOffset.Now:o} {safeMessage}"); }
    }
    public void Dispose() { lock (_gate) { _writer.Dispose(); } }
}
