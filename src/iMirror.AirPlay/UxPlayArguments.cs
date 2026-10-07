using System.Globalization;

namespace iMirror.AirPlay;

public static class UxPlayArguments
{
    public static IReadOnlyList<string> Build(AirPlayOptions options, string configurationFile)
    {
        options.Validate();
        var arguments = new List<string> { "-rc", configurationFile, "-n", options.ReceiverName, "-nh", "-p",
            options.BasePort.ToString(CultureInfo.InvariantCulture), "-vsync", "no", "-as", "0",
            "-vs", options.VideoSink, "-vd", options.VideoDecoder };
        if (options.EnableH265) { arguments.Add("-h265"); }
        arguments.Add("-d");
        if (!options.DetailedNegotiationLogging) { arguments.Add("1"); }
        return arguments;
    }
}
