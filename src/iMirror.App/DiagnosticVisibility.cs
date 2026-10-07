using System.Text.RegularExpressions;
using iMirror.Core.Diagnostics;
namespace iMirror.App;

public static class DiagnosticVisibility
{
    public static bool IsVerbose(LogEntry entry)
    {
        bool renderer = entry.Source == "Video renderer";
        if (!renderer && !entry.Source.StartsWith("UxPlay ", StringComparison.Ordinal)) { return false; }
        if (renderer && entry.Level is LogLevel.Warning or LogLevel.Error) { return false; }
        if (Regex.IsMatch(entry.Message, @"\b(ERROR|WARNING|WARN|failed|failure|exception)\b", RegexOptions.IgnoreCase)) { return false; }
        return !Regex.IsMatch(entry.Message, @"accepted.*client|disconnect|mirroring initialized|begin streaming|resolution|videosink|pipeline.*(playing|error)|renderer|teardown", RegexOptions.IgnoreCase);
    }
}
