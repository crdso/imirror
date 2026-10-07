namespace iMirror.Bluetooth;

public enum AdvertisingState { Idle, Starting, Started, Backoff, Stopping }
public enum AdvertisingSignal { Started, Stopped, Aborted }

// Recovery never owns or creates a GATT provider. The one native Start action
// is serialized with shutdown, and every failed request has a bounded deadline.
public sealed class AdvertisingRecovery(Action start, Func<bool> available, Action<string> report,
    Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null)
{
    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private CancellationTokenSource? _ticket;
    private Task _work = Task.CompletedTask;
    private AdvertisingState _state;
    private int _retry;
    private bool _exhausted;
    private DateTimeOffset _startedAt;
    public AdvertisingState State { get { lock (_gate) { return _state; } } }
    public bool Pending => State == AdvertisingState.Starting;

    public void Ensure(string reason, bool explicitRequest = false)
    {
        lock (_gate)
        {
            if (_state == AdvertisingState.Stopping || !available()) { return; }
            if (explicitRequest && _exhausted) { _exhausted = false; _retry = 0; }
            if (_state is AdvertisingState.Starting or AdvertisingState.Started or AdvertisingState.Backoff || _exhausted) { return; }
            StartLocked(reason);
        }
    }

    public void Observe(AdvertisingSignal signal)
    {
        lock (_gate)
        {
            if (_state == AdvertisingState.Stopping) { return; }
            if (_state == AdvertisingState.Backoff && signal != AdvertisingSignal.Started) { report($"signal={signal}; existing backoff retained; pending=false"); return; }
            bool stable = _state == AdvertisingState.Started && _now() - _startedAt >= TimeSpan.FromSeconds(30);
            CancelTicket();
            if (signal == AdvertisingSignal.Started)
            {
                _state = AdvertisingState.Started; _startedAt = _now();
                report("state=Started; pending=false; same provider"); return;
            }
            _state = AdvertisingState.Idle;
            report($"signal={signal}; state=Idle; pending=false; same provider");
            if (stable) { _retry = 0; _exhausted = false; }
            ScheduleLocked(signal.ToString());
        }
    }

    private void StartLocked(string reason)
    {
        CancelTicket(); _state = AdvertisingState.Starting;
        report($"state=Starting; reason={reason}; retry={_retry}/3; same provider");
        // Hold the reentrant gate across the native request: Stop cannot race a
        // queued Start, and synchronous status callbacks can safely call Observe.
        try { start(); }
        catch (Exception error)
        {
            CancelTicket(); _state = AdvertisingState.Idle;
            report($"start-exception={error.GetType().Name}; HRESULT=0x{error.HResult:X8}; pending=false");
            ScheduleLocked("exception"); return;
        }
        if (_state == AdvertisingState.Starting) { StartTimerLocked(TimeSpan.FromSeconds(10), watchdog:true); }
    }

    private void ScheduleLocked(string reason)
    {
        if (!available()) { _state = AdvertisingState.Idle; return; }
        if (_retry >= 3) { _state = AdvertisingState.Idle; _exhausted = true; report("retry limit reached; pending=false; no automatic loop; same provider retained"); return; }
        int seconds = new[] { 1, 2, 5 }[_retry++];
        _state = AdvertisingState.Backoff;
        report($"state=Backoff; reason={reason}; delay={seconds}s; retry={_retry}/3; pending=false");
        StartTimerLocked(TimeSpan.FromSeconds(seconds), watchdog:false);
    }

    private void StartTimerLocked(TimeSpan wait, bool watchdog)
    {
        var ticket = new CancellationTokenSource(); _ticket = ticket;
        _work = TimerAsync(wait, watchdog, ticket);
    }
    private async Task TimerAsync(TimeSpan wait, bool watchdog, CancellationTokenSource ticket)
    {
        try
        {
            await _delay(wait, ticket.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (ticket.IsCancellationRequested || _ticket != ticket || _state == AdvertisingState.Stopping) { return; }
                _ticket = null;
                if (watchdog)
                {
                    _state = AdvertisingState.Idle; report("Started deadline expired; pending=false"); ScheduleLocked("no Started event");
                }
                else if (available()) { StartLocked("bounded backoff retry"); }
                else { _state = AdvertisingState.Idle; }
            }
        }
        catch (OperationCanceledException) when (ticket.IsCancellationRequested) { }
        finally { ticket.Dispose(); }
    }
    private void CancelTicket()
    { if (_ticket is not null) { _ticket.Cancel(); _ticket = null; } }
    public async Task StopAsync()
    {
        Task pending;
        lock (_gate) { _state = AdvertisingState.Stopping; CancelTicket(); pending = _work; report("state=Stopping; pending=false; retries canceled"); }
        await pending.ConfigureAwait(false);
    }
}
