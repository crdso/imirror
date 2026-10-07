using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BleHidProbe;

internal static class Program
{
    private const string Help = """
        iMirror BLE HID Probe — separado do iMirror; nenhum hook de input
        Pareie pelo iPhone: Ajustes > Bluetooth e toque neste PC.
        Para ver o cursor: Ajustes > Acessibilidade > Toque > AssistiveTouch. Teclado não exige AssistiveTouch.
        O nome anunciado é o nome Bluetooth do PC (não 'iMirror - Windows').
        Abra Notas e um campo vazio para teclado; use texto de teste sem dados pessoais.

          status                advertising, subscribers, conexão e bonding
          move dx dy            deltas relativos, -32767..32767
          click left|right      botão, seguido de release
          scroll n              wheel -127..127, seguido de report neutro
          key <usage>           decimal ou hex 0x04 ('a'), 0x28 (Enter), 0xE1 (Shift)
          type <texto simples>  1..120 caracteres ASCII, layout US; release por tecla
          release               soltar teclado e mouse
          mark <teste> pass|fail observação física: pairing/mouse/click/wheel/keyboard/reconnection
          help                  mostrar comandos
          exit                  release + StopAdvertising + encerrar

        Notify Success não comprova que o iPhone executou a ação. Confirme visualmente antes de mark.
        Para reconnection: desligue/ligue Bluetooth no iPhone e repita os comandos;
        teste também exit + reabrir, mantendo o bond, e registre o resultado.
        Ctrl+C encerra com cleanup. Não feche pelo Gerenciador de Tarefas.
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.SequenceEqual(new[] { "--self-test" })) { return await SelfTests.RunAsync(); }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        { Console.Error.WriteLine("Windows 10 build 19041+ necessário."); return 1; }
        string logPath = Path.GetFullPath("logs/ble-hid-probe.log");
        int duration = 0;
        int waitBluetooth = 0;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--log" && index + 1 < args.Length) { logPath = Path.GetFullPath(args[++index]); }
            else if (args[index] == "--duration" && index + 1 < args.Length && int.TryParse(args[++index], out int seconds) && seconds is >= 1 and <= 120)
            { duration = seconds; }
            else if (args[index] == "--wait-bluetooth" && index + 1 < args.Length && int.TryParse(args[++index], out int waitSeconds) && waitSeconds is >= 1 and <= 1800)
            { waitBluetooth = waitSeconds; }
            else { Console.Error.WriteLine("Uso: [--log arquivo] [--duration 1..120] [--wait-bluetooth 1..1800] ou --self-test"); return 1; }
        }

        using var instance = new Semaphore(1, 1, "Local\\iMirror.BleHidProbe.Instance");
        if (!instance.WaitOne(0)) { Console.Error.WriteLine("Já existe um probe aberto. Use exit no anterior."); return 1; }
        using var log = new ProbeLog(logPath, echo: true);
        using var stop = new CancellationTokenSource();
        var peripheral = new HidPeripheral(log);
        var input = new ManualInput(peripheral);
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pairing"] = "PENDING", ["mouse"] = "PENDING", ["click"] = "PENDING",
            ["wheel"] = "PENDING", ["keyboard"] = "PENDING", ["reconnection"] = "PENDING"
        };
        ConsoleCancelEventHandler cancelHandler = (_, evt) => { evt.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        ConsoleControlHandler closing = signal =>
        {
            if (signal is not (2 or 5 or 6)) { return false; }
            stop.Cancel();
            // Closing the console gives only a small OS grace period. Forced kill cannot guarantee release.
            try { peripheral.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4)); } catch { }
            return true;
        };
        bool closeRegistered = SetConsoleCtrlHandler(closing, true);
        int exitCode = 0;
        try
        {
            log.Write("session", $"START pid={Environment.ProcessId}; independent probe; console-close handler={closeRegistered}; log={logPath}");
            log.Write("physical", "pairing/mouse/click/wheel/keyboard/reconnection=PENDING; operator confirmation required");
            await peripheral.StartAsync(stop.Token, waitBluetooth);
            if (duration > 0)
            {
                log.Write("local-test", $"Advertising hold {duration}s; no manual input; no physical acceptance claimed");
                await Task.Delay(TimeSpan.FromSeconds(duration), stop.Token);
            }
            else
            {
                Console.WriteLine(Help);
                while (!stop.IsCancellationRequested)
                {
                    Console.Write("hid> ");
                    string? line = await Console.In.ReadLineAsync().WaitAsync(stop.Token);
                    if (line is null) { break; }
                    string[] parts = line.TrimStart().Split(' ', 2);
                    string command = parts[0].ToLowerInvariant();
                    string argument = parts.Length == 2 ? parts[1] : "";
                    if (command is "exit" or "quit") { break; }
                    try
                    {
                        switch (command)
                        {
                            case "": break;
                            case "help": Console.WriteLine(Help); break;
                            case "status": peripheral.PrintStatus(); break;
                            case "release": await peripheral.ReleaseAsync(); break;
                            case "move":
                            {
                                string[] xy = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (xy.Length != 2) { throw new ArgumentException("Uso: move dx dy"); }
                                byte[] report = HidSchema.Mouse(Number(xy[0]), Number(xy[1]));
                                log.Write("command", "move (relative); content omitted");
                                await input.PulseAsync(HidSchema.MouseId, report, stop.Token);
                                break;
                            }
                            case "click":
                                byte button = argument.Trim().ToLowerInvariant() switch { "left" => 1, "right" => 2, _ => throw new ArgumentException("Uso: click left|right") };
                                log.Write("command", $"click {(button == 1 ? "left" : "right")}");
                                await input.PulseAsync(HidSchema.MouseId, HidSchema.Mouse(0, 0, buttons: button), stop.Token);
                                break;
                            case "scroll":
                                byte[] wheel = HidSchema.Mouse(0, 0, Number(argument.Trim()));
                                log.Write("command", "scroll (wheel); content omitted");
                                await input.PulseAsync(HidSchema.MouseId, wheel, stop.Token);
                                break;
                            case "key":
                                int usage = Number(argument.Trim());
                                if (usage is < 0 or > 255) { throw new ArgumentException("Usage fora do intervalo."); }
                                byte[] key = HidSchema.Keyboard((byte)usage);
                                log.Write("command", "key; usage/payload omitted");
                                await input.PulseAsync(HidSchema.KeyboardId, key, stop.Token);
                                break;
                            case "type":
                                // Do not print/log the text or reports in application diagnostics.
                                log.Write("command", $"type; characters={argument.Length}; content omitted; US layout");
                                await input.TypeAsync(argument, stop.Token);
                                break;
                            case "mark":
                                string[] verdict = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (verdict.Length != 2 || !results.ContainsKey(verdict[0]) || verdict[1].ToLowerInvariant() is not ("pass" or "fail"))
                                { throw new ArgumentException("Uso: mark pairing|mouse|click|wheel|keyboard|reconnection pass|fail"); }
                                results[verdict[0]] = verdict[1].ToUpperInvariant();
                                log.Write("physical-operator", $"{verdict[0].ToLowerInvariant()}={results[verdict[0]]}; source=manual observation, not inferred from NotifyValueAsync");
                                break;
                            default: Console.WriteLine("Comando desconhecido. Use help."); break;
                        }
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        log.Error("command-error", error);
                        if (error is InputBlockedException or ArgumentException or FormatException or OverflowException)
                        { Console.WriteLine(error is ArgumentOutOfRangeException ? "Valor fora do intervalo; use help." : error.Message); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { log.Write("session", "Cancellation requested"); }
        catch (Exception error)
        {
            exitCode = 1;
            log.Error("fatal", error);
            if (error is InputBlockedException) { Console.Error.WriteLine(error.Message); log.Write("blocked", error.Message); }
            if (error is TimeoutException) { log.Write("blocked", "Advertising did not reach Started within 15s; inspect AdvertisingStatus/Error events"); }
        }
        finally
        {
            await peripheral.DisposeAsync();
            Console.CancelKeyPress -= cancelHandler;
            if (closeRegistered) { SetConsoleCtrlHandler(closing, false); }
            GC.KeepAlive(closing);
            log.Write("physical-summary", string.Join("; ", results.Select(result => $"{result.Key}={result.Value}")));
            log.Write("result", results.Values.All(result => result == "PASS")
                ? "PHYSICAL TESTS OPERATOR-REPORTED PASS; WPF integration still not performed"
                : "PHASE 3A: PENDING PHYSICAL HID VALIDATION; WPF integration not performed");
            log.Write("session", $"END exitCode={exitCode}");
            instance.Release();
        }
        return exitCode;
    }

    private static int Number(string value) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
        : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private delegate bool ConsoleControlHandler(uint signal);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleControlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
