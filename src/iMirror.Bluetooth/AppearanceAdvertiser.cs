using Windows.Devices.Bluetooth.Advertisement;
using Windows.Security.Cryptography;

namespace iMirror.Bluetooth;

// Adapted concept from windows-ble-hid; one optional publisher, independent of the primary HOGP.
public sealed class AppearanceAdvertiser(BluetoothControlLog log) : IDisposable
{
    private BluetoothLEAdvertisementPublisher? _publisher;
    public void Start(ushort appearance)
    {
        if (appearance is not (0x03C1 or 0x03C2)) { throw new ArgumentOutOfRangeException(nameof(appearance)); }
        Dispose();
        var advertisement = new BluetoothLEAdvertisement();
        advertisement.DataSections.Add(new BluetoothLEAdvertisementDataSection
        { DataType = 0x03, Data = CryptographicBuffer.CreateFromByteArray([0x12, 0x18]) });
        advertisement.DataSections.Add(new BluetoothLEAdvertisementDataSection
        { DataType = 0x19, Data = CryptographicBuffer.CreateFromByteArray([(byte)appearance, (byte)(appearance >> 8)]) });
        _publisher = new BluetoothLEAdvertisementPublisher(advertisement);
        _publisher.StatusChanged += OnStatus;
        try { _publisher.Start(); }
        catch (UnauthorizedAccessException error)
        {
            log.Error("appearance-error", error);
            Dispose();
            // These exact AD types are system reserved on Windows; no administrator retry.
            throw new InputBlockedException("Windows bloqueou o anúncio paralelo (UUID/Appearance reservados). HOGP normal continua ativo.");
        }
        log.Write("appearance", $"Optional appearance=0x{appearance:X4}; publisher={_publisher.Status}; system GAP identity unchanged");
    }
    private void OnStatus(BluetoothLEAdvertisementPublisher publisher, BluetoothLEAdvertisementPublisherStatusChangedEventArgs args) =>
        log.Write("appearance", $"Status={args.Status}; Error={args.Error}");
    public void Dispose()
    {
        if (_publisher is null) { return; }
        try { _publisher.Stop(); log.Write("appearance", $"Stop requested; Status={_publisher.Status}"); }
        finally { _publisher.StatusChanged -= OnStatus; _publisher = null; }
    }
}
