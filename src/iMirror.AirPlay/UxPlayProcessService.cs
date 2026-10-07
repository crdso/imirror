using System.Diagnostics;
using iMirror.Core.Diagnostics;

namespace iMirror.AirPlay;

public sealed class UxPlayProcessService : IAirPlayReceiver
{
    private readonly AirPlayOptions _options;
    private readonly IDiagnosticLog _log;
    private readonly IAirPlayDependencyService _dependencies;
    private readonly IReceiverProcessFactory _factory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();
    private AirPlayStatus _status = new();
    private IReceiverProcess? _process;
    private FileStream? _instanceLock;
    private TaskCompletionSource<bool>? _advertised;
    private readonly Stopwatch _startup = new();
    private readonly Stopwatch _connection = new();
    private bool _stopping;
    private AirPlayAttemptLog? _attempt;
    private NegotiationLogFilter _stdoutFilter = new(), _stderrFilter = new();
    private AirPlayNegotiationTrace? _negotiation;
    private bool _receiverStarted, _mirroringInitialized, _windowProbeIssued;
    private CancellationTokenSource? _windowProbeCancellation;
    private Task _windowProbe = Task.CompletedTask;

    public UxPlayProcessService(AirPlayOptions options, IDiagnosticLog log,
        IAirPlayDependencyService? dependencies = null, IReceiverProcessFactory? factory = null)
    {
        _options = options; _log = log;
        _dependencies = dependencies ?? new AirPlayDependencyService(log);
        _factory = factory ?? new ReceiverProcessFactory();
    }
    public AirPlayStatus Status { get { lock (_sync) { return _status; } } }
    public bool IsRunning { get { lock (_sync) { return _process is not null; } } }
    public event Action<AirPlayStatus>? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) { return; }
            _receiverStarted = _mirroringInitialized = _windowProbeIssued = false;
            _options.Validate();
            if (_options.DetailedNegotiationLogging)
            {
                _attempt?.Dispose(); _attempt = new AirPlayAttemptLog(_options);
                _stdoutFilter = new(); _stderrFilter = new();
                _negotiation = new(port => _process is { } owned && !owned.HasExited && MirrorTcpInspection.HasEstablishedConnection(owned.Id, port));
            }
            Set(new(AirPlayState.CheckingDependencies, "Verificando dependências"));
            var dependencies = await _dependencies.CheckAsync(_options, cancellationToken).ConfigureAwait(false);
            if (dependencies.State != DependencyState.Ready)
            { Set(new(AirPlayState.Error, dependencies.Message, AirPlayError.Dependencies)); FinishAttempt("dependency check failed"); return; }
            _options.Validate();
            Directory.CreateDirectory(_options.SessionDirectory);
            try { _instanceLock = new FileStream(Path.Combine(_options.SessionDirectory, "receiver.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex)
            {
                _log.Write(LogLevel.Error, "AirPlay", ex.ToString());
                Set(new(AirPlayState.Error, "Outro iMirror já está usando o receiver.", AirPlayError.StartupFailed)); FinishAttempt("instance lock busy"); return;
            }
            var config = Path.Combine(_options.SessionDirectory, "uxplay-empty.conf");
            await File.WriteAllTextAsync(config, "# iMirror owns receiver arguments; ignore user uxplayrc.\n", cancellationToken).ConfigureAwait(false);
            _log.Write(LogLevel.Information, "AirPlay", "Iniciando receiver (vídeo na janela externa)");
            _stopping = false;
            _advertised = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startup.Restart(); _connection.Reset();
            Set(new(AirPlayState.Starting, "Iniciando AirPlay..."));
            var environment = new Dictionary<string, string>(dependencies.Environment!);
            if (_options.DetailedNegotiationLogging)
            { environment["GST_DEBUG"] = VideoRendererDiagnostics.DebugCategories; environment["GST_DEBUG_NO_COLOR"] = "1"; }
            var request = new ReceiverProcessRequest(dependencies.UxPlayPath!, UxPlayArguments.Build(_options, config), environment);
            _log.Write(LogLevel.Information, "Process", $"{request.Executable}: {string.Join(" | ", request.Arguments)}");
            _attempt?.Arguments(request);
            var launchDiagnostic = $"Receiver launch: UseShellExecute=false; CreateNoWindow=true (console only); WindowStyle=Normal; stdout/stderr redirected; configured sink={_options.VideoSink}; GST_DEBUG={environment.GetValueOrDefault("GST_DEBUG")}";
            _log.Write(LogLevel.Information, "Video renderer", launchDiagnostic); _attempt?.Write(launchDiagnostic);
            var process = _factory.Start(request);
            lock (_sync) { _process = process; }
            process.ReadOutput((line, stderr) => OnOutput(process, line, stderr));
            _ = ObserveExitAsync(process, _advertised);
            bool ready;
            try { ready = await _advertised.Task.WaitAsync(_options.StartupTimeout, cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                _log.Write(LogLevel.Error, "AirPlay", "Nenhum anúncio DNS-SD confirmado dentro do prazo.");
                await StopOwnedAsync().ConfigureAwait(false);
                Set(new(AirPlayState.Error, "AirPlay não confirmou o anúncio. Confira Bonjour e os logs.", AirPlayError.StartupFailed)); return;
            }
            if (!ready)
            {
                var failure = Status;
                await StopOwnedAsync().ConfigureAwait(false);
                Set(failure.State == AirPlayState.Error ? failure : new(AirPlayState.Error, "O receiver encerrou durante a inicialização.", AirPlayError.ProcessExited));
            }
        }
        catch (OperationCanceledException)
        { await StopOwnedAsync().ConfigureAwait(false); Set(new()); throw; }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, "AirPlay", ex.ToString());
            await StopOwnedAsync().ConfigureAwait(false);
            Set(new(AirPlayState.Error, "AirPlay não conseguiu iniciar. Consulte os logs.", AirPlayError.StartupFailed));
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { Set(Status with { State = AirPlayState.Stopping, Message = "Parando AirPlay..." }); await StopOwnedAsync().ConfigureAwait(false); Set(new()); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopOwnedAsync()
    {
        IReceiverProcess? process;
        lock (_sync) { _stopping = true; process = _process; }
        _windowProbeCancellation?.Cancel();
        await _windowProbe.ConfigureAwait(false);
        _windowProbeCancellation?.Dispose(); _windowProbeCancellation = null;
        if (process is not null)
        {
            if (!process.HasExited)
            {
                var graceful = false;
                try { graceful = await process.RequestStopAsync().ConfigureAwait(false); }
                catch (Exception ex) { _log.Write(LogLevel.Warning, "Process", $"Sinal de parada: {ex.Message}"); }
                _log.Write(LogLevel.Information, "Process", graceful ? "CTRL_C enviado ao console isolado do receiver." : "Parada por console indisponível; aguardando antes do fallback.");
                try { await process.WaitForExitAsync().WaitAsync(_options.StopTimeout).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    _log.Write(LogLevel.Warning, "Process", $"Prazo de parada atingido. Encerrando somente a árvore pertencente ao PID {process.Id}.");
                    process.KillOwnedTree(); await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            await process.DrainOutputAsync().ConfigureAwait(false);
            lock (_sync) { if (ReferenceEquals(_process, process)) { _process = null; } }
            process.Dispose();
        }
        _instanceLock?.Dispose(); _instanceLock = null;
        FinishAttempt("receiver stopped/exited");
    }

    private void FinishAttempt(string cause)
    {
        lock (_sync)
        {
            if (_attempt is null) { return; }
            foreach (var summary in _negotiation?.Finish(cause) ?? []) { _attempt.Write(summary); _log.Write(LogLevel.Information, "Negotiation", summary); }
            _attempt.Write($"SESSION END: {cause}; omitted raw stdout lines={_stdoutFilter.OmittedLines}; omitted raw stderr lines={_stderrFilter.OmittedLines}");
            _attempt.Dispose(); _attempt = null; _negotiation = null;
        }
    }

    private async Task ObserveExitAsync(IReceiverProcess process, TaskCompletionSource<bool> advertised)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await process.DrainOutputAsync().ConfigureAwait(false);
            advertised.TrySetResult(false);
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_sync) { if (!ReferenceEquals(_process, process)) { return; } }
                var exitCode = process.ExitCode;
                _log.Write(LogLevel.Error, "Process", $"Receiver PID {process.Id} encerrou inesperadamente, código {exitCode}.");
                _attempt?.Write($"UNEXPECTED RECEIVER EXIT: code={exitCode}");
                var previous = Status;
                await StopOwnedAsync().ConfigureAwait(false);
                Set(previous with { State = AirPlayState.Error, Message = previous.Error != AirPlayError.None ? previous.Message : "O receiver encerrou. Inicie AirPlay novamente.",
                    Error = previous.Error != AirPlayError.None ? previous.Error : AirPlayError.ProcessExited, ExitCode = exitCode });
            }
            finally { _lifecycle.Release(); }
        }
        catch (Exception ex) { _log.Write(LogLevel.Error, "Process", $"Monitor do receiver: {ex}"); }
    }

    private void OnOutput(IReceiverProcess process, string line, bool stderr)
    {
        lock (_sync)
        {
            if (_options.DetailedNegotiationLogging)
            {
                foreach (var safeEvent in (stderr ? _stderrFilter : _stdoutFilter).Feed(line, _receiverStarted, _mirroringInitialized))
                {
                    _log.Write(stderr ? LogLevel.Warning : LogLevel.Information, stderr ? "UxPlay stderr" : "UxPlay stdout", safeEvent);
                    _attempt?.Write($"{(stderr ? "stderr" : "stdout")}: {safeEvent}");
                    foreach (var evidence in _negotiation?.Observe(safeEvent) ?? [])
                    { _log.Write(LogLevel.Information, "Negotiation", evidence); _attempt?.Write(evidence); }
                }
            }
            else { _log.Write(stderr ? LogLevel.Warning : LogLevel.Information, stderr ? "UxPlay stderr" : "UxPlay stdout", line); }
            foreach (var diagnostic in VideoRendererDiagnostics.Parse(line))
            {
                _log.Write(diagnostic.Level, "Video renderer", diagnostic.Message);
                _attempt?.Write($"Video renderer [{diagnostic.Level}]: {diagnostic.Message}");
            }
            if (_stopping || !ReferenceEquals(_process, process)) { return; }
            var signal = UxPlayLogParser.Parse(line, _receiverStarted, _mirroringInitialized);
            var status = _status;
            if (signal.State == AirPlayState.WaitingForDevice)
            {
                _advertised?.TrySetResult(true);
                _receiverStarted = true;
                if (status.State != AirPlayState.Starting) { return; }
                status = status with { ReceiverStartupTime = _startup.Elapsed };
            }
            if (signal.State == AirPlayState.Connecting && status.State is AirPlayState.Connected or AirPlayState.Streaming) { return; }
            if (signal.State == AirPlayState.Connecting && !_connection.IsRunning) { _connection.Restart(); }
            if (signal.State == AirPlayState.Streaming && _connection.IsRunning) { status = status with { ConnectionTime = _connection.Elapsed }; }
            if (signal.State is AirPlayState.Connected or AirPlayState.Streaming) { _mirroringInitialized = true; }
            if (signal.State == AirPlayState.Streaming && !_windowProbeIssued && process is ReceiverProcess)
            {
                _windowProbeIssued = true;
                _windowProbeCancellation?.Cancel(); _windowProbeCancellation?.Dispose();
                _windowProbeCancellation = new(); _windowProbe = InspectVideoWindowAsync(process, _windowProbeCancellation.Token);
            }
            if (signal.State == AirPlayState.Disconnected)
            {
                _connection.Reset(); _mirroringInitialized = _windowProbeIssued = false; _windowProbeCancellation?.Cancel();
                status = status with { Width = null, Height = null, ConnectionTime = null };
            }
            if (signal.Width is not null) { status = status with { Width = signal.Width, Height = signal.Height }; }
            if (signal.DeviceName is not null) { status = status with { DeviceName = signal.DeviceName }; }
            if (signal.State is { } state)
            {
                status = status with { State = state, Error = signal.Error, Message = Message(state, signal.Error) };
                if (state == AirPlayState.Error) { _advertised?.TrySetResult(false); }
            }
            if (status != _status) { Set(status); }
        }
    }
    private async Task InspectVideoWindowAsync(IReceiverProcess process, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var delay in new[] { 250, 750, 2000 })
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_stopping || !ReferenceEquals(_process, process) || process.HasExited) { return; }
                    var snapshot = VideoWindowInspection.Read(process.Id);
                    var message = $"Video window for owned PID {process.Id}: created={snapshot.VideoWindows}; visible={snapshot.VisibleWindows}; classes={string.Join(",", snapshot.Classes)}. Window visibility is not proof of decoded iPhone frames.";
                    _log.Write(LogLevel.Information, "Video renderer", message); _attempt?.Write(message);
                    if (snapshot.VisibleWindows > 0) { return; }
                }
            }
            lock (_sync)
            {
                if (_stopping || !ReferenceEquals(_process, process)) { return; }
                const string message = "No visible GStreamer video window observed within 3 seconds of video packets. Inspect sink/decoder diagnostics; AirPlay negotiation is a separate stage.";
                _log.Write(LogLevel.Warning, "Video renderer", message); _attempt?.Write(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (!_stopping && ReferenceEquals(_process, process))
                { _log.Write(LogLevel.Warning, "Video renderer", $"Window inspection unavailable: {ex.GetType().Name}. No startup failure inferred."); }
            }
        }
    }
    private void Set(AirPlayStatus status)
    {
        lock (_sync) { _status = status; }
        _log.Write(status.State == AirPlayState.Error ? LogLevel.Error : LogLevel.Information, "AirPlay", status.Message);
        _attempt?.Write($"iMirror state={status.State}; error={status.Error}; {status.Message}");
        StatusChanged?.Invoke(status);
    }
    private static string Message(AirPlayState state, AirPlayError error) => state switch
    {
        AirPlayState.WaitingForDevice => "Aguardando iPhone...",
        AirPlayState.Connecting => "iPhone conectando",
        AirPlayState.Connected => "iPhone conectado",
        AirPlayState.Streaming => "Stream iniciado — confira a janela de vídeo",
        AirPlayState.Disconnected => "Dispositivo desconectado — aguardando nova conexão",
        AirPlayState.Error => error switch
        {
            AirPlayError.PortInUse => "A porta do receiver está ocupada. Feche o outro receiver ou altere a porta.",
            AirPlayError.ServiceDiscovery => "Não foi possível anunciar AirPlay. Confira Bonjour e firewall.",
            AirPlayError.VideoPipeline => "O vídeo não pôde iniciar. Confira os plugins GStreamer nos logs.",
            AirPlayError.GStreamer => "Falha no pipeline GStreamer. Confira a mensagem nativa nos logs de vídeo.",
            AirPlayError.Decoder => "O decoder não conseguiu processar o vídeo. Consulte os logs de vídeo.",
            AirPlayError.VideoRenderer => "O renderizador de vídeo falhou. Confira o erro de Direct3D/videosink nos logs.",
            AirPlayError.SinkWindow => "A janela de vídeo foi fechada ou não pôde ser criada. Consulte os logs de vídeo.",
            AirPlayError.NativeRuntime => "Falha nativa durante a execução do receiver. Consulte a mensagem original nos logs.",
            AirPlayError.Authentication => "O iPhone não pôde autenticar. Reconecte o espelhamento.",
            AirPlayError.CodecNegotiation => "A negociação do codec falhou. Confira o perfil HEVC e os logs.",
            AirPlayError.Decryption => "Falha de descriptografia do vídeo. Consulte o log da negociação.",
            AirPlayError.FairPlay => "Falha na negociação FairPlay. Consulte o log da tentativa.",
            AirPlayError.MirrorConnection => "A conexão TCP do vídeo falhou ou foi encerrada pelo cliente.",
            AirPlayError.IncompleteSetup => "O iPhone encerrou antes de completar o SETUP do vídeo.",
            _ => "AirPlay encontrou um erro. Pare e inicie novamente; consulte os logs."
        },
        _ => state.ToString()
    };
}
