using System.Buffers.Binary;
using iMirror.Bluetooth;
using iMirror.Input;
using iMirror.AirPlay;

internal static class Program
{
    private static int _passed;
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("Assertion failed"); } }
    private static async Task Test(string name, Func<Task> action) { await action(); _passed++; Console.WriteLine("PASS: " + name); }
    private static Task Check(Action action) { action(); return Task.CompletedTask; }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--native")) { return await NativeTest(); }
            await Test("Report Map byte exact upstream 7d66ef1", () => Check(() => Require(HidSchema.ReportMap.SequenceEqual(BleHid.Core.HidDescriptors.ReportMapValue))));
            await Test("Keyboard reserved byte and six usages without report ID", () => Check(() => Require(HidSchema.KeyboardState(2, [4,5]).SequenceEqual(new byte[] {2,0,4,5,0,0,0,0}))));
            await Test("Keyboard rollover and deduplication", () => Check(() => { Require(HidSchema.KeyboardState(0, [4,4]).Count(value => value == 4) == 1); Require(HidSchema.KeyboardState(0, [4,5,6,7,8,9,10]).Skip(2).All(value => value == 1)); }));
            await Test("Mouse 16-bit relative axes and signed wheel", () => Check(() => { var data = HidSchema.Mouse(-30000,30000,-5,2); Require(data.Length == 6 && data[0] == 2 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(1)) == -30000 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(3)) == 30000 && (sbyte)data[5] == -5); }));
            await Test("Clamp both axes wheel and button mask", () => Check(() => { var data = HidSchema.ClampedMouse(255,int.MaxValue,int.MinValue,999); Require(data[0] == 7 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(1)) == 32767 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(3)) == -32767 && data[5] == 127); }));
            await Test("Release both formats", () => Check(() => Require(HidSchema.Neutral(1).Length == 8 && HidSchema.Neutral(2).Length == 6 && HidSchema.Neutral(1).All(value => value == 0) && HidSchema.Neutral(2).All(value => value == 0))));
            await Test("Left/right modifiers distinct", () => Check(() => { var keys = new KeyboardState(); keys.Update(0xA0,true); keys.Update(0xA3,true); keys.Update(0x41,true); Require(keys.Modifiers == 18 && keys.Usages.SequenceEqual(new byte[] {4})); keys.Update(0xA0,false); Require(keys.Modifiers == 16); keys.Reset(); Require(keys.Modifiers == 0 && keys.Usages.Length == 0); }));
            await Test("Keyboard arrows editing numbers and ESC local", () => Check(() => Require(VirtualKeyMap.Usage(0x1B) == 0 && VirtualKeyMap.Usage(0x25) == 0x50 && VirtualKeyMap.Usage(0x08) == 0x2A && VirtualKeyMap.Usage(0x0D) == 0x28 && VirtualKeyMap.Usage(0x30) == 0x27 && VirtualKeyMap.Usage(0x5A) == 29)));
            await Test("Repeated down has one state transition", () => Check(() => { var keys = new KeyboardState(); Require(keys.Update(0x41,true) && !keys.Update(0x41,true) && keys.Update(0x41,false)); }));
            await Test("Portrait pillarbox mapping", () => Check(() => { var viewport = VideoViewport.Fit(1000,800,998,2160); var center = viewport.ToStream(500,400)!.Value; Require(Math.Abs(center.X-499)<0.001 && Math.Abs(center.Y-1080)<0.001 && !viewport.Contains(100,400)); }));
            await Test("Landscape top/bottom bars and edges", () => Check(() => { var viewport = VideoViewport.Fit(800,1000,2160,998); Require(!viewport.Contains(400,10) && viewport.Contains(400,500) && !viewport.Contains(viewport.Left+viewport.Width,500)); }));
            await Test("DPI physical mapping", () => Check(() => { var viewport = VideoViewport.Fit(VideoViewport.Physical(800,144),VideoViewport.Physical(600,144),1200,900); Require(viewport.Scale == 1 && viewport.ToStream(600,450) == (600.0,450.0)); }));
            await Test("Invalid dimensions and resize/orientation no double rotation", () => Check(() => { Require(!VideoViewport.Fit(0,500,998,2160).Contains(0,0)); Require(VideoViewport.Fit(400,800,100,200).Scale == 4 && VideoViewport.Fit(800,400,200,100).Scale == 4); }));
            await Test("Motion coalesces bounded, click and release remain ordered", () => Check(() => { var buffer = new InputReportBuffer(); for(int i=0;i<10000;i++) { Require(buffer.Enqueue(InputReport.Mouse(0,10,-10),true)); } Require(buffer.Count==1); buffer.Enqueue(InputReport.Mouse(1)); buffer.Enqueue(InputReport.Mouse(1,5,5),true); buffer.Enqueue(InputReport.Mouse(0)); Require(buffer.Count==4 && buffer.Take()!.X==32767 && buffer.Take()!.Buttons==1 && buffer.Take()!.X==5 && buffer.Take()!.Buttons==0); }));
            await Test("Discrete queue overflow blocks without dropping release", () => Check(() => { var buffer = new InputReportBuffer(2); Require(buffer.Enqueue(InputReport.Mouse(1)) && buffer.Enqueue(InputReport.Mouse(0)) && !buffer.Enqueue(InputReport.Key(0,[4])) && buffer.Count==2); buffer.Clear(); Require(buffer.Count==0); }));
            await Test("Wheel partial notch signs intensity", () => Check(() => { var wheel = new WheelAccumulator(); Require(wheel.Add(60,3)==0 && wheel.Add(60,3)==3 && wheel.Add(-240,2)==-4); wheel.Reset(); Require(wheel.Add(120,99)==5); }));
            await Test("Capture excludes foreign window bars inactive and no subscriber", () => Check(() => { Require(CapturePolicy.CanSend(true,true,true,true)); foreach(var values in new[] { (false,true,true,true),(true,false,true,true),(true,true,false,true),(true,true,true,false) }) { Require(!CapturePolicy.CanSend(values.Item1,values.Item2,values.Item3,values.Item4)); } Require(CapturePolicy.Emergency(0x1B,false,false) && CapturePolicy.Emergency(0x51,true,true) && !CapturePolicy.Emergency(0x51,true,false)); }));
            await Test("Mouse and keyboard sender paced, disconnect clears queue + release", async () => { var fake = new Fake(); var buffer = new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(1)); buffer.Enqueue(InputReport.Key(2,[4])); buffer.Enqueue(InputReport.Mouse(0)); int stops=0; using var token = new CancellationTokenSource(2000); using var log = Log(); fake.AfterSend = () => { if (fake.Sent==2) { fake.Live=false; } }; await InputSender.RunAsync(buffer,fake,()=>true,_=>stops++,log,token.Token); Require(fake.Sent==2 && fake.Release==1 && stops==1 && buffer.Count==0 && fake.MinGap>=10); });
            await Test("No subscriber blocks all queued input and releases", async () => { var fake = new Fake { Live=false }; var buffer = new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(1)); using var log = Log(); await InputSender.RunAsync(buffer,fake,()=>true,_=>{},log,CancellationToken.None); Require(fake.Sent==0 && fake.Release==1 && buffer.Count==0); });
            await Test("Notify exception stops and neutralizes", async () => { var fake = new Fake { Fail=true }; var buffer = new InputReportBuffer(); buffer.Enqueue(InputReport.Key(2,[4])); int stops=0; using var log = Log(); await InputSender.RunAsync(buffer,fake,()=>true,_=>stops++,log,CancellationToken.None); Require(fake.Sent==1 && fake.Release==1 && stops==1 && buffer.Count==0); });
            await Test("Focus loss never sends and neutralizes", async () => { var fake = new Fake(); var buffer = new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(1)); using var log = Log(); await InputSender.RunAsync(buffer,fake,()=>false,_=>{},log,CancellationToken.None); Require(fake.Sent==0 && fake.Release==1); });
            await Test("Cancellation cannot suppress release", async () => { var fake = new Fake(); using var token = new CancellationTokenSource(); token.Cancel(); using var log = Log(); await InputSender.RunAsync(new(),fake,()=>true,_=>{},log,token.Token); Require(fake.Release==1 && fake.Sent==0); });
            await Test("Inactive capture idempotent stop and no-subscriber start rejects", async () => { var fake = new Fake { Live=false }; using var log = Log(); var capture = new InputCapture(fake,log); await capture.StopAsync(); await capture.StopAsync(); try { await capture.StartAsync(()=>0,()=>(998,2160),true,1); throw new Exception("Expected block"); } catch(InputBlockedException) { Require(!capture.IsActive); } });
            Console.WriteLine($"PHASE 3 TESTS: {_passed}/{_passed}; physical validation pending"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static BluetoothControlLog Log() => new(Path.Combine("logs","phase3-automated-tests.log"));
    private sealed class Fake : IBluetoothController
    {
        public bool Live=true, Fail; public int Sent,Release; public double MinGap=double.MaxValue; private DateTimeOffset _last;
        public Action? AfterSend;
        public BluetoothStatus Status => new(Live ? BluetoothState.HidConnected : BluetoothState.Disconnected,"test",[],"fake",Live,Live);
        public event Action<BluetoothStatus>? StatusChanged;
        public Task ConnectAsync(CancellationToken token=default) { StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task DisconnectAsync() { Live=false; StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task SelectHostAsync(string id)=>Task.CompletedTask;
        public Task SetAppearanceAsync(ushort? appearance)=>Task.CompletedTask;
        private Task Send() { var now=DateTimeOffset.UtcNow; if(Sent!=0) { MinGap=Math.Min(MinGap,(now-_last).TotalMilliseconds); } _last=now; Sent++; AfterSend?.Invoke(); if(Fail) { throw new IOException("simulated notify failure"); } return Task.CompletedTask; }
        public Task SendMouseAsync(byte buttons,int dx,int dy,int wheel,CancellationToken token)=>Send();
        public Task SendKeyboardAsync(byte modifiers,byte[] usages,CancellationToken token)=>Send();
        public Task ReleaseAsync() { Release++; return Task.CompletedTask; }
    }
    private static async Task<int> NativeTest()
    {
        using var log = new BluetoothControlLog(Path.Combine("logs","phase3-native-validation.log"),echo:true);
        await using var controller = new BluetoothController(log);
        for(int cycle=1;cycle<=2;cycle++)
        {
            await controller.ConnectAsync();
            if(controller.Status.State is BluetoothState.Error or BluetoothState.RadioOff) { return 1; }
            if(controller.Status.IsConnected) { log.Write("native-test","Physical host present; no synthetic input will be sent"); }
            else
            {
                try { await controller.SendMouseAsync(1,1,1,0,CancellationToken.None); throw new Exception("Unsubscribed input accepted"); }
                catch(InputBlockedException) { log.Write("native-test","No-subscriber input blocked as expected"); }
            }
            if(cycle==1)
            {
                foreach(ushort value in new ushort[] {0x03C1,0x03C2})
                { try { await controller.SetAppearanceAsync(value); await Task.Delay(1500); await controller.SetAppearanceAsync(null); } catch(Exception error) { log.Error("optional-appearance-unavailable",error); } }
            }
            await Task.Delay(1500); await controller.DisconnectAsync();
            log.Write("native-test",$"Cycle {cycle}/2 start/stop finished; physical validation pending");
        }
        await NativeCaptureTest(log);
        return 0;
    }
    private static async Task NativeCaptureTest(BluetoothControlLog log)
    {
        var options = AirPlayConfiguration.Load(Path.GetFullPath("airplay.json"));
        var environment = new Dictionary<string,string> { ["PATH"] = options.GStreamerBinPath + ";" + Environment.GetEnvironmentVariable("PATH") };
        var request = new ReceiverProcessRequest(Path.Combine(options.GStreamerBinPath!,"gst-launch-1.0.exe"),
            ["videotestsrc","is-live=true","!","video/x-raw,width=320,height=640","!","videoconvert","!","d3d11videosink"],environment);
        using var process = new ReceiverProcessFactory().Start(request);
        process.ReadOutput((line,error)=>log.Write(error ? "gst-test-stderr" : "gst-test-stdout",line));
        var fake = new Fake(); var capture = new InputCapture(fake,log);
        try
        {
            for(int attempt=0;attempt<100 && VideoWindow.Find(process.Id) is null;attempt++) { await Task.Delay(50); }
            log.Write("native-test", $"Synthetic Gst exited={process.HasExited}; windows={VideoWindowInspection.Read(process.Id)}; matchingVideo={VideoWindow.Find(process.Id) is not null}");
            Require(VideoWindow.Find(process.Id) is not null && VideoWindow.Find(0) is null && VideoWindow.Find(Environment.ProcessId) is null);
            try { await capture.StartAsync(()=>process.HasExited ? 0 : process.Id,()=>(320,640),true,1); }
            catch(InputBlockedException error) when(error.Message.Contains("Selecione a janela",StringComparison.Ordinal))
            {
                Require(!capture.IsActive && fake.Sent==0 && fake.Release==1);
                log.Write("native-test","Foreground transfer unavailable to background test process; verified fail-open and no hooks/input. SKIP real hook activation: requires foreground user activation in iMirror, still pending physical validation.");
                return;
            }
            Require(capture.IsActive);
            fake.Live=false;
            await capture.Completion.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!capture.IsActive && fake.Release>=2 && fake.Sent==0);
            fake.Live=true;
            await capture.StartAsync(()=>process.Id,()=>(640,320),false,2);
            Require(capture.IsActive);
            await capture.StopAsync("native smoke stop"); await capture.StopAsync();
            Require(!capture.IsActive && fake.Release>=4 && fake.Sent==0);
            log.Write("native-test","Actual Gst HWND matched owned PID; real hooks installed; fake HID disconnect removed hooks and released; reactivation/stop passed; no physical input emitted");
        }
        finally
        {
            await capture.StopAsync("test cleanup");
            if(!process.HasExited) { process.KillOwnedTree(); }
            await process.WaitForExitAsync(); await process.DrainOutputAsync();
        }
    }
}
