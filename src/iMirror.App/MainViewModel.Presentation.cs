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
    public string BluetoothSummary => Bluetooth.IsConnected ? "Conectado" : Bluetooth.State switch
    {
        BluetoothState.Starting => "Preparando Bluetooth...",
        BluetoothState.RadioOff => "Bluetooth desligado",
        BluetoothState.Error => "Erro no Bluetooth · confira Diagnóstico",
        BluetoothState.WaitingForPairing => "Aguardando pareamento",
        BluetoothState.BondedWithoutHid => "Pareado · aguardando mouse e teclado",
        _ => "Desconectado"
    };
    public string ControlSummary => ControlActive ? "Ativo" : "Desativado";
    public string MirrorTitle => AirPlay.State == AirPlayState.Streaming ? "Espelhamento ativo" : AirPlay.State == AirPlayState.Error ? "Confira a instalação" : "Aguardando iPhone";
    public string MirrorInstruction => AirPlay.State == AirPlayState.Streaming ? "A tela do iPhone está na janela externa de vídeo. Abra essa janela para usar mouse e teclado." :
        AirPlay.State == AirPlayState.Error ? AirPlay.Message : "Abra a Central de Controle e selecione iMirror - Windows em Espelhamento de Tela.";
    public bool CanFocusVideo => AirPlay.State == AirPlayState.Streaming;
    public ICollectionView LogView { get; private set; } = null!;
    public IReadOnlyList<string> LogFilters { get; } = ["Todos", "AirPlay", "Bluetooth", "Input", "Erro"];
    public string LogFilter { get => _logFilter; set { _logFilter = value; OnPropertyChanged(); LogView.Refresh(); } }
    public RelayCommand DiagnosticsCommand { get; private set; } = null!;
    public RelayCommand CloseDiagnosticsCommand { get; private set; } = null!;
    public RelayCommand ClearLogsCommand { get; private set; } = null!;
    public RelayCommand CopyLogsCommand { get; private set; } = null!;
    public RelayCommand OpenLogFolderCommand { get; private set; } = null!;
    public RelayCommand FocusVideoCommand { get; private set; } = null!;
    private void InitializePresentation()
    {
        LogView = CollectionViewSource.GetDefaultView(Logs);
        LogView.Filter = item => item is LogEntry entry && (_logFilter switch
        {
            "Erro" => entry.Level == LogLevel.Error,
            "Bluetooth" => entry.Source.StartsWith("BLE/", StringComparison.Ordinal),
            "Input" => entry.Source.Contains("input", StringComparison.OrdinalIgnoreCase) || entry.Source.Contains("capture", StringComparison.OrdinalIgnoreCase),
            "AirPlay" => !entry.Source.StartsWith("BLE/", StringComparison.Ordinal) && entry.Source is not ("UI" or "App"),
            _ => true
        });
        DiagnosticsCommand = new RelayCommand(() => DiagnosticsOpen = true);
        CloseDiagnosticsCommand = new RelayCommand(() => DiagnosticsOpen = false);
        ClearLogsCommand = new RelayCommand(() => _entries.Clear()); // Persisted files remain intact.
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
