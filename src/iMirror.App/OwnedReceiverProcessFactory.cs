using iMirror.AirPlay;

namespace iMirror.App;

// Decorates the existing factory without changing arguments, environment or launch flags.
public sealed class OwnedReceiverProcessFactory : IReceiverProcessFactory
{
    private IReceiverProcess? _process;
    public int ProcessId
    {
        get
        {
            try { var process = Volatile.Read(ref _process); return process is not null && !process.HasExited ? process.Id : 0; }
            catch (InvalidOperationException) { return 0; }
        }
    }
    public IReceiverProcess Start(ReceiverProcessRequest request)
    { var process = new ReceiverProcessFactory().Start(request); Volatile.Write(ref _process, process); return process; }
}
