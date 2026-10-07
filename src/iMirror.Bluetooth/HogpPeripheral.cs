using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Security.Cryptography;

namespace iMirror.Bluetooth;

public sealed class InputBlockedException(string message) : Exception(message);

public sealed class HogpPeripheral(BluetoothControlLog log) : IReportTransport, IAsyncDisposable
{
    private sealed class Host(string alias)
    {
        public string Alias { get; } = alias;
        public BluetoothLEDevice? Device { get; set; }
        public string LastSummary { get; set; } = "";
        public bool LookupAttempted { get; set; }
        public string? Name { get; set; }
        public bool InfoRead { get; set; }
        public bool MapRead { get; set; }
        public bool ModeWritten { get; set; }
        public byte Mode { get; set; } = 1;
    }

    private readonly ConcurrentDictionary<string, Host> _hosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<GattSession, string> _sessions = new();
    private readonly ConcurrentDictionary<byte, bool> _pendingRelease = new();
    private readonly List<Action> _unhooks = [];
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _monitorStop = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GattServiceProvider? _provider;
    private GattServiceProvider? _batteryProvider;
    private GattLocalCharacteristic? _keyboard;
    private GattLocalCharacteristic? _mouse;
    private GattLocalCharacteristic? _bootKeyboard;
    private GattLocalCharacteristic? _bootMouse;
    private byte _protocolMode => _selectedId is not null && _hosts.TryGetValue(_selectedId, out var host) ? host.Mode : (byte)1;
    private byte _leds;
    private Task? _monitor;
    private byte[] _keyboardValue = HidSchema.Neutral(HidSchema.KeyboardId);
    private byte[] _mouseValue = HidSchema.Neutral(HidSchema.MouseId);
    private string? _selectedId;
    private int _hostNumber;
    private int _stopping;
    private volatile bool _suspended;
    private DateTimeOffset _lastReleaseAttempt;
    private string _lastSubscriptionSummary = "";
    private string _lastPublished = "";
    private Radio? _radio;
    private bool _everConnected;
    public event Action<BluetoothStatus>? StatusChanged;

    public BluetoothStatus GetStatus()
    {
        if (Volatile.Read(ref _stopping) != 0) { return BluetoothStatus.Stopped with { Message = "Desconectado" }; }
        var keyboard = KeyboardCharacteristic?.SubscribedClients.Where(IsLive).Select(client => client.Session.DeviceId.Id).ToHashSet() ?? [];
        var mouse = MouseCharacteristic?.SubscribedClients.Where(IsLive).Select(client => client.Session.DeviceId.Id).ToHashSet() ?? [];
        if (_suspended || _radio?.State != RadioState.On) { keyboard.Clear(); mouse.Clear(); }
        string[] live = new[] { _keyboard, _mouse, _bootKeyboard, _bootMouse }.Where(characteristic => characteristic is not null)
            .SelectMany(characteristic => characteristic!.SubscribedClients).Where(IsLive).Select(client => client.Session.DeviceId.Id).Distinct().ToArray();
        if (_selectedId is null && live.Length == 1)
        { _selectedId = live[0]; log.Write("target", $"Selected={_hosts.GetValueOrDefault(_selectedId)?.Alias ?? "host"}"); }
        keyboard = KeyboardCharacteristic?.SubscribedClients.Where(IsLive).Select(client => client.Session.DeviceId.Id).ToHashSet() ?? [];
        mouse = MouseCharacteristic?.SubscribedClients.Where(IsLive).Select(client => client.Session.DeviceId.Id).ToHashSet() ?? [];
        if (_suspended || _radio?.State != RadioState.On) { keyboard.Clear(); mouse.Clear(); }
        bool k = _selectedId is not null && keyboard.Contains(_selectedId), m = _selectedId is not null && mouse.Contains(_selectedId);
        if (k || m) { _everConnected = true; }
        var hosts = _hosts.Select(pair => new BluetoothHost(pair.Key, pair.Value.Alias,
            pair.Value.Name ?? pair.Value.Alias, pair.Value.Device?.DeviceInformation.Pairing.IsPaired ?? false,
            pair.Value.Device?.ConnectionStatus.ToString() ?? (_sessions.Any(session => session.Value == pair.Key && session.Key.SessionStatus == GattSessionStatus.Active) ? "GATT Active" : "Disconnected"),
            keyboard.Contains(pair.Key), mouse.Contains(pair.Key), pair.Value.InfoRead, pair.Value.MapRead, pair.Value.ModeWritten)).OrderBy(host => host.Alias).ToArray();
        var state = _radio?.State != RadioState.On ? BluetoothState.RadioOff : k || m ? BluetoothState.HidConnected :
            hosts.Any(host => host.Bonded) ? BluetoothState.BondedWithoutHid : _everConnected ? BluetoothState.Disconnected : BluetoothState.WaitingForPairing;
        string message = state switch
        {
            BluetoothState.RadioOff => "Bluetooth desligado",
            BluetoothState.HidConnected => "Controle Bluetooth conectado",
            BluetoothState.BondedWithoutHid => "Pareado sem HID — reconecte em Ajustes > Bluetooth",
            BluetoothState.Disconnected => "Desconectado — reconecte em Ajustes > Bluetooth",
            _ => "Aguardando pareamento no iPhone"
        };
        return new(state, message, hosts, _selectedId, k, m, _protocolMode);
    }

