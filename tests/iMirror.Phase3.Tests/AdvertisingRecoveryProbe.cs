using iMirror.Bluetooth;

internal static class AdvertisingRecoveryProbe
{
    private static void Require(bool value) { if (!value) { throw new Exception("Advertising recovery invariant failed"); } }
    private sealed class Clock
    {
        internal sealed record Wait(TimeSpan Duration, TaskCompletionSource Done);
        public readonly List<Wait> Waits = [];
        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(Waits) { Waits.Add(new(duration, done)); }
            return Run();
            async Task Run() { using var registration = token.Register(() => done.TrySetCanceled(token)); await done.Task; }
        }
        public void Complete(int seconds)
        {
            lock(Waits) { Waits.Last(wait => wait.Duration == TimeSpan.FromSeconds(seconds) && !wait.Done.Task.IsCompleted).Done.SetResult(); }
        }
        public int Count(int seconds) { lock(Waits) { return Waits.Count(wait => wait.Duration == TimeSpan.FromSeconds(seconds)); } }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) { await Task.Delay(1, timeout.Token); }
    }
    public static async Task Aborted()
    {
        var clock = new Clock(); int starts = 0;
        var recovery = new AdvertisingRecovery(() => Interlocked.Increment(ref starts), () => true, _ => {}, clock.Delay);
        recovery.Ensure("first"); Require(recovery.Pending);
        recovery.Observe(AdvertisingSignal.Aborted); Require(!recovery.Pending && recovery.State == AdvertisingState.Backoff);
        recovery.Observe(AdvertisingSignal.Aborted); Require(clock.Count(1) == 1);
        recovery.Ensure("duplicate request"); Require(starts == 1);
        clock.Complete(1); await Until(() => starts == 2);
        recovery.Observe(AdvertisingSignal.Started); Require(!recovery.Pending && recovery.State == AdvertisingState.Started);
        await recovery.StopAsync(); Require(starts == 2);
    }
    public static async Task Exceptions()
    {
        var clock = new Clock(); int starts = 0;
        var recovery = new AdvertisingRecovery(() => { Interlocked.Increment(ref starts); throw new InvalidOperationException("fake native failure"); }, () => true, _ => {}, clock.Delay);
        recovery.Ensure("first"); Require(!recovery.Pending && clock.Count(1) == 1);
        clock.Complete(1); await Until(() => clock.Count(2) == 1);
        clock.Complete(2); await Until(() => clock.Count(5) == 1);
        clock.Complete(5); await Until(() => starts == 4 && recovery.State == AdvertisingState.Idle);
        for (int i = 0; i < 20; i++) { recovery.Ensure("monitor/drop"); }
        Require(starts == 4 && !recovery.Pending);
        await recovery.StopAsync();
    }
    public static async Task WatchdogAndDrop()
    {
        var clock = new Clock(); int starts = 0;
        var recovery = new AdvertisingRecovery(() => Interlocked.Increment(ref starts), () => true, _ => {}, clock.Delay);
        recovery.Ensure("first"); clock.Complete(10); await Until(() => recovery.State == AdvertisingState.Backoff);
        Require(!recovery.Pending); clock.Complete(1); await Until(() => starts == 2);
        recovery.Observe(AdvertisingSignal.Started);
        recovery.Observe(AdvertisingSignal.Stopped); Require(!recovery.Pending && recovery.State == AdvertisingState.Backoff);
        clock.Complete(2); await Until(() => starts == 3);
        recovery.Observe(AdvertisingSignal.Aborted);
        await recovery.StopAsync();
        recovery.Ensure("late drop", explicitRequest:true); recovery.Observe(AdvertisingSignal.Aborted);
        Require(starts == 3 && recovery.State == AdvertisingState.Stopping && !recovery.Pending);
    }
}
