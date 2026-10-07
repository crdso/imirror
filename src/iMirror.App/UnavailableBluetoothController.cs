using iMirror.Bluetooth;
using iMirror.Core.Features;

namespace iMirror.App;

// Compatibility for legacy Phase 1 UI tests; production always uses BluetoothController.
internal sealed class UnavailableBluetoothController(IFeatureService feature) : IBluetoothController
{
    public BluetoothStatus Status { get; } = BluetoothStatus.Stopped with { Message = feature.Status.Description };
    public event Action<BluetoothStatus>? StatusChanged;
    public Task ConnectAsync(CancellationToken token = default) { feature.ReportAvailability(); StatusChanged?.Invoke(Status); return Task.CompletedTask; }
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task SelectHostAsync(string id) => Task.CompletedTask;
    public Task SetAppearanceAsync(ushort? appearance) => Task.CompletedTask;
    public Task SendMouseAsync(byte buttons, int dx, int dy, int wheel, CancellationToken token) => Task.FromException(new InputBlockedException("Sem transporte HID."));
    public Task SendKeyboardAsync(byte modifiers, byte[] usages, CancellationToken token) => Task.FromException(new InputBlockedException("Sem transporte HID."));
    public Task ReleaseAsync() => Task.CompletedTask;
}
