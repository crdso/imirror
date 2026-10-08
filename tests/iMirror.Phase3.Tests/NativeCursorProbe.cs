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
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window,int mode);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    private sealed class ForegroundLease(Action release) : IDisposable { public void Dispose() => release(); }
    public static IDisposable ActivateOwnedTestWindow(VideoWindow video,BluetoothControlLog log)
    {
        // Test harness only: temporarily attach to the foreground input queue.
        // The production app receives foreground permission from the user's click.
        uint current=GetCurrentThreadId(), foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _);
        uint target=GetWindowThreadProcessId(video.Handle,out _);
        bool attached=foreground!=0 && foreground!=current && AttachThreadInput(current,foreground,true);
        bool targetAttached=target!=0 && target!=current && target!=foreground && AttachThreadInput(current,target,true);
        void Release() { if(targetAttached) { AttachThreadInput(current,target,false); } if(attached) { AttachThreadInput(current,foreground,false); } }
        try
        {
            ShowWindow(video.Handle,5); BringWindowToTop(video.Handle); bool requested=SetForegroundWindow(video.Handle);
            log.Write("native-test",$"Native foreground test: attached={attached}; targetAttached={targetAttached}; requested={requested}; ownForeground={video.IsForeground}");
            return new ForegroundLease(Release);
        }
        catch { Release(); throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page,Usage; public uint Flags; public nint Window; }
    [DllImport("user32.dll")] private static extern uint GetRegisteredRawInputDevices([Out] Device[]? devices,ref uint count,uint size);
    private static nint MouseRegistration()
    {
        uint count=0; uint size=(uint)Marshal.SizeOf<Device>();
        if(GetRegisteredRawInputDevices(null,ref count,size)==uint.MaxValue) { throw new Exception("Cannot inspect Raw Input registration"); }
        if(count==0) { return 0; }
        var devices=new Device[count]; if(GetRegisteredRawInputDevices(devices,ref count,size)==uint.MaxValue) { throw new Exception("Cannot inspect Raw Input registration"); }
        return devices.FirstOrDefault(d=>d.Page==1 && d.Usage==2).Window;
    }
    private static bool Visible()
    { var info=new CursorInfo { Size=(uint)Marshal.SizeOf<CursorInfo>() }; if(!GetCursorInfo(ref info)) { throw new InvalidOperationException("GetCursorInfo failed"); } return (info.Flags&1)!=0 && info.Cursor!=0; }
    private static async Task Expect(Func<bool> condition,string message)
    { for(int i=0;i<40;i++) { if(condition()) { return; } await Task.Delay(25); } throw new InvalidOperationException(message); }
    public static async Task RunAsync(VideoWindow video,InputCapture capture,BluetoothControlLog log,Func<int> motionCount,Action<Task?> blockRelease)
    {
        GetCursorPos(out var original);
        try
        {
            GetClientRect(video.Handle,out var client); var center=new Point {X=(client.Right-client.Left)/2,Y=(client.Bottom-client.Top)/2}; ClientToScreen(video.Handle,ref center);
            await Task.Delay(100); log.Write("native-test",capture.CursorDiagnostic+"; cursorVisible="+Visible());
            await Expect(()=>!Visible(),"Local cursor did not hide inside the captured viewport");
            GetClipCursor(out var clip); GetCursorPos(out var anchor);
            if(clip.Right-clip.Left!=1 || clip.Bottom-clip.Top!=1 || MouseRegistration()==0) { throw new Exception("Cursor not confined or Raw Input not registered"); }
            int sentBefore=motionCount();
            if(!video.IsForeground) { throw new InvalidOperationException("Cursor surface stole video foreground"); }
            GetWindowRect(video.Handle,out var bounds); SetCursorPos(bounds.Left+15,bounds.Top+10);
            await Task.Delay(150); GetCursorPos(out var constrained);
            if(constrained.X!=anchor.X || constrained.Y!=anchor.Y || motionCount()!=sentBefore || Visible()) { throw new Exception("Escape/warp generated phone movement or exposed local cursor"); }
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); blockRelease(release.Task);
            capture.RequestStop("native cursor stop test");
            await Expect(()=>{GetClipCursor(out var rect);return rect.Right-rect.Left>1 && rect.Bottom-rect.Top>1;},"Cursor remained confined while awaiting BLE release");
            release.SetResult(); await capture.StopAsync(); blockRelease(null);
            if(MouseRegistration()!=0) { throw new Exception("Raw mouse registration survived capture shutdown"); }
            SetCursorPos(bounds.Left+15,bounds.Top+10); GetCursorPos(out var free);
            if(free.X!=bounds.Left+15 || free.Y!=bounds.Top+10) { throw new Exception("Local cursor not free after stop"); }
            // The renderer owns its client-area cursor. Verify the Windows arrow
            // on the PC's normal non-client area, after leaving the video freely.
            await Expect(Visible,"Local cursor did not restore for normal PC use after stop");
            log.Write("native-test","PASS: hidden cursor confined to video; Raw Input registered; no HID motion from cursor warp; immediate unclipping before BLE release; cursor free and Raw Input removed after Stop; fake HID only");
        }
        finally { blockRelease(null); await capture.StopAsync("native cursor cleanup"); SetCursorPos(original.X,original.Y); }
    }
}
