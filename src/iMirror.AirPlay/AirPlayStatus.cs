namespace iMirror.AirPlay;

public enum AirPlayState { Stopped, CheckingDependencies, Starting, WaitingForDevice, Connecting, Connected, Streaming, Disconnected, Stopping, Error }
public enum AirPlayError { None, Dependencies, PortInUse, ServiceDiscovery, VideoPipeline, Authentication, StartupFailed, ProcessExited,
    CodecNegotiation, Decryption, FairPlay, MirrorConnection, IncompleteSetup,
    GStreamer, Decoder, VideoRenderer, SinkWindow, NativeRuntime }

public sealed record AirPlayStatus(AirPlayState State = AirPlayState.Stopped,
    string Message = "AirPlay parado", AirPlayError Error = AirPlayError.None,
    string? DeviceName = null, int? Width = null, int? Height = null,
    TimeSpan? ReceiverStartupTime = null, TimeSpan? ConnectionTime = null, int? ExitCode = null)
{
    // Protocol/packet evidence. Visible frames still require the manual external-window test.
    public bool IsConnected => State is AirPlayState.Connected or AirPlayState.Streaming;
    public string StateText => Message;
}

public interface IAirPlayReceiver
{
    AirPlayStatus Status { get; }
    bool IsRunning { get; }
    event Action<AirPlayStatus>? StatusChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
