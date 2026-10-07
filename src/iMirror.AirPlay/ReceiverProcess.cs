using System.Diagnostics;
using System.Text;

namespace iMirror.AirPlay;

public sealed record ReceiverProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public interface IReceiverProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    void ReadOutput(Action<string, bool> onLine);
    Task WaitForExitAsync();
    Task DrainOutputAsync();
    Task<bool> RequestStopAsync();
    void KillOwnedTree();
}

public interface IReceiverProcessFactory { IReceiverProcess Start(ReceiverProcessRequest request); }

public sealed class ReceiverProcessFactory : IReceiverProcessFactory
{
    public IReceiverProcess Start(ReceiverProcessRequest request)
    {
        var start = new ProcessStartInfo(request.Executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            // CREATE_NO_WINDOW suppresses only the console. SW_HIDE/STARTF_USESHOWWINDOW
            // also hides GStreamer's first GUI window, so never use Hidden for this process.
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetDirectoryName(request.Executable)!
        };
        foreach (var argument in request.Arguments) { start.ArgumentList.Add(argument); }
        foreach (var pair in request.Environment) { start.Environment[pair.Key] = pair.Value; }
        return new ReceiverProcess(Process.Start(start) ?? throw new IOException("Process.Start returned null."));
    }
}

public sealed class ReceiverProcess(Process process) : IReceiverProcess
{
    private Task _stdout = Task.CompletedTask;
    private Task _stderr = Task.CompletedTask;
    public int Id => process.Id;
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public void ReadOutput(Action<string, bool> onLine)
    {
        _stdout = ReadAsync(process.StandardOutput, false, onLine);
        _stderr = ReadAsync(process.StandardError, true, onLine);
    }
    private static async Task ReadAsync(StreamReader stream, bool error, Action<string, bool> onLine)
    {
        while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
        { onLine(line.Length <= 16384 ? line : line[..16384] + " [truncated]", error); }
    }
    public Task WaitForExitAsync() => process.WaitForExitAsync();
    public Task DrainOutputAsync() => Task.WhenAll(_stdout, _stderr);
    public async Task<bool> RequestStopAsync()
    {
        if (HasExited) { return true; }
        var script = Path.Combine(AppContext.BaseDirectory, "send-ctrl-c.ps1");
        if (!File.Exists(script)) { return false; }
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ReceiverId", Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        { start.ArgumentList.Add(argument); }
        using var helper = Process.Start(start);
        if (helper is null) { return false; }
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (!helper.HasExited) { helper.Kill(entireProcessTree: true); } }
        await Task.WhenAll(output, error).ConfigureAwait(false);
        return helper.HasExited && helper.ExitCode == 0;
    }
    public void KillOwnedTree() { if (!HasExited) { process.Kill(entireProcessTree: true); } }
    public void Dispose() => process.Dispose();
}
