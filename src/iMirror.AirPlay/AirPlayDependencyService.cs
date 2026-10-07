using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using iMirror.Core.Diagnostics;

namespace iMirror.AirPlay;

public enum DependencyState { Ready, UxPlayMissing, GStreamerMissing, ServiceDiscoveryMissing, InvalidInstallation }
public sealed record DependencyReport(DependencyState State, string Message, string? UxPlayPath = null,
    string? GStreamerBinPath = null, string? UxPlayVersion = null, string? GStreamerVersion = null,
    IReadOnlyDictionary<string, string>? Environment = null);
public interface IAirPlayDependencyService
{
    Task<DependencyReport> CheckAsync(AirPlayOptions options, CancellationToken cancellationToken);
}

public sealed class AirPlayDependencyService(IDiagnosticLog log, ICommandProbe? probe = null,
    string? searchPath = null, string? programFiles = null) : IAirPlayDependencyService
{
    private readonly ICommandProbe _probe = probe ?? new CommandProbe();
    private readonly string _path = searchPath ?? System.Environment.GetEnvironmentVariable("PATH") ?? "";
    private readonly string _programFiles = programFiles ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);

    public async Task<DependencyReport> CheckAsync(AirPlayOptions options, CancellationToken cancellationToken)
    {
        log.Write(LogLevel.Information, "AirPlay", "Verificando dependências");
        try
        {
            options.Validate();
            if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            { return Fail(DependencyState.InvalidInstallation, "Esta versão do receptor exige Windows x64."); }
            var executable = FindExecutable(options.UxPlayPath, "uxplay.exe");
            if (executable is null) { return Fail(DependencyState.UxPlayMissing, "UxPlay não encontrado. Configure o caminho de uxplay.exe."); }
            if (!IsX64Pe(executable)) { return Fail(DependencyState.InvalidInstallation, "O executável UxPlay deve ser um arquivo Windows x64 válido."); }
            log.Write(LogLevel.Information, "AirPlay", $"UxPlay encontrado: {executable}");
            var uxDirectory = Path.GetDirectoryName(executable)!;
            var inspect = FindExecutable(options.GStreamerBinPath is null ? null : Path.Combine(options.GStreamerBinPath, "gst-inspect-1.0.exe"),
                "gst-inspect-1.0.exe", uxDirectory);
            if (inspect is null) { return Fail(DependencyState.GStreamerMissing, "GStreamer não encontrado. Configure sua pasta bin x64."); }
            var gstDirectory = Path.GetDirectoryName(inspect)!;
            var launch = Path.Combine(gstDirectory, "gst-launch-1.0.exe");
            if (!IsX64Pe(inspect) || !File.Exists(launch) || !IsX64Pe(launch))
            { return Fail(DependencyState.InvalidInstallation, "Ferramentas GStreamer ausentes ou incompatíveis com x64."); }
            var dns = FindExecutable(options.BonjourDirectory is null ? null : Path.Combine(options.BonjourDirectory, "dnssd.dll"),
                "dnssd.dll", uxDirectory, Path.Combine(_programFiles, "Bonjour"), System.Environment.SystemDirectory);
            if (dns is null || !IsX64Pe(dns))
            { return Fail(DependencyState.ServiceDiscoveryMissing, "Bonjour/DNS-SD x64 ausente. Instale o Bonjour antes de iniciar."); }
            Directory.CreateDirectory(options.SessionDirectory);
            var environment = new Dictionary<string, string>
            {
                ["PATH"] = string.Join(Path.PathSeparator, new[] { uxDirectory, gstDirectory, Path.GetDirectoryName(dns)!, _path }),
                ["GST_REGISTRY_1_0"] = Path.Combine(options.SessionDirectory, "gst-registry-x64.bin"),
                ["GST_DEBUG"] = "2"
            };
            var version = await _probe.RunAsync(executable, ["-v"], environment, cancellationToken).ConfigureAwait(false);
            log.Write(LogLevel.Information, "Dependency", version.Output.Trim());
            var uxVersion = Regex.Match(version.Output, @"UxPlay\s+(?:version\s+)?(\d+\.\d+(?:\.\d+)?)").Groups[1].Value;
            if (version.ExitCode != 0 || version.TimedOut || uxVersion != AirPlayOptions.SupportedUxPlayVersion)
            { return Fail(DependencyState.InvalidInstallation, "Use UxPlay 1.73.7 e suas DLLs compatíveis. A verificação de versão falhou."); }
            var gstVersion = await _probe.RunAsync(inspect, ["--version"], environment, cancellationToken).ConfigureAwait(false);
            log.Write(LogLevel.Information, "Dependency", gstVersion.Output.Trim());
            var gstNumber = Regex.Match(gstVersion.Output, @"GStreamer\s+(\d+\.\d+\.\d+)").Groups[1].Value;
            if (gstVersion.ExitCode != 0 || gstVersion.TimedOut || gstNumber.Length == 0)
            { return Fail(DependencyState.GStreamerMissing, "GStreamer não pôde carregar. Confira DLLs e arquitetura."); }
            foreach (var element in new[] { "appsrc", "h264parse", "videoconvert", "rtph264depay", "decodebin", options.VideoDecoder, options.VideoSink })
            {
                var result = await _probe.RunAsync(inspect, [element], environment, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0 || result.TimedOut)
                {
                    log.Write(LogLevel.Error, "Dependency", $"gst-inspect {element}: {result.Output}");
                    return Fail(DependencyState.GStreamerMissing, $"GStreamer precisa do plugin {element}. Consulte a instalação no README.");
                }
            }
            if (options.EnableH265)
            {
                foreach (var element in new[] { "h265parse", "rtph265depay", options.VideoDecoder.Replace("h264", "h265", StringComparison.Ordinal) }.Distinct())
                {
                    var result = await _probe.RunAsync(inspect, [element], environment, cancellationToken).ConfigureAwait(false);
                    if (result.ExitCode != 0 || result.TimedOut)
                    { return Fail(DependencyState.GStreamerMissing, $"O perfil HEVC precisa do plugin {element}."); }
                }
            }
            var service = await _probe.RunAsync(Path.Combine(System.Environment.SystemDirectory, "sc.exe"),
                ["query", "Bonjour Service"], environment, cancellationToken).ConfigureAwait(false);
            log.Write(LogLevel.Information, "Dependency", service.Output.Trim());
            // Numeric state is locale independent (4 = SERVICE_RUNNING); no English text assumption.
            if (service.ExitCode != 0 || !Regex.IsMatch(service.Output, @":\s*4\s+"))
            { return Fail(DependencyState.ServiceDiscoveryMissing, "O serviço Bonjour não está em execução. Inicie ou instale o serviço.") with
                { UxPlayPath = executable, GStreamerBinPath = gstDirectory, UxPlayVersion = uxVersion, GStreamerVersion = gstNumber }; }
            var report = new DependencyReport(DependencyState.Ready, "Dependências prontas", executable,
                gstDirectory, uxVersion, gstNumber, environment);
            log.Write(LogLevel.Information, "AirPlay", $"[Ready] UxPlay {uxVersion}; GStreamer {gstNumber}; Bonjour em execução.");
            return report;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            log.Write(LogLevel.Error, "Dependency", ex.ToString());
            return Fail(DependencyState.InvalidInstallation, "Não foi possível validar a instalação. Veja os detalhes nos logs.");
        }
    }

    private DependencyReport Fail(DependencyState state, string message)
    {
        log.Write(LogLevel.Error, "AirPlay", $"[{state}] {message}");
        return new DependencyReport(state, message);
    }

    private string? FindExecutable(string? configured, string name, params string[] extraDirectories)
    {
        if (!string.IsNullOrWhiteSpace(configured)) { return File.Exists(configured) ? Path.GetFullPath(configured) : null; }
        foreach (var directory in extraDirectories.Concat(_path.Split(Path.PathSeparator)))
        {
            if (string.IsNullOrWhiteSpace(directory)) { continue; }
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
        }
        return null;
    }

    public static bool IsX64Pe(string path)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5A4D) { return false; }
            reader.BaseStream.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > reader.BaseStream.Length - 6) { return false; }
            reader.BaseStream.Position = offset;
            return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