    private void Publish()
    {
        try
        {
            var status = GetStatus();
            string signature = $"{status.State}:{status.SelectedHostId}:{status.KeyboardConnected}:{status.MouseConnected}:{status.ProtocolMode}:" +
                string.Join(";", status.Hosts.Select(host => $"{host.Alias}:{host.DisplayName}:{host.ConnectionStatus}:{host.Bonded}:{host.KeyboardSubscribed}:{host.MouseSubscribed}:{host.HidInformationRead}:{host.ReportMapRead}:{host.ProtocolModeWritten}"));
            if (signature == _lastPublished) { return; }
            _lastPublished = signature; StatusChanged?.Invoke(status);
        }
        catch (Exception error) { log.Error("status-error", error); }
    }

    public async Task SelectHostAsync(string id)
    {
        if (!_hosts.ContainsKey(id)) { throw new InputBlockedException("Host não pertence a esta sessão HID."); }
        await ReleaseAsync(); _selectedId = id; SetNeutralPending();
        log.Write("target", $"Selected={_hosts[id].Alias}; no broadcast"); Publish();
    }

    public async Task StartAsync(CancellationToken token, int waitBluetoothSeconds = 0, bool enableRadio = false)
    {
        BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token)
            ?? throw new InputBlockedException("Nenhum adaptador Bluetooth encontrado.");
        log.Write("adapter", $"LE={adapter.IsLowEnergySupported}; Peripheral={adapter.IsPeripheralRoleSupported}");
        if (!adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported)
        { throw new InputBlockedException("O adaptador não suporta BLE peripheral."); }
        Radio radio = await adapter.GetRadioAsync().AsTask(token);
        _radio = radio;
        radio.StateChanged += OnRadio;
        _unhooks.Add(() => radio.StateChanged -= OnRadio);
        log.Write("radio", $"State={radio.State}; OS={Environment.OSVersion.Version}");
        bool radioWasOff = radio.State != RadioState.On;
        if (radioWasOff && enableRadio)
        {
            RadioAccessStatus access = await Radio.RequestAccessAsync().AsTask(token);
            log.Write("radio", $"RequestAccess={access}");
            if (access == RadioAccessStatus.Allowed)
            { log.Write("radio", $"SetState On={await radio.SetStateAsync(RadioState.On).AsTask(token)}"); }
        }
        if (radio.State != RadioState.On && waitBluetoothSeconds > 0)
        {
            log.Write("radio", $"WAITING FOR BLUETOOTH ON; timeout={waitBluetoothSeconds}s; enable it in Windows; Ctrl+C cancels; probe does not change radio state");
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(waitBluetoothSeconds));
            try
            {
                while (radio.State != RadioState.On) { await Task.Delay(1000, wait.Token); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new InputBlockedException("Tempo esgotado aguardando Bluetooth. Ligue Bluetooth e reabra o probe."); }
            log.Write("radio", $"State={radio.State}; continuing startup");
        }
        if (radio.State != RadioState.On)
        { throw new InputBlockedException("Bluetooth desligado. Ligue Bluetooth no Windows e abra novamente o probe."); }

