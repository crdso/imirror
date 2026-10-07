using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using iMirror.Core.Diagnostics;
using iMirror.Core.Features;
using iMirror.AirPlay;
using iMirror.Bluetooth;
using iMirror.Input;
using Microsoft.Win32;

namespace iMirror.App;

public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDiagnosticLog _log;
    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<LogEntry> _entries = [];
    private readonly ObservableCollection<LogEntry> _allEntries = [];
    private bool _isFullscreen;
    private bool _disposed;
    private readonly IAirPlayReceiver _receiver;
    private readonly IBluetoothController _bluetooth;
    private readonly InputCapture? _capture;
    private readonly Func<int> _ownedPid;
    private readonly BluetoothControlLog? _controlLog;
    private bool _captureKeyboard = true;
    private int _wheelIntensity = 1;
    private double _cursorSpeed = 1;
    private KeyboardLayoutMode _keyboardLayoutMode;
    private BluetoothHost? _selectedHost;
    private int _appearanceIndex;
    private bool _bluetoothStarted;
    private CancellationTokenSource? _bluetoothStart;
    private bool _bluetoothStopping;
    private bool _pairingVisible;
    private DateTimeOffset? _visualPairingStarted;
    private readonly DispatcherTimer _pairingTimer;
    private readonly Func<string, bool> _confirmRecovery;
    private readonly CancellationTokenSource _shutdown = new();
    private string _notice = "Inicie AirPlay e selecione iMirror - Windows no Espelhamento de Tela do iPhone.";

    public MainViewModel(IDiagnosticLog log, IAirPlayReceiver airPlay, IFeatureService bluetooth,
        Dispatcher dispatcher, string logFilePath)
        : this(log, airPlay, new UnavailableBluetoothController(bluetooth), dispatcher, logFilePath) { }

    public MainViewModel(IDiagnosticLog log, IAirPlayReceiver airPlay, IBluetoothController bluetooth,
        Dispatcher dispatcher, string logFilePath, BluetoothControlLog? controlLog = null, Func<int>? ownedPid = null,
        Func<string, bool>? confirmRecovery = null)
    {
        _log = log;
        _dispatcher = dispatcher;
        _confirmRecovery = confirmRecovery ?? (message => MessageBox.Show(message, "iMirror — recuperação HID", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        _pairingTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => { OnPropertyChanged(nameof(ProviderDiagnostics)); OnPropertyChanged(nameof(PairingGuidance)); }, dispatcher);
        AirPlay = airPlay.Status;
        _receiver = airPlay;
        _receiver.StatusChanged += OnAirPlayStatus;
        Bluetooth = bluetooth.Status;
        _bluetooth = bluetooth; _controlLog = controlLog; _ownedPid = ownedPid ?? (() => 0);
        _bluetooth.StatusChanged += OnBluetoothStatus;
        if (controlLog is not null)
        {
            _capture = new InputCapture(bluetooth, controlLog);
            _capture.Stopped += OnCaptureStopped;
            _capture.PanelRequested += OnPanelRequested;
            SystemEvents.PowerModeChanged += OnPowerMode;
        }
        LogFilePath = logFilePath;
        Logs = new ReadOnlyObservableCollection<LogEntry>(_entries);
        AllLogs = new ReadOnlyObservableCollection<LogEntry>(_allEntries);
        _log.EntryAdded += OnEntryAdded;
        foreach (var entry in _log.Snapshot()) { Append(entry); }
        AirPlayCommand = new AsyncRelayCommand(async () =>
        {
            if (_receiver.IsRunning) { await StopControlAsync(); await _receiver.StopAsync(); }
            else { await _receiver.StartAsync(_shutdown.Token); }
        }, ex => { _log.Write(LogLevel.Error, "AirPlay", ex.ToString()); Notice = "Não foi possível alterar AirPlay. Consulte os logs."; }, dispatcher);
        BluetoothCommand = new AsyncRelayCommand(async () =>
        {
            if (!CanConnectBluetooth) { return; }
            _pairingVisible = true; _visualPairingStarted = DateTimeOffset.UtcNow;
            using var start = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _bluetoothStart = start; RefreshControl();
            try
            {
                await StopControlAsync();
                await Task.Run(() => _bluetooth.ConnectAsync(start.Token));
                _bluetoothStarted = IsBluetoothActive(_bluetooth.Status);
                Notice = _bluetooth.Status.Message;
            }
            finally { _bluetoothStart = null; RefreshControl(); }
        }, BluetoothError, dispatcher);
        ControlCommand = new AsyncRelayCommand(async () =>
        {
            if (_capture is { IsActive: true }) { await StopControlAsync(); }
            else
            {
                if (!CanControl || _capture is null) { throw new InputBlockedException("É necessário streaming AirPlay e subscriber de mouse no host selecionado."); }
                await _capture.StartAsync(_ownedPid, () => (_receiver.Status.Width ?? 0, _receiver.Status.Height ?? 0), CaptureKeyboard && Bluetooth.KeyboardConnected, WheelIntensity, CursorSpeed, KeyboardLayoutMode);
                RefreshControl();
            }
        }, BluetoothError, dispatcher);
        RestartHidCommand = new AsyncRelayCommand(async () =>
        {
            if (_bluetoothStopping) { return; }
            if (!_confirmRecovery("Reiniciar serviço HID? Isso encerra o provider atual. Se a parada não for confirmada pelo Windows, feche e reabra o iMirror. Não use durante a validação estável.")) { return; }
            await StopControlAsync();
            await Task.Run(() => _bluetooth.RestartAsync(_shutdown.Token));
            _bluetoothStarted = _bluetooth.Status.State is not (BluetoothState.Error or BluetoothState.RadioOff or BluetoothState.Stopped);
            RefreshControl();
        }, BluetoothError, dispatcher);
        StopBluetoothCommand = new AsyncRelayCommand(async () =>
        {
            if (!CanStopBluetooth) { return; }
            if (!_confirmRecovery("Parar serviço HID? Use somente para recuperação avançada. Cancela anúncio e input; o provider permanece até fechar ou reiniciar explicitamente. Para apenas recolher o pareamento, use Cancelar espera.")) { return; }
            _bluetoothStopping = true; RefreshControl();
            try
            {
                _bluetoothStart?.Cancel();
                await StopControlAsync();
                await BluetoothCommand.ExecutionTask;
                await RestartHidCommand.ExecutionTask;
                await ControlCommand.ExecutionTask;
                // Activation can still be awaiting native focus/release when Stop is clicked.
                await StopControlAsync();
                await Task.Run(_bluetooth.StopAsync);
                _bluetoothStarted = false;
                CancelPairingVisual();
                Notice = _bluetooth.Status.Message;
            }
            finally { _bluetoothStopping = false; RefreshControl(); }
        }, BluetoothError, dispatcher);
        UnpairHostCommand = new AsyncRelayCommand(async () =>
        {
            var host = SelectedHost;
            if (!CanUnpairHost || host is null || !_confirmRecovery($"Esquecer o vínculo de {host.DisplayName} no Windows? Apenas o host desta sessão HID será afetado. Depois esqueça o PC no iPhone uma única vez.")) { return; }
            await StopControlAsync();
            var result = await Task.Run(() => _bluetooth.UnpairHostAsync(host.Id, _shutdown.Token));
            Notice = $"DeviceUnpairingResult: {result.Status}. " + (result.Success ? "Esqueça também o PC no iPhone uma única vez. Serviço HID mantido." : "O vínculo não foi removido; consulte o resultado.");
            RefreshControl();
        }, BluetoothError, dispatcher);
        PairingCommand = new RelayCommand(() =>
        {
            if (!CanTogglePairing) { return; }
            if (_pairingVisible) { CancelPairingVisual(); return; }
            if (CanConnectBluetooth) { BluetoothCommand.Execute(null); }
            else { _pairingVisible = true; _visualPairingStarted = DateTimeOffset.UtcNow; RefreshControl(); }
        });
        FullscreenCommand = new RelayCommand(ToggleFullscreen);
        InitializePresentation();
    }

    public AirPlayStatus AirPlay { get; private set; }
    public BluetoothStatus Bluetooth { get; private set; }
    public IReadOnlyList<BluetoothHost> Hosts => Bluetooth.Hosts;
    public BluetoothHost? SelectedHost
    {
        get => _selectedHost;
        set { if (value is null || value.Id == _selectedHost?.Id) { return; } _selectedHost = value; OnPropertyChanged(); _ = ChangeHostAsync(value.Id); }
    }
    private async Task ChangeHostAsync(string id)
    { try { await StopControlAsync(); await Task.Run(() => _bluetooth.SelectHostAsync(id)); } catch (Exception error) { BluetoothError(error); } }
    public string BluetoothButtonText => _pairingVisible ? "Cancelar espera" : _bluetoothStarted || _bluetoothStart is not null ? "Mostrar pareamento" : "Conectar Bluetooth";
    public bool PairingVisible
    { get => _pairingVisible; set { if (_pairingVisible == value) { return; } _pairingVisible = value; _visualPairingStarted = value ? DateTimeOffset.UtcNow : null; RefreshControl(); } }
    public bool CanTogglePairing => !_bluetoothStopping && Bluetooth.State != BluetoothState.StopUnconfirmed && !_shutdown.IsCancellationRequested;
    public void CancelPairingVisual()
    { _pairingVisible = false; _visualPairingStarted = null; _controlLog?.Write("pairing-visual", "Visual wait canceled; provider, advertising, input and bond untouched"); RefreshControl(); }
    public bool CanUnpairHost => !_bluetoothStopping && SelectedHost is { Bonded: true, CanUnpair: true };
    public string HidSchemaProfile => "iOS Stable / Known Good";
    public string StableKeyboardLimitation => "iOS Stable: International1 do ABNT2 (/ e ?) não é enviado. US e demais teclas/dead keys PT-BR mantidos; descriptor fixo 0x65.";
    public string ProviderDiagnostics => $"Provider generation: {Bluetooth.ProviderGeneration}\nProvider lifetime: {(Bluetooth.ProviderCreatedAt is { } at ? (DateTimeOffset.UtcNow - at).ToString(@"hh\:mm\:ss") : "não iniciado")}\nAdvertising state: {Bluetooth.AdvertisingRecoveryState}\nHID completo live: {(Bluetooth.HidReadySince is { } healthy ? (DateTimeOffset.UtcNow - healthy).ToString(@"hh\:mm\:ss") : "aguardando etapas HID")}\nReconexão HID observada no mesmo provider: {Bluetooth.HidReconnectObserved}";
    private static bool IsBluetoothActive(BluetoothStatus status) => status.State is not
        (BluetoothState.Stopped or BluetoothState.Disconnected or BluetoothState.Error or BluetoothState.RadioOff or BluetoothState.StopUnconfirmed);
    public bool CanConnectBluetooth => !_bluetoothStopping && !_bluetoothStarted && _bluetoothStart is null &&
        !IsBluetoothActive(Bluetooth) && Bluetooth.State != BluetoothState.StopUnconfirmed;
    public bool CanStopBluetooth => !_bluetoothStopping && (_bluetoothStart is not null || _bluetoothStarted || IsBluetoothActive(Bluetooth));
    public string HidStatus => Bluetooth.State == BluetoothState.Stopped ? "Mouse e teclado Bluetooth desativados" :
        Bluetooth.State == BluetoothState.StopUnconfirmed ? "Envio HID bloqueado · parada do anúncio não confirmada" :
        $"Keyboard: {(Bluetooth.KeyboardConnected ? "conectado" : "aguardando")}  |  Mouse: {(Bluetooth.MouseConnected ? "conectado" : "aguardando")}  |  {(Bluetooth.ProtocolMode == 1 ? "Report mode" : "Boot mode: sem wheel")}";
    public string HostDiagnostics => SelectedHost is { } host ? $"HID Information: {host.HidInformationRead}; Report Map: {host.ReportMapRead}; Protocol Mode escrito: {host.ProtocolModeWritten}; Bond: {host.Bonded}; Link: {host.ConnectionStatus}" : "Nenhum host HID nesta sessão. O nome anunciado é o nome Bluetooth deste PC.";
    public bool ControlActive => _capture?.IsActive == true;
    public bool CanControl => !_bluetoothStopping && (ControlActive || Bluetooth.ControlReady && AirPlay.State == AirPlayState.Streaming && AirPlay.Width > 0 && AirPlay.Height > 0);
    public string ControlButtonText => ControlActive ? "Desativar controle" : "Ativar controle";
    public string ControlStatus => ControlActive ? "Controle ativo — ESC ou Ctrl+Alt+Q para parar" : "Mouse relativo — captura somente na janela de vídeo em foco";
    public bool CaptureKeyboard
    { get => _captureKeyboard; set { if (_captureKeyboard == value) { return; } _captureKeyboard = value; _capture?.RequestStop("opção de teclado alterada"); OnPropertyChanged(); } }
    public int WheelIntensity
    { get => _wheelIntensity; set { _wheelIntensity = Math.Clamp(value, 1, 5); _capture?.RequestStop("intensidade alterada"); OnPropertyChanged(); } }
    public double CursorSpeed
    { get => _cursorSpeed; set { _cursorSpeed = double.IsFinite(value) ? Math.Clamp(value,0.25,3) : 1; _capture?.RequestStop("velocidade alterada; reative o controle"); OnPropertyChanged(); } }
    public IReadOnlyList<KeyboardLayoutMode> KeyboardLayouts { get; } = Enum.GetValues<KeyboardLayoutMode>();
    public KeyboardLayoutMode KeyboardLayoutMode
    { get => _keyboardLayoutMode; set { _keyboardLayoutMode = value; _capture?.RequestStop("layout alterado; reative o controle"); OnPropertyChanged(); } }
    public int AppearanceIndex
    { get => _appearanceIndex; set { if (_appearanceIndex == value) { return; } _appearanceIndex = value; OnPropertyChanged(); _ = ChangeAppearanceAsync(); } }
    private async Task ChangeAppearanceAsync()
    {
        try { await StopControlAsync(); await Task.Run(() => _bluetooth.SetAppearanceAsync(AppearanceIndex switch { 1 => (ushort?)0x03C1, 2 => 0x03C2, _ => null })); }
        catch (Exception error) { _appearanceIndex = 0; OnPropertyChanged(nameof(AppearanceIndex)); BluetoothError(error); }
    }
    public bool CanChangeAppearance => _bluetoothStarted;
    public string ConnectionStatus => AirPlay.State == AirPlayState.Stopped ? "Nenhum iPhone conectado" : AirPlay.Message;
    public string AirPlayButtonText => AirPlay.State is AirPlayState.CheckingDependencies or AirPlayState.Starting ? "Iniciando..." :
        AirPlay.State == AirPlayState.Stopping ? "Parando..." : _receiver.IsRunning ? "Parar AirPlay" : "Iniciar AirPlay";
    public string VideoMetrics => $"Resolução: {(AirPlay.Width is { } w ? $"{w} × {AirPlay.Height}" : "N/A")}  |  FPS: N/A  |  Latência: N/A";
    public string TimingMetrics => $"Receiver: {Format(AirPlay.ReceiverStartupTime)}  |  Conexão → stream: {Format(AirPlay.ConnectionTime)}";
    private static string Format(TimeSpan? value) => value is { } time ? $"{time.TotalMilliseconds:F0} ms" : "N/A";
    public string LogFilePath { get; }
    public ReadOnlyObservableCollection<LogEntry> Logs { get; }
    public ReadOnlyObservableCollection<LogEntry> AllLogs { get; }
    public AsyncRelayCommand AirPlayCommand { get; }
    public AsyncRelayCommand BluetoothCommand { get; }
    public AsyncRelayCommand StopBluetoothCommand { get; }
    public AsyncRelayCommand UnpairHostCommand { get; }
    public RelayCommand PairingCommand { get; }
    public AsyncRelayCommand ControlCommand { get; }
    public AsyncRelayCommand RestartHidCommand { get; }
    public RelayCommand FullscreenCommand { get; }
    public bool IsFullscreen => _isFullscreen;
    public string FullscreenButtonText => _isFullscreen ? "Sair do Fullscreen" : "Fullscreen";
    public string Notice
    {
        get => _notice;
        private set { _notice = value; OnPropertyChanged(); }
    }

    public void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        OnPropertyChanged(nameof(IsFullscreen));
        OnPropertyChanged(nameof(FullscreenButtonText));
        _log.Write(LogLevel.Information, "UI", _isFullscreen ? "Fullscreen ativado. ESC para sair." : "Fullscreen desativado.");
    }

    private void OnAirPlayStatus(AirPlayStatus status)
    {
        if (status.State != AirPlayState.Streaming) { _capture?.RequestStop("AirPlay sem stream ativo"); }
        void Apply()
        {
            if (_disposed) { return; }
            AirPlay = status;
            Notice = status.State == AirPlayState.Error ? status.Message :
                "AirPlay e controle BLE validados no iPhone. Ajustes de cursor e teclado aguardam seu teste.";
            foreach (var name in new[] { nameof(AirPlay), nameof(ConnectionStatus), nameof(AirPlayButtonText), nameof(VideoMetrics), nameof(TimingMetrics), nameof(PhoneHeadline), nameof(MirrorTitle), nameof(MirrorInstruction), nameof(CanFocusVideo) })
            { OnPropertyChanged(name); }
            RefreshControl();
        }
        if (_dispatcher.CheckAccess()) { Apply(); }
        else if (!_dispatcher.HasShutdownStarted) { _dispatcher.BeginInvoke(Apply); }
    }

    public async Task ShutdownAsync()
    {
        await _shutdown.CancelAsync();
        await StopControlAsync();
        await BluetoothCommand.ExecutionTask;
        await StopBluetoothCommand.ExecutionTask;
        await UnpairHostCommand.ExecutionTask;
        await RestartHidCommand.ExecutionTask;
        await ControlCommand.ExecutionTask;
        // An activation may have been awaiting native startup when close was requested.
        await StopControlAsync();
        await Task.Run(_bluetooth.DisconnectAsync);
        await AirPlayCommand.ExecutionTask;
        if (_receiver.IsRunning) { await _receiver.StopAsync(); }
    }

    private Task StopControlAsync() => _capture?.StopAsync() ?? Task.CompletedTask;
    private void BluetoothError(Exception error)
    { _controlLog?.Error("UI-error", error); Notice = error is InputBlockedException ? error.Message : "Erro no controle Bluetooth. Consulte bluetooth-control.log."; RefreshControl(); }
    private void OnBluetoothStatus(BluetoothStatus status)
    {
        if (!status.ControlReady || status.SelectedHostId != Bluetooth.SelectedHostId)
        { _capture?.RequestStop("host ou subscriber HID perdido"); }
        Dispatch(() =>
        {
            Bluetooth = status;
            if (status.State == BluetoothState.Starting) { _appearanceIndex = 0; OnPropertyChanged(nameof(AppearanceIndex)); }
            _selectedHost = status.Hosts.FirstOrDefault(host => host.Id == status.SelectedHostId);
            foreach (var name in new[] { nameof(Bluetooth), nameof(Hosts), nameof(SelectedHost), nameof(HidStatus), nameof(HostDiagnostics), nameof(PairingSteps), nameof(PairingGuidance), nameof(WindowsBluetoothStatus) }) { OnPropertyChanged(name); }
            RefreshControl();
        });
    }
    private void OnCaptureStopped(string reason) => Dispatch(() => { Notice = $"Controle parado: {reason}. Input devolvido ao Windows."; RefreshControl(); });
    private void OnPanelRequested() => Dispatch(() => PanelRequested?.Invoke());
    private void RefreshControl()
    { foreach (var name in new[] { nameof(ControlActive), nameof(CanControl), nameof(ControlButtonText), nameof(ControlStatus), nameof(BluetoothButtonText), nameof(CanConnectBluetooth), nameof(CanStopBluetooth), nameof(CanTogglePairing), nameof(PairingVisible), nameof(CanUnpairHost), nameof(ProviderDiagnostics), nameof(PairingGuidance), nameof(CanChangeAppearance), nameof(BluetoothSummary), nameof(ControlSummary) }) { OnPropertyChanged(name); } }
    private void Dispatch(Action action)
    { if (_disposed || _dispatcher.HasShutdownStarted) { return; } if (_dispatcher.CheckAccess()) { action(); } else { _dispatcher.BeginInvoke(action); } }
    private void OnPowerMode(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) { _capture?.RequestStop("Windows suspenso"); _controlLog?.Write("power", "Suspend: capture stopped; neutral release pending if link closed"); }
        if (args.Mode == PowerModes.Resume) { _capture?.RequestStop("Windows retomado"); _controlLog?.Write("power", "Resume: no automatic capture; recheck HID subscribers"); Dispatch(() => Notice = "Windows retomado. Use AssistiveTouch > Dispositivos > Dispositivos Bluetooth se os subscribers não retornarem."); }
    }

    public void ReportShutdownFailure(Exception exception)
    {
        _log.Write(LogLevel.Error, "AirPlay", exception.ToString());
        Notice = "Não foi possível encerrar AirPlay. Tente parar novamente e consulte os logs.";
    }

    private void OnEntryAdded(LogEntry entry)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) { return; }
        if (_dispatcher.CheckAccess()) { Append(entry); }
        else { _dispatcher.BeginInvoke(() => { if (!_disposed) { Append(entry); } }); }
    }

    private void Append(LogEntry entry)
    {
        _allEntries.Add(entry);
        while (_allEntries.Count > FileDiagnosticLog.HistoryCapacity) { _allEntries.RemoveAt(0); }
        if (!DiagnosticVisibility.IsVerbose(entry)) { _entries.Add(entry); }
        while (_entries.Count > FileDiagnosticLog.HistoryCapacity) { _entries.RemoveAt(0); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _disposed = true;
        _pairingTimer.Stop();
        _log.EntryAdded -= OnEntryAdded;
        _receiver.StatusChanged -= OnAirPlayStatus;
        _bluetooth.StatusChanged -= OnBluetoothStatus;
        _capture?.RequestStop("ViewModel encerrado");
        if (_capture is not null) { _capture.Stopped -= OnCaptureStopped; _capture.PanelRequested -= OnPanelRequested; SystemEvents.PowerModeChanged -= OnPowerMode; }
    }
}
