using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
namespace iMirror.App.Presentation;

public sealed record SavedBounds(double X, double Y, double Width, double Height)
{
    [JsonIgnore] public Rect? Rect => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0 ? new(X, Y, Width, Height) : null;
}
public sealed record WindowPreferences(SavedBounds? NormalBounds = null, bool AutoSizeVideo = true, bool FocusMode = false)
{
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iMirror", "window-settings.json");
    public static WindowPreferences Load()
    {
        try { return File.Exists(PathName) ? JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(PathName)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var pending = PathName + ".tmp"; File.WriteAllText(pending, JsonSerializer.Serialize(this)); File.Move(pending, PathName, true);
    }
}
