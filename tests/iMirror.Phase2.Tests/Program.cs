using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using iMirror.AirPlay;
using iMirror.Core.Diagnostics;

internal static class Program
{
    private static void Assert(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--child"])
        {
            Console.WriteLine("register_dnssd: advertised AirPlay service with \"Features\" code = 0x1234");
            Console.Error.WriteLine("simulated stderr — not an AirPlay implementation");
            await Task.Delay(Timeout.Infinite); return 0;
        }
        if (args.Length != 3) { Console.Error.WriteLine("Usage: tests <evidence> <dotnet.exe> <project root>"); return 2; }
        Directory.CreateDirectory(args[0]);
        using var log = new FileDiagnosticLog(Path.Combine(args[0], "airplay"));
        var tests = new (string, Func<Task>)[]
        {
            ("Arguments are separate, pinned and audio-free", () => Arguments(args[0])),
            ("Parser uses protocol evidence; ignores socket probes/closures", Parser),
            ("iOS27 diagnostics redact secrets and distinguish negotiation from TCP/video", () => Ios27Diagnostics(args[0], log)),
            ("Renderer diagnostics retain native errors; warnings do not reset successful negotiation", () => RendererDiagnostics(args[0], log)),
            ("GStreamer window is visible using the real receiver process factory", () => VideoWindowRegression(args, log)),
            ("Dependency paths, architecture, versions, plugins and service", () => Dependencies(args[0], log)),
            ("Lifecycle, duplicate prevention, metrics and reconnection", () => Lifecycle(args[0], log)),
            ("Startup timeout, cancellation and unexpected exit", () => Failures(args[0], log)),
            ("Owned real child output and bounded kill; unrelated child preserved", () => RealChild(args, log)),
            ("Local native dependency diagnostic (not an iPhone test)", () => Diagnostic(args, log))
        };
        try
        {
            foreach (var (name, test) in tests) { await test(); Console.WriteLine("PASS: " + name); }
            Console.WriteLine($"PASS: {tests.Length} Phase 2 groups. Manual iPhone milestone remains pending."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static Task Arguments(string directory)
    {
        var options = new AirPlayOptions();
        var config = Path.Combine(directory, "space & quote ' config.txt");
        var list = UxPlayArguments.Build(options, config).ToArray();
        Assert(list[1] == config && list[3] == "iMirror - Windows" && list.Contains("-nh"), "Shell quoting or name changed.");
        Assert(list.Contains("-vsync") && list.Contains("-as") && !list.Contains("-vrtp"), "External milestone bypassed.");
        Assert(list[^2] == "-d" && list[^1] == "1", "Required debug events missing.");
        var ios27 = UxPlayArguments.Build(options with { EnableH265 = true, DetailedNegotiationLogging = true, AttemptLogPath = Path.Combine(directory, "attempt.log") }, config);
        Assert(ios27.Contains("-h265") && ios27[^1] == "-d" && !ios27.Contains("-vd avdec_h265"), "HEVC profile/debug changed unexpectedly.");
        try { UxPlayArguments.Build(options with { BasePort = 65534 }, config); throw new InvalidOperationException("Invalid ports accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { UxPlayArguments.Build(options with { VideoSink = "sink ! shell" }, config); throw new InvalidOperationException("Invalid sink accepted."); }
        catch (ArgumentException) { }
        var file = Path.Combine(directory, "config.json");
        File.WriteAllText(file, "{\"UxPlayPath\":\"relative/uxplay.exe\"}");
        Assert(AirPlayConfiguration.Load(file).UxPlayPath == Path.GetFullPath("relative/uxplay.exe", directory), "Config used current directory.");
        return Task.CompletedTask;
    }
    private static Task Parser()
    {
        var cases = new (string, AirPlayState?, AirPlayError)[]
        {
            ("register_dnssd: advertised AirPlay service with Features code = 1", AirPlayState.WaitingForDevice, AirPlayError.None),
            ("connection request from João’s iPhone (iPhone15,3) with deviceID = ABC", AirPlayState.Connecting, AirPlayError.None),
            ("Mirroring initialized successfully", AirPlayState.Connected, AirPlayError.None),
            ("Begin streaming to GStreamer video pipeline", AirPlayState.Streaming, AirPlayError.None),
            ("video_reset: type = RTP_Shutdown", AirPlayState.Disconnected, AirPlayError.None),
            ("video_reset: type = NoHold", AirPlayState.Disconnected, AirPlayError.None),
            ("*** ERROR lost connection with client (network problem?)", AirPlayState.Disconnected, AirPlayError.None),
            ("*** ERROR: Error initialising socket 10048", AirPlayState.Error, AirPlayError.PortInUse),
            ("*** ERROR: No DNS-SD Server found", AirPlayState.Error, AirPlayError.ServiceDiscovery),
            ("*** ERROR: dnssd_register_raop failed with error code -65563", AirPlayState.Error, AirPlayError.ServiceDiscovery),
            ("received type 0x01 packet with no payload", AirPlayState.Error, AirPlayError.CodecNegotiation),
            ("this indicates non-h264 video", AirPlayState.Error, AirPlayError.CodecNegotiation),
            ("decryption failed", AirPlayState.Error, AirPlayError.Decryption),
            ("FairPlay failure", AirPlayState.Error, AirPlayError.FairPlay),
            ("fairplay_decrypt ret = -1", AirPlayState.Error, AirPlayError.FairPlay),
            ("fairplay_decrypt ret = 0", null, AirPlayError.None),
            ("mirror data connection not opened", AirPlayState.Error, AirPlayError.MirrorConnection),
            ("client disconnected during SETUP", AirPlayState.Error, AirPlayError.IncompleteSetup),
            ("SETUP incompleto", AirPlayState.Error, AirPlayError.IncompleteSetup),
            ("GStreamer error (video): d3d11 driver failed", AirPlayState.Error, AirPlayError.VideoRenderer),
            ("*** ERROR: Client Authentication Failure", AirPlayState.Error, AirPlayError.Authentication),
            ("Accepted IPv4 client on socket 12, port 35000", null, AirPlayError.None),
            ("Connection closed on socket 12", null, AirPlayError.None),
            ("Open connections: 0", null, AirPlayError.None),
            ("unknown log", null, AirPlayError.None)
        };
        foreach (var (line, state, error) in cases)
        { var signal = UxPlayLogParser.Parse(line); Assert(signal.State == state && signal.Error == error, line); }
        var size = UxPlayLogParser.Parse("begin video stream wxh = 1170x2532; source 1170x2532");
        Assert(size.Width == 1170 && size.Height == 2532 && size.State is null, "Dimension event claims connection.");
        Assert(UxPlayLogParser.Parse("connection request from João’s iPhone (iPhone15,3) with deviceID = ABC").DeviceName == "João’s iPhone", "Name lost.");
        return Task.CompletedTask;
    }
    private static void Pe(string path, bool x64 = true)
    {
        var bytes = new byte[128]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[60] = 64; bytes[64] = 0x50; bytes[65] = 0x45;
        bytes[68] = x64 ? (byte)0x64 : (byte)0x4c; bytes[69] = x64 ? (byte)0x86 : (byte)0x01; File.WriteAllBytes(path, bytes);
    }
    private sealed class Probe : ICommandProbe
    {
        public bool Service = true;
        public string Version = "1.73.7";
        public string? MissingElement;
        public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = arguments[0] switch { "-v" => "UxPlay version " + Version, "--version" => "GStreamer 1.28.7", "query" => Service ? "ESTADO : 4 RUNNING" : "ESTADO : 1 STOPPED", _ => "element present" };
            return Task.FromResult(new CommandResult(arguments[0] == MissingElement ? 1 : 0, output));
        }
    }
    private static async Task Dependencies(string directory, IDiagnosticLog log)
    {
        var folder = Path.Combine(directory, "fixtures"); Directory.CreateDirectory(folder);
        var probe = new Probe();
        var service = new AirPlayDependencyService(log, probe, folder, folder);
        var options = new AirPlayOptions { SessionDirectory = Path.Combine(folder, "session"), UxPlayPath = Path.Combine(folder, "uxplay.exe"), GStreamerBinPath = folder, BonjourDirectory = folder };
        async Task Expect(DependencyState state) => Assert((await service.CheckAsync(options, default)).State == state, state.ToString());
        await Expect(DependencyState.UxPlayMissing);
        Pe(options.UxPlayPath!, false); await Expect(DependencyState.InvalidInstallation);
        Pe(options.UxPlayPath!); await Expect(DependencyState.GStreamerMissing);
        Pe(Path.Combine(folder, "gst-inspect-1.0.exe")); Pe(Path.Combine(folder, "gst-launch-1.0.exe"));
        await Expect(DependencyState.ServiceDiscoveryMissing);
        Pe(Path.Combine(folder, "dnssd.dll")); await Expect(DependencyState.Ready);
        probe.Service = false; await Expect(DependencyState.ServiceDiscoveryMissing); probe.Service = true;
        probe.Version = "1.74"; await Expect(DependencyState.InvalidInstallation); probe.Version = "1.73.7";
        probe.MissingElement = "avdec_h264"; await Expect(DependencyState.GStreamerMissing); probe.MissingElement = null;
        probe.MissingElement = "avdec_h265";
        Assert((await service.CheckAsync(options with { EnableH265 = true }, default)).State == DependencyState.GStreamerMissing, "Missing HEVC decoder accepted.");
        probe.MissingElement = null;
        var fromPath = await service.CheckAsync(options with { UxPlayPath = null, GStreamerBinPath = null, BonjourDirectory = null }, default);
        Assert(fromPath.State == DependencyState.Ready && fromPath.Environment!["PATH"].Contains(folder), "PATH not searched/prepared.");
        Assert(!AirPlayDependencyService.IsX64Pe(Path.Combine(folder, "missing.dll")), "Missing PE accepted.");
    }
    private sealed class ReadyDependencies : IAirPlayDependencyService
    {
        public Task<DependencyReport> CheckAsync(AirPlayOptions options, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new DependencyReport(DependencyState.Ready, "Ready", "fixture.exe", Environment: new Dictionary<string, string>())); }
    }
    private static async Task Ios27Diagnostics(string directory, IDiagnosticLog log)
    {
        var filter = new NegotiationLogFilter();
        var secret = "FAKE_SECRET_SENTINEL";
        var lines = new[]
        {
            "GET /info?deviceID=" + secret + " RTSP/1.0", "POST /pair-setup RTSP/1.0", "POST /pair-verify RTSP/1.0",
            "POST /fp-setup RTSP/1.0", "SETUP rtsp://192.0.2.15/" + secret + " RTSP/1.0", "RECORD rtsp://192.0.2.15/123 RTSP/1.0",
            "POST /feedback RTSP/1.0", "User-Agent: AirPlay/980.67.2", "Authorization: Digest " + secret,
            "client SRP6a proof <M>: " + secret, "<key>ekey</key><data>" + secret + "</data>",
            "<key>deviceID</key><string>" + secret + "</string>", "<key>osVersion</key>", "<string>27.0.1</string>",
            "<key>sourceVersion</key><string>980.67.2</string>", "<key>combinedGetInfoWithControlSetup</key><true/>",
            "<key>dataPort</key><integer>35002</integer>", "Mirroring initialized successfully",
            "raop_rtp_mirror accepting client", "received type 0x01 packet with no payload",
            "Accepted IPv4 client on socket 12, port 35000 peer 192.0.2.15 " + secret,
            "Accepted IPv6 client on socket 13 peer 2001:db8::1234 " + secret,
            "*** ERROR: FairPlay failure " + secret, "TEARDOWN rtsp://192.0.2.15/123 RTSP/1.0", "00 01 02 03 " + secret
        };
        var sanitized = string.Join('\n', lines.SelectMany(line => filter.Feed(line)));
        Assert(!sanitized.Contains(secret) && !sanitized.Contains("192.0.2.15") && !sanitized.Contains("Authorization"), "Sensitive native data persisted.");
        foreach (var expected in new[] { "GET /info", "pair-setup", "pair-verify", "fp-setup", "SETUP", "RECORD", "feedback",
            "dataPort=35002", "Mirroring initialized successfully", "TEARDOWN", "User-Agent AirPlay/980.67.2", "osVersion=27.0.1", "sourceVersion=980.67.2", "combinedGetInfoWithControlSetup=true",
            "Accepted IPv4 client (control TCP", "Accepted IPv6 client (control TCP" })
        { Assert(sanitized.Contains(expected), "Missing negotiation field " + expected); }
        var noTcp = new AirPlayNegotiationTrace(_ => false);
        noTcp.Observe("Request SETUP"); noTcp.Observe("Mirror TCP dataPort=35002"); noTcp.Observe("Mirroring initialized successfully");
        Assert(noTcp.Observe("Request TEARDOWN").Any(line => line.Contains("not observed")), "Initialization wrongly proved mirror TCP/video.");
        var incomplete = new AirPlayNegotiationTrace(_ => false); incomplete.Observe("Request SETUP");
        Assert(incomplete.Finish("client disconnected").Any(line => line.Contains("SETUP incomplete")), "Incomplete SETUP not recorded.");
        var tcp = new AirPlayNegotiationTrace(_ => true); tcp.Observe("Request SETUP");
        Assert(tcp.Observe("Mirror TCP dataPort=35002").Any(line => line.Contains("ESTABLISHED")), "TCP evidence lost.");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        var localPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new System.Net.Sockets.TcpClient(); await client.ConnectAsync(System.Net.IPAddress.Loopback, localPort);
        using var accepted = await listener.AcceptTcpClientAsync();
        Assert(MirrorTcpInspection.HasEstablishedConnection(Environment.ProcessId, localPort), "Owned TCP inspection failed.");
        Assert(!MirrorTcpInspection.HasEstablishedConnection(-1, localPort), "TCP inspection ignored PID.");
        var attemptPath = Path.Combine(directory, "sanitized-attempt.log");
        var options = Options(directory, "ios27-fixture") with { EnableH265 = true, DetailedNegotiationLogging = true, AttemptLogPath = attemptPath };
        var factory = new FakeFactory(); var receiver = new UxPlayProcessService(options, log, new ReadyDependencies(), factory);
        await receiver.StartAsync(); foreach (var line in lines) { factory.Process.Emit(line); }
        factory.Process.Exit(27);
        var deadline = Stopwatch.StartNew(); while (receiver.IsRunning && deadline.Elapsed < TimeSpan.FromSeconds(2)) { await Task.Delay(10); }
        await receiver.StopAsync();
        var persisted = File.ReadAllText(attemptPath);
        Assert(persisted.Contains("Arguments JSON=") && persisted.Contains("UNEXPECTED RECEIVER EXIT: code=27") && !persisted.Contains(secret), "Attempt log lost metadata/exit or leaked secret.");
        Assert(!log.Snapshot().Any(entry => entry.Message.Contains(secret)), "Raw diagnostics leaked through general iMirror log.");
    }
    private sealed class FakeProcess : IReceiverProcess
    {
        private Action<string, bool>? _output;
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Id => 12345;
        public bool HasExited => _exit.Task.IsCompleted;
        public int ExitCode { get; private set; }
        public bool Advertise = true, Graceful = true, Killed;
        public string? InitialError;
        public void ReadOutput(Action<string, bool> onLine) { _output = onLine; if (InitialError is not null) { Emit(InitialError); } if (Advertise) { Emit("register_dnssd: advertised AirPlay service"); } }
        public void Emit(string line) => _output!(line, false);
        public void Exit(int code) { ExitCode = code; _exit.TrySetResult(); }
        public Task WaitForExitAsync() => _exit.Task;
        public Task DrainOutputAsync() => Task.CompletedTask;
        public Task<bool> RequestStopAsync() { if (Graceful) { Exit(0); } return Task.FromResult(Graceful); }
        public void KillOwnedTree() { Killed = true; Exit(-1); }
        public void Dispose() { }
    }
    private static async Task RendererDiagnostics(string directory, IDiagnosticLog log)
    {
        var warning = "0:00:03.123456700 123 456 WARN videodecoder gstvideodecoder.c:1:test:<avdec_h264-0> recoverable decode warning; not an error";
        Assert(UxPlayLogParser.Parse(warning, true, true).State is null, "Warning classified as failure.");
        Assert(UxPlayLogParser.Parse(warning.Replace("WARN", "\u001b[33mWARN\u001b[0m"), true, true).State is null, "ANSI-colored warning classified as failure.");
        var diagnostics = VideoRendererDiagnostics.Parse(warning);
        Assert(diagnostics.Count == 1 && diagnostics[0].Level == LogLevel.Warning && diagnostics[0].Message.Contains("recoverable decode warning"), "Native warning detail lost.");
        foreach (var (line, error) in new[]
        {
            ("GStreamer error (video): avdec_h265-0 Could not decode stream", AirPlayError.Decoder),
            ("GStreamer error (video): d3d11videosink0 Failed to create swap chain, HRESULT 0x887a0001", AirPlayError.VideoRenderer),
            ("GStreamer error (video): d3d11videosink0 Output window was closed", AirPlayError.SinkWindow),
            ("GStreamer error (video): video_source Internal data stream error", AirPlayError.GStreamer),
            ("*** ERROR: GStreamer gst_parse_launch failed to create video pipeline", AirPlayError.GStreamer),
            ("*** ERROR: unexpected native runtime condition", AirPlayError.NativeRuntime)
        })
        {
            Assert(UxPlayLogParser.Parse(line, true, true).Error == error, "Wrong stage/category: " + line);
            var expectedBody = line.StartsWith("*** ERROR:", StringComparison.Ordinal) ? line[10..].Trim() : line;
            Assert(VideoRendererDiagnostics.Parse(line).Any(entry => entry.Message.Contains(expectedBody)), "Original native diagnostic text lost: " + line);
        }
        var stateEvent = "GStreamer h264 bus message d3d11videosink0 state-changed PAUSED PLAYING";
        Assert(VideoRendererDiagnostics.Parse(stateEvent).Single().Message.Contains("PAUSED PLAYING"), "Sink transition lost.");
        Assert(VideoRendererDiagnostics.Parse("GStreamer: automatically-selected videosink (renderer 1: h264) is \"d3d12videosink\"").Single().Message.Contains("d3d12videosink"), "Actual sink name lost.");
        Assert(VideoRendererDiagnostics.Parse("begin video stream wxh = 998x2160; source 998x2160").Single().Message.Contains("998x2160"), "Physical dimensions lost.");
        var redacted = string.Join('\n', VideoRendererDiagnostics.Parse("GStreamer error (video): receiver 192.0.2.15 2001:db8::1234 driver failure").Select(entry => entry.Message));
        Assert(!redacted.Contains("192.0.2.15") && !redacted.Contains("2001:db8::1234") && redacted.Contains("driver failure"), "Renderer diagnostics exposed peer addresses or lost failure.");
        var proof = VideoRendererDiagnostics.Parse("*** ERROR: FairPlay failure FAKE_SECRET_SENTINEL").Single().Message;
        Assert(!proof.Contains("FAKE_SECRET_SENTINEL"), "Original crypto proof leaked through renderer diagnostics.");

        var attemptFile = Path.Combine(directory, "renderer-runtime-attempt.log");
        var factory = new FakeFactory();
        var service = new UxPlayProcessService(Options(directory, "renderer-runtime") with { DetailedNegotiationLogging = true, AttemptLogPath = attemptFile }, log, new ReadyDependencies(), factory);
        await service.StartAsync();
        try
        {
            factory.Process.Emit("Mirroring initialized successfully"); factory.Process.Emit("Begin streaming to GStreamer video pipeline");
            factory.Process.Emit("begin video stream wxh = 998x2160; source 998x2160"); factory.Process.Emit(stateEvent); factory.Process.Emit(warning);
            Assert(service.Status.State == AirPlayState.Streaming && service.Status.Width == 998, "Warning regressed successful stream or dimensions.");
            factory.Process.Emit("*** ERROR: unexpected native runtime condition");
            factory.Process.Emit("*** ERROR: repeated native runtime condition");
            Assert(service.Status.Error == AirPlayError.NativeRuntime && service.IsRunning, "Runtime failure was labeled StartupFailed or killed receiver.");
        }
        finally { await service.StopAsync(); }
        var persisted = File.ReadAllText(attemptFile);
        Assert(persisted.Contains("Native error category=NativeRuntime") && !persisted.Contains("category=StartupFailed") && persisted.Contains("unexpected native runtime condition"), "Contextual category or full diagnostic lost in physical-attempt log.");
    }
    private static async Task VideoWindowRegression(string[] args, IDiagnosticLog log)
    {
        var options = AirPlayConfiguration.Load(Path.Combine(args[2], "airplay.json"));
        var gst = Path.Combine(options.GStreamerBinPath!, "gst-launch-1.0.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(gst)) { Console.WriteLine("SKIP: native Windows/GStreamer window regression unavailable on this machine."); return; }
        foreach (var (encoder, parser, decoder) in new[] { ("openh264enc", "h264parse", "avdec_h264"), ("x265enc", "h265parse", "avdec_h265") })
        {
            var stdout = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var stderr = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var environment = new Dictionary<string, string> { ["PATH"] = options.GStreamerBinPath + ";" + Environment.GetEnvironmentVariable("PATH"), ["GST_DEBUG_NO_COLOR"] = "1", ["GST_DEBUG"] = "basesink:6,d3d11window:5" };
            var arguments = new List<string> { "-v", "videotestsrc", "num-buffers=60", "is-live=true", "!", "video/x-raw,format=I420,width=320,height=640,framerate=30/1", "!", "videoconvert", "!", encoder };
            if (encoder == "x265enc") { arguments.Add("tune=zerolatency"); }
            arguments.AddRange(["!", parser, "!", decoder, "!", "videoconvert", "!", "d3d11videosink"]);
            var request = new ReceiverProcessRequest(gst, arguments, environment);
            using var owned = new ReceiverProcessFactory().Start(request);
            owned.ReadOutput((line, error) => (error ? stderr : stdout).Enqueue(line));
            var visible = false; var watch = Stopwatch.StartNew();
            try
            {
                while (!owned.HasExited && watch.Elapsed < TimeSpan.FromSeconds(15))
                { visible |= VideoWindowInspection.Read(owned.Id).VisibleWindows > 0; await Task.Delay(25); }
                if (!owned.HasExited) { owned.KillOwnedTree(); throw new TimeoutException("Synthetic renderer did not reach EOS."); }
                await owned.WaitForExitAsync(); await owned.DrainOutputAsync();
                File.WriteAllLines(Path.Combine(args[0], encoder + "-window-stdout.log"), stdout);
                File.WriteAllLines(Path.Combine(args[0], encoder + "-window-stderr.log"), stderr);
                Assert(owned.ExitCode == 0 && stdout.Any(line => line.Contains("Got EOS")), "Synthetic decode/render pipeline failed: " + string.Join('\n', stderr.TakeLast(8)));
                Assert(visible, "Regression: GStreamer created a hidden video window through ReceiverProcessFactory.");
                Assert(stderr.Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"rendered:\s*[1-9]\d*,\s*dropped:")), "Visible window had no synthetic rendered frames.");
                log.Write(LogLevel.Information, "Renderer regression", $"{encoder} -> {parser} -> {decoder} -> d3d11videosink: visible owned window, frames rendered, EOS, exit 0. SYNTHETIC ONLY.");
            }
            finally { if (!owned.HasExited) { owned.KillOwnedTree(); await owned.WaitForExitAsync(); await owned.DrainOutputAsync(); } }
        }
    }
    private sealed class FakeFactory : IReceiverProcessFactory
    {
        public FakeProcess Process = new(); public int Starts;
        public IReceiverProcess Start(ReceiverProcessRequest request) { Starts++; return Process; }
    }
    private static AirPlayOptions Options(string directory, string name) => new() { SessionDirectory = Path.Combine(directory, name), StartupTimeout = TimeSpan.FromMilliseconds(200), StopTimeout = TimeSpan.FromMilliseconds(50) };
    private static async Task Lifecycle(string directory, IDiagnosticLog log)
    {
        var factory = new FakeFactory(); var options = Options(directory, "lifecycle");
        var service = new UxPlayProcessService(options, log, new ReadyDependencies(), factory);
        await Task.WhenAll(service.StartAsync(), service.StartAsync());
        Assert(factory.Starts == 1 && service.Status.State == AirPlayState.WaitingForDevice && service.Status.ReceiverStartupTime is not null, "Duplicate or fake readiness.");
        var other = new UxPlayProcessService(options, log, new ReadyDependencies(), new FakeFactory());
        await other.StartAsync(); Assert(other.Status.State == AirPlayState.Error && !other.IsRunning, "Instance lock failed.");
        var process = factory.Process;
        process.Emit("connection request from Test iPhone (iPhone) with deviceID = ABC");
        process.Emit("Mirroring initialized successfully"); Assert(service.Status.State == AirPlayState.Connected, "Connected event lost.");
        process.Emit("Begin streaming to GStreamer video pipeline");
        process.Emit("begin video stream wxh = 1080x1920; source 1080x1920");
        Assert(service.Status.IsConnected && service.Status.Width == 1080 && service.Status.ConnectionTime is not null, "Stream metrics missing.");
        process.Emit("Connection closed on socket 2"); Assert(service.Status.State == AirPlayState.Streaming, "Auxiliary socket caused disconnect.");
        process.Emit("video_reset: type = RTP_Shutdown"); Assert(service.Status.State == AirPlayState.Disconnected && service.Status.Width is null, "Disconnect did not clear dimensions.");
        process.Emit("connection request from Test iPhone (iPhone) with deviceID = ABC"); process.Emit("Begin streaming to GStreamer video pipeline");
        Assert(service.Status.State == AirPlayState.Streaming, "Reconnection state failed.");
        await service.StopAsync(); await service.StopAsync();
        Assert(!service.IsRunning && service.Status.State == AirPlayState.Stopped && !process.Killed, "Graceful stop failed.");
        factory.Process = new(); await service.StartAsync(); Assert(factory.Starts == 2, "Restart failed."); await service.StopAsync();
    }
    private static async Task Failures(string directory, IDiagnosticLog log)
    {
        var factory = new FakeFactory { Process = new() { Advertise = false, Graceful = false } };
        var service = new UxPlayProcessService(Options(directory, "timeout"), log, new ReadyDependencies(), factory);
        await service.StartAsync(); Assert(service.Status.State == AirPlayState.Error && !service.IsRunning && factory.Process.Killed, "Timeout claimed readiness or leaked child.");
        factory.Process = new() { Advertise = false }; using var cancel = new CancellationTokenSource(30);
        try { await service.StartAsync(cancel.Token); throw new InvalidOperationException("Cancellation ignored."); } catch (OperationCanceledException) { }
        Assert(!service.IsRunning && service.Status.State == AirPlayState.Stopped, "Cancellation leaked child.");
        factory.Process = new() { InitialError = "*** ERROR: Error initialising socket 10048" };
        await service.StartAsync(); Assert(service.Status.Error == AirPlayError.PortInUse && !service.IsRunning, "Startup error was overwritten by a late advertisement.");
        factory.Process = new(); await service.StartAsync(); factory.Process.Exit(23);
        var deadline = Stopwatch.StartNew(); while (service.IsRunning && deadline.Elapsed < TimeSpan.FromSeconds(2)) { await Task.Delay(10); }
        Assert(service.Status.Error == AirPlayError.ProcessExited && service.Status.ExitCode == 23, "Unexpected exit not reported.");
        factory.Process = new(); await service.StartAsync(); factory.Process.Emit("GStreamer error (video): failed");
        Assert(service.Status.Error == AirPlayError.GStreamer && service.IsRunning, "Runtime pipeline error lost."); await service.StopAsync();
    }
    private sealed class RealFactory(string dotnet) : IReceiverProcessFactory
    {
        public int? Id;
        public IReceiverProcess Start(ReceiverProcessRequest request)
        {
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--child");
            var child = Process.Start(start)!; Id = child.Id; return new ReceiverProcess(child);
        }
    }
    private static async Task RealChild(string[] args, IDiagnosticLog log)
    {
        var factory = new RealFactory(args[1]);
        var unrelated = new RealFactory(args[1]).Start(new("unused", [], new Dictionary<string, string>()));
        unrelated.ReadOutput((_, _) => { });
        var service = new UxPlayProcessService(Options(args[0], "real-child") with { StartupTimeout = TimeSpan.FromSeconds(10) }, log, new ReadyDependencies(), factory);
        try
        {
            await service.StartAsync(); Assert(service.Status.State == AirPlayState.WaitingForDevice, "Child stdout not read.");
            await service.StopAsync(); Assert(!service.IsRunning && !unrelated.HasExited, "Stop killed unrelated process.");
            try { using var owned = Process.GetProcessById(factory.Id!.Value); Assert(owned.HasExited, "Owned child survived."); } catch (ArgumentException) { }
            Assert(log.Snapshot().Any(x => x.Source == "UxPlay stderr" && x.Message.Contains("simulated stderr")), "stderr not drained.");
        }
        finally { await service.StopAsync(); unrelated.KillOwnedTree(); await unrelated.WaitForExitAsync(); await unrelated.DrainOutputAsync(); unrelated.Dispose(); }
    }
    private static async Task Diagnostic(string[] args, IDiagnosticLog log)
    {
        var options = AirPlayConfiguration.Load(Path.Combine(args[2], "airplay.json")) with { SessionDirectory = Path.Combine(args[0], "native-diagnostic") };
        var report = await new AirPlayDependencyService(log).CheckAsync(options, default);
        File.WriteAllText(Path.Combine(args[0], "dependencies.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        Console.WriteLine("INFO: machine dependency state = " + report.State);
        // This report can be missing dependencies on a clean machine. It must never be treated as video proof.
        Assert(Enum.IsDefined(report.State), "Invalid diagnostic result.");
    }
}
