using System.Text.Json;

namespace iMirror.AirPlay;

public static class AirPlayConfiguration
{
    public static AirPlayOptions Load(string? file)
    {
        if (file is null) { return new(); }
        var options = JsonSerializer.Deserialize<AirPlayOptions>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty AirPlay configuration.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        string? Resolve(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), directory);
        options = options with { UxPlayPath = Resolve(options.UxPlayPath), GStreamerBinPath = Resolve(options.GStreamerBinPath), BonjourDirectory = Resolve(options.BonjourDirectory), SessionDirectory = Resolve(options.SessionDirectory)!, AttemptLogPath = Resolve(options.AttemptLogPath) };
        options.Validate();
        return options;
    }
    public static string? FindConfig()
    {
        var configured = Environment.GetEnvironmentVariable("IMIRROR_AIRPLAY_CONFIG");
        if (!string.IsNullOrWhiteSpace(configured)) { return configured; }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "airplay.json");
            if (File.Exists(path)) { return path; }
        }
        return null;
    }
}
