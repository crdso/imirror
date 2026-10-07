using System.ComponentModel;
using System.Runtime.InteropServices;

namespace iMirror.Input;

// A non-activating cursor surface owned by our hook thread. GSTD3D11's
// cross-process class rejects SetClassLongPtr (ACCESS_DENIED). WM_SETCURSOR
// runs on our own window: no ShowCursor counter or system-wide cursor change.
public sealed class VideoCursor : IDisposable
{
    private readonly VideoWindow _video;
    private readonly Func<(int Width,int Height)> _dimensions;
    private readonly Action<Exception>? _onError;
    private readonly CursorNative.WindowProc _procedure;
    private readonly uint _thread;
    private readonly string _className = "iMirror.Cursor." + Guid.NewGuid().ToString("N");
    private readonly nint _instance;
    private nint _handle;
    private int _desired;
    private bool _shown;
    private (int X,int Y,int Width,int Height) _bounds;
    public nint Handle => _handle;
    public bool IsVisible => _handle!=0 && Native.IsWindowVisible(_handle);
    public bool PointerOnSurface { get; private set; }
    public VideoCursor(VideoWindow video, Func<(int Width,int Height)> dimensions, Action<Exception>? onError = null)
    {
        _video = video; _dimensions = dimensions; _onError=onError; _thread = Native.GetCurrentThreadId();
        _instance = Native.GetModuleHandle(null); _procedure = Procedure;
        var definition = new CursorNative.WindowClass { Size=(uint)Marshal.SizeOf<CursorNative.WindowClass>(),
            Procedure=_procedure,Instance=_instance,ClassName=_className,
            Cursor=CursorNative.LoadCursor(0,32512),Background=CursorNative.GetStockObject(4) };
        if (CursorNative.RegisterClassEx(ref definition)==0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        // Alpha 1/255 is necessary for layered-window hit testing. Alpha zero
        // would pass the pointer back to the renderer and its visible cursor.
        _handle = CursorNative.CreateWindowEx(0x08080088,_className,"",0x80000000,0,0,1,1,0,0,_instance,0);
        if (_handle==0) { CursorNative.UnregisterClass(_className,_instance); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (!CursorNative.SetLayeredWindowAttributes(_handle,0,1,2)) { int error=Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
    }
    public bool Owns(nint root) => root != 0 && root == _handle;
    public void SetHidden(bool hidden)
    {
        Volatile.Write(ref _desired,hidden ? 1 : 0);
        if (_handle==0) { return; }
        if (Native.GetCurrentThreadId()==_thread) { Apply(); }
        else { CursorNative.PostMessage(_handle,0x8001,0,0); }
    }
    private void Apply()
    {
        bool hidden = Volatile.Read(ref _desired)!=0 && _video.IsForeground;
        if (hidden)
        {
            Native.GetCursorPos(out var pointer); var dims=_dimensions();
            var position=_video.Read(pointer.X,pointer.Y,dims.Width,dims.Height);
            hidden = position.Viewport.Contains(position.X,position.Y);
            if (hidden)
            {
                var viewport=position.Viewport;
                var corner=new Native.Point {X=(int)Math.Ceiling(viewport.Left),Y=(int)Math.Ceiling(viewport.Top)};
                Native.ClientToScreen(_video.Handle,ref corner);
                int width=(int)Math.Floor(viewport.Left+viewport.Width)-(int)Math.Ceiling(viewport.Left);
                int height=(int)Math.Floor(viewport.Top+viewport.Height)-(int)Math.Ceiling(viewport.Top);
                if (width<=0 || height<=0) { hidden=false; }
                else
                {
                    var bounds=(corner.X,corner.Y,width,height);
                    if (!_shown || bounds!=_bounds)
                    {
                        if (!CursorNative.SetWindowPos(_handle,-1,corner.X,corner.Y,width,height,0x50))
                        { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                        _shown=true; _bounds=bounds;
                    }
                    PointerOnSurface=Native.WindowFromPoint(pointer)==_handle;
                    if (PointerOnSurface) { CursorNative.SetCursor(0); }
                }
            }
        }
        if (!hidden)
        {
            if (_shown) { CursorNative.SetCursor(CursorNative.LoadCursor(0,32512)); }
            CursorNative.ShowWindow(_handle,0);
            _shown=false;
        }
    }
    private nint Procedure(nint window,uint message,nuint wparam,nint lparam)
    {
        // Never let a managed exception cross the native window-procedure ABI.
        try
        {
            if (message==0x8001) { Apply(); return 0; }
            if (message==0x0F)
            {
                nint context=CursorNative.BeginPaint(window,out var paint);
                Native.GetClientRect(window,out var rect); CursorNative.FillRect(context,ref rect,CursorNative.GetStockObject(4));
                CursorNative.EndPaint(window,ref paint); return 0;
            }
            if (message==0x21) { return 3; } // MA_NOACTIVATE.
            if (message==0x84) { return 1; } // HTCLIENT, viewport rectangle only.
            if (message==0x20 && Volatile.Read(ref _desired)!=0 && _video.IsForeground)
            { CursorNative.SetCursor(0); return 1; }
        }
        catch (Exception error)
        {
            Volatile.Write(ref _desired,0); CursorNative.SetCursor(CursorNative.LoadCursor(0,32512)); CursorNative.ShowWindow(window,0); _shown=false;
            try { _onError?.Invoke(error); } catch { /* Never cross the native ABI. */ }
        }
        return CursorNative.DefWindowProc(window,message,wparam,lparam);
    }
    public void Dispose()
    {
        SetHidden(false);
        if (_handle!=0) { CursorNative.DestroyWindow(_handle); _handle=0; }
        CursorNative.UnregisterClass(_className,_instance);
        GC.KeepAlive(_procedure);
    }
}

internal static class CursorNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Paint
    { public nint Context; public int Erase; public Native.Rect Rect; public int Restore,Update; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Reserved; }
    internal delegate nint WindowProc(nint window,uint message,nuint wparam,nint lparam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct WindowClass
    {
        public uint Size,Style; public WindowProc Procedure; public int ClassExtra,WindowExtra;
        public nint Instance,Icon,Cursor,Background; public string? MenuName; public string ClassName; public nint SmallIcon;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool UnregisterClass(string name,nint instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern nint CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetLayeredWindowAttributes(nint window,uint color,byte alpha,uint flags);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint DefWindowProc(nint window,uint message,nuint wparam,nint lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint LoadCursor(nint instance,nint name);
    [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window,uint message,nuint wparam,nint lparam);
    [DllImport("gdi32.dll")] internal static extern nint GetStockObject(int objectId);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint window,out Paint paint);
    [DllImport("user32.dll")] internal static extern bool EndPaint(nint window,ref Paint paint);
    [DllImport("user32.dll")] internal static extern int FillRect(nint context,ref Native.Rect rect,nint brush);
}
