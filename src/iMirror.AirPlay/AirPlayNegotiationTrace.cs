namespace iMirror.AirPlay;

public sealed class AirPlayNegotiationTrace(Func<int, bool> tcpEstablished)
{
    private bool _setup, _initialized, _record, _stream, _tcp;
    private int? _port;
    public IReadOnlyList<string> Observe(string safeEvent)
    {
        var extra = new List<string>();
        if (safeEvent == "Request SETUP" && !_setup) { _setup = true; _initialized = _record = _stream = _tcp = false; _port = null; }
        if (safeEvent.StartsWith("Mirror TCP dataPort=", StringComparison.Ordinal) && int.TryParse(safeEvent.Split('=')[1], out var port)) { _port = port; }
        if (safeEvent == "Mirroring initialized successfully") { _initialized = true; }
        if (safeEvent == "Request RECORD") { _record = true; }
        if (safeEvent == "Begin streaming to GStreamer video pipeline") { _stream = true; }
        if (_port is { } dataPort && !_tcp && tcpEstablished(dataPort))
        { _tcp = true; extra.Add($"Mirror TCP ESTABLISHED confirmed for owned receiver PID; dataPort={dataPort}"); }
        if (safeEvent == "Request TEARDOWN" || safeEvent.StartsWith("Client disconnected", StringComparison.Ordinal))
        {
            extra.AddRange(Finish(safeEvent == "Request TEARDOWN" ? "TEARDOWN" : "client disconnected"));
        }
        return extra;
    }
    public IReadOnlyList<string> Finish(string cause)
    {
        if (!_setup) { return []; }
        var events = new List<string> { $"Negotiation summary: cause={cause}; SETUP=true; MirroringInitialized={_initialized}; RECORD={_record}; MirrorTcpObserved={_tcp}; VideoPacketsObserved={_stream}" };
        if (!_initialized) { events.Add("SETUP incomplete: session ended before mirroring initialization"); }
        else if (!_tcp && !_stream) { events.Add("Mirror data connection not observed in native logs/TCP snapshots; initialization alone is not video proof"); }
        _setup = false; return events;
    }
}
