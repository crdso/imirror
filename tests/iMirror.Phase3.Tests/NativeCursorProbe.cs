using System.Runtime.InteropServices;
using iMirror.Input;
using iMirror.Bluetooth;

internal static class NativeCursorProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public uint Size,Flags; public nint Cursor; public Point Position; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    private static bool Visible()
    { var info=new CursorInfo { Size=(uint)Marshal.SizeOf<CursorInfo>() }; if(!GetCursorInfo(ref info)) { throw new InvalidOperationException("GetCursorInfo failed"); } return (info.Flags&1)!=0 && info.Cursor!=0; }
    private static async Task Expect(Func<bool> condition,string message)
    { for(int i=0;i<40;i++) { if(condition()) { return; } await Task.Delay(25); } throw new InvalidOperationException(message); }
    public static async Task RunAsync(VideoWindow video,InputCapture capture,BluetoothControlLog log)
    {
        GetCursorPos(out var original);
        try
        {
            GetClientRect(video.Handle,out var client); var center=new Point {X=(client.Right-client.Left)/2,Y=(client.Bottom-client.Top)/2}; ClientToScreen(video.Handle,ref center);
            SetCursorPos(center.X,center.Y); await Task.Delay(60); SetCursorPos(center.X+1,center.Y);
            await Task.Delay(100); log.Write("native-test",capture.CursorDiagnostic+"; cursorVisible="+Visible());
            await Expect(()=>!Visible(),"Local cursor did not hide inside the captured viewport");
            if(!video.IsForeground) { throw new InvalidOperationException("Cursor surface stole video foreground"); }
            GetWindowRect(video.Handle,out var bounds); SetCursorPos(bounds.Left+15,bounds.Top+10);
            await Expect(Visible,"Local cursor did not restore outside viewport");
            SetCursorPos(center.X,center.Y); await Task.Delay(60); SetCursorPos(center.X+1,center.Y);
            await Expect(()=>!Visible(),"Cursor did not hide after reentry");
            await capture.StopAsync("native cursor stop test");
            await Expect(Visible,"Local cursor did not restore after stop");
            log.Write("native-test","PASS: real Win32 cursor invisible inside viewport; visible outside; hides on reentry; visible after awaited Stop; video foreground preserved; fake HID only");
        }
        finally { await capture.StopAsync("native cursor cleanup"); SetCursorPos(original.X,original.Y); }
    }
}
