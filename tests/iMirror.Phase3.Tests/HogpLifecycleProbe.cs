using iMirror.Bluetooth;
internal static class HogpLifecycleProbe
{
    // Exercise the exact lease behavior without sharing the live HID publisher's lease.
    private static readonly string LeaseName = "Local\\iMirror.Tests.Hogp." + Guid.NewGuid().ToString("N");
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("HOGP lifetime/evidence invariant failed."); } }
    private sealed class Peripheral : IHogpPeripheral
    {
        public bool CanRecreateAfterStop { get; set; } = true;
        public int Starts, Pairings, Disposes, Pauses, Unpairs;
        private bool _paused;
        public BluetoothStatus Status = new(BluetoothState.WaitingForPairing, "test", [], Advertising:true, RadioOn:true);
        public event Action<BluetoothStatus>? StatusChanged;
        public BluetoothStatus GetStatus() => Status;
        public Task StartAsync(CancellationToken token, int waitBluetoothSeconds = 0, bool enableRadio = false) { Starts++; return Task.CompletedTask; }
        public void BeginPairing()
        {
            if (_paused && !CanRecreateAfterStop) { throw new InputBlockedException("Feche e reabra o iMirror"); }
            _paused = false; Pairings++;
            Status = Status with { State = BluetoothState.WaitingForPairing, Advertising = true }; StatusChanged?.Invoke(Status);
        }
        public Task StopServiceAsync()
        {
            if (!_paused) { Pauses++; _paused = true; Publish(BluetoothStatus.Stopped with { State = CanRecreateAfterStop ? BluetoothState.Stopped : BluetoothState.StopUnconfirmed }); }
            return Task.CompletedTask;
        }
        public Task<BluetoothUnpairResult> UnpairHostAsync(string id, CancellationToken token)
        { Unpairs++; return Task.FromResult(new BluetoothUnpairResult("Unpaired", true)); }
        public void Publish(BluetoothStatus status) { Status = status; StatusChanged?.Invoke(status); }
        public Task SelectHostAsync(string id) => Task.CompletedTask;
        public Task SendAsync(byte id, byte[] payload, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
    private static BluetoothControlLog Log() => new(Path.Combine("logs", "hogp-lifecycle-tests.log"));
    public static async Task UnconfirmedStop()
    {
        using var log = Log(); var instances = new List<Peripheral>();
        await using var controller = new BluetoothController(log, _ => { var p = new Peripheral { CanRecreateAfterStop = false }; instances.Add(p); return p; }, LeaseName);
        await controller.ConnectAsync();
        await controller.RestartAsync();
        Require(controller.Status.State == BluetoothState.Error && controller.Status.Message.Contains("Feche e reabra", StringComparison.Ordinal));
        await controller.ConnectAsync(); await controller.RestartAsync();
        Require(instances.Count == 1 && instances[0].Starts == 1 && instances[0].Disposes == 1 && controller.Status.ProviderGeneration == 1);
        // The native lease remains held until process shutdown, preventing another local provider.
        using var semaphore = new Semaphore(1, 1, LeaseName);
        Require(!semaphore.WaitOne(0));
        await controller.DisconnectAsync();
        Require(semaphore.WaitOne(0)); semaphore.Release();
    }
    public static async Task Connect()
    {
        using var log = Log(); var instances = new List<Peripheral>();
        await using var controller = new BluetoothController(log, _ => { var p = new Peripheral(); instances.Add(p); return p; }, LeaseName);
        await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => controller.ConnectAsync()));
        Require(instances.Count == 1 && instances[0].Starts == 1 && controller.Status.ProviderGeneration == 1);
        var timedOut = PairingStatus.Create(true, true, [], null, false, false, 1, false, TimeSpan.FromSeconds(31));
        instances[0].Publish(timedOut);
        Require(controller.Status.PairingTimedOut && instances[0].Disposes == 0 && controller.Status.ProviderGeneration == 1);
    }
    public static async Task StopWaiting()
    {
        using var log = Log(); var instances = new List<Peripheral>();
        await using var controller = new BluetoothController(log, _ => { var p = new Peripheral(); instances.Add(p); return p; }, LeaseName);
        await controller.ConnectAsync();
        await Task.WhenAll(controller.StopAsync(), controller.StopAsync());
        Require(instances[0].Disposes == 0 && instances[0].Pauses == 1 && controller.Status.State == BluetoothState.Stopped && !controller.Status.IsConnected);
        await controller.ConnectAsync();
        Require(instances.Count == 1 && instances[0].Starts == 1 && controller.Status.ProviderGeneration == 1);
        instances[0].CanRecreateAfterStop = false;
        await controller.StopAsync(); await controller.StopAsync();
        Require(controller.Status.State == BluetoothState.StopUnconfirmed && instances[0].Disposes == 0);
        using var lease = new Semaphore(1, 1, LeaseName);
        Require(!lease.WaitOne(0));
        await controller.ConnectAsync();
        Require(instances.Count == 1 && instances[0].Disposes == 0 && controller.Status.State == BluetoothState.Error);
        await controller.DisconnectAsync();
        Require(lease.WaitOne(0)); lease.Release();
    }
    public static async Task UnpairSafety()
    {
        using var log = Log(); var peripheral = new Peripheral();
        await using var controller = new BluetoothController(log, _ => peripheral, LeaseName);
        await controller.ConnectAsync();
        var host = new BluetoothHost("observed-session", "H1", "fixture", true, "GATT Active", true, true, true, true, true, true, true);
        peripheral.Publish(PairingStatus.Create(true, true, [host], host.Id, true, true, 1, true, TimeSpan.Zero));
        try { await controller.UnpairHostAsync("same-name-other-device"); throw new Exception("Unknown host allowed"); } catch(InputBlockedException) { }
        peripheral.Publish(controller.Status with { Hosts = [host with { CanUnpair = false }] });
        try { await controller.UnpairHostAsync(host.Id); throw new Exception("Unknown bond allowed"); } catch(InputBlockedException) { }
        Require(peripheral.Unpairs == 0);
        peripheral.Publish(controller.Status with { Hosts = [host] });
        var result = await controller.UnpairHostAsync(host.Id);
        Require(result.Status == "Unpaired" && peripheral.Unpairs == 1 && peripheral.Disposes == 0 && controller.Status.ProviderGeneration == 1);
    }
    public static async Task Reconnect()
    {
        using var log = Log(); var p = new Peripheral();
        await using var controller = new BluetoothController(log, _ => p, LeaseName);
        await controller.ConnectAsync();
        var host = new BluetoothHost("test", "H1", "test", true, "GATT Active", true, true, true, true, false, true);
        p.Publish(PairingStatus.Create(true, true, [host], "test", true, true, 1, true, TimeSpan.Zero));
        Require(controller.Status.ControlReady);
        p.Publish(PairingStatus.Create(true, true, [host with { GattActive=false, KeyboardSubscribed=false, MouseSubscribed=false }], "test", false, false, 1, true, TimeSpan.Zero));
        Require(!controller.Status.IsConnected && controller.Status.SelectedHostId == "test" && p.Disposes == 0);
        p.Publish(PairingStatus.Create(true, true, [host], "test", true, true, 1, true, TimeSpan.Zero));
        Require(controller.Status.ControlReady && controller.Status.ProviderGeneration == 1 && p.Starts == 1);
    }
    public static async Task Reset()
    {
        using var log = Log(); var instances = new List<Peripheral>();
        await using var controller = new BluetoothController(log, _ => { var p = new Peripheral(); instances.Add(p); return p; }, LeaseName);
        await controller.ConnectAsync(); await controller.RestartAsync();
        Require(instances.Count == 2 && instances[0].Disposes == 1 && instances[1].Starts == 1 && controller.Status.ProviderGeneration == 2);
        await controller.ConnectAsync(); Require(instances.Count == 2);
        await controller.DisconnectAsync(); await controller.DisconnectAsync();
        Require(instances[1].Disposes == 1);
    }
    public static void States()
    {
        var host = new BluetoothHost("test", "H1", "test", true, "Disconnected", false, false, false, false, false);
        BluetoothStatus State(BluetoothHost h, bool k = false, bool m = false) => PairingStatus.Create(true, true, [h], "test", k, m, 1, false, TimeSpan.Zero);
        Require(State(host).State == BluetoothState.BondedWithoutHid && !State(host).IsConnected);
        host = host with { GattActive=true }; Require(State(host).State == BluetoothState.GattDetected);
        host = host with { HidInformationRead=true, ReportMapRead=true }; Require(State(host).State == BluetoothState.HidIncomplete && !State(host).IsConnected);
        Require(State(host,true).State == BluetoothState.KeyboardConnected && !State(host,true).ControlReady);
        Require(State(host,false,true).State == BluetoothState.MouseConnected && !State(host,false,true).ControlReady);
        Require(State(host,true,true).State == BluetoothState.HidConnected && State(host,true,true).ControlReady);
        Require(PairingStatus.Create(false,true,[host],"test",false,false,1,true,TimeSpan.Zero).State == BluetoothState.RadioOff);
        Require(PairingStatus.Create(true,true,[host],"another",false,false,1,true,TimeSpan.Zero).DiagnosticHost is null);
    }
}
