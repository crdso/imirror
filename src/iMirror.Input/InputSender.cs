using iMirror.Bluetooth;

namespace iMirror.Input;

public static class InputSender
{
    public static async Task RunAsync(InputReportBuffer buffer, IBluetoothController bluetooth, Func<bool> allowed,
        Action<string> stop, BluetoothControlLog log, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Delay after each completed send; PeriodicTimer can deliver a catch-up tick
                // immediately after a late send and does not guarantee minimum spacing.
                await Task.Delay(16, token);
                if (!allowed() || !bluetooth.Status.MouseConnected) { stop("conexão ou foco perdido"); break; }
                if (buffer.Take() is not { } report) { continue; }
                if (report.Keyboard && !bluetooth.Status.KeyboardConnected) { stop("keyboard subscriber perdido"); break; }
                Task send = report.Keyboard ? bluetooth.SendKeyboardAsync(report.Modifiers, report.Keys, token) :
                    bluetooth.SendMouseAsync(report.Buttons, report.X, report.Y, report.Wheel, token);
                await send.WaitAsync(TimeSpan.FromSeconds(3), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { log.Error("send-error", error); stop("falha no envio HID"); }
        finally
        {
            buffer.Clear();
            try { await bluetooth.ReleaseAsync().WaitAsync(TimeSpan.FromSeconds(7)); }
            catch (Exception error) { log.Error("release-error", error); }
        }
    }
}
