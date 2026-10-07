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
            if (args.Contains("--native-lifetime")) { return await NativeLifetimeTest(); }
            if (args.Contains("--native-stop-waiting")) { return await NativeStopWaitingTest(); }
            if (args.Contains("--native-associations")) { return await NativeAssociationsTest(); }
            if (args.Contains("--native-cursor"))
            { using var cursorLog=new BluetoothControlLog(Path.Combine("logs","phase3b-native-cursor.log"),echo:true); await NativeCaptureTest(cursorLog); return 0; }
            await Test("iOS stable ReportMap matches known-good commit byte-for-byte and SHA256", () => Check(() =>
            {
                Require(HidSchema.ReportMap.SequenceEqual(BleHid.Core.HidDescriptors.ReportMapValue));
                Require(HidSchema.ReportMap.Length == 113 && HidSchema.Profile == "iOS-stable");
                Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(HidSchema.ReportMap)) == "3097B7140EDA569B37EECC11502A31AF653B444FFEDB923E0CC85F3CCB5C0A5D");
                var copy = HidSchema.ReportMap; copy[0] = 0; Require(HidSchema.ReportMap[0] == 5);
                Require(HidSchema.KeyboardState(0,[0x87])[2] == 0);
            }));
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
            await Test("Linear sensitivity fractional remainder clamp and reset", () => Check(() =>
            {
                var motion = new RelativeMotion(); Require(motion.Add(4,-4,0.25)==(1,-1));
                Require(motion.Add(0.5,-0.5,1)==(0,0) && motion.Add(0.5,-0.5,1)==(1,-1));
                Require(motion.Add(2,-2,3)==(6,-6)); Require(motion.Add(100000,-100000,3)==(32767,-32767));
                Require(motion.Add(0,0,1)==(0,0)); motion.Add(0.5,0.5,1); motion.Reset(); Require(motion.Add(0.5,0.5,1)==(0,0));
            }));
            await Test("Auto HKL Brazil US unsupported fallback and explicit override", () => Check(() =>
            {
                Require(KeyboardLayout.Resolve(KeyboardLayoutMode.Auto,(nint)0x04160416)==KeyboardLayoutMode.PortugueseBrazilAbnt2);
                Require(KeyboardLayout.Resolve(KeyboardLayoutMode.Auto,(nint)0x04090409)==KeyboardLayoutMode.UnitedStates);
                Require(KeyboardLayout.Resolve(KeyboardLayoutMode.Auto,0)==KeyboardLayoutMode.UnitedStates);
                Require(KeyboardLayout.Resolve(KeyboardLayoutMode.UnitedStates,(nint)0x04160416)==KeyboardLayoutMode.UnitedStates);
            }));
            await Test("ABNT2 OEM scans and international keys independent of virtual-key name", () => Check(() =>
            {
                foreach(var mode in new[] {KeyboardLayoutMode.UnitedStates,KeyboardLayoutMode.PortugueseBrazilAbnt2})
                {
                    Require(KeyboardLayout.Usage(0xC0,0x29,false,mode)==0x35);
                    Require(KeyboardLayout.Usage(0xDE,0x1A,false,mode)==0x2F);
                    Require(KeyboardLayout.Usage(0xBA,0x27,false,mode)==0x33);
                    Require(KeyboardLayout.Usage(0xC1,0x73,false,mode)==0);
                    Require(KeyboardLayout.Usage(0xE2,0x56,false,mode)==0x64);
                }
                var state = new KeyboardState(); state.Update(0xDE,true,0x2F); state.Update(0xDE,false,0x34); Require(state.Usages.Length==0);
            }));
            await Test("Every requested ABNT2 symbol has independently expected physical usage and release", () => Check(() =>
            {
                const string chars="'\"`~^´ç?/\\|:;,.<>[]{}-_=+@";
                PhysicalKey[][] expected = [
                    [new(0x35)],[new(0x35,2)],[new(0x2F,2),new(0x2C)],[new(0x34),new(0x2C)],
                    [new(0x34,2),new(0x2C)],[new(0x2F),new(0x2C)],[new(0x33)],
                    [new(0x87,2)],[new(0x87)],[new(0x64)],[new(0x64,2)],
                    [new(0x38,2)],[new(0x38)],[new(0x36)],[new(0x37)],[new(0x36,2)],[new(0x37,2)],
                    [new(0x30)],[new(0x31)],[new(0x30,2)],[new(0x31,2)],
                    [new(0x2D)],[new(0x2D,2)],[new(0x2E)],[new(0x2E,2)],[new(0x1F,2)]
                ];
                Require(chars.Length==expected.Length);
                for(int i=0;i<chars.Length;i++)
                {
                    if (chars[i] is '/' or '?')
                    { try { KeyboardLayout.Compose(chars[i],KeyboardLayoutMode.PortugueseBrazilAbnt2); throw new Exception("Expected stable-profile limitation"); } catch(InputBlockedException) { } }
                    else { Require(KeyboardLayout.Compose(chars[i],KeyboardLayoutMode.PortugueseBrazilAbnt2).SequenceEqual(expected[i])); }
                }
                AssertReleases(KeyboardLayout.PrepareText(chars.Replace("?", "").Replace("/", ""),KeyboardLayoutMode.PortugueseBrazilAbnt2));
                try { KeyboardLayout.PrepareText("abc?",KeyboardLayoutMode.PortugueseBrazilAbnt2); throw new Exception("Expected atomic text rejection"); } catch(InputBlockedException) { }
            }));
            await Test("US punctuation physical usage modifiers and unsupported non-US text rejected", () => Check(() =>
            {
                const string chars="'\"`~^?/\\|:;,.<>[]{}-_=+@";
                byte[] usage=[0x34,0x34,0x35,0x35,0x23,0x38,0x38,0x31,0x31,0x33,0x33,0x36,0x37,0x36,0x37,0x2F,0x30,0x2F,0x30,0x2D,0x2D,0x2E,0x2E,0x1F];
                const string shifted="\"~^?|:<> {}_+@";
                for(int i=0;i<chars.Length;i++) { Require(KeyboardLayout.Compose(chars[i],KeyboardLayoutMode.UnitedStates).SequenceEqual(new[] {new PhysicalKey(usage[i],shifted.Contains(chars[i]) ? (byte)2 : (byte)0)})); }
                AssertReleases(KeyboardLayout.PrepareText(chars,KeyboardLayoutMode.UnitedStates));
                try { KeyboardLayout.PrepareText("abcç",KeyboardLayoutMode.UnitedStates); throw new Exception("Expected unsupported"); } catch(ArgumentException) { }
            }));
            await Test("All requested accents compose dead key then base with neutral after each", () => Check(() =>
            {
                const string accents="áàâãéêíóôõú"; byte[] dead=[0x2F,0x2F,0x34,0x34,0x2F,0x34,0x2F,0x2F,0x34,0x34,0x2F];
                byte[] mods=[0,2,2,0,0,2,0,0,2,0,0]; const string bases="aaaaeeiooou";
                for(int i=0;i<accents.Length;i++)
                {
                    var expected = new[] {new PhysicalKey(dead[i],mods[i]),new PhysicalKey((byte)(4+bases[i]-'a'))};
                    Require(KeyboardLayout.Compose(accents[i],KeyboardLayoutMode.PortugueseBrazilAbnt2).SequenceEqual(expected));
                }
                AssertReleases(KeyboardLayout.PrepareText(accents+"ç",KeyboardLayoutMode.PortugueseBrazilAbnt2));
            }));
            await Test("Exit viewport discards queued movement; quick reentry still neutralizes first", async () =>
            {
                var fake = new Fake(); var buffer = new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(0,999,0),true);
                buffer.ClearAndRelease(); buffer.Enqueue(InputReport.Key(2,[4]));
                using var token = new CancellationTokenSource(150); using var log=Log();
                await InputSender.RunAsync(buffer,fake,()=>true,_=>{},log,token.Token,()=>true);
                Require(fake.MouseSent==0 && fake.KeyboardSent==1 && fake.Release>=2 && fake.FirstAction=="release");
            });
            await Test("Outside viewport sends zero movement while active; stopped sender remains silent", async () =>
            {
                var fake=new Fake(); var buffer=new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(1,200,200));
                using var token=new CancellationTokenSource(90); using var log=Log();
                await InputSender.RunAsync(buffer,fake,()=>true,_=>{},log,token.Token,()=>false);
                int stopped=fake.Sent; await Task.Delay(40); Require(stopped==0 && fake.Sent==0 && fake.Release==1 && buffer.Count==0);
            });
            await Test("Stop drains an in-flight send before neutral and completion", async () =>
            {
                var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var fake=new Fake { HoldSend=pending.Task }; var buffer=new InputReportBuffer(); buffer.Enqueue(InputReport.Mouse(1,10,10));
                using var token=new CancellationTokenSource(); using var log=Log();
                var task=InputSender.RunAsync(buffer,fake,()=>true,_=>{},log,token.Token);
                for(int i=0;i<50 && fake.Sent==0;i++) { await Task.Delay(5); }
                Require(fake.Sent==1); token.Cancel(); await Task.Delay(25); Require(!task.IsCompleted && fake.Release==0);
                pending.SetResult(); await task; int sent=fake.Sent; await Task.Delay(25);
                Require(fake.Release==1 && fake.Sent==sent && fake.Delivered==1);
            });
            await Test("HOGP concurrent Connect reuses one provider; timeout retains generation", HogpLifecycleProbe.Connect);
            await Test("HOGP disconnect/reconnect keeps provider and selected host", HogpLifecycleProbe.Reconnect);
            await Test("Explicit HID reset recreates exactly once and shutdown releases", HogpLifecycleProbe.Reset);
            await Test("Stop waiting is idempotent and retains lease when native stop is unconfirmed", HogpLifecycleProbe.StopWaiting);
            await Test("EnsureAdvertising Aborted -> bounded retry possible on same provider", AdvertisingRecoveryProbe.Aborted);
            await Test("EnsureAdvertising exception -> pending reset, 1/2/5s backoff and finite attempts", AdvertisingRecoveryProbe.Exceptions);
            await Test("Missing Started -> watchdog; shutdown cancels retry; drop reuses advertising action", AdvertisingRecoveryProbe.WatchdogAndDrop);
            await Test("Unpair targets only an exact observed paired HID session and retains provider", HogpLifecycleProbe.UnpairSafety);
            await Test("Unconfirmed native advertising stop blocks duplicate provider until exit", HogpLifecycleProbe.UnconfirmedStop);
            await Test("Windows BLE link/bond cannot promote HID connection or select input target", () => Check(() =>
            {
                var links = new WindowsBluetoothSnapshot(true,[new("W1","test","BLE",true,true)]);
                var status = BluetoothStatus.Stopped with { WindowsObservation=links, RadioOn=true, Advertising=true };
                Require(links.BleConnected && !status.IsConnected && !status.ControlReady && status.SelectedHostId is null && status.DiagnosticHost is null);
                Require(!new WindowsBluetoothSnapshot(true,[new("W2","test","Classic",true,true)]).BleConnected);
                Require(!new WindowsBluetoothSnapshot(true,[new("W3","test","BLE",true,null)]).BleConnected);
            }));
            await Test("Bond, GATT, metadata, CCCD and physical effect are distinct", () => Check(HogpLifecycleProbe.States));
            await Test("Panel recovery shortcut respects registration and Shift fallback", () => Check(() =>
            {
                Require(CapturePolicy.PanelRecovery(0x49,true,true,false,true,false));
                Require(CapturePolicy.PanelRecovery(0x49,true,true,true,true,true));
                Require(!CapturePolicy.PanelRecovery(0x49,true,true,false,true,true));
                Require(!CapturePolicy.PanelRecovery(0x49,true,true,false,false,false));
                Require(!CapturePolicy.PanelRecovery(0x49,false,true,false,true,false));
                Require(CapturePolicy.Emergency(0x1B,false,false) && CapturePolicy.Emergency(0x51,true,true));
            }));
            Console.WriteLine($"PHASE 3 TESTS: {_passed}/{_passed}; UX physical validation pending"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static BluetoothControlLog Log() => new(Path.Combine("logs","phase3-automated-tests.log"));
    private static void AssertReleases(byte[][] reports)
    {
        Require(reports.Length%2==0);
        for(int i=0;i<reports.Length;i+=2) { Require(reports[i].Length==8 && reports[i][1]==0 && reports[i+1].All(value=>value==0)); }
    }
    private sealed class Fake : IBluetoothController
    {
        public bool Live=true, Fail; public int Sent,Release; public double MinGap=double.MaxValue; private DateTimeOffset _last;
        public Action? AfterSend;
        public int MouseSent,KeyboardSent; public string? FirstAction;
        public Task? HoldSend; public int Delivered;
        public BluetoothStatus Status => new(Live ? BluetoothState.HidConnected : BluetoothState.Disconnected,"test",[],"fake",Live,Live);
        public event Action<BluetoothStatus>? StatusChanged;
        public Task ConnectAsync(CancellationToken token=default) { StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task DisconnectAsync() { Live=false; StatusChanged?.Invoke(Status); return Task.CompletedTask; }
        public Task SelectHostAsync(string id)=>Task.CompletedTask;
        public Task SetAppearanceAsync(ushort? appearance)=>Task.CompletedTask;
        private async Task Send() { var now=DateTimeOffset.UtcNow; if(Sent!=0) { MinGap=Math.Min(MinGap,(now-_last).TotalMilliseconds); } _last=now; Sent++; AfterSend?.Invoke(); if(Fail) { throw new IOException("simulated notify failure"); } if(HoldSend is not null) { await HoldSend; } Delivered++; }
        public Task SendMouseAsync(byte buttons,int dx,int dy,int wheel,CancellationToken token) { FirstAction ??= "mouse"; MouseSent++; return Send(); }
        public Task SendKeyboardAsync(byte modifiers,byte[] usages,CancellationToken token) { FirstAction ??= "keyboard"; KeyboardSent++; return Send(); }
        public Task ReleaseAsync() { FirstAction ??= "release"; Release++; return Task.CompletedTask; }
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
    private static async Task<int> NativeLifetimeTest()
    {
        using var log = new BluetoothControlLog(Path.Combine("logs","final-polish-native-hid.log"),echo:true);
        await using var controller = new BluetoothController(log);
        await controller.ConnectAsync();
        if (controller.Status.State is BluetoothState.Error or BluetoothState.RadioOff) { return 1; }
        int generation = controller.Status.ProviderGeneration;
        for (int attempt=0;attempt<3;attempt++) { await controller.ConnectAsync(); }
        await Task.Delay(TimeSpan.FromSeconds(31));
        Require(controller.Status.ProviderGeneration == generation);
        if (!controller.Status.ControlReady) { Require(controller.Status.PairingTimedOut); }
        log.Write("native-validation",$"PASS: same generation={generation}; advertising={controller.Status.Advertising}; visual timeout={controller.Status.PairingTimedOut}; keyboard={controller.Status.KeyboardConnected}; mouse={controller.Status.MouseConnected}; no key/click/movement sent; physical validation pending");
        if (!controller.Status.KeyboardConnected && !controller.Status.MouseConnected)
        {
            await controller.RestartAsync();
            if (controller.Status.State == BluetoothState.Error)
            {
                Require(controller.Status.ProviderGeneration == generation && controller.Status.Message.Contains("Feche e reabra", StringComparison.Ordinal));
                await controller.ConnectAsync();
                Require(controller.Status.ProviderGeneration == generation && controller.Status.State == BluetoothState.Error);
                log.Write("native-validation", "PASS: native stop unconfirmed; reset and reconnect safely blocked; no second provider created; reopen app required");
            }
            else { Require(controller.Status.ProviderGeneration == generation + 1); log.Write("native-validation", "PASS: native stop confirmed; one explicit reset only"); }
        }
        return 0;
    }
    private static async Task<int> NativeStopWaitingTest()
    {
        using var log = new BluetoothControlLog(Path.Combine("logs", "bluetooth-native-stop-waiting.log"), echo:true);
        await using var controller = new BluetoothController(log);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await controller.ConnectAsync(timeout.Token);
        Require(controller.Status.Advertising);
        int generation = controller.Status.ProviderGeneration;
        await controller.StopAsync();
        Require(controller.Status.State is BluetoothState.Stopped or BluetoothState.StopUnconfirmed);
        Require(!controller.Status.IsConnected && controller.Status.RadioOn);
        var result = controller.Status.State;
        await controller.StopAsync();
        Require(controller.Status.State == result && controller.Status.ProviderGeneration == generation);
        if (result == BluetoothState.StopUnconfirmed)
        {
            using var lease = new Semaphore(1, 1, "Local\\iMirror.BleHidProbe.Instance");
            Require(!lease.WaitOne(0));
            log.Write("native-validation", "PASS: stop requested; native termination unconfirmed; input blocked; lease retained; no second provider; reopen required");
        }
        else { log.Write("native-validation", "PASS: native stop confirmed; waiting ended; repeated stop harmless"); }
        log.Write("native-validation", "No mouse/key input, pairing reset, radio toggle or AirPlay change performed; physical connection not validated");
        return 0;
    }
    private static async Task<int> NativeAssociationsTest()
    {
        using var log = new BluetoothControlLog(Path.Combine("logs","bluetooth-native-associations.log"), echo:true);
        using var observer = new WindowsBluetoothObservation(log);
        var completed = new TaskCompletionSource<WindowsBluetoothSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Changed += snapshot => { if (snapshot.Complete || snapshot.Issue is not null) { completed.TrySetResult(snapshot); } };
        observer.Start();
        var snapshot = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Require(snapshot is { Complete:true, Issue:null });
        log.Write("native-validation", $"PASS: native Classic/BLE watchers completed; known endpoints={snapshot.Links.Count}; no HID provider created; no pairing, advertising or input sent");
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
            var video=VideoWindow.Find(process.Id)!;
            using(var surface=new VideoCursor(video,()=>(320,640)))
            {
                Require(surface.Handle!=0 && surface.Owns(surface.Handle) && !surface.Owns(video.Handle));
                surface.SetHidden(false); surface.SetHidden(false);
                surface.Dispose(); Require(surface.Handle==0);
                log.Write("native-test","Cursor surface created and destroyed idempotently; renderer HWND distinct; no foreign class mutation or ShowCursor counter used");
            }
            try { await capture.StartAsync(()=>process.HasExited ? 0 : process.Id,()=>(320,640),true,1); }
            catch(InputBlockedException error) when(error.Message.Contains("Selecione a janela",StringComparison.Ordinal))
            {
                Require(!capture.IsActive && fake.Sent==0 && fake.Release==1);
                log.Write("native-test","Foreground transfer unavailable to background test process; verified fail-open and no hooks/input. SKIP real hook activation: requires foreground user activation in iMirror, still pending physical validation.");
                return;
            }
            Require(capture.IsActive);
            await NativeCursorProbe.RunAsync(video,capture,log);
            int sentAtStop=fake.Sent; await Task.Delay(40); Require(!capture.IsActive && fake.Sent==sentAtStop);
            fake.Live=true;
            await capture.StartAsync(()=>process.Id,()=>(640,320),false,2);
            Require(capture.IsActive);
            fake.Live=false; await capture.Completion.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!capture.IsActive && fake.Release>=4);
            fake.Live=true; await capture.StartAsync(()=>process.Id,()=>(320,640),false,2);
            await capture.StopAsync("native smoke stop"); await capture.StopAsync();
            Require(!capture.IsActive && fake.Release>=6);
            log.Write("native-test","Actual Gst HWND matched owned PID; real hooks installed; fake HID disconnect removed hooks and released; reactivation/stop passed; no physical HID input emitted");
        }
        finally
        {
            await capture.StopAsync("test cleanup");
            if(!process.HasExited) { process.KillOwnedTree(); }
            await process.WaitForExitAsync(); await process.DrainOutputAsync();
        }
    }
}
