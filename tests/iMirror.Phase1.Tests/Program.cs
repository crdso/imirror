using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using iMirror.AirPlay;
using iMirror.App;
using iMirror.Bluetooth;
using iMirror.Core.Diagnostics;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--airplay-profile-readiness", var root, var configuration, var output])
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { return AirPlayReadinessProbe.Run(root, output, configuration); }
            finally { Application.Current.Shutdown(); }
        }
        if (args is ["--airplay-readiness", var projectRoot, var resultFile])
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { return AirPlayReadinessProbe.Run(projectRoot, resultFile); }
            finally { Application.Current.Shutdown(); }
        }
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Uso: testes <diretório de evidências> <dotnet.exe> <iMirror.dll>");
            return 2;
        }
        Directory.CreateDirectory(args[0]);
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            Test("Logs persistem e mantêm apenas 500 entradas em memória", () => CheckLogHistory(args[0]));
            Test("Falha de caminho de log é visível", () => CheckLogFailure(args[0]));
            Test("Rotação limita tamanho, sessões e preserva logs alheios", () => CheckRotation(args[0]));
            Test("Janela, bindings e botões informam disponibilidade real", () => CheckWindow(args[0]));
            Test("Páginas responsivas, diagnóstico retrátil e filtros", () => CheckPresentation(args[0]));
            Test("Geometria, DPI, debounce, focus e ICO multi-resolução", () => FinalPolishProbe.CheckGeometryAndBranding());
            Test("Renderer nativo: HWND próprio, ícone, aspect e recuperação do painel", () => FinalPolishProbe.CheckRenderer(args[0]));
            Test("UI BLE exige subscriber, restringe ativação e reflete desconexão", () => CheckBluetoothUi(args[0]));
            Test("Cancelar espera é visual durante startup; recuperação exige confirmação", () => CheckStopDuringBluetoothStartup(args[0]));
            Test("Esquecer vínculo exige host HID conhecido e confirmação; preserva provider", () => CheckSafeUnpairUi(args[0]));
            Test("Botão AirPlay diagnostica a instalação real sem bloquear a UI", () => CheckAirPlayButton(args[0]));
            Test("Executável abre e encerra sem erro", () => CheckExecutable(args));
            Console.WriteLine($"PASS: {_passed} grupos de verificação. Evidências: {args[0]}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
        finally { Application.Current.Shutdown(); }
    }

    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static void CheckLogHistory(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory, "history"));
        var notifications = 0;
        log.EntryAdded += _ => Interlocked.Increment(ref notifications);
        Parallel.For(0, 620, i => log.Write(LogLevel.Information, "Test", $"Entrada {i}"));
        Assert(log.Snapshot().Count == 500, "Histórico excede o limite.");
        using (var reader = new StreamReader(new FileStream(log.FilePath, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite)))
        {
            Assert(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 620,
                "Arquivo perdeu entradas.");
        }
        Assert(notifications == 620, "Eventos de log perdidos.");
        log.Dispose();
        log.Dispose();
        try { log.Write(LogLevel.Information, "Test", "Após Dispose"); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("Escrita após Dispose foi aceita.");
    }

    private static void CheckLogFailure(string directory)
    {
        var file = Path.Combine(directory, "not-a-directory.txt");
        File.WriteAllText(file, "Arquivo, não diretório.");
        try { using var log = new FileDiagnosticLog(Path.Combine(file, "logs")); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Falha de persistência foi ocultada.");
    }

    private static void CheckRotation(string directory)
    {
        var folder = Path.Combine(directory, "rotation"); Directory.CreateDirectory(folder);
        var unrelated = Path.Combine(folder, "physical-evidence.txt"); File.WriteAllText(unrelated, "preserve");
        var path = Path.Combine(folder, "bounded.log");
        using (var log = new BoundedLogFile(path, maxBytes: 1024))
        { for (var i = 0; i < 100; i++) { log.WriteLine($"{i}: ação " + new string('ç', 160)); } }
        var files = Directory.GetFiles(folder, "bounded.log*");
        Assert(files.Length == 5 && files.All(file => new FileInfo(file).Length <= 1024), "Log rotation size/count exceeded.");
        Assert(File.ReadAllText(path).Contains("99: ação") && File.ReadAllText(unrelated) == "preserve", "Latest entry lost or unrelated file changed.");
        using (var log = new BoundedLogFile(path, maxBytes: 1024)) { log.WriteLine("reopened"); }
        Assert(File.ReadAllText(path).Contains("reopened"), "Reopening lost new log.");
        var sessions = Path.Combine(folder, "sessions");
        for (var i = 0; i < 9; i++) { using var log = new FileDiagnosticLog(sessions); log.Write(LogLevel.Information, "Test", "session"); }
        Assert(Directory.GetFiles(sessions).Length == 5, "Session logs accumulate indefinitely.");
    }

    private static void CheckPresentation(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory, "presentation"));
        using var model = new MainViewModel(log, new MissingReceiver(), new PhaseOneBluetoothService(log), Dispatcher.CurrentDispatcher, log.FilePath);
        var window = new MainWindow(model) { ShowActivated=false, ShowInTaskbar=false, Left=-10000, Top=-10000, WindowStartupLocation=WindowStartupLocation.Manual };
        try
        {
            window.Show(); Pump();
            Assert(!model.DiagnosticsOpen && !((FrameworkElement)window.FindName("DiagnosticsPanel")).IsVisible, "Logs permanently occupy the screen.");
            using var responsiveModel = new MainViewModel(log, new MissingReceiver(), new PhaseOneBluetoothService(log), Dispatcher.CurrentDispatcher, log.FilePath);
            var responsiveWindow = new MainWindow(responsiveModel);
            var content=(FrameworkElement)responsiveWindow.Content;
            content.DataContext=responsiveModel;
            content.Resources.MergedDictionaries.Add(responsiveWindow.Resources);
            TextElement.SetFontFamily(content,responsiveWindow.FontFamily); TextElement.SetFontSize(content,responsiveWindow.FontSize);
            TextElement.SetForeground(content,responsiveWindow.Foreground);
            responsiveWindow.Content=null; // Never attach this visual to an HWND.
            foreach (var size in new[] { (820,520), (960,600), (1366,768), (1920,1080), (2560,1440) })
            {
                // Detach the visual from the monitor-capped HWND so no native layout clip
                // hides the right side of a viewport larger than this notebook's display.
                for (var page=0; page<5; page++)
                {
                    responsiveModel.SelectedPage=page; Pump();
                    content.Measure(new Size(size.Item1,size.Item2)); content.Arrange(new Rect(0,0,size.Item1,size.Item2)); content.UpdateLayout();
                    Assert(Math.Abs(content.ActualWidth-size.Item1)<1 && Math.Abs(content.ActualHeight-size.Item2)<1, $"Requested viewport {size} became {content.RenderSize}.");
                    if (page==0) { Assert(((Border)responsiveWindow.FindName("VideoPlaceholder")).ActualHeight>=160, "Video area collapsed."); }
                    SaveElementPreview(content, window.Background, Path.Combine(directory, $"ux-{size.Item1}-{page}.png"));
                }
            }
            responsiveWindow.Close(); Pump();
            model.SelectedPage=1; model.ToggleFullscreen(); Pump();
            Assert(!((FrameworkElement)window.FindName("Sidebar")).IsVisible && !((FrameworkElement)window.FindName("Header")).IsVisible && model.SelectedPage==0, "Fullscreen chrome not hidden.");
            model.ToggleFullscreen(); Pump(); Assert(model.SelectedPage==1, "Fullscreen lost selected page.");
            log.Write(LogLevel.Information, "AirPlay", "receiver"); log.Write(LogLevel.Information, "BLE/input", "capture"); log.Write(LogLevel.Error, "UI", "test error"); Pump();
            model.DiagnosticsCommand.Execute(null); Pump(); Assert(((FrameworkElement)window.FindName("DiagnosticsPanel")).IsVisible, "Diagnostics does not open.");
            model.LogFilter="Erro"; Assert(model.LogView.Cast<LogEntry>().All(entry=>entry.Level==LogLevel.Error), "Error filter leaks unrelated entries.");
            model.LogFilter="Input"; Assert(model.LogView.Cast<LogEntry>().Single().Source=="BLE/input", "Input filter incorrect.");
            model.LogFilter="Todos"; Pump();
            var list=(ListBox)window.FindName("LogList"); list.UpdateLayout(); Pump();
            Assert(list.Items.Count==model.Logs.Count && list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem { Content: LogEntry }, "Filtered logs did not render in the diagnostic list.");
            SavePreview(window, Path.Combine(directory,"ux-diagnostics.png"));
            model.ClearLogsCommand.Execute(null);
            using var reader = new StreamReader(new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert(model.Logs.Count==0 && reader.ReadToEnd().Contains("test error"), "Clear deleted persisted evidence.");
            log.Write(LogLevel.Information,"AirPlay","Streaming");
            for (int i=0;i<600;i++) { log.Write(LogLevel.Information,i%2==0 ? "UxPlay stdout" : "Video renderer","GStreamer INFO [videodecoder]: gstvideodecoder.c:3171 Guessing PTS"); }
            Pump(); Assert(model.Logs.Any(entry=>entry.Message=="Streaming") && model.AllLogs.Count==500,"Verbose traffic erased relevant UI events or exceeded bounds.");
            model.LogFilter="Verbose"; Assert(model.LogView.Cast<LogEntry>().Any(entry=>entry.Message.Contains("Guessing PTS")),"Verbose filter hides native detail.");
            model.LogFilter="Todos"; Assert(!model.LogView.Cast<LogEntry>().Any(entry=>entry.Message.Contains("Guessing PTS")),"Normal filter floods native detail.");
            model.CloseDiagnosticsCommand.Execute(null); Pump(); Assert(!((FrameworkElement)window.FindName("DiagnosticsPanel")).IsVisible,"Diagnostics does not close.");
        }
        finally { window.Close(); Pump(); }
    }

    private static void CheckWindow(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory, "window"));
        log.Write(LogLevel.Information, "Test", "Início da validação da janela.");
        using var model = new MainViewModel(log, new MissingReceiver(),
            new PhaseOneBluetoothService(log), Dispatcher.CurrentDispatcher, log.FilePath);
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000
        };
        try
        {
            window.Show();
            Pump();
            Assert(window.Title == "iMirror" && window.IsLoaded, "Janela não carregada.");
            Assert(window.FindName("VideoPlaceholder") is Border { ActualHeight: > 100 }, "Área de vídeo ausente.");
            var airPlay = (Button)window.FindName("AirPlayButton");
            var bluetooth = (Button)window.FindName("BluetoothButton");
            var fullscreen = (Button)window.FindName("FullscreenButton");
            Assert(airPlay.Command == model.AirPlayCommand && bluetooth.Command == model.PairingCommand,
                "Bindings dos botões ausentes.");
            airPlay.Command.Execute(null);
            Pump();
            Assert(model.Notice.Contains("UxPlay") && !model.AirPlay.IsConnected && model.AirPlay.State == AirPlayState.Error, "AirPlay simula conexão.");
            bluetooth.Command.Execute(null);
            while (!model.BluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump();
            Assert(model.Notice.Contains("fase 3") && !model.Bluetooth.IsConnected, "Bluetooth simula conexão.");
            Assert(model.Logs.Count(x => x.Level == LogLevel.Warning) == 1, "Aviso Bluetooth ausente.");

            var source = PresentationSource.FromVisual(window)!;
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Pump();
            Assert(model.IsFullscreen && window.WindowStyle == WindowStyle.None &&
                window.WindowState == WindowState.Maximized, "F11 não ativa fullscreen.");
            Assert((string)fullscreen.Content == "Sair do Fullscreen", "Texto fullscreen não atualizou.");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Pump();
            Assert(!model.IsFullscreen && window.WindowStyle == WindowStyle.SingleBorderWindow &&
                window.WindowState == WindowState.Normal, "ESC não restaura a janela.");
            window.WindowState = WindowState.Maximized;
            model.ToggleFullscreen();
            model.ToggleFullscreen();
            Assert(window.WindowState == WindowState.Maximized, "Estado maximizado anterior perdido.");
            window.WindowState = WindowState.Normal;

            Task.Run(() => log.Write(LogLevel.Information, "Worker", "Log vindo de outra thread.")).GetAwaiter().GetResult();
            Pump();
            Assert(model.Logs.Any(x => x.Source == "Worker"), "Log de outra thread não chegou à UI.");
            var list = (ListBox)window.FindName("LogList");
            Assert(list.Items.Count == model.Logs.Count, "Painel de logs não atualizou.");
            window.Width = 1000;
            window.Height = 740;
            window.UpdateLayout();
            Pump();
            SavePreview(window, Path.Combine(directory, "phase2-window.png"));
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Pump();
            Assert(((Border)window.FindName("VideoPlaceholder")).ActualHeight >= 160,
                "Layout mínimo perdeu área de vídeo.");
            SavePreview(window, Path.Combine(directory, "phase2-window-minimum.png"));
            var count = model.Logs.Count;
            model.Dispose();
            log.Write(LogLevel.Information, "Test", "Depois de desconectar observador.");
            Assert(model.Logs.Count == count, "ViewModel mantém assinatura após Dispose.");
        }
        finally { window.Close(); }
    }

    private static void CheckExecutable(string[] args)
    {
        var logsDirectory = Path.Combine(args[0], "application");
        var start = new ProcessStartInfo(args[1])
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(args[2]);
        start.Environment["IMIRROR_LOG_DIRECTORY"] = logsDirectory;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Processo não iniciado.");
        try
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                process.Refresh();
                if (process.HasExited) { break; }
                if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle == "iMirror") { break; }
                Thread.Sleep(100);
            }
            Assert(!process.HasExited && process.MainWindowHandle != IntPtr.Zero &&
                process.MainWindowTitle == "iMirror", "Executável não criou janela iMirror.");
            Assert(process.CloseMainWindow(), "WM_CLOSE não enviado.");
            Assert(process.WaitForExit(10000), "Aplicativo não encerrou em 10 segundos.");
            Assert(process.ExitCode == 0, $"Aplicativo terminou com código {process.ExitCode}.");
            var file = Directory.GetFiles(logsDirectory, "*.log").Single();
            var text = File.ReadAllText(file);
            Assert(text.Contains("Janela pronta") && text.Contains("Aplicativo encerrado") &&
                !text.Contains("[Error]"), "Logs do ciclo de vida incompletos ou com erro.");
            Assert(File.Exists(Path.Combine(logsDirectory, "bluetooth", "bluetooth-control.log")),
                "Log Bluetooth não respeitou o diretório isolado desta instância de teste.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
        }
    }

    private static void CheckBluetoothUi(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory,"bluetooth-ui"));
        using var controlLog = new BluetoothControlLog(Path.Combine(directory,"bluetooth-ui-control.log"));
        var receiver = new MissingReceiver(); var bluetooth = new FakeBluetooth();
        using var model = new MainViewModel(log,receiver,bluetooth,Dispatcher.CurrentDispatcher,log.FilePath,controlLog,()=>0, _ => true);
        var window = new MainWindow(model) { ShowActivated=false, ShowInTaskbar=false, Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual };
        try
        {
            window.Show(); Pump();
            var control = (Button)window.FindName("ControlButton");
            var connect = (Button)window.FindName("BluetoothButton");
            var stopBluetooth = (Button)window.FindName("StopBluetoothButton");
            Assert(connect.IsEnabled && !stopBluetooth.IsEnabled, "Bluetooth idle actions incorrect.");
            var speed=(Slider)window.FindName("CursorSpeedSlider"); var layouts=(ComboBox)window.FindName("KeyboardLayoutSelector");
            Assert(speed.Minimum==0.25 && speed.Maximum==3 && model.CursorSpeed==1 && layouts.Items.Count==3,"UX defaults or layout choices incorrect.");
            speed.Value=0.25; Pump(); Assert(model.CursorSpeed==0.25,"Speed binding failed.");
            layouts.SelectedItem=iMirror.Input.KeyboardLayoutMode.PortugueseBrazilAbnt2; Pump();
            Assert(model.KeyboardLayoutMode==iMirror.Input.KeyboardLayoutMode.PortugueseBrazilAbnt2,"Layout binding failed.");
            SavePreview(window,Path.Combine(directory,"phase3b-options.png"));
            Assert(!control.IsEnabled && !model.CanControl,"Control allowed without mouse subscriber.");
            model.BluetoothCommand.Execute(null);
            while(!model.BluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump(); Assert(!model.Bluetooth.IsConnected && model.BluetoothButtonText == "Cancelar espera","Advertising mistaken for HID success.");
            model.BluetoothCommand.Execute(null);
            while(!model.BluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump();
            Assert(bluetooth.Connects == 1 && bluetooth.Disconnects == 0 && connect.IsEnabled && stopBluetooth.IsEnabled,"Pairing toggle must remain available without recreating HOGP.");
            model.PairingCommand.Execute(null); Pump();
            Assert(!model.PairingVisible && bluetooth.Stops == 0 && bluetooth.Disconnects == 0 && model.BluetoothButtonText == "Mostrar pareamento", "Visual cancel must preserve provider/advertising.");
            model.PairingCommand.Execute(null); Pump();
            Assert(model.PairingVisible && bluetooth.Connects == 1, "Showing pairing must not start another provider.");
            receiver.Stream(); Pump();
            model.StopBluetoothCommand.Execute(null);
            while(!model.StopBluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump();
            Assert(bluetooth.Stops == 1 && receiver.IsRunning && model.Bluetooth.State == BluetoothState.Stopped && connect.IsEnabled && !stopBluetooth.IsEnabled, "Stop waiting must stop HID, preserve AirPlay and allow deliberate reconnect.");
            model.StopBluetoothCommand.Execute(null);
            while(!model.StopBluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Assert(bluetooth.Stops == 1, "Repeated stop must be harmless.");
            model.AirPlayCommand.Execute(null);
            while(!model.AirPlayCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            model.BluetoothCommand.Execute(null);
            while(!model.BluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            bluetooth.Publish(true,false); Pump();
            Assert(!model.CanControl && model.Bluetooth.KeyboardConnected && !model.Bluetooth.MouseConnected,"Keyboard-only status incorrect.");
            bluetooth.Publish(true,true); Pump();
            Assert(!model.CanControl,"Control allowed without AirPlay stream.");
            receiver.Stream(); Pump();
            Assert(model.CanControl && control.IsEnabled && model.HostDiagnostics.Contains("Report Map: True"),"Live HID/stream state missing from UI.");
            model.ControlCommand.Execute(null);
            while(!model.ControlCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump(); Assert(!model.ControlActive && model.Notice.Contains("janela de vídeo"),"Capture accepted a foreign/missing HWND.");
            model.ReleaseControl(); Assert(bluetooth.Disconnects == 0,"Stop capture destroyed HOGP.");
            model.AirPlayCommand.Execute(null);
            while(!model.AirPlayCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Assert(!receiver.IsRunning && bluetooth.Disconnects == 0 && model.Bluetooth.MouseConnected,"Stop AirPlay destroyed HOGP.");
            bluetooth.Publish(false,false); Pump();
            Assert(!model.CanControl && !control.IsEnabled && !model.Bluetooth.IsConnected,"Disconnected input remains enabled.");
            receiver.Stream(); bluetooth.PublishWindowsOnly(); Pump();
            Assert(!model.CanControl && !control.IsEnabled && !model.Bluetooth.IsConnected && model.WindowsBluetoothStatus.Contains("BLE") && model.PairingGuidance.Contains("vínculo BLE"),"OS BLE link/bond must be visible without enabling HID input.");
            Assert(model.BluetoothSummary.Contains("Sem resposta HID após 30 s"), "Timeout remains visibly waiting instead of explaining missing HID response.");
            model.SelectedPage = 1; Pump();
            SavePreview(window,Path.Combine(directory,"bluetooth-timeout-feedback.png"));
            bluetooth.StopUnconfirmed = true;
            model.StopBluetoothCommand.Execute(null);
            while(!model.StopBluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
            Pump();
            Assert(!connect.IsEnabled && !stopBluetooth.IsEnabled && model.PairingGuidance.Contains("Feche e reabra"), "Unconfirmed native stop must remain visible and block another provider.");
        }
        finally
        {
            var shutdown = model.ShutdownAsync(); while(!shutdown.IsCompleted) { Pump(); Thread.Sleep(10); } shutdown.GetAwaiter().GetResult(); window.Close(); Pump();
            Assert(bluetooth.Disconnects>0,"UI shutdown omitted Bluetooth cleanup.");
        }
    }
    private static void CheckStopDuringBluetoothStartup(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory, "bluetooth-stop-startup"));
        var receiver = new MissingReceiver(); receiver.Stream();
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bluetooth = new FakeBluetooth { HoldStartup = true, StartupCompletion = startup.Task };
        bool approved = false;
        using var model = new MainViewModel(log, receiver, bluetooth, Dispatcher.CurrentDispatcher, log.FilePath, confirmRecovery: _ => approved);
        model.PairingCommand.Execute(null);
        Assert(model.CanStopBluetooth && !model.CanConnectBluetooth, "Stop must be available while native startup is pending.");
        model.PairingCommand.Execute(null);
        Assert(!model.PairingVisible && !model.BluetoothCommand.ExecutionTask.IsCompleted && bluetooth.Stops == 0 && bluetooth.Disconnects == 0, "Visual cancel must not cancel native startup/dispose provider.");
        startup.SetResult();
        var timeout = Stopwatch.StartNew();
        while (!model.BluetoothCommand.ExecutionTask.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(10); }
        Assert(model.BluetoothCommand.ExecutionTask.IsCompleted, "Native startup did not complete after visual cancellation.");
        model.BluetoothCommand.ExecutionTask.GetAwaiter().GetResult(); Pump();
        Assert(bluetooth.Connects == 1 && bluetooth.Stops == 0 && receiver.IsRunning && !model.CanConnectBluetooth && model.CanStopBluetooth,
            "Visual cancellation must leave one live provider and AirPlay running.");
        model.StopBluetoothCommand.Execute(null);
        while(!model.StopBluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
        Assert(bluetooth.Stops == 0, "Unconfirmed recovery must not call the native stop.");
        approved = true;
        bluetooth.Publish(true, true); Pump();
        Assert(model.CanControl, "Connected HID/stream must allow control before stop.");
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bluetooth.StopCompletion = stopGate.Task;
        model.StopBluetoothCommand.Execute(null);
        Assert(!model.CanControl && !model.CanConnectBluetooth && !model.StopBluetoothCommand.CanExecute(null),
            "Native stop in progress must block another activation/connect/stop.");
        stopGate.SetResult();
        while(!model.StopBluetoothCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); }
        Assert(receiver.IsRunning && !model.CanControl, "Stop must leave AirPlay running and input disabled.");
        var shutdown = model.ShutdownAsync();
        while(!shutdown.IsCompleted) { Pump(); Thread.Sleep(10); }
        shutdown.GetAwaiter().GetResult();
    }
    private sealed class FakeBluetooth : IBluetoothController
    {
        public BluetoothStatus Status { get; private set; } = BluetoothStatus.Stopped;
        public int Disconnects, Connects, Stops, Unpairs;
        public bool StopUnconfirmed;
        public bool HoldStartup;
        public Task? StartupCompletion;
        public Task? StopCompletion;
        public event Action<BluetoothStatus>? StatusChanged;
        public void Publish(bool keyboard,bool mouse, bool canUnpair=false)
        {
            Status = new(keyboard||mouse ? BluetoothState.HidConnected : BluetoothState.Disconnected,"test",
                [new("fake","H1","Test host",true,"test",keyboard,mouse,true,true,true,CanUnpair:canUnpair)],"fake",keyboard,mouse);
            StatusChanged?.Invoke(Status);
        }
        public void PublishWindowsOnly()
        {
            Status = new(BluetoothState.WaitingForPairing,"Aguardando iPhone",[], Advertising:true, RadioOn:true, PairingTimedOut:true,
                WindowsObservation:new(true,[new("W1","Test iPhone","BLE",true,true)]));
            StatusChanged?.Invoke(Status);
        }
        public async Task ConnectAsync(CancellationToken token=default)
        {
            Connects++;
            Status=new(HoldStartup ? BluetoothState.Starting : BluetoothState.WaitingForPairing,"Aguardando pareamento",[]); StatusChanged?.Invoke(Status);
            if (HoldStartup) { await (StartupCompletion ?? Task.Delay(Timeout.Infinite, token)).WaitAsync(token); Status = Status with { State = BluetoothState.WaitingForPairing }; StatusChanged?.Invoke(Status); }
        }
        public Task DisconnectAsync() { Disconnects++; Publish(false,false); return Task.CompletedTask; }
        public async Task StopAsync()
        {
            Stops++; if (StopCompletion is not null) { await StopCompletion; }
            Status = BluetoothStatus.Stopped with
            { State = StopUnconfirmed ? BluetoothState.StopUnconfirmed : BluetoothState.Stopped,
              Message = StopUnconfirmed ? "Feche e reabra o iMirror." : "Bluetooth parado." };
            StatusChanged?.Invoke(Status);
        }
        public Task SelectHostAsync(string id)=>Task.CompletedTask;
        public Task<BluetoothUnpairResult> UnpairHostAsync(string id, CancellationToken token=default)
        {
            Assert(id == "fake" && Status.Hosts.Single().CanUnpair, "Unpair targeted an unknown device");
            Unpairs++; return Task.FromResult(new BluetoothUnpairResult("Unpaired", true));
        }
        public Task SetAppearanceAsync(ushort? appearance)=>Task.CompletedTask;
        public Task SendMouseAsync(byte buttons,int dx,int dy,int wheel,CancellationToken token)=>throw new InvalidOperationException("Test must not send input");
        public Task SendKeyboardAsync(byte modifiers,byte[] usages,CancellationToken token)=>throw new InvalidOperationException("Test must not send input");
        public Task ReleaseAsync()=>Task.CompletedTask;
    }

    private static void CheckSafeUnpairUi(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory,"unpair-ui"));
        var receiver = new MissingReceiver(); receiver.Stream(); var bluetooth = new FakeBluetooth();
        bool approved=false;
        using var model = new MainViewModel(log,receiver,bluetooth,Dispatcher.CurrentDispatcher,log.FilePath,confirmRecovery: _=>approved);
        void Execute()
        { model.UnpairHostCommand.Execute(null); while(!model.UnpairHostCommand.ExecutionTask.IsCompleted) { Pump(); Thread.Sleep(10); } Pump(); }
        Assert(!model.CanUnpairHost,"Unpair enabled without HID host"); Execute(); Assert(bluetooth.Unpairs==0,"Unknown host unpaired");
        bluetooth.Publish(true,true); Pump(); Assert(!model.CanUnpairHost,"Unpair enabled without exact eligible Windows bond");
        bluetooth.Publish(true,true,canUnpair:true); Pump(); model.SelectedHost=model.Hosts.Single(); Pump();
        Assert(model.CanUnpairHost,"Known paired HID host cannot be recovered"); Execute(); Assert(bluetooth.Unpairs==0,"Unpair ran without confirmation");
        approved=true; Execute();
        Assert(bluetooth.Unpairs==1 && model.Notice.Contains("DeviceUnpairingResult: Unpaired") && bluetooth.Disconnects==0 && bluetooth.Stops==0 && receiver.IsRunning,"Unpair result/lifecycle unsafe");
        var shutdown=model.ShutdownAsync(); while(!shutdown.IsCompleted) { Pump(); Thread.Sleep(10); } shutdown.GetAwaiter().GetResult();
    }

    private static void CheckAirPlayButton(string directory)
    {
        using var log = new FileDiagnosticLog(Path.Combine(directory, "native-ui"));
        var options = AirPlayConfiguration.Load(AirPlayConfiguration.FindConfig()) with { SessionDirectory = Path.Combine(directory, "native-ui-session") };
        var receiver = new UxPlayProcessService(options, log);
        using var model = new MainViewModel(log, receiver, new PhaseOneBluetoothService(log), Dispatcher.CurrentDispatcher, log.FilePath);
        var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show(); Pump();
            model.AirPlayCommand.Execute(null);
            Assert(!model.AirPlayCommand.CanExecute(null), "Async command stayed enabled during start.");
            var timeout = Stopwatch.StartNew();
            while (!model.AirPlayCommand.ExecutionTask.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(90)) { Pump(); Thread.Sleep(20); }
            Assert(model.AirPlayCommand.ExecutionTask.IsCompleted, "Dependency diagnostic did not finish.");
            Pump();
            Assert(model.AirPlay.State is AirPlayState.Error or AirPlayState.WaitingForDevice, "No real dependency/readiness result.");
            Assert(!model.AirPlay.IsConnected && model.VideoMetrics.Contains("FPS: N/A"), "UI claims iPhone metrics without evidence.");
            window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
            SavePreview(window, Path.Combine(directory, "phase2-native-dependencies.png"));
            Console.WriteLine("INFO: UI AirPlay = " + model.AirPlay.Message);
        }
        finally
        {
            var shutdown = model.ShutdownAsync();
            while (!shutdown.IsCompleted) { Pump(); Thread.Sleep(20); }
            shutdown.GetAwaiter().GetResult(); window.Close(); Pump();
            Assert(!receiver.IsRunning, "Window test leaked owned receiver.");
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class MissingReceiver : IAirPlayReceiver
    {
        public AirPlayStatus Status { get; private set; } = new();
        public bool IsRunning => Status.State == AirPlayState.Streaming;
        public event Action<AirPlayStatus>? StatusChanged;
        public Task StartAsync(CancellationToken cancellationToken = default)
        { Status = new(AirPlayState.Error, "UxPlay não encontrado", AirPlayError.Dependencies); StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { Status = new(); StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public void Stream() { Status = new(AirPlayState.Streaming,"test",Width:998,Height:2160); StatusChanged?.Invoke(Status); }
    }

    private static void SavePreview(MainWindow window, string path)
    {
        // VisualBrush ignores the parent's margin transform. Draw the window background explicitly.
        var content = (FrameworkElement)window.Content;
        SaveElementPreview(content,window.Background,path);
    }
    private static void SaveElementPreview(FrameworkElement content, Brush background, string path)
    {
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null,
                new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
    }
}
