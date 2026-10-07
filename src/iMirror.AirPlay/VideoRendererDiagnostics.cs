using System.Text.RegularExpressions;
using iMirror.Core.Diagnostics;

namespace iMirror.AirPlay;

public sealed record VideoRendererEvent(LogLevel Level, string Message);

// Retain diagnostic text, not the RTSP/plist dump emitted by UxPlay -d.
public static class VideoRendererDiagnostics
{
    public const string DebugCategories = "2,GST_STATES:4,GST_ELEMENT_FACTORY:4,d3d11window:5,d3d11videosink:4,d3d12window:5,d3d12videosink:4,videodecoder:4";

    public static IReadOnlyList<VideoRendererEvent> Parse(string raw)
    {
        var line = Regex.Replace(raw, @"\x1b\[[0-9;]*m", "").Trim();
        var gst = Regex.Match(line, @"^\d+:\d{2}:\d{2}\.\d+\s+\d+\s+\S+\s+(ERROR|WARN|INFO|DEBUG)\s+(\S+)\s+(.+)$");
        if (gst.Success)
        {
            var severity = gst.Groups[1].Value;
            var category = gst.Groups[2].Value;
            if (severity is "ERROR" or "WARN" || category is "GST_STATES" or "GST_ELEMENT_FACTORY" or "videodecoder" ||
                Regex.IsMatch(category, @"^d3d1[12](window|videosink)$"))
            {
                return [new(severity == "ERROR" ? LogLevel.Error : severity == "WARN" ? LogLevel.Warning : LogLevel.Information,
                    $"GStreamer {severity} [{category}]: {Redact(gst.Groups[3].Value)}")];
            }
            return [];
        }
        if (line.StartsWith("*** ERROR:", StringComparison.Ordinal))
        { return [new(LogLevel.Error, "Native ERROR: " + Redact(line[10..].Trim()))]; }
        if (Regex.IsMatch(line, @"^GStreamer (?:error|warning|debug(?:ging)? info) \(video\):", RegexOptions.IgnoreCase))
        { return [new(line.Contains("warning", StringComparison.OrdinalIgnoreCase) ? LogLevel.Warning : LogLevel.Error, Redact(line))]; }
        if (Regex.IsMatch(line, @"^GStreamer (?:h264|h265|hls)\s+bus message \S+ (?:state-changed|error|warning)\b"))
        { return [new(line.Contains(" warning ") ? LogLevel.Warning : line.Contains(" error ") ? LogLevel.Error : LogLevel.Information, Redact(line))]; }
        if (Regex.IsMatch(line, @"^GStreamer: automatically-selected videosink|^Initialized GStreamer video renderer|^video_pipeline state change|^video renderer(?:_start| resumed| pause):"))
        { return [new(LogLevel.Information, Redact(line))]; }
        var dimensions = Regex.Match(line, @"^begin video stream wxh = (\d+)x(\d+); source (\d+)x(\d+)$");
        if (dimensions.Success)
        { return [new(LogLevel.Information, $"Received video dimensions={dimensions.Groups[1].Value}x{dimensions.Groups[2].Value}; source={dimensions.Groups[3].Value}x{dimensions.Groups[4].Value}")]; }
        return [];
    }
    private static string Redact(string diagnostic)
    {
        // Authentication/crypto records can contain opaque binary proofs on the same line.
        if (Regex.IsMatch(diagnostic, @"(Authorization|FairPlay|fp-setup|pair-setup|pair-verify|SRP|deviceID|<data>|\bekey\b|\beiv\b|\bproof\b|signature|password|\btoken\b|\bsecret\b|\bpk\s*=|\bpi\s*=)", RegexOptions.IgnoreCase))
        { return "[authentication/crypto/identifier details omitted; see negotiation category]"; }
        diagnostic = Regex.Replace(diagnostic, @"\b\d{1,3}(?:\.\d{1,3}){3}\b", "[IPv4 omitted]");
        diagnostic = Regex.Replace(diagnostic, @"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b|\b(?:[0-9a-f]{2}:){5}[0-9a-f]{2}\b", "[identifier omitted]", RegexOptions.IgnoreCase);
        diagnostic = Regex.Replace(diagnostic, @"(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f:]+(?:%\d+)?", match =>
            System.Net.IPAddress.TryParse(match.Value, out _) ? "[IPv6 omitted]" : match.Value, RegexOptions.IgnoreCase);
        return diagnostic;
    }
}
