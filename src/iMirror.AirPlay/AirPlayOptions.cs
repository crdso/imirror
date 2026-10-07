namespace iMirror.AirPlay;

public sealed record AirPlayOptions
{
    public const string SupportedUxPlayVersion = "1.73.7";
    public string ReceiverName { get; init; } = "iMirror - Windows";
    public string? UxPlayPath { get; init; }
    public string? GStreamerBinPath { get; init; }
    public string? BonjourDirectory { get; init; }
    public string SessionDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iMirror", "airplay");
    public string VideoSink { get; init; } = "d3d11videosink";
    public string VideoDecoder { get; init; } = "avdec_h264";
    public bool EnableH265 { get; init; }
    public bool DetailedNegotiationLogging { get; init; }
    public string? AttemptLogPath { get; init; }
    public int BasePort { get; init; } = 35000;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (BasePort is < 1024 or > 65533) { throw new ArgumentOutOfRangeException(nameof(BasePort)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(ReceiverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionDirectory);
        if (DetailedNegotiationLogging) { ArgumentException.ThrowIfNullOrWhiteSpace(AttemptLogPath); }
        // A named element, never arbitrary shell or pipeline text from the UI.
        foreach (var element in new[] { VideoSink, VideoDecoder })
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(element, "^[a-zA-Z0-9_]+$"))
            { throw new ArgumentException("Nome de elemento GStreamer inválido."); }
        }
        if (StartupTimeout <= TimeSpan.Zero || StopTimeout <= TimeSpan.Zero)
        { throw new ArgumentOutOfRangeException(nameof(StartupTimeout)); }
    }
}
