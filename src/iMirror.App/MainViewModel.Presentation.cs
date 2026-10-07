using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using iMirror.AirPlay;
using iMirror.Bluetooth;
using iMirror.Core.Diagnostics;
using iMirror.Input;

namespace iMirror.App;

public sealed partial class MainViewModel
{
    private int _selectedPage;
    private bool _diagnosticsOpen;
    private string _logFilter = "Todos";
    public int SelectedPage { get => _selectedPage; set { _selectedPage = Math.Clamp(value, 0, 4); OnPropertyChanged(); } }
    public bool DiagnosticsOpen { get => _diagnosticsOpen; set { _diagnosticsOpen = value; OnPropertyChanged(); } }
    public string PhoneHeadline => AirPlay.IsConnected ? "● iPhone conectado" : _receiver.IsRunning ? "◌ Aguardando iPhone" : "○ Nenhum iPhone conectado";
    public string BluetoothSummary => Bluetooth.State switch
    {
        BluetoothState.Starting => "HID inicializando",
        BluetoothState.RadioOff => "Bluetooth desligado",
        BluetoothState.Error => "Erro no Bluetooth · confira Diagnóstico",
        BluetoothState.Stopped => "HID não iniciado",
        _ => Bluetooth.Message
    };
    public string PairingSteps
    {
        get
        {
            var host = Bluetooth.DiagnosticHost;
            string Step(bool yes, string text) => $"{(yes ? "✓" : "○")}  {text}";
            return string.Join(Environment.NewLine, Step(Bluetooth.RadioOn, "BLE ativo"), Step(Bluetooth.Advertising, "Serviço HID anunciado"),
                Step(host?.GattActive == true, "iPhone detectado · sessão GATT"), Step(host?.HidInformationRead == true, "HID Information"),
                Step(host?.ReportMapRead == true, "Report Map"), Step(Bluetooth.KeyboardConnected, "Keyboard · subscription"), Step(Bluetooth.MouseConnected, "Mouse · subscription"));
        }
    }
    public string PairingGuidance => Bluetooth.State == BluetoothState.Error ? Bluetooth.Message : Bluetooth.PairingTimedOut ? Bluetooth.DiagnosticHost?.GattActive == true ?
        "Pareamento detectado, mas o iPhone não ativou Mouse/Keyboard HID. O serviço continua disponível." :
        "Ainda aguardando o iPhone — o serviço continua disponível. Se o PC aparece conectado no iPhone, pode ser a entrada Bluetooth normal em vez da BLE HID." :
        "Pareie em Ajustes → Bluetooth. AssistiveTouch só é necessário para mostrar o ponteiro. A conexão é confirmada pelos subscribers reais.";
    private bool _autoSizeVideo = true, _focusMode;
    public bool AutoSizeVideo { get => _autoSizeVideo; set { _autoSizeVideo = value; OnPropertyChanged(); } }
    public bool FocusMode { get => _focusMode; set { _focusMode = value; OnPropertyChanged(); } }
    public RelayCommand PairingHelpCommand { get; private set; } = null!;
    public void PresentationNotice(string text) => Notice = text;
    public int OwnedReceiverPid => _ownedPid();
    public event Action? PanelRequested;
    public void SetPanelRecoveryHotkey(bool enabled, bool requireShift) => _capture?.SetPanelRecoveryHotkey(enabled, requireShift);
    public void LogPresentation(string text) => _log.Write(LogLevel.Information, "Renderer/UI", text);
    public string ControlSummary => ControlActive ? "Ativo" : "Desativado";
    public string MirrorTitle => AirPlay.State == AirPlayState.Streaming ? "Espelhamento ativo" : AirPlay.State == AirPlayState.Error ? "Confira a instalação" : "Aguardando iPhone";
    public string MirrorInstruction => AirPlay.State == AirPlayState.Streaming ? "A tela do iPhone está na janela externa de vídeo. Abra essa janela para usar mouse e teclado." :
        AirPlay.State == AirPlayState.Error ? AirPlay.Message : "Abra a Central de Controle e selecione iMirror - Windows em Espelhamento de Tela.";
    public bool CanFocusVideo => AirPlay.State == AirPlayState.Streaming;
    public ICollectionView LogView { get; private set; } = null!;
    public IReadOnlyList<string> LogFilters { get; } = ["Todos", "AirPlay", "Bluetooth", "Input", "Erro", "Verbose"];
    public string LogFilter { get => _logFilter; set { _logFilter = value; OnPropertyChanged(); RefreshLogView(); } }
    public RelayCommand DiagnosticsCommand { get; private set; } = null!;
    public RelayCommand CloseDiagnosticsCommand { get; private set; } = null!;
    public RelayCommand ClearLogsCommand { get; private set; } = null!;
    public RelayCommand CopyLogsCommand { get; private set; } = null!;
    public RelayCommand OpenLogFolderCommand { get; private set; } = null!;
    public RelayCommand FocusVideoCommand { get; private set; } = null!;
    private void InitializePresentation()
    {
        RefreshLogView();
        InitializePresentationCommands();
    }
    private void RefreshLogView()
    {
        LogView = CollectionViewSource.GetDefaultView(_logFilter == "Verbose" ? AllLogs : Logs);
        LogView.Filter = item => item is LogEntry entry && (_logFilter == "Verbose" || !DiagnosticVisibility.IsVerbose(entry)) && (_logFilter switch
        {
            "Erro" => entry.Level == LogLevel.Error,
            "Bluetooth" => entry.Source.StartsWith("BLE/", StringComparison.Ordinal),
            "Input" => entry.Source.Contains("input", StringComparison.OrdinalIgnoreCase) || entry.Source.Contains("capture", StringComparison.OrdinalIgnoreCase),
            "AirPlay" => !entry.Source.StartsWith("BLE/", StringComparison.Ordinal) && entry.Source is not ("UI" or "App"),
            _ => true
        });
        LogView.Refresh(); OnPropertyChanged(nameof(LogView));
    }
    private void InitializePresentationCommands()
    {
        DiagnosticsCommand = new RelayCommand(() => DiagnosticsOpen = true);
        PairingHelpCommand = new RelayCommand(() => MessageBox.Show("1. Não ligue/desligue o provider repetidamente.\n2. Se aparecerem duas entradas do PC, escolha uma e aguarde as etapas HID.\n3. A entrada correta fará HID Information, Report Map, Keyboard e Mouse avançarem.\n4. Sem atividade HID, esqueça apenas aquela entrada no iPhone e tente a outra.\n5. AssistiveTouch não é necessário para parear; só para mostrar o ponteiro.\n\nNão é possível remover a entrada Classic do iPhone pelo iMirror.", "iMirror — problemas para parear?", MessageBoxButton.OK, MessageBoxImage.Information));
        CloseDiagnosticsCommand = new RelayCommand(() => DiagnosticsOpen = false);
        ClearLogsCommand = new RelayCommand(() => { _entries.Clear(); _allEntries.Clear(); }); // Persisted files remain intact.
        CopyLogsCommand = new RelayCommand(() => PresentationAction(() => Clipboard.SetText(string.Join(Environment.NewLine, LogView.Cast<LogEntry>().Select(entry => entry.DisplayText)))));
        OpenLogFolderCommand = new RelayCommand(() => PresentationAction(() =>
        {
            var directory = Path.GetDirectoryName(LogFilePath)!;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }));
        FocusVideoCommand = new RelayCommand(() =>
        {
            if (VideoWindow.Find(_ownedPid()) is { IsValid: true } window)
            { ShowWindow(window.Handle, 9); if (!SetForegroundWindow(window.Handle)) { Notice = "Selecione a janela de vídeo na barra de tarefas."; } }
            else { Notice = "A janela de vídeo ainda não está disponível. Consulte Diagnóstico."; }
        });
    }
    private void PresentationAction(Action action)
    { try { action(); } catch (Exception ex) { _log.Write(LogLevel.Error, "UI", ex.Message); Notice = "Não foi possível executar a ação. Consulte Diagnóstico."; } }
    public void ReleaseControl() => _capture?.RequestStop("ESC na interface");
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
