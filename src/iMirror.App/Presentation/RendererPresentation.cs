using System.Windows;
using iMirror.Input;
namespace iMirror.App.Presentation;

public sealed class RendererPresentation(Action<string> log) : IDisposable
{
    private readonly BrandingIcons _icons = new("iPhone");
    private readonly VideoResizePolicy _resize = new();
    private VideoWindow? _window;
    private (nint Small, nint Big) _original;
    public bool Observe(int ownPid, int width, int height, bool streaming, bool autoSize)
    {
        var window = streaming ? VideoWindow.Find(ownPid) : null;
        if (_window?.Handle != window?.Handle)
        {
            Restore(); _window = window;
            if (window is not null && WindowNative.IsOwnedGst(window.Handle, ownPid))
            {
                _original = _icons.Apply(window.Handle);
                WindowNative.SetWindowText(window.Handle, "iMirror — iPhone");
                log("Owned Gst HWND found: second branding icon and iMirror — iPhone title applied.");
            }
        }
        if (window is null || !WindowNative.IsOwnedGst(window.Handle, ownPid)) { _resize.ShouldResize(0, 0, 0, false, DateTimeOffset.UtcNow); return false; }
        if (_resize.ShouldResize(window.Handle, width, height, autoSize, DateTimeOffset.UtcNow))
        {
            var work = WindowNative.WorkArea(window.Handle); var dpi = WindowNative.GetDpiForWindow(window.Handle);
            var border = new WindowNative.NativeRect();
            if (!WindowNative.AdjustWindowRectExForDpi(ref border, (uint)WindowNative.GetWindowLongPtr(window.Handle, -16), false, (uint)WindowNative.GetWindowLongPtr(window.Handle, -20), dpi)) { throw new System.ComponentModel.Win32Exception(); }
            int borderWidth = border.Right - border.Left, borderHeight = border.Bottom - border.Top;
            var client = WindowGeometry.VideoClient(work, width, height, borderWidth, borderHeight);
            int outerWidth = client.Width + borderWidth, outerHeight = client.Height + borderHeight;
            WindowNative.Place(window.Handle, new Rect(work.X + (work.Width - outerWidth) / 2, work.Y + (work.Height - outerHeight) / 2, outerWidth, outerHeight));
            log($"Renderer client={client.Width}x{client.Height}; stream={width}x{height}; dpi={dpi}; aspect preserved; resize on dimensions only.");
        }
        return true;
    }
    private void Restore()
    { if (_window is { } window && WindowNative.IsOwnedGst(window.Handle, window.ProcessId)) { BrandingIcons.Restore(window.Handle, _original); } _window = null; }
    public void Dispose() { Restore(); _icons.Dispose(); }
}
