using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using iMirror.AirPlay;
using iMirror.App;
using iMirror.App.Presentation;
using iMirror.Core.Diagnostics;
using iMirror.Input;
using iMirror.Bluetooth;

internal static class FinalPolishProbe
{
    private static void Require(bool yes, string message) { if (!yes) { throw new InvalidOperationException(message); } }
    public static void CheckGeometryAndBranding()
    {
        foreach (var work in new[] { new Rect(0,0,1366,728), new Rect(0,0,1920,1040), new Rect(-1920,0,1920,1040), new Rect(0,0,800,480) })
        foreach (var dpi in new[] { 96,144,192 })
        {
            var bounds = WindowGeometry.FitMain(work,dpi);
            Require(work.Contains(bounds) && bounds.Width <= work.Width*.85 + 1 && bounds.Height <= work.Height*.85 + 1,"Initial bounds exceed WorkArea.");
            var invalid = WindowGeometry.FitMain(work,dpi,new Rect(99999,99999,5000,5000));
            Require(invalid==bounds,"Offscreen bounds were restored.");
            var oversized = WindowGeometry.FitMain(work,dpi,new Rect(work.X,work.Y,5000,5000));
            Require(work.Contains(oversized),"Saved oversize not clamped.");
            foreach (var stream in new[] { (998,2160), (2160,998) })
            {
                var client = WindowGeometry.VideoClient(work,stream.Item1,stream.Item2,16*dpi/96,39*dpi/96);
                Require(Math.Abs((double)client.Width/client.Height-(double)stream.Item1/stream.Item2)<.012,"Client aspect mismatch.");
                Require(client.Width+16*dpi/96 <= work.Width*.85 && client.Height+39*dpi/96 <= work.Height*.85,"DPI/client decoration overflow.");
            }
        }
        Require(WindowGeometry.FitMain(new Rect(0,0,1366,728),96).Width <= 960,"Notebook window too large.");
        var fullHd = WindowGeometry.FitMain(new Rect(0,0,1920,1040),96);
        Require(fullHd.Width is >=1000 and <=1080 && fullHd.Height is >=650 and <=700,"Full HD initial size incorrect.");
        var resize = new VideoResizePolicy(); var now=DateTimeOffset.UtcNow;
        Require(!resize.ShouldResize(1,998,2160,true,now) && resize.ShouldResize(1,998,2160,true,now.AddSeconds(1)),"Initial debounce failed.");
        Require(!resize.ShouldResize(1,998,2160,true,now.AddSeconds(2)),"Manual resize would be fought.");
        Require(!resize.ShouldResize(1,2160,998,false,now.AddSeconds(3)) && !resize.ShouldResize(1,2160,998,false,now.AddSeconds(4)),"Auto off ignored.");
        Require(!resize.ShouldResize(1,2160,998,true,now.AddSeconds(5)) && resize.ShouldResize(1,2160,998,true,now.AddSeconds(6)),"Auto on ignored.");
        var focus = new FocusModePolicy(); int hides=0,shows=0;
        void Update(bool request,bool stream,bool video,bool recover) => focus.Update(request,stream,video,recover,()=>hides++,()=>shows++);
        Update(true,true,false,true); Update(true,true,true,false); Require(hides==0,"Unsafe focus hide.");
        Update(true,true,true,true); Update(true,true,true,true); Require(hides==1,"Repeated focus hide.");
        Update(true,true,false,true); Require(shows==1 && !focus.Hidden,"Renderer loss did not recover panel.");
        Update(true,true,true,true); Update(false,true,true,true); Require(shows==2,"Manual return failed.");
        foreach (var name in new[] { "iMirror","iPhone" })
        {
            using var stream=Application.GetResourceStream(new Uri($"pack://application:,,,/iMirror;component/Assets/Branding/{name}.ico"))!.Stream;
            using var reader=new BinaryReader(stream); Require(reader.ReadUInt16()==0 && reader.ReadUInt16()==1,"Not an ICO.");
            Require(reader.ReadUInt16()==9,"ICO must have nine resolutions.");
            foreach (var size in new[] {16,20,24,32,40,48,64,128,256})
            { int width=reader.ReadByte(),height=reader.ReadByte(); Require((width==0?256:width)==size && (height==0?256:height)==size,"Wrong icon resolution."); reader.ReadBytes(14); }
            using var icons=new BrandingIcons(name); Require(icons.Small.Handle!=0 && icons.Big.Handle!=0,"HICON creation failed.");
        }
        Require(DiagnosticVisibility.IsVerbose(new(DateTimeOffset.Now,LogLevel.Warning,"UxPlay stderr","gstvideodecoder Guessing PTS")),"Chatty native line leaked into normal UI.");
        Require(!DiagnosticVisibility.IsVerbose(new(DateTimeOffset.Now,LogLevel.Warning,"UxPlay stderr","WARNING: decoder failed")),"Real native warning hidden.");
    }
    public static void CheckRenderer(string directory)
    {
        var options=AirPlayConfiguration.Load(AirPlayConfiguration.FindConfig());
        var start=new ProcessStartInfo(Path.Combine(options.GStreamerBinPath!,"gst-launch-1.0.exe")) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Normal,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach (var arg in new[] {"videotestsrc","is-live=true","!","d3d11videosink"}) { start.ArgumentList.Add(arg); }
        start.Environment["PATH"]=options.GStreamerBinPath+";"+Environment.GetEnvironmentVariable("PATH");
        start.Environment["GST_REGISTRY_1_0"]=Path.Combine(directory,"polish-gst-registry.bin");
        var messages=new List<string>();
        using var process=Process.Start(start)!;
        process.OutputDataReceived+=(_,args)=> { if(args.Data is not null) { lock(messages) { messages.Add(args.Data); } } };
        process.ErrorDataReceived+=(_,args)=> { if(args.Data is not null) { lock(messages) { messages.Add(args.Data); } } };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var shell=new Window { Width=840,Height=520,ShowInTaskbar=false,ShowActivated=false,Left=-10000,Top=-10000 };
        try
        {
            shell.Show(); Pump();
            VideoWindow? video=null;
            Wait(()=> (video=VideoWindow.Find(process.Id)) is not null, 10000);
            Require(video is not null,"Native renderer window missing.");
            using var presentation=new RendererPresentation(text=> { lock(messages) { messages.Add(text); } });
            void Observe(int w,int h,bool auto=true) => presentation.Observe(process.Id,w,h,true,auto);
            Observe(998,2160); Thread.Sleep(350); Observe(998,2160);
            var handle=video!.Handle;
            var title=new StringBuilder(256); GetWindowText(handle,title,title.Capacity); Require(title.ToString()=="iMirror — iPhone","Renderer title missing.");
            Require(IsRedIcon(SendMessage(handle,0x7F,0,0)) && IsRedIcon(SendMessage(handle,0x7F,1,0)),"Renderer must use the second (red) branding icon in both sizes.");
            GetClientRect(handle,out var client); Require(Math.Abs((double)client.Right/client.Bottom-998.0/2160)<.01,"Portrait client aspect wrong.");
            SetWindowPos(handle,0,0,0,320,520,0x16); GetWindowRect(handle,out var manual);
            Observe(998,2160); GetWindowRect(handle,out var still); Require(manual.Right-manual.Left==still.Right-still.Left && manual.Bottom-manual.Top==still.Bottom-still.Top,"User resize overridden.");
            Observe(2160,998); Thread.Sleep(350); Observe(2160,998);
            GetClientRect(handle,out client); Require(Math.Abs((double)client.Right/client.Bottom-2160.0/998)<.01,"Landscape client aspect wrong.");
            GetWindowRect(handle,out var landscape); Observe(998,2160,false); Thread.Sleep(350); Observe(998,2160,false);
            GetWindowRect(handle,out still); Require(landscape.Right==still.Right && landscape.Bottom==still.Bottom,"Disabled auto-size changed native bounds.");
            var focus=new FocusModePolicy();
            focus.Update(true,true,video.IsValid,true,shell.Hide,shell.Show); Require(!shell.IsVisible,"Focus did not hide actual WPF panel.");
            using var log = new FileDiagnosticLog(Path.Combine(directory,"focus-integration"));
            var receiver = new StreamReceiver();
            using var model = new MainViewModel(log,receiver,new NoInputBluetooth(),Dispatcher.CurrentDispatcher,log.FilePath,ownedPid:()=>process.Id);
            var panel = new MainWindow(model) { ShowInTaskbar=false, ShowActivated=false };
            try
            {
                panel.Show(); Pump();
                using var manager = new WindowPresentation(panel,model,new WindowPreferences());
                model.FocusMode = true;
                if (manager.RecoveryAvailable)
                {
                    Wait(()=>!panel.IsVisible,4000);
                    PostMessage(new WindowInteropHelper(panel).Handle,0x312,0x494D,0);
                    Wait(()=>panel.IsVisible && !model.FocusMode,4000);
                    model.FocusMode=true; Wait(()=>!panel.IsVisible,4000);
                    receiver.StopAsync().GetAwaiter().GetResult(); Wait(()=>panel.IsVisible,4000);
                    receiver.Stream(); Wait(()=>!panel.IsVisible,4000);
                    process.CloseMainWindow(); Wait(()=>panel.IsVisible,5000);
                    Console.WriteLine("INFO: Real tray/hotkey registration, WM_HOTKEY return, stream stop and renderer-close recovery passed.");
                }
                else { Thread.Sleep(500); Pump(); Require(panel.IsVisible,"Hotkey conflict hid panel unsafely."); Console.WriteLine("SKIP: Both recovery shortcuts occupied; safe visible-panel fallback verified."); }
            }
            finally { bool closed=false; panel.Closed+=(_,_)=>closed=true; panel.Hide(); panel.Close(); Wait(()=>closed,4000); }
            process.CloseMainWindow(); Wait(()=> !video.IsValid,5000);
            focus.Update(true,true,video.IsValid,true,shell.Hide,shell.Show); Require(shell.IsVisible,"Renderer closure did not restore actual WPF panel.");
            Require(!presentation.Observe(Environment.ProcessId,998,2160,true,true),"Foreign/non-Gst window accepted.");
        }
        finally
        {
            shell.Close();
            if (!process.HasExited) { process.CloseMainWindow(); if(!process.WaitForExit(3000)) { process.Kill(entireProcessTree:true); process.WaitForExit(); } }
            lock(messages) { File.WriteAllLines(Path.Combine(directory,"polish-renderer.log"),messages); }
        }
    }
    private static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    private static bool IsRedIcon(nint handle)
    {
        if (handle==0) { return false; }
        using var icon=System.Drawing.Icon.FromHandle(handle); using var bitmap=icon.ToBitmap();
        long red=0,blue=0; for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) { var pixel=bitmap.GetPixel(x,y); red+=pixel.R; blue+=pixel.B; }
        return red > blue*1.2;
    }
    private sealed class StreamReceiver : IAirPlayReceiver
    {
        public AirPlayStatus Status { get; private set; } = new(AirPlayState.Streaming,"synthetic",Width:998,Height:2160);
        public bool IsRunning => Status.State==AirPlayState.Streaming;
        public event Action<AirPlayStatus>? StatusChanged;
        public void Stream() { Status=new(AirPlayState.Streaming,"synthetic",Width:998,Height:2160); StatusChanged?.Invoke(Status); }
        public Task StartAsync(CancellationToken token=default) { Stream(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token=default) { Status=new(); StatusChanged?.Invoke(Status); return Task.CompletedTask; }
    }
    private sealed class NoInputBluetooth : IBluetoothController
    {
        public BluetoothStatus Status => BluetoothStatus.Stopped;
        public event Action<BluetoothStatus>? StatusChanged;
        public Task ConnectAsync(CancellationToken token=default) { StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SelectHostAsync(string id) => Task.CompletedTask;
        public Task SetAppearanceAsync(ushort? appearance) => Task.CompletedTask;
        public Task ReleaseAsync() => Task.CompletedTask;
        public Task SendMouseAsync(byte buttons,int x,int y,int wheel,CancellationToken token) => throw new InvalidOperationException("No physical input in renderer test.");
        public Task SendKeyboardAsync(byte modifiers,byte[] usages,CancellationToken token) => throw new InvalidOperationException("No physical input in renderer test.");
    }
    private static void Wait(Func<bool> ready,int milliseconds) { var timer=Stopwatch.StartNew(); while(!ready() && timer.ElapsedMilliseconds<milliseconds) { Pump(); Thread.Sleep(50); } Require(ready(),"Native window wait timed out."); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint window,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window,uint message,nint wParam,nint lParam);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window,uint message,nint wParam,nint lParam);
}