        GattServiceProviderResult result = await GattServiceProvider.CreateAsync(HidSchema.Uuid(0x1812)).AsTask(token);
        // A newly powered radio can report On before its LE server is available.
        // Retry only that observed transition, only RadioNotAvailable, with a fixed bound.
        for (int retry = 1; radioWasOff && result.Error == BluetoothError.RadioNotAvailable && retry <= 6; retry++)
        {
            log.Write("gatt", $"Radio became On but GATT returned RadioNotAvailable; transition retry={retry}/6");
            await Task.Delay(500, token);
            result = await GattServiceProvider.CreateAsync(HidSchema.Uuid(0x1812)).AsTask(token);
        }
        log.Write("gatt", $"HID 0x1812 CreateAsync={result.Error}");
        if (result.Error != BluetoothError.Success || result.ServiceProvider is null)
        { throw new InputBlockedException($"Falha ao criar HID 0x1812: {result.Error}."); }
        _provider = result.ServiceProvider;
        GattLocalService service = _provider.Service;
        await ReadableAsync(service, 0x2A4A, "HID Information", () => [0x11, 0x01, 0x00, 0x03], token);
        await ReadableAsync(service, 0x2A4B, "Report Map", () => HidSchema.ReportMap, token);
        await ControlPointAsync(service, token);
        await ProtocolAsync(service, token);
        _keyboard = await InputAsync(service, HidSchema.KeyboardId, token);
        _mouse = await InputAsync(service, HidSchema.MouseId, token);
        _bootKeyboard = await BootInputAsync(service, 0x2A22, "boot keyboard", () => _keyboardValue, token);
        _bootMouse = await BootInputAsync(service, 0x2A33, "boot mouse", () => BootMouse(_mouseValue), token);
        await BootOutputAsync(service, token);
        var battery = await GattServiceProvider.CreateAsync(HidSchema.Uuid(0x180F)).AsTask(token);
        log.Write("gatt", $"Battery Service 0x180F={battery.Error}");
        if (battery.Error != BluetoothError.Success) { throw new InputBlockedException($"Battery Service: {battery.Error}"); }
        _batteryProvider = battery.ServiceProvider;
        GattLocalCharacteristic level = await CreateAsync(_batteryProvider.Service, 0x2A19, "Battery Level", GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, token);
        AttachRead(level, "Battery Level", () => [100]);
        _provider.AdvertisementStatusChanged += OnAdvertising;
        _unhooks.Add(() => _provider!.AdvertisementStatusChanged -= OnAdvertising);
        log.Write("gatt", "Composite HID; keyboard ID=1 bytes=8; mouse ID=2 bytes=6; encrypted; system CCCD; no payload ID prefix; Protocol Mode report=1/boot=0; standard boot reports supported");
        // Publish BAS in the same OS GATT database without a second discoverable service advertisement.
        _batteryProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = false });
        _provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        if (_provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Started) { _started.TrySetResult(); }
        await _started.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        if (_provider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Started)
        { throw new InputBlockedException($"Advertising não está ativo: {_provider.AdvertisementStatus}."); }
        log.Write("ready", "READY FOR ISOLATED IPHONE HID TEST; advertised system PC name; no input hooks");
        PrintStatus();
        _monitor = MonitorAsync(_monitorStop.Token);
        Publish();
    }

    private void OnRadio(Radio radio, object args) { log.Write("radio", $"State={radio.State}"); SetNeutralPending(); Publish(); }

    private void OnAdvertising(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
    {
        log.Write("advertising", $"AdvertisingStatus={args.Status}; Error={args.Error}");
        if (args.Status == GattServiceProviderAdvertisementStatus.Started) { _started.TrySetResult(); }
        Publish();
        // Report transient Aborted exactly as observed; don't assume it is benign or final.
    }

    private async Task<GattLocalCharacteristic> CreateAsync(GattLocalService service, ushort uuid,
        string label, GattCharacteristicProperties properties, CancellationToken token)
    {
        var parameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = properties,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        };
        GattLocalCharacteristicResult result = await service.CreateCharacteristicAsync(HidSchema.Uuid(uuid), parameters).AsTask(token);
        log.Write("gatt", $"{label} 0x{uuid:X4}={result.Error}");
        if (result.Error != BluetoothError.Success || result.Characteristic is null)
        { throw new InputBlockedException($"Falha em {label}: {result.Error}."); }
        return result.Characteristic;
    }

    private async Task ReadableAsync(GattLocalService service, ushort uuid, string label, Func<byte[]> value, CancellationToken token)
    {
        GattLocalCharacteristic characteristic = await CreateAsync(service, uuid, label, GattCharacteristicProperties.Read, token);
        AttachRead(characteristic, label, value);
    }

    private void AttachRead(GattLocalCharacteristic characteristic, string label, Func<byte[]> value)
    {
        TypedEventHandler<GattLocalCharacteristic, GattReadRequestedEventArgs> handler = async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                TrackSession(args.Session);
                GattReadRequest? request = await args.GetRequestAsync();
                if (request is null) { log.Write("read", $"{label}: request canceled"); return; }
                // Input state belongs only to the selected host; other encrypted readers see neutral.
                byte[] bytes = label == "Protocol Mode" ? [_hosts[args.Session.DeviceId.Id].Mode] :
                    args.Session.DeviceId.Id != _selectedId && label is "keyboard" or "mouse" or "boot keyboard" or "boot mouse"
                    ? label == "boot mouse" ? new byte[3] : HidSchema.Neutral(label.Contains("keyboard", StringComparison.Ordinal) ? HidSchema.KeyboardId : HidSchema.MouseId) : value();
                log.Write("read", $"{label}; offset={request.Offset}; length={request.Length}; attributeBytes={bytes.Length}");
                if (request.Offset > bytes.Length) { request.RespondWithProtocolError(0x07); return; }
                // Long Report Map reads may arrive in multiple ATT read/blob requests.
                byte[] response = bytes.AsSpan((int)request.Offset, Math.Min((int)request.Length, bytes.Length - (int)request.Offset)).ToArray();
                request.RespondWithValue(CryptographicBuffer.CreateFromByteArray(response));
                if (response.Length > 0 && _hosts.TryGetValue(args.Session.DeviceId.Id, out Host? host))
                { if (label == "HID Information") { host.InfoRead = true; } if (label == "Report Map") { host.MapRead = true; } }
                Publish();
            }
            catch (Exception error) { log.Error("read-error", error); }
        };
        characteristic.ReadRequested += handler;
        _unhooks.Add(() => characteristic.ReadRequested -= handler);
    }

    private async Task<GattLocalCharacteristic> InputAsync(GattLocalService service, byte id, CancellationToken token)
    {
        string label = Kind(id);
        GattLocalCharacteristic characteristic = await CreateAsync(service, 0x2A4D, label,
            GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, token);
        AttachRead(characteristic, label, () => id == HidSchema.KeyboardId ? _keyboardValue : _mouseValue);
        var parameters = new GattLocalDescriptorParameters
        {
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = CryptographicBuffer.CreateFromByteArray([id, 1])
        };
        GattLocalDescriptorResult descriptor = await characteristic.CreateDescriptorAsync(HidSchema.Uuid(0x2908), parameters).AsTask(token);
        log.Write("gatt", $"{label} Report Reference 0x2908 id={id} type=Input result={descriptor.Error}");
        if (descriptor.Error != BluetoothError.Success)
        { throw new InputBlockedException($"Falha no Report Reference de {label}: {descriptor.Error}."); }
        characteristic.SubscribedClientsChanged += OnSubscriptions;
        _unhooks.Add(() => characteristic.SubscribedClientsChanged -= OnSubscriptions);
        return characteristic;
    }

    private async Task ControlPointAsync(GattLocalService service, CancellationToken token)
    {
        GattLocalCharacteristic characteristic = await CreateAsync(service, 0x2A4C, "HID Control Point",
            GattCharacteristicProperties.WriteWithoutResponse, token);
        TypedEventHandler<GattLocalCharacteristic, GattWriteRequestedEventArgs> handler = async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                TrackSession(args.Session);
                GattWriteRequest? request = await args.GetRequestAsync();
                if (request is null) { return; }
                CryptographicBuffer.CopyToByteArray(request.Value, out byte[] bytes);
                bool valid = request.Offset == 0 && bytes.Length == 1 && bytes[0] <= 1;
                if (valid)
                {
                    _suspended = bytes[0] == 0;
                    log.Write("hid", $"Control Point={(_suspended ? "Suspend" : "ExitSuspend")}");
                    SetNeutralPending();
                    Publish();
                }
                else { log.Write("hid", "Invalid Control Point write"); }
                if (request.Option == GattWriteOption.WriteWithResponse)
                {
                    if (valid) { request.Respond(); } else { request.RespondWithProtocolError(0x0D); }
                }
            }
            catch (Exception error) { log.Error("write-error", error); }
        };
        characteristic.WriteRequested += handler;
        _unhooks.Add(() => characteristic.WriteRequested -= handler);
    }

    private async Task ProtocolAsync(GattLocalService service, CancellationToken token)
    {
        GattLocalCharacteristic mode = await CreateAsync(service, 0x2A4E, "Protocol Mode",
            GattCharacteristicProperties.Read | GattCharacteristicProperties.WriteWithoutResponse, token);
        AttachRead(mode, "Protocol Mode", () => [_protocolMode]);
        AttachWrite(mode, "Protocol Mode", bytes =>
        {
            if (bytes.Length != 1 || bytes[0] > 1) { return false; }
            return true;
        });
    }

    private async Task<GattLocalCharacteristic> BootInputAsync(GattLocalService service, ushort uuid, string label, Func<byte[]> value, CancellationToken token)
    {
        var characteristic = await CreateAsync(service, uuid, label, GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, token);
        AttachRead(characteristic, label, value);
        characteristic.SubscribedClientsChanged += OnSubscriptions;
        _unhooks.Add(() => characteristic.SubscribedClientsChanged -= OnSubscriptions);
        return characteristic;
    }

    private async Task BootOutputAsync(GattLocalService service, CancellationToken token)
    {
        var characteristic = await CreateAsync(service, 0x2A32, "Boot Keyboard Output",
            GattCharacteristicProperties.Read | GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, token);
        AttachRead(characteristic, "Boot Keyboard Output", () => [_leds]);
        AttachWrite(characteristic, "Boot Keyboard Output", bytes =>
        { if (bytes.Length != 1) { return false; } _leds = (byte)(bytes[0] & 0x1F); return true; });
    }

    private void AttachWrite(GattLocalCharacteristic characteristic, string label, Func<byte[], bool> apply)
    {
        TypedEventHandler<GattLocalCharacteristic, GattWriteRequestedEventArgs> handler = async (_, args) =>
        {
            using var deferral = args.GetDeferral();
            try
            {
                TrackSession(args.Session);
                var request = await args.GetRequestAsync(); if (request is null) { return; }
                if (label == "Protocol Mode" && _hosts.TryGetValue(args.Session.DeviceId.Id, out Host? host)) { host.ModeWritten = true; }
                CryptographicBuffer.CopyToByteArray(request.Value, out byte[] bytes);
                bool valid = request.Offset == 0 && apply(bytes);
                if (valid && label == "Protocol Mode" && _hosts.TryGetValue(args.Session.DeviceId.Id, out var protocolHost))
                {
                    protocolHost.Mode = bytes[0];
                    if (_selectedId == args.Session.DeviceId.Id) { SetNeutralPending(); }
                    log.Write("protocol", $"{protocolHost.Alias} Mode={(bytes[0] == 0 ? "boot" : "report")}");
                }
                log.Write("write", $"{label}; valid={valid}");
                if (request.Option == GattWriteOption.WriteWithResponse)
                { if (valid) { request.Respond(); } else { request.RespondWithProtocolError(0x0D); } }
                Publish();
            }
            catch (Exception error) { log.Error("write-error", error); }
        };
        characteristic.WriteRequested += handler;
        _unhooks.Add(() => characteristic.WriteRequested -= handler);
    }

    private GattLocalCharacteristic? KeyboardCharacteristic => _protocolMode == 0 ? _bootKeyboard : _keyboard;
    private GattLocalCharacteristic? MouseCharacteristic => _protocolMode == 0 ? _bootMouse : _mouse;
    private static byte[] BootMouse(byte[] report) => [report[0], unchecked((byte)(sbyte)Math.Clamp((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(1)), -127, 127)), unchecked((byte)(sbyte)Math.Clamp((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(3)), -127, 127))];

    private void OnSubscriptions(GattLocalCharacteristic sender, object args)
    {
        try
        {
            foreach (GattSubscribedClient client in sender.SubscribedClients) { TrackSession(client.Session); }
            log.Write("subscriptions", $"{(sender == _keyboard || sender == _bootKeyboard ? "keyboard" : "mouse")} raw={sender.SubscribedClients.Count}");
            SetNeutralPending();
            Publish();
        }
        catch (Exception error) { log.Error("subscription-error", error); }
    }

    private void TrackSession(GattSession session)
    {
        if (Volatile.Read(ref _stopping) != 0) { return; }
        string id = session.DeviceId.Id;
        Host host = _hosts.GetOrAdd(id, _ => new Host($"H{Interlocked.Increment(ref _hostNumber)}"));
        if (_sessions.TryAdd(session, id))
        {
            session.SessionStatusChanged += OnSession;
            log.Write("link", $"{host.Alias} GattSession={session.SessionStatus}");
        }
    }

    private void OnSession(GattSession sender, GattSessionStatusChangedEventArgs args)
    {
        try
        {
            string id = sender.DeviceId.Id;
            if (_hosts.TryGetValue(id, out Host? host))
            { log.Write("link", $"{host.Alias} GattSession={args.Status}; Error={args.Error}"); }
            SetNeutralPending();
            Publish();
        }
        catch (Exception error) { log.Error("session-error", error); }
    }

    private void OnConnection(BluetoothLEDevice sender, object args)
    {
        try
        {
            if (_hosts.TryGetValue(sender.DeviceId, out Host? host))
            { log.Write("link", $"{host.Alias} BluetoothConnectionStatus={sender.ConnectionStatus}; Bonded={sender.DeviceInformation.Pairing.IsPaired}"); }
            SetNeutralPending();
            Publish();
        }
        catch (Exception error) { log.Error("link-error", error); }
    }

    private void SetNeutralPending()
    {
        _keyboardValue = HidSchema.Neutral(HidSchema.KeyboardId);
        _mouseValue = HidSchema.Neutral(HidSchema.MouseId);
        _pendingRelease[HidSchema.KeyboardId] = true;
        _pendingRelease[HidSchema.MouseId] = true;
    }

    private bool IsLive(GattSubscribedClient client)
    {
        if (_radio?.State != RadioState.On) { return false; }
        if (client.Session.SessionStatus != GattSessionStatus.Active) { return false; }
        return !_hosts.TryGetValue(client.Session.DeviceId.Id, out Host? host) ||
            host.Device is null || host.Device.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }

    private GattSubscribedClient? Target(GattLocalCharacteristic characteristic, bool neutral)
    {
        // No broadcast and no silent switch to a different host after disconnection.
        if (_selectedId is null)
        {
            string[] ids = (KeyboardCharacteristic?.SubscribedClients ?? []).Concat(MouseCharacteristic?.SubscribedClients ?? [])
                .Where(IsLive).Select(client => client.Session.DeviceId.Id).Distinct().ToArray();
            if (ids.Length != 1)
            {
                if (neutral) { return null; }
                throw new InputBlockedException(ids.Length == 0 ? "Nenhum host HID ativo/subscrito." : "Mais de um host; encerre o probe e conecte somente o iPhone de teste.");
            }
            _selectedId = ids[0];
            log.Write("target", $"Selected={_hosts.GetValueOrDefault(_selectedId)?.Alias ?? "host"}; no broadcast");
        }
        GattSubscribedClient? target = characteristic.SubscribedClients.FirstOrDefault(client =>
            client.Session.DeviceId.Id == _selectedId && IsLive(client));
        if (target is null && !neutral) { throw new InputBlockedException("Host selecionado sem subscription/link ativo neste report."); }
        return target;
    }

    public Task SendAsync(byte id, byte[] payload, CancellationToken token) => SendCoreAsync(id, payload, token, false);

    private async Task SendCoreAsync(byte id, byte[] payload, CancellationToken token, bool onlyPending)
    {
        bool neutral = payload.All(value => value == 0);
        await _sendGate.WaitAsync(token);
        try
        {
            // A reconnect monitor may race with the first new input. Never neutralize a
            // held key/button after that input has already synchronized its pending release.
            if (onlyPending && !_pendingRelease.ContainsKey(id)) { return; }
            if (payload.Length != HidSchema.Neutral(id).Length) { throw new ArgumentException("Report length mismatch."); }
            if (neutral) { _pendingRelease[id] = true; }
            GattLocalCharacteristic? characteristic = id == HidSchema.KeyboardId ? KeyboardCharacteristic : MouseCharacteristic;
            if (characteristic is null || (_suspended && !neutral))
            { throw new InputBlockedException("HID não iniciado ou suspenso pelo host."); }
            GattSubscribedClient? target = Target(characteristic, neutral);
            if (target is null)
            {
                if (neutral) { if (id == HidSchema.KeyboardId) { _keyboardValue = payload; } else { _mouseValue = payload; } }
                log.Write("release", $"{Kind(id)} pending; no live subscribed target; delivery not claimed");
                return;
            }
            if (!neutral && _pendingRelease.ContainsKey(id))
            {
                byte[] release = _protocolMode == 0 && id == HidSchema.MouseId ? new byte[3] : HidSchema.Neutral(id);
                using var syncTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                syncTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                var synchronization = await characteristic.NotifyValueAsync(CryptographicBuffer.CreateFromByteArray(release), target).AsTask(syncTimeout.Token);
                if (synchronization.Status != GattCommunicationStatus.Success) { throw new InputBlockedException("Neutral synchronization failed; input blocked."); }
                _pendingRelease.TryRemove(id, out _);
                log.Write("release", $"{Kind(id)} neutral synchronized before input; transport success, physical effect unconfirmed");
            }
            if (id == HidSchema.KeyboardId) { _keyboardValue = payload; } else { _mouseValue = payload; }
            if (id == HidSchema.MouseId && _protocolMode == 0)
            {
                if (payload[5] != 0) { throw new InputBlockedException("Boot mouse não suporta wheel; reconecte em report mode."); }
                payload = BootMouse(payload);
            }
            if (payload.Length > target.MaxNotificationSize) { throw new InputBlockedException("Report excede MaxNotificationSize."); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            GattClientNotificationResult result = await characteristic.NotifyValueAsync(
                CryptographicBuffer.CreateFromByteArray(payload), target).AsTask(timeout.Token);
            if (neutral || result.Status != GattCommunicationStatus.Success)
            { log.Write("notify", $"{Kind(id)} {(neutral ? "neutral/release" : "input")}; bytes={payload.Length}; Status={result.Status}; ProtocolError={result.ProtocolError?.ToString() ?? "none"}; physical effect unconfirmed"); }
            if (result.Status != GattCommunicationStatus.Success)
            { throw new InputBlockedException($"Notify {Kind(id)} falhou: {result.Status}."); }
            if (neutral) { _pendingRelease.TryRemove(id, out _); }
        }
        finally { _sendGate.Release(); }
    }

    public async Task ReleaseAsync()
    {
        foreach (byte id in new[] { HidSchema.KeyboardId, HidSchema.MouseId })
        {
            if ((id == HidSchema.KeyboardId ? _keyboard : _mouse) is null) { continue; }
            try { await SendAsync(id, HidSchema.Neutral(id), CancellationToken.None); }
            catch (Exception error) { log.Error("release-error", error); }
        }
    }

    public void PrintStatus()
    {
        var snapshot = GetStatus();
        log.Write("status", $"State={snapshot.State}; ProtocolMode={snapshot.ProtocolMode}; Selected={_hosts.GetValueOrDefault(snapshot.SelectedHostId ?? "")?.Alias ?? "none"}; keyboard={snapshot.KeyboardConnected}; mouse={snapshot.MouseConnected}");
        foreach (var host in snapshot.Hosts) { log.Write("metadata", $"{host.Alias}; InfoRead={host.HidInformationRead}; MapRead={host.ReportMapRead}; ModeWrite={host.ProtocolModeWritten}; Bonded={host.Bonded}; KeyboardCCCD={host.KeyboardSubscribed}; MouseCCCD={host.MouseSubscribed}"); }
        log.Write("status", $"AdvertisingStatus={_provider?.AdvertisementStatus}; keyboard subscribers raw={_keyboard?.SubscribedClients.Count ?? 0} live={_keyboard?.SubscribedClients.Count(IsLive) ?? 0}; mouse subscribers raw={_mouse?.SubscribedClients.Count ?? 0} live={_mouse?.SubscribedClients.Count(IsLive) ?? 0}; Suspended={_suspended}; pendingRelease={_pendingRelease.Count}");
        foreach (Host host in _hosts.Values) { log.Write("status", $"{host.Alias} connection={host.Device?.ConnectionStatus.ToString() ?? "unresolved"}; Bonded={host.Device?.DeviceInformation.Pairing.IsPaired.ToString() ?? "unknown"}"); }
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                foreach (GattSubscribedClient client in (KeyboardCharacteristic?.SubscribedClients ?? []).Concat(MouseCharacteristic?.SubscribedClients ?? []))
                    { TrackSession(client.Session); }
                    foreach ((string id, Host host) in _hosts)
                    {
                        if (!host.LookupAttempted)
                        {
                            host.LookupAttempted = true;
                            try
                            {
                                host.Device = await BluetoothLEDevice.FromIdAsync(id).AsTask(token).WaitAsync(TimeSpan.FromSeconds(3), token);
                                if (host.Device is not null) { host.Name = host.Device.Name; host.Device.ConnectionStatusChanged += OnConnection; }
                            }
                            catch (Exception error) when (error is not OperationCanceledException) { log.Error("host-lookup", error); }
                        }
                        string summary = $"{host.Alias} connection={host.Device?.ConnectionStatus.ToString() ?? "unresolved"}; Bonded={host.Device?.DeviceInformation.Pairing.IsPaired.ToString() ?? "unknown"}";
                        if (summary != host.LastSummary) { host.LastSummary = summary; log.Write("link", summary); }
                    }
                    string subscriptions = $"keyboard raw={_keyboard?.SubscribedClients.Count ?? 0} live={_keyboard?.SubscribedClients.Count(IsLive) ?? 0}; mouse raw={_mouse?.SubscribedClients.Count ?? 0} live={_mouse?.SubscribedClients.Count(IsLive) ?? 0}";
                    if (_lastSubscriptionSummary != subscriptions) { _lastSubscriptionSummary = subscriptions; log.Write("subscriptions", subscriptions); }
                    if (_pendingRelease.Count > 0 && DateTimeOffset.UtcNow - _lastReleaseAttempt >= TimeSpan.FromSeconds(5) &&
                        (KeyboardCharacteristic?.SubscribedClients.Any(IsLive) == true || MouseCharacteristic?.SubscribedClients.Any(IsLive) == true))
                    {
                        _lastReleaseAttempt = DateTimeOffset.UtcNow;
                        // Neutral synchronization on subscription/reconnect; never synthesize a press.
                        foreach (byte id in _pendingRelease.Keys)
                        {
                            // Synchronize just the missing report. A keyboard without a subscriber
                            // must not repeatedly release an otherwise active mouse drag.
                            await SendCoreAsync(id, HidSchema.Neutral(id), token, onlyPending: true);
                        }
                    }
                    Publish();
                }
                catch (Exception error) when (error is not OperationCanceledException) { log.Error("monitor", error); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) { return; }
        _monitorStop.Cancel();
        if (_monitor is not null)
        {
            try { await _monitor.WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (Exception error) { log.Error("monitor-stop", error); }
        }
        if (_keyboard is not null || _mouse is not null) { SetNeutralPending(); }
        await ReleaseAsync();
        if (_provider is not null)
        {
            try
            {
                _provider.StopAdvertising();
                for (int attempt = 0; attempt < 20 && _provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Started; attempt++)
                { await Task.Delay(100); }
                log.Write("cleanup", $"StopAdvertising requested; observed AdvertisingStatus={_provider.AdvertisementStatus}; process exit releases local registration");
            }
            catch (Exception error) { log.Error("stop-advertising", error); }
        }
        if (_batteryProvider is not null)
        { try { _batteryProvider.StopAdvertising(); } catch (Exception error) { log.Error("stop-battery", error); } }
        foreach (Action unhook in _unhooks)
        {
            try { unhook(); } catch (Exception error) { log.Error("unhook", error); }
        }
        foreach (GattSession session in _sessions.Keys) { session.SessionStatusChanged -= OnSession; }
        foreach (Host host in _hosts.Values)
        {
            if (host.Device is null) { continue; }
            host.Device.ConnectionStatusChanged -= OnConnection;
            host.Device.Dispose();
        }
        _keyboard = null;
        _mouse = null;
        _bootKeyboard = null; _bootMouse = null;
        _unhooks.Clear(); _sessions.Clear(); _hosts.Clear();
        // GattServiceProvider has no IClosable/Dispose API in SDK 19041. Stop, unhook and drop references.
        // Never Marshal.FinalReleaseComObject a CsWinRT projection.
        _provider = null;
        _batteryProvider = null;
        _radio = null;
        log.Write("cleanup", $"Handlers detached; local service lifetime ends with process; pending neutral synchronization={_pendingRelease.Count}; no delivery assumed on closed link");
    }

    private static string Kind(byte id) => id == HidSchema.KeyboardId ? "keyboard" : "mouse";
}
