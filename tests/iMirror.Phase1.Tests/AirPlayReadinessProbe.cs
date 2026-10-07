using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using iMirror.AirPlay;
using iMirror.App;
using iMirror.Bluetooth;
using iMirror.Core.Diagnostics;

internal static class AirPlayReadinessProbe
{
    public static int Run(string root, string resultFile, string? configuration = null)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(resultFile))!;
        Directory.CreateDirectory(folder);
        using var fileLog = new FileDiagnosticLog(Path.Combine(folder, "receiver-logs"));
        var log = new SafeLog(fileLog);
        var options = AirPlayConfiguration.Load(configuration ?? Path.Combine(root, "airplay.json"));
        if (options.DetailedNegotiationLogging) { options = options with { AttemptLogPath = Path.Combine(folder, "ios27-local-validation.log") }; }
        var report = Wait(new AirPlayDependencyService(log).CheckAsync(options, default), 90);
        var cycles = new List<Cycle>();
        bool? argumentsAccepted = null;
        object? argumentProbe = null;
        if (report.State == DependencyState.ServiceDiscoveryMissing && report.UxPlayPath is not null)
        {
            var config = Path.Combine(folder, "uxplay-empty.conf");
            File.WriteAllText(config, "# Controlled argument check; no physical iPhone\n");
            var environment = new Dictionary<string, string> { ["PATH"] = options.GStreamerBinPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"), ["GST_DEBUG"] = "2", ["GST_DEBUG_NO_COLOR"] = "1" };
            // Bonjour's missing-service IPC error can take longer than a version/plugin probe.
            var trial = Wait(new CommandProbe(TimeSpan.FromSeconds(40)).RunAsync(report.UxPlayPath, UxPlayArguments.Build(options, config), environment, default), 45);
            argumentsAccepted = !trial.TimedOut && trial.Output.Split('\n').Any(line => UxPlayLogParser.Parse(line).Error == AirPlayError.ServiceDiscovery);
            var errorCode = Regex.Match(trial.Output, @"dnssd_register_\w+ failed with error code (-?\d+)");
            argumentProbe = new { trial.ExitCode, trial.TimedOut,
                DnsSdErrorCode = errorCode.Success ? errorCode.Groups[1].Value : null };
            // A DNS-SD error after startup proves that argument parsing succeeded, not readiness.
        }
        var result = "BLOCKED";
        var reason = report.Message;
        if (report.State == DependencyState.Ready)
        {
            var factory = new RecordingFactory();
            var receiver = new UxPlayProcessService(options, log, factory: factory);
            using var model = new MainViewModel(log, receiver, new PhaseOneBluetoothService(log), Dispatcher.CurrentDispatcher, fileLog.FilePath);
            var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); Pump();
                for (var number = 1; number <= 5; number++)
                {
                    var before = factory.Started.Count;
                    var stdout = log.Stdout; var stderr = log.Stderr;
                    model.AirPlayCommand.Execute(null);
                    model.AirPlayCommand.Execute(null); // Busy command must reject a duplicate click.
                    Wait(model.AirPlayCommand.ExecutionTask, 90); Pump();
                    Require(receiver.IsRunning && model.AirPlay.State == AirPlayState.WaitingForDevice, "Receiver did not reach WaitingForDevice.");
                    var owned = factory.Started.Last();
                    Require(factory.Started.Count == before + 1 && !owned.HasExited, "Duplicate or immediately exited receiver.");
                    Wait(receiver.StartAsync(), 15); // Backend must also reject an independent duplicate start.
                    Require(factory.Started.Count == before + 1, "Backend created duplicate process.");
                    Wait(Task.Delay(1500), 5); Pump();
                    Require(receiver.IsRunning && !owned.HasExited && model.AirPlay.State == AirPlayState.WaitingForDevice, "Receiver failed during wait.");
                    var browse = Browse(options);
                    Require(browse, "Local Bonjour browser did not observe the configured AirPlay name.");
                    model.AirPlayCommand.Execute(null);
                    Wait(model.AirPlayCommand.ExecutionTask, 20); Pump();
                    Require(!receiver.IsRunning && model.AirPlay.State == AirPlayState.Stopped && owned.Exited, "Stop did not clean up receiver.");
                    var noOrphan = true;
                    try { using var child = Process.GetProcessById(owned.Id); noOrphan = child.HasExited; }
                    catch (ArgumentException) { }
                    Require(noOrphan && owned.StdoutLines > 0 && owned.StderrLines > 0 && log.Stdout > stdout &&
                        (options.DetailedNegotiationLogging || log.Stderr > stderr), "Orphan or missing stdout/stderr capture.");
                    cycles.Add(new(number, true, true, true, true, browse, noOrphan, owned.Code));
                    Console.WriteLine($"Cycle {number}/5: PASS (local receiver only)");
                }
                argumentsAccepted = true;
                result = "PASS"; reason = "Five real cycles through the WPF AirPlay command; no physical iPhone tested.";
            }
            catch (Exception exception) { result = "FAIL"; reason = exception.Message; }
            finally
            {
                try { Wait(model.ShutdownAsync(), 30); window.Close(); Pump(); }
                catch (Exception) { result = "FAIL"; reason = "Receiver cleanup failed."; }
            }
        }
        var payload = new { Result = result, Reason = reason, DependencyState = report.State,
            UxPlayVersion = report.UxPlayVersion, GStreamerVersion = report.GStreamerVersion,
            ReceiverName = options.ReceiverName, ArgumentsAccepted = argumentsAccepted, ArgumentProbe = argumentProbe, CyclesPassed = cycles.Count, CyclesRequired = 5,
            Cycles = cycles, PhysicalIPhoneValidation = "PENDING PHYSICAL IPHONE VALIDATION" };
        File.WriteAllText(resultFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        Console.WriteLine($"Native readiness: {result}; {reason}");
        return result == "PASS" ? 0 : 1;
    }
    private sealed record Cycle(int Number, bool Waiting, bool DuplicatePrevented, bool Stdout,
        bool Stderr, bool LocalMdnsBrowse, bool NoOrphan, int? ExitCode);
    private static bool Browse(AirPlayOptions options)
    {
        var executable = Path.Combine(options.GStreamerBinPath!, "dns-sd.exe");
        if (!File.Exists(executable)) { return false; }
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("_airplay._tcp"); start.ArgumentList.Add("local.");
        start.Environment["PATH"] = options.GStreamerBinPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        Wait(Task.Delay(2000), 5);
        if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        Wait(process.WaitForExitAsync(), 10);
        return Wait(output, 10).Split('\n').Any(line => line.TrimEnd().EndsWith(" " + options.ReceiverName, StringComparison.Ordinal)
            && Regex.IsMatch(line, @"\bAdd\b")) && Wait(error, 10).Length == 0;
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidOperationException(reason); } }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Wait(Task task, int seconds)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed.TotalSeconds < seconds) { Pump(); Thread.Sleep(10); }
        if (!task.IsCompleted) { throw new TimeoutException("Readiness operation exceeded its deadline."); }
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task, int seconds) { Wait((Task)task, seconds); return task.GetAwaiter().GetResult(); }
    private sealed class SafeLog(FileDiagnosticLog inner) : IDiagnosticLog
    {
        public int Stdout, Stderr;
        public event Action<LogEntry>? EntryAdded { add => inner.EntryAdded += value; remove => inner.EntryAdded -= value; }
        public IReadOnlyList<LogEntry> Snapshot() => inner.Snapshot();
        public void Write(LogLevel level, string source, string message)
        {
            if (source == "UxPlay stdout") { Interlocked.Increment(ref Stdout); }
            if (source == "UxPlay stderr") { Interlocked.Increment(ref Stderr); }
            // No addresses, interface IDs, hardware IDs, device names or raw environment in readiness logs.
            inner.Write(level, source, source == "AirPlay" ? message : "Diagnostic output received (content omitted).");
        }
    }
    private sealed class RecordingFactory : IReceiverProcessFactory
    {
        public List<RecordingProcess> Started { get; } = [];
        public IReceiverProcess Start(ReceiverProcessRequest request)
        {
            var environment = new Dictionary<string, string>(request.Environment) { ["GST_DEBUG"] = "4", ["GST_DEBUG_NO_COLOR"] = "1" };
            var process = new RecordingProcess(new ReceiverProcessFactory().Start(request with { Environment = environment }));
            Started.Add(process); return process;
        }
    }
    private sealed class RecordingProcess(IReceiverProcess inner) : IReceiverProcess
    {
        public int Id { get; } = inner.Id;
        public bool Exited; public int? Code;
        public int StdoutLines, StderrLines;
        public bool HasExited => inner.HasExited;
        public int ExitCode => inner.ExitCode;
        public void ReadOutput(Action<string, bool> onLine) => inner.ReadOutput((line, error) =>
        { if (error) { Interlocked.Increment(ref StderrLines); } else { Interlocked.Increment(ref StdoutLines); } onLine(line, error); });
        public Task WaitForExitAsync() => inner.WaitForExitAsync();
        public Task DrainOutputAsync() => inner.DrainOutputAsync();
        public Task<bool> RequestStopAsync() => inner.RequestStopAsync();
        public void KillOwnedTree() => inner.KillOwnedTree();
        public void Dispose() { Exited = inner.HasExited; Code = Exited ? inner.ExitCode : null; inner.Dispose(); }
    }
}
