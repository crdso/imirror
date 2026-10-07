using System.IO;
using System.Windows;
using DrawingIcon = System.Drawing.Icon;
namespace iMirror.App.Presentation;

public sealed class BrandingIcons : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly DrawingIcon _source;
    public DrawingIcon Small { get; }
    public DrawingIcon Big { get; }
    public BrandingIcons(string name)
    {
        using var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/iMirror;component/Assets/Branding/{name}.ico"))!.Stream;
        _stream = new MemoryStream(); resource.CopyTo(_stream); _stream.Position = 0;
        _source = new DrawingIcon(_stream); Small = new DrawingIcon(_source, 16, 16); Big = new DrawingIcon(_source, 256, 256);
    }
    internal (nint Small, nint Big) Apply(nint window)
    {
        WindowNative.SendMessageTimeout(window, 0x80, 0, Small.Handle, 2, 300, out var oldSmall);
        WindowNative.SendMessageTimeout(window, 0x80, 1, Big.Handle, 2, 300, out var oldBig);
        return (oldSmall, oldBig);
    }
    internal static void Restore(nint window, (nint Small, nint Big) previous)
    {
        WindowNative.SendMessageTimeout(window, 0x80, 0, previous.Small, 2, 300, out _);
        WindowNative.SendMessageTimeout(window, 0x80, 1, previous.Big, 2, 300, out _);
    }
    public void Dispose() { Small.Dispose(); Big.Dispose(); _source.Dispose(); _stream.Dispose(); }
}
