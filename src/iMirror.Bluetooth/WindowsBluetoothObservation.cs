using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace iMirror.Bluetooth;

public sealed record WindowsBluetoothLink(string Alias, string DisplayName, string Transport, bool Paired, bool? Connected);
public sealed record WindowsBluetoothSnapshot(bool Complete, IReadOnlyList<WindowsBluetoothLink> Links, string? Issue = null)
{
    public bool BleConnected => Links.Any(link => link.Transport == "BLE" && link.Connected == true);
    public string Summary => Issue is not null ? "Vínculos do Windows indisponíveis · " + Issue :
        !Complete ? "Consultando vínculos Classic/BLE no Windows..." :
        $"Windows: BLE pareados {Links.Count(link => link.Transport == "BLE" && link.Paired)}, conectados {Links.Count(link => link.Transport == "BLE" && link.Connected == true)} · Classic conectados {Links.Count(link => link.Transport == "Classic" && link.Connected == true)}";
}

// Observe OS associations independently of encrypted HID callbacks. Never pair/unpair,
// open a remote GATT client, advertise, select an input host or infer HID readiness here.
internal sealed class WindowsBluetoothObservation(BluetoothControlLog log) : IDisposable
{
    private sealed record Entry(DeviceInformation Device, string Alias, string Transport);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _devices = new(StringComparer.Ordinal);
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly List<Action> _unhooks = [];
    private readonly HashSet<string> _completed = [];
    private int _number;
    private bool _disposed;
    private string? _issue;
    private string _lastSignature = "";
    public event Action<WindowsBluetoothSnapshot>? Changed;
    public void Start()
    {
        try
        {
            Watch("Classic", BluetoothDevice.GetDeviceSelector());
            Watch("BLE", BluetoothLEDevice.GetDeviceSelector());
            log.Write("windows-link", "Read-only Classic/BLE association watchers started; OS link/bond is independent of HID subscriptions; no remote GATT query");
        }
        catch (Exception error) { log.Error("windows-link-error", error); Observe(() => _issue = "falha de enumeração; consulte Diagnóstico"); }
    }
    private void Watch(string transport, string query)
    {
        var watcher = DeviceInformation.CreateWatcher(query,
            ["System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired"], DeviceInformationKind.AssociationEndpoint);
        TypedEventHandler<DeviceWatcher, DeviceInformation> added = (_, device) => Observe(() => _devices[device.Id] = new(device, $"W{++_number}", transport));
        TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> updated = (_, update) => Observe(() => { if (_devices.TryGetValue(update.Id, out var item)) { item.Device.Update(update); } });
        TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> removed = (_, update) => Observe(() => _devices.Remove(update.Id));
        TypedEventHandler<DeviceWatcher, object> completed = (_, _) => Observe(() => _completed.Add(transport));
        TypedEventHandler<DeviceWatcher, object> stopped = (sender, _) => Observe(() => { if (sender.Status == DeviceWatcherStatus.Aborted) { _issue = "observação interrompida pelo Windows"; } });
        watcher.Added += added; watcher.Updated += updated; watcher.Removed += removed; watcher.EnumerationCompleted += completed; watcher.Stopped += stopped;
        _unhooks.Add(() => { watcher.Added -= added; watcher.Updated -= updated; watcher.Removed -= removed; watcher.EnumerationCompleted -= completed; watcher.Stopped -= stopped; });
        _watchers.Add(watcher); watcher.Start();
    }
    private void Observe(Action update)
    {
        try { lock (_gate) { if (_disposed) { return; } update(); Publish(); } }
        catch (Exception error)
        {
            log.Error("windows-link-event-error", error);
            lock (_gate) { if (_disposed) { return; } _issue = "falha de observação; consulte Diagnóstico"; Changed?.Invoke(new(false, [], _issue)); }
        }
    }
    private void Publish()
    {
        WindowsBluetoothSnapshot snapshot;
        string signature;
        lock (_gate)
        {
            if (_disposed) { return; }
            var links = _devices.Values.Select(entry => new WindowsBluetoothLink(entry.Alias,
                Regex.Replace(entry.Device.Name, @"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b|\b[0-9a-f]{12}\b", "[endereço]"), entry.Transport,
                entry.Device.Pairing.IsPaired,
                entry.Device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is bool connected ? connected : null))
                .Where(link => link.Paired || link.Connected == true).OrderBy(link => link.Alias).ToArray();
            snapshot = new(_completed.Count == 2, links, _issue);
            signature = $"complete={snapshot.Complete}; issue={snapshot.Issue}; " + string.Join("; ", links.Select(link => $"{link.Alias} {link.Transport} paired={link.Paired} connected={link.Connected?.ToString() ?? "unknown"}"));
            string uiSignature = signature + "|" + string.Join("\u0001", links.Select(link => link.DisplayName));
            if (uiSignature == _lastSignature) { return; } _lastSignature = uiSignature;
            // Persist aliases/status only. Friendly names appear solely in the local UI.
            log.Write("windows-link", signature);
            Changed?.Invoke(snapshot);
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) { return; } _disposed = true; _devices.Clear(); }
        foreach (var unhook in _unhooks) { try { unhook(); } catch (Exception error) { log.Error("windows-link-unhook-error", error); } }
        _unhooks.Clear();
        foreach (var watcher in _watchers)
        {
            try { if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) { watcher.Stop(); } }
            catch (Exception error) { log.Error("windows-link-stop-error", error); }
        }
        _watchers.Clear(); Changed = null;
    }
}
