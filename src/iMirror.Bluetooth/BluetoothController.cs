namespace iMirror.Bluetooth;

public sealed class BluetoothController(BluetoothControlLog log, Func<BluetoothControlLog, IHogpPeripheral>? factory = null,
    string? instanceLeaseName = null) : IBluetoothController, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IHogpPeripheral? _peripheral;
    private int _generation;
    private bool _recreationBlocked;
    private Semaphore? _instance;
    private volatile BluetoothStatus _status = BluetoothStatus.Stopped;
    private readonly object _statusGate = new();
    private WindowsBluetoothObservation? _windowsObserver;
    public BluetoothStatus Status => _status;
    public event Action<BluetoothStatus>? StatusChanged;
    private void Set(BluetoothStatus status)
    {
        BluetoothStatus next;
        lock (_statusGate) { next = _status = status with { ProviderGeneration = _generation, WindowsObservation = _status.WindowsObservation }; }
        StatusChanged?.Invoke(next);
    }
    private void OnWindowsObservation(WindowsBluetoothSnapshot snapshot)
    {
        BluetoothStatus next;
        lock (_statusGate) { next = _status = _status with { WindowsObservation = snapshot }; }
        StatusChanged?.Invoke(next);
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token);
        try
        {
            RequireSafeRecreation();
            if (factory is null && _windowsObserver is null)
            {
                _windowsObserver = new(log); _windowsObserver.Changed += OnWindowsObservation; _windowsObserver.Start();
            }
            if (_peripheral is not null) { _peripheral.BeginPairing(); Set(_peripheral.GetStatus()); log.Write("pairing", $"generation={_generation}; reuse provider; visual pairing window renewed"); return; }
            Set(new(BluetoothState.Starting, "Iniciando controle Bluetooth...", []));
            _instance = new Semaphore(1, 1, instanceLeaseName ?? "Local\\iMirror.BleHidProbe.Instance");
            if (!_instance.WaitOne(0)) { _instance.Dispose(); _instance = null; throw new InputBlockedException("Feche o probe BLE antes de conectar pelo iMirror."); }
            _peripheral = factory?.Invoke(log) ?? new HogpPeripheral(log);
            _generation++;
            log.SetProviderGeneration(_generation);
            log.Write("provider", $"generation={_generation}; created; lifetime=application; local id only");
            _peripheral.StatusChanged += Set;
            await _peripheral.StartAsync(token, waitBluetoothSeconds: 8, enableRadio: true);
            _peripheral.BeginPairing();
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
        try { await CleanupAsync(); ReleaseInstance(); _windowsObserver?.Dispose(); _windowsObserver = null; Set(BluetoothStatus.Stopped with { State = BluetoothState.Disconnected, Message = "Desconectado — reconecte em Ajustes > Bluetooth se necessário" }); }
        finally { _lifecycle.Release(); }
    }

    private async Task CleanupAsync()
    {
        if (_peripheral is not null)
        {
            _peripheral.StatusChanged -= Set;
            await _peripheral.DisposeAsync();
            _recreationBlocked |= !_peripheral.CanRecreateAfterStop;
            _peripheral = null;
            log.Write("provider", $"generation={_generation}; references released; stop confirmed={!_recreationBlocked}");
        }
        // Keep the single-instance lease until exit if native advertising might still be active.
        if (!_recreationBlocked) { ReleaseInstance(); }
    }
    private void ReleaseInstance()
    {
        if (_instance is not null) { _instance.Release(); _instance.Dispose(); _instance = null; }
    }
    private void RequireSafeRecreation()
    {
        if (_recreationBlocked) { throw new InputBlockedException("O Windows não confirmou a parada do anúncio HID. Feche e reabra o iMirror para recuperar com segurança; outro provider não será criado nesta sessão."); }
    }

    public async Task RestartAsync(CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token);
        try
        {
            log.Write("provider-restart", $"generation={_generation}; reason=explicit user recovery; no automatic retry");
            await CleanupAsync();
            RequireSafeRecreation();
            Set(BluetoothStatus.Stopped);
            await Task.Delay(500, token);
            // Keep the lifecycle gate across the reset: a concurrent Connect cannot create a second provider.
            _instance = new Semaphore(1, 1, instanceLeaseName ?? "Local\\iMirror.BleHidProbe.Instance");
            if (!_instance.WaitOne(0)) { _instance.Dispose(); _instance = null; throw new InputBlockedException("Feche o outro receiver/probe HID."); }
            _peripheral = factory?.Invoke(log) ?? new HogpPeripheral(log);
            _generation++;
            log.SetProviderGeneration(_generation);
            log.Write("provider", $"generation={_generation}; created once after explicit reset");
            _peripheral.StatusChanged += Set;
            Set(new(BluetoothState.Starting, "HID inicializando", []));
            await _peripheral.StartAsync(token, 8, true);
            _peripheral.BeginPairing();
            Set(_peripheral.GetStatus());
        }
        catch (Exception error) { log.Error("restart-error", error); await CleanupAsync(); Set(new(BluetoothState.Error, error is InputBlockedException ? error.Message : "Falha ao reiniciar HID. Consulte Diagnóstico.", [])); }
        finally { _lifecycle.Release(); }
    }

    public async Task SelectHostAsync(string id)
    {
        await _lifecycle.WaitAsync();
        try { if (_peripheral is not null) { await _peripheral.SelectHostAsync(id); } }
        finally { _lifecycle.Release(); }
    }
    public async Task SetAppearanceAsync(ushort? appearance)
    {
        await Task.CompletedTask;
        if (appearance is not null) { throw new InputBlockedException("Anúncio Appearance paralelo desativado: acesso negado observado neste Windows. Use o anúncio HOGP único."); }
    }

    public Task SendMouseAsync(byte buttons, int dx, int dy, int wheel, CancellationToken token) =>
        Required().SendAsync(HidSchema.MouseId, HidSchema.ClampedMouse(buttons, dx, dy, wheel), token);
    public Task SendKeyboardAsync(byte modifiers, byte[] usages, CancellationToken token) =>
        Required().SendAsync(HidSchema.KeyboardId, HidSchema.KeyboardState(modifiers, usages), token);
    private IHogpPeripheral Required() => _peripheral ?? throw new InputBlockedException("HOGP não iniciado.");
    public Task ReleaseAsync() => _peripheral?.ReleaseAsync() ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
