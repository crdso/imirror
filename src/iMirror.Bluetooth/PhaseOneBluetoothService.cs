using iMirror.Core.Diagnostics;
using iMirror.Core.Features;

namespace iMirror.Bluetooth;

public sealed class PhaseOneBluetoothService(IDiagnosticLog log) : IFeatureService
{
    public FeatureStatus Status { get; } = new("Bluetooth",
        "Controle BLE HID previsto para a fase 3, após validar o vídeo. Nenhum dispositivo está sendo anunciado.");

    public void ReportAvailability() => log.Write(LogLevel.Warning, Status.Name, Status.Description);
}
