using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace iMirror.AirPlay;

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut = false);
public interface ICommandProbe
{
    Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken);
}

public sealed class CommandProbe(TimeSpan? timeout = null) : ICommandProbe
{
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        // sc.exe has no console here and emits OEM bytes; UxPlay/GStreamer emit UTF-8.
        var encoding = OperatingSystem.IsWindows() && Path.GetFileName(executable).Equals("sc.exe", StringComparison.OrdinalIgnoreCase)
            ? CodePagesEncodingProvider.Instance.GetEncoding((int)GetOEMCP()) ?? Encoding.UTF8 : Encoding.UTF8;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)!,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        foreach (var pair in environment) { start.Environment[pair.Key] = pair.Value; }
        using var process = Process.Start(start) ?? throw new IOException("Não foi possível executar o diagnóstico.");
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new CommandResult(process.ExitCode, await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CommandResult(-1, await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false), true);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (text.Length < 65536) { text.Append(buffer, 0, Math.Min(count, 65536 - text.Length)); }
        }
        return text.ToString();
    }
}
