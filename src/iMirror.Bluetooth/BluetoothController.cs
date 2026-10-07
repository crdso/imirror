namespace iMirror.Bluetooth;

public sealed class BluetoothController(BluetoothControlLog log) : IBluetoothController, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private HogpPeripheral? _peripheral;
    private AppearanceAdvertiser? _appearance;
    private Semaphore? _instance;
    private volatile BluetoothStatus _status = BluetoothStatus.Stopped;
    public BluetoothStatus Status => _status;
    public event Action<BluetoothStatus>? StatusChanged;
    private void Set(BluetoothStatus status) { _status = status; StatusChanged?.Invoke(status); }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token);
        try
        {
            if (_peripheral is not null) { return; }
            Set(new(BluetoothState.Starting, "Iniciando controle Bluetooth...", []));
            _instance = new Semaphore(1, 1, "Local\\iMirror.BleHidProbe.Instance");
            if (!_instance.WaitOne(0)) { _instance.Dispose(); _instance = null; throw new InputBlockedException("Feche o probe BLE antes de conectar pelo iMirror."); }
            _peripheral = new HogpPeripheral(log);
            _peripheral.StatusChanged += Set;
            await _peripheral.StartAsync(token, waitBluetoothSeconds: 8, enableRadio: true);
            Set(_peripheral.GetStatus());
        }
        catch (Exception error)
        {
            log.Error("connect-error", error); await CleanupAsync();
            Set(new(error is InputBlockedException && error.Message.Contains("Bluetooth", StringComparison.Ordinal) ? BluetoothState.RadioOff : BluetoothState.Error,
                error is InputBlockedException ? error.Message : "Erro Bluetooth. Consulte bluetooth-control.log.", []));
        }
        finally { _lifecycle.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycle.WaitAsync();
        try { await CleanupAsync(); Set(BluetoothStatus.Stopped with { State = BluetoothState.Disconnected, Message = "Desconectado — reconecte em Ajustes > Bluetooth se necessário" }); }
        finally { _lifecycle.Release(); }
    }

    private async Task CleanupAsync()
    {
        try { _appearance?.Dispose(); } catch (Exception error) { log.Error("appearance-stop", error); }
        _appearance = null;
        if (_peripheral is not null)
        {
            _peripheral.StatusChanged -= Set;
            await _peripheral.DisposeAsync(); _peripheral = null;
        }
        if (_instance is not null) { _instance.Release(); _instance.Dispose(); _instance = null; }
    }

    public async Task SelectHostAsync(string id)
    {
        await _lifecycle.WaitAsync();
        try { if (_peripheral is not null) { await _peripheral.SelectHostAsync(id); } }
        finally { _lifecycle.Release(); }
    }
    public async Task SetAppearanceAsync(ushort? appearance)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_peripheral is null) { throw new InputBlockedException("Inicie HOGP antes do anúncio alternativo."); }
            _appearance?.Dispose(); _appearance = null;
            if (appearance is { } value)
            {
                _appearance = new AppearanceAdvertiser(log);
                try { _appearance.Start(value); } catch (Exception error) { log.Error("appearance", error); _appearance.Dispose(); _appearance = null; throw; }
            }
        }
        finally { _lifecycle.Release(); }
    }

    public Task SendMouseAsync(byte buttons, int dx, int dy, int wheel, CancellationToken token) =>
        Required().SendAsync(HidSchema.MouseId, HidSchema.ClampedMouse(buttons, dx, dy, wheel), token);
    public Task SendKeyboardAsync(byte modifiers, byte[] usages, CancellationToken token) =>
        Required().SendAsync(HidSchema.KeyboardId, HidSchema.KeyboardState(modifiers, usages), token);
    private HogpPeripheral Required() => _peripheral ?? throw new InputBlockedException("HOGP não iniciado.");
    public Task ReleaseAsync() => _peripheral?.ReleaseAsync() ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
