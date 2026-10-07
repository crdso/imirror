namespace iMirror.Bluetooth;

// Evidence layers deliberately stay separate. A bond or metadata read is never a subscriber.
public static class PairingStatus
{
    public static BluetoothStatus Create(bool radioOn, bool advertising, IReadOnlyList<BluetoothHost> hosts,
        string? selected, bool keyboard, bool mouse, byte mode, bool previouslyConnected, TimeSpan waiting)
    {
        var host = hosts.FirstOrDefault(h => h.Id == selected) ?? (selected is null && hosts.Count == 1 ? hosts[0] : null);
        var state = !radioOn ? BluetoothState.RadioOff : keyboard && mouse ? BluetoothState.HidConnected :
            keyboard ? BluetoothState.KeyboardConnected : mouse ? BluetoothState.MouseConnected :
            previouslyConnected && host?.GattActive != true ? BluetoothState.ReconnectionRequired :
            host?.GattActive == true && (host.HidInformationRead || host.ReportMapRead) ? BluetoothState.HidIncomplete :
            host?.GattActive == true ? BluetoothState.GattDetected : host?.Bonded == true ? BluetoothState.BondedWithoutHid :
            advertising ? BluetoothState.WaitingForPairing : BluetoothState.Advertising;
        var text = state switch
        {
            BluetoothState.RadioOff => "Bluetooth desligado",
            BluetoothState.HidConnected => "Controle pronto",
            BluetoothState.KeyboardConnected => "Keyboard conectado · aguardando Mouse",
            BluetoothState.MouseConnected => "Mouse conectado · aguardando Keyboard",
            BluetoothState.ReconnectionRequired => "Reconexão necessária · serviço HID mantido",
            BluetoothState.HidIncomplete => "Conexão HID incompleta · metadata recebida, aguardando subscriptions",
            BluetoothState.GattDetected => "Sessão GATT detectada · aguardando HID",
            BluetoothState.BondedWithoutHid => "Pareado, aguardando HID",
            BluetoothState.Advertising => "HID aguardando advertising",
            _ => "Aguardando iPhone"
        };
        bool timedOut = waiting >= TimeSpan.FromSeconds(30) && !(keyboard && mouse);
        return new(state, text, hosts, selected, keyboard, mouse, mode, advertising, radioOn, timedOut);
    }
}
