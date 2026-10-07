using System.IO;
using System.Windows;
using iMirror.AirPlay;
using iMirror.Bluetooth;
using iMirror.Core.Diagnostics;

namespace iMirror.App;

public partial class App : Application
{
    private FileDiagnosticLog? _log;
    private MainViewModel? _viewModel;
    private BluetoothControlLog? _bluetoothLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += async (_, args) =>
        {
            args.Handled = true;
            try { if (_viewModel is not null) { await _viewModel.ShutdownAsync(); } }
            catch (Exception ex) { _log?.Write(LogLevel.Error, "App", $"Limpeza após falha: {ex}"); }
            ReportFatalError(args.Exception);
            Shutdown(1);
        };
        try
        {
            var directory = Environment.GetEnvironmentVariable("IMIRROR_LOG_DIRECTORY")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "iMirror", "logs");
            _log = new FileDiagnosticLog(directory);
            _log.Write(LogLevel.Information, "App", $"iMirror iniciado. Fase 2 externa; Windows {Environment.OSVersion.Version}; .NET {Environment.Version}.");
            var options = AirPlayConfiguration.Load(AirPlayConfiguration.FindConfig());
            if (options.DetailedNegotiationLogging)
            { _log.Write(LogLevel.Information, "App", "Perfil UxPlay-iOS27: HEVC habilitado; negociação sanitizada em " + options.AttemptLogPath); }
            var config = AirPlayConfiguration.FindConfig();
            var projectDirectory = config is not null ? new DirectoryInfo(Path.GetDirectoryName(config)!) : null;
            while (projectDirectory is not null && !File.Exists(Path.Combine(projectDirectory.FullName, "iMirror.sln"))) { projectDirectory = projectDirectory.Parent; }
            var controlDirectory = projectDirectory is not null ? Path.Combine(projectDirectory.FullName, "logs") : directory;
            _bluetoothLog = new BluetoothControlLog(Path.Combine(controlDirectory, "bluetooth-control.log"));
            _bluetoothLog.Written += (category, message) =>
            {
                if (category is "notify" or "read") { return; }
                _log.Write(category.Contains("error", StringComparison.Ordinal) ? LogLevel.Error : LogLevel.Information, "BLE/" + category, message);
            };
            var owned = new OwnedReceiverProcessFactory();
            _viewModel = new MainViewModel(_log, new UxPlayProcessService(options, _log, factory: owned),
                new BluetoothController(_bluetoothLog), Dispatcher, options.DetailedNegotiationLogging ? options.AttemptLogPath! : _log.FilePath,
                _bluetoothLog, () => owned.ProcessId);
            MainWindow = new MainWindow(_viewModel);
            MainWindow.Show();
            if (e.Args.Contains("--start-bluetooth", StringComparer.Ordinal))
            {
                _viewModel.SelectedPage = 1;
                Dispatcher.BeginInvoke(new Action(() => _viewModel.BluetoothCommand.Execute(null)));
            }
            if (e.Args.Contains("--start-airplay", StringComparer.Ordinal))
            {
                Dispatcher.BeginInvoke(new Action(() => _viewModel.AirPlayCommand.Execute(null)));
            }
            _log.Write(LogLevel.Information, "App", "Janela pronta. PHASE 3 BLE TRANSPORT: PHYSICALLY VALIDATED; PHASE 3 INPUT: PHYSICALLY VALIDATED; PHASE 3 UX: UPDATED — PENDING USER VALIDATION.");
        }
        catch (Exception ex)
        {
            ReportFatalError(ex);
            Shutdown(1);
        }
    }

    private void ReportFatalError(Exception exception)
    {
        var message = exception.ToString();
        try { _log?.Write(LogLevel.Error, "App", message); }
        catch (Exception logError) { message += $"\nFalha adicional ao gravar log: {logError.Message}"; }
        MessageBox.Show("O aplicativo encontrou uma falha e será fechado. Consulte o arquivo de logs para os detalhes.", "iMirror — falha", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        try
        {
            _log?.Write(LogLevel.Information, "App", "Aplicativo encerrado.");
            _log?.Dispose();
            _bluetoothLog?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "iMirror — falha ao encerrar logs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        base.OnExit(e);
    }
}
