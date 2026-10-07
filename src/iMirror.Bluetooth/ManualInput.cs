namespace iMirror.Bluetooth;

public interface IReportTransport
{
    Task SendAsync(byte id, byte[] payload, CancellationToken token);
}

public sealed class ManualInput(IReportTransport transport)
{
    public async Task PulseAsync(byte id, byte[] press, CancellationToken token)
    {
        try
        {
            await transport.SendAsync(id, press, token);
            await Task.Delay(65, token);
        }
        finally
        {
            // Release must be attempted even when press, delay or cancellation fails.
            // The transport persists a failed neutral release for the next live subscription.
            await transport.SendAsync(id, HidSchema.Neutral(id), CancellationToken.None);
        }
    }

    public async Task TypeAsync(string text, CancellationToken token)
    {
        byte[][] reports = HidSchema.PrepareText(text);
        foreach (byte[] report in reports)
        {
            token.ThrowIfCancellationRequested();
            await PulseAsync(HidSchema.KeyboardId, report, token);
            await Task.Delay(35, token);
        }
    }
}
