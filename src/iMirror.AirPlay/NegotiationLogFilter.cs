using System.Text.RegularExpressions;

namespace iMirror.AirPlay;

// Native -d output is transient only. An allowlist prevents keys, proofs, IDs,
// addresses, Authorization, plist <data>, and video payloads reaching ANY file/UI log.
public sealed class NegotiationLogFilter
{
    private string? _pendingKey;
    public long OmittedLines { get; private set; }
    public IReadOnlyList<string> Feed(string raw, bool receiverStarted = false, bool mirroringInitialized = false)
    {
        var events = new List<string>();
        var line = raw.Trim();
        var keys = Regex.Matches(line, @"<key>([^<]*)</key>");
        if (keys.Count > 0)
        {
            _pendingKey = null;
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i].Groups[1].Value;
                if (key is not ("osVersion" or "sourceVersion" or "combinedGetInfoWithControlSetup" or "dataPort")) { continue; }
                var start = keys[i].Index + keys[i].Length;
                var end = i + 1 < keys.Count ? keys[i + 1].Index : line.Length;
                if (!Value(key, line[start..end], events) && i == keys.Count - 1) { _pendingKey = key; }
            }
        }
        else if (_pendingKey is { } pending && line.Length > 0)
        {
            _pendingKey = null;
            Value(pending, line, events);
        }
        var request = Regex.Match(line, @"^(GET|POST|SETUP|RECORD|TEARDOWN|OPTIONS|FLUSH) (\S+) (RTSP|HTTP)/[\d.]+$");
        var handling = Regex.Match(line, @"^Handling request (GET|POST|SETUP|RECORD|TEARDOWN|OPTIONS|FLUSH) with URL (\S+)$");
        if (request.Success || handling.Success)
        {
            var match = request.Success ? request : handling;
            var method = match.Groups[1].Value; var url = match.Groups[2].Value;
            if (method is "SETUP" or "RECORD" or "TEARDOWN" or "OPTIONS" or "FLUSH") { events.Add("Request " + method); }
            else
            {
                var path = url.Split('?')[0];
                if (path is "/info" or "/pair-setup" or "/pair-verify" or "/fp-setup" or "/feedback" or "/pair-setup-pin")
                { events.Add($"Request {method} {path}"); }
            }
        }
        var userAgent = Regex.Match(line, @"\bUser-Agent:\s*(AirPlay/\d+(?:\.\d+){1,4})(?:\s|$)");
        if (userAgent.Success) { events.Add("User-Agent " + userAgent.Groups[1].Value); }
        var response = Regex.Match(line, @"^(RTSP|HTTP)/[\d.]+ (\d{3})(?:\s|$)");
        if (response.Success) { events.Add("Response status=" + response.Groups[2].Value); }
        var sequence = Regex.Match(line, @"^CSeq:\s*(\d{1,10})$");
        if (sequence.Success) { events.Add("CSeq=" + sequence.Groups[1].Value); }
        var port = Regex.Match(line, @"raop_rtp_mirror local data port socket \d+ port TCP (\d{1,5})");
        if (port.Success) { events.Add("Mirror TCP dataPort=" + port.Groups[1].Value); }
        var control = Regex.Match(line, @"Accepted (IPv4|IPv6) client on socket");
        if (control.Success) { events.Add("Accepted " + control.Groups[1].Value + " client (control TCP; peer/socket identifiers omitted)"); }
        if (line.Contains("raop_rtp_mirror accepting client", StringComparison.Ordinal)) { events.Add("Mirror TCP accept requested (awaiting confirmation)"); }
        if (line.Contains("Mirroring initialized successfully", StringComparison.Ordinal)) { events.Add("Mirroring initialized successfully"); }
        if (line.Contains("Begin streaming to GStreamer video pipeline", StringComparison.Ordinal)) { events.Add("Begin streaming to GStreamer video pipeline"); }
        if (line.StartsWith("register_dnssd: advertised AirPlay service", StringComparison.Ordinal)) { events.Add("register_dnssd: advertised AirPlay service"); }
        if (line is "SETUP 1" or "SETUP 2") { events.Add(line); }
        if (line.Contains("pair-verify: signature is verified", StringComparison.Ordinal)) { events.Add("pair-verify: signature is verified"); }
        var fairplay = Regex.Match(line, @"^fairplay_decrypt ret\s*=\s*(-?\d{1,8})$");
        if (fairplay.Success) { events.Add("fairplay_decrypt ret=" + fairplay.Groups[1].Value); }
        if (line.Contains("lost connection with client", StringComparison.OrdinalIgnoreCase)) { events.Add("Client disconnected (native connection-loss indication)"); }
        if (line.Contains("video_reset: type = RTP_Shutdown", StringComparison.Ordinal)) { events.Add("video_reset: type = RTP_Shutdown"); }
        if (line.Contains("video_reset: type = NoHold", StringComparison.Ordinal)) { events.Add("video_reset: type = NoHold"); }
        var error = UxPlayLogParser.Parse(line, receiverStarted, mirroringInitialized).Error;
        if (error != AirPlayError.None)
        {
            events.Add(error switch
            {
                AirPlayError.CodecNegotiation => line.Contains("no payload", StringComparison.OrdinalIgnoreCase)
                    ? "received type 0x01 packet with no payload" : "non-h264 video / codec negotiation failed",
                AirPlayError.FairPlay => "FairPlay failure (native error)",
                AirPlayError.Decryption => "decryption failed (native error)",
                AirPlayError.MirrorConnection => "mirror data connection failed/closed (native error)",
                AirPlayError.IncompleteSetup => "SETUP incomplete / client disconnected during SETUP (native error)",
                _ => "Native error category=" + error
            });
        }
        if (line.StartsWith('"') && line.Contains("appsrc", StringComparison.Ordinal))
        {
            var elements = Regex.Matches(line, @"\b(rtph26[45]depay|h26[45]parse|avdec_h26[45]|decodebin|d3d1[12]videosink|autovideosink)\b")
                .Select(match => match.Value).Distinct();
            events.Add("GStreamer pipeline elements=" + string.Join(",", elements));
        }
        if (events.Count == 0) { OmittedLines++; }
        return events;
    }
    private static bool Value(string key, string text, List<string> events)
    {
        string? value = null;
        if (key == "combinedGetInfoWithControlSetup")
        {
            var boolean = Regex.Match(text, @"<(true|false)\s*/>");
            if (boolean.Success) { value = boolean.Groups[1].Value; }
        }
        else
        {
            var match = Regex.Match(text, @"<(string|integer)>([^<]*)</\1>");
            if (match.Success)
            {
                var candidate = match.Groups[2].Value;
                if (key == "dataPort" && int.TryParse(candidate, out var number) && number is > 0 and <= 65535) { value = candidate; }
                else if (key != "dataPort" && Regex.IsMatch(candidate, @"^\d{1,6}(?:\.\d{1,6}){1,4}$")) { value = candidate; }
            }
        }
        if (value is null) { return false; }
        events.Add(key + "=" + value); return true;
    }
}
