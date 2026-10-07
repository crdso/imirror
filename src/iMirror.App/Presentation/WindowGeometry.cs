using System.Windows;
namespace iMirror.App.Presentation;

public static class WindowGeometry
{
    public static Rect FitMain(Rect work, double dpi, Rect? saved = null)
    {
        var scale = Math.Max(1, dpi) / 96;
        var maxWidth = work.Width * .85; var maxHeight = work.Height * .85;
        double width = Math.Min(maxWidth, Math.Clamp(work.Width * .70, 820 * scale, (work.Width / scale < 1600 ? 960 : 1080) * scale));
        double height = Math.Min(maxHeight, Math.Clamp(work.Height * .80, 520 * scale, (work.Height / scale < 900 ? 620 : 700) * scale));
        if (saved is { } previous && Valid(previous) && previous.IntersectsWith(work))
        {
            width = Math.Min(maxWidth, Math.Max(Math.Min(820 * scale, maxWidth), previous.Width));
            height = Math.Min(maxHeight, Math.Max(Math.Min(520 * scale, maxHeight), previous.Height));
            return new(Math.Clamp(previous.X, work.Left, work.Right - width), Math.Clamp(previous.Y, work.Top, work.Bottom - height), width, height);
        }
        return new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }
    private static bool Valid(Rect rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;
    public static (int Width, int Height) VideoClient(Rect work, int streamWidth, int streamHeight, int borderWidth, int borderHeight)
    {
        if (streamWidth <= 0 || streamHeight <= 0) { throw new ArgumentOutOfRangeException(nameof(streamWidth)); }
        var availableWidth = Math.Max(1, work.Width * .85 - borderWidth);
        var availableHeight = Math.Max(1, work.Height * .85 - borderHeight);
        var scale = Math.Min(availableWidth / streamWidth, availableHeight / streamHeight);
        return (Math.Max(1, (int)Math.Floor(streamWidth * scale)), Math.Max(1, (int)Math.Floor(streamHeight * scale)));
    }
}

// Debounces dimensions only. Never reacts to a manual resize or to every frame.
public sealed class VideoResizePolicy
{
    private (nint Window, int Width, int Height, bool Enabled) _observed;
    private DateTimeOffset _due;
    private bool _pending;
    public bool ShouldResize(nint window, int width, int height, bool enabled, DateTimeOffset now)
    {
        var current = (window, width, height, enabled);
        if (current != _observed) { _observed = current; _due = now.AddMilliseconds(250); _pending = enabled && window != 0 && width > 0 && height > 0; }
        if (!_pending || now < _due) { return false; }
        _pending = false; return true;
    }
}

public sealed class FocusModePolicy
{
    public bool Hidden { get; private set; }
    public void PanelShown() => Hidden = false;
    public void Update(bool requested, bool streaming, bool rendererVisible, bool recoveryAvailable, Action hide, Action show)
    {
        bool shouldHide = requested && streaming && rendererVisible && recoveryAvailable;
        if (shouldHide && !Hidden) { hide(); Hidden = true; }
        else if (!shouldHide && Hidden) { show(); Hidden = false; }
    }
}
