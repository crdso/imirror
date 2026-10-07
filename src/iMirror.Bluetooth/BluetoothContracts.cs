namespace iMirror.Bluetooth;

public enum BluetoothState { Stopped, RadioOff, Starting, WaitingForPairing, BondedWithoutHid, HidConnected, Disconnected, Error }
public sealed record BluetoothHost(string Id, string Alias, string DisplayName, bool Bonded,
    string ConnectionStatus, bool KeyboardSubscribed, bool MouseSubscribed,
    bool HidInformationRead, bool ReportMapRead, bool ProtocolModeWritten);
public sealed record BluetoothStatus(BluetoothState State, string Message, IReadOnlyList<BluetoothHost> Hosts,
    string? SelectedHostId = null, bool KeyboardConnected = false, bool MouseConnected = false, byte ProtocolMode = 1)
{
    public static BluetoothStatus Stopped { get; } = new(BluetoothState.Stopped, "Controle Bluetooth desligado", []);
    public string StateText => Message;
    public string Description => Message;
    public bool IsConnected => KeyboardConnected || MouseConnected;
}

public interface IBluetoothController
{
    BluetoothStatus Status { get; }
    event Action<BluetoothStatus>? StatusChanged;
    Task ConnectAsync(CancellationToken token = default);
    Task DisconnectAsync();
    Task SelectHostAsync(string id);
    Task SetAppearanceAsync(ushort? appearance);
    Task SendMouseAsync(byte buttons, int dx, int dy, int wheel, CancellationToken token);
    Task SendKeyboardAsync(byte modifiers, byte[] usages, CancellationToken token);
    Task ReleaseAsync();
}
