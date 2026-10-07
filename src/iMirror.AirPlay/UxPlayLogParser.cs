using System.Text.RegularExpressions;

namespace iMirror.AirPlay;

public sealed record UxPlaySignal(AirPlayState? State = null, AirPlayError Error = AirPlayError.None,
    string? DeviceName = null, int? Width = null, int? Height = null);

// Messages checked against v1.73.7 source. These are protocol/renderer indications, not a frame callback.
public static class UxPlayLogParser
{
    public static UxPlaySignal Parse(string line, bool receiverStarted = false, bool mirroringInitialized = false)
    {
        line = Regex.Replace(line, @"\x1b\[[0-9;]*m", "").Trim();
        // A GStreamer WARN/INFO line may contain "error" in its explanation.
        // Only actual ERROR messages or explicit native failures change the state.
        if (Regex.IsMatch(line, @"^\s*\d+:\d{2}:\d{2}\.\d+\s+\d+\s+\S+\s+(WARN|INFO|DEBUG|LOG|TRACE)\s+")) { return new(); }
        if (Regex.IsMatch(line, @"^\s*(?:\*\*\* )?(?:WARNING|WARN)\b|^GStreamer warning", RegexOptions.IgnoreCase)) { return new(); }
        if (Regex.IsMatch(line, @"(received type 0x01 packet with no payload|non-h264 video|failed to set video codec|invalid video codec change)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.CodecNegotiation); }
        if (Regex.IsMatch(line, @"((fairplay|fp-setup).*(fail|error|invalid)|fairplay_decrypt ret\s*=\s*-[1-9]\d*)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.FairPlay); }
        if (Regex.IsMatch(line, @"(decrypt(?:ion|ing)?[^\r\n]*(?:fail|error)|decryption of video packet failed)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.Decryption); }
        if (Regex.IsMatch(line, @"(mirror data connection not opened|raop_rtp_mirror error\s+in (?:accept|header recv)|raop_rtp_mirror tcp socket was closed by client)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.MirrorConnection); }
        if (Regex.IsMatch(line, @"(client disconnected during SETUP|SETUP (?:incomplete|incompleto))", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.IncompleteSetup); }
        if (line.Contains("lost connection with client", StringComparison.OrdinalIgnoreCase))
        { return new(AirPlayState.Disconnected); }
        if (Regex.IsMatch(line, "(address already in use|WSAEADDRINUSE|(?:bind|socket).*10048)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.PortInUse); }
        if (Regex.IsMatch(line, "(No DNS-SD Server found|DNSServiceRegister.*(returned|failed)|Could not initialize dnssd|dnssd_register_.*failed)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.ServiceDiscovery); }
        var gstError = line.Contains("GStreamer error (video)", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(line, @"^\s*\d+:\d{2}:\d{2}\.\d+\s+\d+\s+\S+\s+ERROR\s+|^GStreamer (?:h264|h265|hls)\s+bus message \S+ error\b", RegexOptions.IgnoreCase) || line.StartsWith("*** ERROR:", StringComparison.Ordinal);
        if ((gstError && Regex.IsMatch(line, @"(Output window was closed|Invalid window handle|window.*(?:fail|error|closed)|(?:fail|error).*window|HWND.*(?:invalid|fail))", RegexOptions.IgnoreCase)))
        { return new(AirPlayState.Error, AirPlayError.SinkWindow); }
        if (Regex.IsMatch(line, @"(no decoder|no element[^\r\n]*(?:avdec_|h26[45]dec)|decoder.*(?:fail|error)|Could not decode stream)", RegexOptions.IgnoreCase) ||
            (gstError && Regex.IsMatch(line, @"(avdec_h26[45]|h26[45]dec|decoder)", RegexOptions.IgnoreCase)))
        { return new(AirPlayState.Error, AirPlayError.Decoder); }
        if (gstError && Regex.IsMatch(line, @"(d3d1[12]|videosink|swap.?chain|DXGI|D3D device|video renderer)", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.VideoRenderer); }
        if (line.Contains("GStreamer error (video)", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("state change to PLAYING failed", StringComparison.OrdinalIgnoreCase) ||
            (gstError && Regex.IsMatch(line, @"\b(?:GStreamer|gst_parse_launch|video_pipeline)\b|pipeline h26[45]: state change to (?:NULL|READY|PAUSED|PLAYING) failed", RegexOptions.IgnoreCase)) ||
            Regex.IsMatch(line, @"^\s*\d+:\d{2}:\d{2}\.\d+\s+\d+\s+\S+\s+ERROR\s+|no element.*h26[45]", RegexOptions.IgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.GStreamer); }
        if (line.Contains("Authentication Failure", StringComparison.OrdinalIgnoreCase))
        { return new(AirPlayState.Error, AirPlayError.Authentication); }
        if (line.StartsWith("*** ERROR:", StringComparison.Ordinal) && !line.Contains("Unsupported HLS"))
        { return new(AirPlayState.Error, receiverStarted || mirroringInitialized ? AirPlayError.NativeRuntime : AirPlayError.StartupFailed); }
        if (line.Contains("register_dnssd: advertised AirPlay service")) { return new(AirPlayState.WaitingForDevice); }
        var name = Regex.Match(line, @"connection request from (.+?) \(.+?\) with deviceID =");
        if (name.Success) { return new(AirPlayState.Connecting, DeviceName: name.Groups[1].Value); }
        // Accepted TCP sockets may be probes, not an iPhone mirroring request.
        if (line.Contains("Mirroring initialized successfully")) { return new(AirPlayState.Connected); }
        if (line.Contains("Begin streaming to GStreamer video pipeline")) { return new(AirPlayState.Streaming); }
        // An RTSP control socket closing alone must never be treated as a video disconnect.
        if (line.Contains("video_reset: type = RTP_Shutdown") || line.Contains("video_reset: type = NoHold"))
        { return new(AirPlayState.Disconnected); }
        var size = Regex.Match(line, @"begin video stream wxh = (\d+)x(\d+); source (\d+)x(\d+)");
        if (size.Success && int.TryParse(size.Groups[1].Value, out var width) &&
            int.TryParse(size.Groups[2].Value, out var height) && width > 0 && height > 0)
        { return new(Width: width, Height: height); }
        return new();
    }
}
