namespace iMirror.Bluetooth;

public enum BluetoothState { Stopped, RadioOff, Starting, WaitingForPairing, BondedWithoutHid, HidConnected, Disconnected, Error, Advertising, GattDetected, KeyboardConnected, MouseConnected, HidIncomplete, ReconnectionRequired }
public sealed record BluetoothHost(string Id, string Alias, string DisplayName, bool Bonded,
    string ConnectionStatus, bool KeyboardSubscribed, bool MouseSubscribed,
    bool HidInformationRead, bool ReportMapRead, bool ProtocolModeWritten, bool GattActive = false);
public sealed record BluetoothStatus(BluetoothState State, string Message, IReadOnlyList<BluetoothHost> Hosts,
    string? SelectedHostId = null, bool KeyboardConnected = false, bool MouseConnected = false, byte ProtocolMode = 1,
    bool Advertising = false, bool RadioOn = false, bool PairingTimedOut = false, int ProviderGeneration = 0,
    WindowsBluetoothSnapshot? WindowsObservation = null)
{
    public static BluetoothStatus Stopped { get; } = new(BluetoothState.Stopped, "Controle Bluetooth desligado", []);
    public string StateText => Message;
    public string Description => Message;
    public bool IsConnected => KeyboardConnected || MouseConnected;
    public bool ControlReady => KeyboardConnected && MouseConnected;
    public BluetoothHost? DiagnosticHost => Hosts.FirstOrDefault(host => host.Id == SelectedHostId) ?? (SelectedHostId is null && Hosts.Count == 1 ? Hosts[0] : null);
}

public interface IBluetoothController
{
    BluetoothStatus Status { get; }
    event Action<BluetoothStatus>? StatusChanged;
    Task ConnectAsync(CancellationToken token = default);
    Task DisconnectAsync();
    Task RestartAsync(CancellationToken token = default) => throw new InputBlockedException("Reset HID não disponível.");
    Task SelectHostAsync(string id);
    Task SetAppearanceAsync(ushort? appearance);
    Task SendMouseAsync(byte buttons, int dx, int dy, int wheel, CancellationToken token);
    Task SendKeyboardAsync(byte modifiers, byte[] usages, CancellationToken token);
    Task ReleaseAsync();
}

// The controller owns one provider for its lifetime; injectable native boundary enables lifecycle tests.
public interface IHogpPeripheral : IAsyncDisposable
{
    bool CanRecreateAfterStop { get; }
    event Action<BluetoothStatus>? StatusChanged;
    BluetoothStatus GetStatus();
    Task StartAsync(CancellationToken token, int waitBluetoothSeconds = 0, bool enableRadio = false);
    void BeginPairing();
    Task SelectHostAsync(string id);
    Task SendAsync(byte id, byte[] payload, CancellationToken token);
    Task ReleaseAsync();
}
